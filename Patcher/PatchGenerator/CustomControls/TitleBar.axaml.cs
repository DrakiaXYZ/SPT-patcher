using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.Windows.Input;

namespace PatchGenerator.CustomControls
{
    public partial class TitleBar : UserControl
    {
        public TitleBar()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public static readonly StyledProperty<string> TitleProperty =
            AvaloniaProperty.Register<TitleBar, string>(nameof(Title));

        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly StyledProperty<IBrush> XButtonForegroundProperty =
            AvaloniaProperty.Register<TitleBar, IBrush>(nameof(XButtonForeground));

        public IBrush XButtonForeground
        {
            get => GetValue(XButtonForegroundProperty);
            set => SetValue(XButtonForegroundProperty, value);
        }

        public static new readonly StyledProperty<IBrush> ForegroundProperty =
            AvaloniaProperty.Register<TitleBar, IBrush>(nameof(Foreground));

        public new IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static new readonly StyledProperty<IBrush> BackgroundProperty =
            AvaloniaProperty.Register<TitleBar, IBrush>(nameof(Background));

        public new IBrush Background
        {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        //Close Button Command (X Button) Property
        public static readonly StyledProperty<ICommand> XButtonCommandProperty =
            AvaloniaProperty.Register<TitleBar, ICommand>(nameof(XButtonCommand));

        public ICommand XButtonCommand
        {
            get => GetValue(XButtonCommandProperty);
            set => SetValue(XButtonCommandProperty, value);
        }
    }
}
