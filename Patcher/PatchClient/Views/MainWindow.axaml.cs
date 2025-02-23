using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using PatchClient.ViewModels;
using ReactiveUI;

namespace PatchClient.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.WhenActivated(disposables => { });
            AvaloniaXamlLoader.Load(this);
        }
    }
}
