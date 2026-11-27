using System;
using System.Windows;
using System.Windows.Threading;

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>
    /// Fits a dialog's height to its content. SizeToContent on a WPF-UI FluentWindow (content extended into the title bar)
    /// leaves extra empty space at the bottom, more the longer the wrapped text: the content is measured before the window
    /// has its final width and chrome. After the first render this measures the content again at its real width and sets
    /// the height to fit (keeping the dialog centred where it was). Call once, after InitializeComponent.
    /// </summary>
    public static class DialogSizing
    {
        public static void FitHeightToContent(Window window, FrameworkElement root, params System.Windows.Controls.Expander[] growing)
        {
            window.ContentRendered += (_, __) => Fit(window, root);
            // An expander that opens or closes changes the content height: fit again once the layout has updated.
            foreach (var e in growing)
            {
                e.Expanded += (_, __) => Refit(window, root);
                e.Collapsed += (_, __) => Refit(window, root);
            }
        }

        private static void Refit(Window window, FrameworkElement root) =>
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Fit(window, root)));

        private static void Fit(Window window, FrameworkElement root)
        {
            if (root.ActualWidth <= 0 || window.ActualHeight <= 0) return;
            root.Measure(new Size(root.ActualWidth, double.PositiveInfinity));
            double chrome = Math.Max(0, window.ActualHeight - root.ActualHeight); // frame outside the content, if any
            double want = Math.Ceiling(root.DesiredSize.Height + chrome);
            if (!double.IsInfinity(window.MaxHeight)) want = Math.Min(want, window.MaxHeight);
            if (Math.Abs(want - window.ActualHeight) < 1) return;
            double centreY = window.Top + window.ActualHeight / 2;
            window.SizeToContent = SizeToContent.Manual;
            window.Height = want;
            if (!double.IsNaN(centreY)) window.Top = centreY - want / 2;
        }
    }
}
