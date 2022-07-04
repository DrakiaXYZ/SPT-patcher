﻿using PatchClient.Models;
using PatcherUtils.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PatcherUtils
{
    public class PatchHelper
    {
        private string SourceFolder = "";
        private string TargetFolder = "";
        private string DeltaFolder = "";

        private int fileCountTotal;
        private int filesProcessed;

        private int deltaCount;
        private int newCount;
        private int delCount;
        private int existCount;

        private List<LineItem> AdditionalInfo = new List<LineItem>();

        /// <summary>
        /// Reports patch creation or application progress
        /// </summary>
        /// <remarks>Includes an array of <see cref="LineItem"/> with details for each type of patch</remarks>
        public event ProgressChangedHandler ProgressChanged;

        protected virtual void RaiseProgressChanged(int progress, int total, string Message = "", params LineItem[] AdditionalLineItems)
        {
            int percent = (int)Math.Floor((double)progress / total * 100);

            ProgressChanged?.Invoke(this, progress, total, percent, Message, AdditionalLineItems);
        }

        /// <summary>
        /// A helper class to create and apply patches to folders
        /// </summary>
        /// <param name="SourceFolder">The directory that will have patches applied to it.</param>
        /// <param name="TargetFolder">The directory to compare against during patch creation.</param>
        /// <param name="DeltaFolder">The directory where the patches are/will be located.</param>
        /// <remarks><paramref name="TargetFolder"/> can be null if you only plan to apply patches.</remarks>
        public PatchHelper(string SourceFolder, string TargetFolder, string DeltaFolder)
        {
            this.SourceFolder = SourceFolder;
            this.TargetFolder = TargetFolder;
            this.DeltaFolder = DeltaFolder;
        }

        /// <summary>
        /// Get the delta folder file path. 
        /// </summary>
        /// <param name="SourceFilePath"></param>
        /// <param name="SourceFolderPath"></param>
        /// <param name="FileExtension">The extension to append to the file</param>
        /// <returns>A file path inside the delta folder</returns>
        private string GetDeltaPath(string SourceFilePath, string SourceFolderPath, string FileExtension)
        {
            return Path.Join(DeltaFolder, $"{SourceFilePath.Replace(SourceFolderPath, "")}.{FileExtension}");
        }

        /// <summary>
        /// Check if two files have the same MD5 hash
        /// </summary>
        /// <param name="SourceFilePath"></param>
        /// <param name="TargetFilePath"></param>
        /// <returns>True if the hashes match</returns>
        private bool CompareFileHashes(string SourceFilePath, string TargetFilePath)
        {
            var sourceInfo = new FileInfo(SourceFilePath);
            var targetInfo = new FileInfo(TargetFilePath);

            using (MD5 md5Service = MD5.Create())
            using (var sourceStream = File.OpenRead(SourceFilePath))
            using (var targetStream = File.OpenRead(TargetFilePath))
            {
                byte[] sourceHash = md5Service.ComputeHash(sourceStream);
                byte[] targetHash = md5Service.ComputeHash(targetStream);

                bool matched = Enumerable.SequenceEqual(sourceHash, targetHash);

                PatchLogger.LogInfo($"Hash Check: S({sourceInfo.Name}|{Convert.ToBase64String(sourceHash)}) - T({targetInfo.Name}|{Convert.ToBase64String(targetHash)}) - Match:{matched}");

                return matched;
            }
        }

        /// <summary>
        /// Apply a delta to a file using xdelta
        /// </summary>
        /// <param name="SourceFilePath"></param>
        /// <param name="DeltaFilePath"></param>
        private (bool, string) ApplyDelta(string SourceFilePath, string DeltaFilePath)
        {
            string decodedPath = SourceFilePath + ".decoded";

            Process.Start(new ProcessStartInfo
            {
                FileName = LazyOperations.XDelta3Path,
                Arguments = $"-d -f -s \"{SourceFilePath}\" \"{DeltaFilePath}\" \"{decodedPath}\"",
                CreateNoWindow = true
            })
            .WaitForExit();

            if (File.Exists(decodedPath))
            {
                PatchLogger.LogInfo($"File delta decoded: {SourceFilePath}");

                try
                {
                    File.Move(decodedPath, SourceFilePath, true);
                    PatchLogger.LogInfo($"Delta applied: {DeltaFilePath}");
                    return (true, "");
                }
                catch (Exception ex)
                {
                    PatchLogger.LogException(ex);
                    return (false, ex.Message);
                }
            }
            else
            {
                string error = $"Failed to decode file delta: {SourceFilePath}";
                PatchLogger.LogError(error);
                return (false, error);
            }
        }

        /// <summary>
        /// Create a .delta file using xdelta
        /// </summary>
        /// <param name="SourceFilePath"></param>
        /// <param name="TargetFilePath"></param>
        /// <remarks>Used to patch an existing file with xdelta</remarks>
        private void CreateDelta(string SourceFilePath, string TargetFilePath)
        {
            FileInfo sourceFileInfo = new FileInfo(SourceFilePath);

            string deltaPath = GetDeltaPath(SourceFilePath, SourceFolder, "delta");

            try
            {
                Directory.CreateDirectory(deltaPath.Replace(sourceFileInfo.Name + ".delta", ""));
            }
            catch(Exception ex)
            {
                PatchLogger.LogException(ex);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = LazyOperations.XDelta3Path,
                Arguments = $"-0 -e -f -s \"{SourceFilePath}\" \"{TargetFilePath}\" \"{deltaPath}\"",
                CreateNoWindow = true
            })
            .WaitForExit();

            if (File.Exists(deltaPath))
            {
                PatchLogger.LogInfo($"File created [DELTA]: {deltaPath}");
            }
            else
            {
                PatchLogger.LogError($"File Create failed [DELTA]: {deltaPath}");
            }
        }

        /// <summary>
        /// Create a .del file
        /// </summary>
        /// <param name="SourceFile"></param>
        /// <remarks>Used to mark a file for deletion</remarks>
        private void CreateDelFile(string SourceFile)
        {
            FileInfo sourceFileInfo = new FileInfo(SourceFile);

            string deltaPath = GetDeltaPath(SourceFile, SourceFolder, "del");

            try
            {
                Directory.CreateDirectory(deltaPath.Replace(sourceFileInfo.Name + ".del", ""));
            }
            catch(Exception ex)
            {
                PatchLogger.LogException(ex);
            }

            try
            {
                File.Create(deltaPath);
                PatchLogger.LogInfo($"File Created [DEL]: {deltaPath}");
            }
            catch(Exception ex)
            {
                PatchLogger.LogException(ex);
            }
        }

        /// <summary>
        /// Create a .new file
        /// </summary>
        /// <param name="TargetFile"></param>
        /// <remarks>Used to mark a file that needs to be added</remarks>
        private void CreateNewFile(string TargetFile)
        {
            FileInfo targetSourceInfo = new FileInfo(TargetFile);

            string deltaPath = GetDeltaPath(TargetFile, TargetFolder, "new");

            try
            {
                Directory.CreateDirectory(deltaPath.Replace(targetSourceInfo.Name + ".new", ""));
            }
            catch(Exception ex)
            {
                PatchLogger.LogException(ex);
            }

            try
            {
                targetSourceInfo.CopyTo(deltaPath, true);
                PatchLogger.LogInfo($"File Created [NEW]: {deltaPath}");
            }
            catch(Exception ex)
            {
                PatchLogger.LogException(ex);
            }
        }

        /// <summary>
        /// Generate a full set of patches using the source and target folders specified during contruction./>
        /// </summary>
        /// <returns></returns>
        /// <remarks>Patches are created in the delta folder specified during contruction</remarks>
        public PatchMessage GeneratePatches()
        {
            PatchLogger.LogInfo(" ::: Starting patch generation :::");
            //get all directory information needed
            DirectoryInfo sourceDir = new DirectoryInfo(SourceFolder);
            DirectoryInfo targetDir = new DirectoryInfo(TargetFolder);
            DirectoryInfo deltaDir = Directory.CreateDirectory(DeltaFolder);

            //make sure all directories exist
            if (!sourceDir.Exists)
            {
                string message = $"Could not find source directory: {sourceDir.FullName}";
                PatchLogger.LogError(message);
                return new PatchMessage(message, PatcherExitCode.MissingDir);
            }

            if (!targetDir.Exists)
            {
                string message = $"Could not find target directory: {targetDir.FullName}";
                PatchLogger.LogError(message);
                return new PatchMessage(message, PatcherExitCode.MissingDir);
            }

            if (!deltaDir.Exists)
            {
                string message = $"Could not find delta directory: {deltaDir.FullName}";
                PatchLogger.LogError(message);
                return new PatchMessage(message, PatcherExitCode.MissingDir);
            }

            LazyOperations.ExtractResourcesToTempDir();

            List<FileInfo> SourceFiles = sourceDir.GetFiles("*", SearchOption.AllDirectories).ToList();

            fileCountTotal = SourceFiles.Count;

            PatchLogger.LogInfo($"Total source files: {fileCountTotal}");

            AdditionalInfo.Clear();
            AdditionalInfo.Add(new LineItem("Delta Patch", 0));
            AdditionalInfo.Add(new LineItem("New Patch", 0));
            AdditionalInfo.Add(new LineItem("Del Patch", 0));
            AdditionalInfo.Add(new LineItem("File Exists", 0));

            filesProcessed = 0;

            RaiseProgressChanged(0, fileCountTotal, "Generating deltas...");

            foreach (FileInfo targetFile in targetDir.GetFiles("*", SearchOption.AllDirectories))
            {
                //find a matching source file based on the relative path of the file
                FileInfo sourceFile = SourceFiles.Find(f => f.FullName.Replace(sourceDir.FullName, "") == targetFile.FullName.Replace(targetDir.FullName, ""));

                //if the target file doesn't exist in the source files, the target file needs to be added.
                if (sourceFile == null)
                {
                    PatchLogger.LogInfo("::: Creating .new file :::");
                    CreateNewFile(targetFile.FullName);

                    newCount++;
                    filesProcessed++;

                    RaiseProgressChanged(filesProcessed, fileCountTotal, $"{targetFile.FullName.Replace(TargetFolder, "...")}.new", AdditionalInfo.ToArray());

                    continue;
                }

                string extension = "";

                //if a matching source file was found, check the file hashes and get the delta.
                if (!CompareFileHashes(sourceFile.FullName, targetFile.FullName))
                {
                    PatchLogger.LogInfo("::: Creating .delta file :::");
                    CreateDelta(sourceFile.FullName, targetFile.FullName);
                    extension = ".delta";
                    deltaCount++;
                }
                else
                {
                    PatchLogger.LogInfo("::: File Exists :::");
                    existCount++;
                }

                try
                {
                    SourceFiles.Remove(sourceFile);
                }
                catch(Exception ex)
                {
                    PatchLogger.LogException(ex);
                }

                filesProcessed++;

                AdditionalInfo[0].ItemValue = deltaCount;
                AdditionalInfo[1].ItemValue = newCount;
                AdditionalInfo[3].ItemValue = existCount;

                RaiseProgressChanged(filesProcessed, fileCountTotal, $"{targetFile.FullName.Replace(TargetFolder, "...")}{extension}", AdditionalInfo.ToArray());
            }

            //Any remaining source files do not exist in the target folder and can be removed.
            //reset progress info

            if (SourceFiles.Count == 0)
            {
                PatchLogger.LogInfo("::: Patch Generation Complete :::");

                return new PatchMessage("Generation Done", PatcherExitCode.Success);
            }

            RaiseProgressChanged(0, SourceFiles.Count, "Processing .del files...");
            filesProcessed = 0;
            fileCountTotal = SourceFiles.Count;

            foreach (FileInfo delFile in SourceFiles)
            {
                PatchLogger.LogInfo("::: Creating .del file :::");
                CreateDelFile(delFile.FullName);

                delCount++;

                AdditionalInfo[2].ItemValue = delCount;

                filesProcessed++;
                RaiseProgressChanged(filesProcessed, fileCountTotal, $"{delFile.FullName.Replace(SourceFolder, "...")}.del", AdditionalInfo.ToArray());
            }

            PatchLogger.LogInfo("::: Patch Generation Complete :::");

            return new PatchMessage("Generation Done", PatcherExitCode.Success);
        }

        /// <summary>
        /// Apply a set of patches using the source and delta folders specified during construction.
        /// </summary>
        /// <returns></returns>
        public PatchMessage ApplyPatches()
        {
            PatchLogger.LogInfo("::: Starting patch application :::");

            PatchLogger.LogOSInfo();

            //get needed directory information
            DirectoryInfo sourceDir = new DirectoryInfo(SourceFolder);
            DirectoryInfo deltaDir = new DirectoryInfo(DeltaFolder);

            //check directories exist
            if (!sourceDir.Exists)
            {
                string message = $"Could not find source directory: {sourceDir.FullName}";
                PatchLogger.LogError(message);
                return new PatchMessage(message, PatcherExitCode.MissingDir);
            }

            if(!deltaDir.Exists)
            {
                string message = $"Could not find delta directory: {deltaDir.FullName}";
                PatchLogger.LogError(message);
                return new PatchMessage(message, PatcherExitCode.MissingDir);
            }

            LazyOperations.ExtractResourcesToTempDir();

            List<FileInfo> SourceFiles = sourceDir.GetFiles("*", SearchOption.AllDirectories).ToList();

            List<FileInfo> deltaFiles = deltaDir.GetFiles("*", SearchOption.AllDirectories).ToList();

            deltaCount = deltaFiles.Where(x => x.Extension == ".delta").Count();
            newCount = deltaFiles.Where(x => x.Extension == ".new").Count();
            delCount = deltaFiles.Where(x => x.Extension == ".del").Count();

            PatchLogger.LogInfo($"Patch File Counts: DELTA({deltaCount}) - NEW({newCount}) - DEL({delCount})");

            AdditionalInfo = new List<LineItem>()
            {
                new LineItem("Patches Remaining", deltaCount),
                new LineItem("New Files to Add", newCount),
                new LineItem("Files to Delete", delCount)
            };

            filesProcessed = 0;

            fileCountTotal = deltaFiles.Count;

            foreach (FileInfo deltaFile in deltaDir.GetFiles("*", SearchOption.AllDirectories))
            {
                switch (deltaFile.Extension)
                {
                    case ".delta":
                        {
                            //apply delta
                            FileInfo sourceFile = SourceFiles.Find(f => f.FullName.Replace(sourceDir.FullName, "") == deltaFile.FullName.Replace(deltaDir.FullName, "").Replace(".delta", ""));

                            if (sourceFile == null)
                            {
                                return new PatchMessage($"Failed to find matching source file for '{deltaFile.FullName}'", PatcherExitCode.MissingFile);
                            }

                            PatchLogger.LogInfo("::: Applying Delta :::");
                            var result = ApplyDelta(sourceFile.FullName, deltaFile.FullName);

                            if(!result.Item1)
                            {
                                return new PatchMessage(result.Item2, PatcherExitCode.PatchFailed);
                            }

                            deltaCount--;

                            break;
                        }
                    case ".new":
                        {
                            //copy new file
                            string destination = Path.Join(sourceDir.FullName, deltaFile.FullName.Replace(deltaDir.FullName, "").Replace(".new", ""));

                            PatchLogger.LogInfo("::: Adding New File :::");

                            try
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                                File.Copy(deltaFile.FullName, destination, true);
                                PatchLogger.LogInfo($"File added: {destination}");
                            }
                            catch(Exception ex)
                            {
                                PatchLogger.LogException(ex);
                                return new PatchMessage(ex.Message, PatcherExitCode.PatchFailed);
                            }

                            newCount--;

                            break;
                        }
                    case ".del":
                        {
                            //remove unneeded file
                            string delFilePath = Path.Join(sourceDir.FullName, deltaFile.FullName.Replace(deltaDir.FullName, "").Replace(".del", ""));

                            PatchLogger.LogInfo("::: Removing Uneeded File :::");

                            try
                            {
                                File.Delete(delFilePath);
                                PatchLogger.LogInfo($"File removed: {delFilePath}");
                            }
                            catch(Exception ex)
                            {
                                PatchLogger.LogException(ex);
                                return new PatchMessage(ex.Message, PatcherExitCode.PatchFailed);
                            }

                            delCount--;

                            break;
                        }
                }

                AdditionalInfo[0].ItemValue = deltaCount;
                AdditionalInfo[1].ItemValue = newCount;
                AdditionalInfo[2].ItemValue = delCount;

                ++filesProcessed;
                RaiseProgressChanged(filesProcessed, fileCountTotal, deltaFile.Name, AdditionalInfo.ToArray());
            }

            PatchLogger.LogInfo("::: Patching Complete :::");
            return new PatchMessage($"Patching Complete. You can delete the patcher.exe file.", PatcherExitCode.Success);
        }
    }
}
