using System;
using System.Windows;
using Wpf.Ui.Controls;

namespace SrsMcPsqa.App
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            // Restore size: the design size, or 90 % of the work area on a smaller screen.
            var wa = SystemParameters.WorkArea;
            Width = Math.Min(Width, wa.Width * 0.9);
            Height = Math.Min(Height, wa.Height * 0.9);
            MinWidth = Math.Min(MinWidth, Width);
            MinHeight = Math.Min(MinHeight, Height);

            // Open maximised. WPF-UI's ClientAreaBorder adds the padding that keeps a maximised borderless window inside the
            // screen only when the window state *changes*; a window created maximised (WindowState in XAML) never gets it and
            // overhangs the screen edges. So start normal and maximise once loaded: the state change applies the padding,
            // the title bar shows the Restore button, and Restore returns to the size above.
            Loaded += (_, __) => WindowState = WindowState.Maximized;
        }
    }
}
