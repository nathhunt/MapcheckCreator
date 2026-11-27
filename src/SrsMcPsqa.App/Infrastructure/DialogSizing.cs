using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>
    /// Fits a dialog's height to its content. SizeToContent on a WPF-UI FluentWindow (WindowChrome with no caption and the
    /// glass frame extended, content drawn into the title bar) leaves empty space below the content: the window's size is
    /// worked out for a frame it no longer has, and with the text measured before the final width is known.
    /// After the first render this finds where the content really ends inside the window (its top in the window plus its
    /// height measured at the real width) and removes everything below that, whatever the cause. Logged, so a dialog that
    /// still doesn't fit can be diagnosed from the log. Call once, after InitializeComponent.
    /// </summary>
    public static class DialogSizing
    {
        public static void FitHeightToContent(Window window, FrameworkElement root, params Expander[] growing)
        {
            window.ContentRendered += (_, __) =>
            {
                Fit(window, root, log: true);
                // Once more when everything has settled (templates, fonts, the title bar).
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => Fit(window, root, log: false)));
            };
            foreach (var e in growing)
            {
                e.Expanded += (_, __) => Refit(window, root);
                e.Collapsed += (_, __) => Refit(window, root);
            }
        }

        private static void Refit(Window window, FrameworkElement root) =>
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Fit(window, root, log: false)));

        private static void Fit(Window window, FrameworkElement root, bool log)
        {
            try
            {
                if (!window.IsLoaded || root.ActualWidth <= 0 || window.ActualHeight <= 0) return;
                // The window's client area: its top visual (the template root) fills it.
                var client = VisualTreeHelper.GetChildrenCount(window) > 0 ? VisualTreeHelper.GetChild(window, 0) as FrameworkElement : null;
                double clientHeight = client?.ActualHeight ?? window.ActualHeight;

                // Content height at its real width, without the space it was given.
                root.Measure(new Size(root.ActualWidth, double.PositiveInfinity));
                double contentHeight = root.DesiredSize.Height;
                // Where that content ends in the client area (the root's own margin is part of DesiredSize).
                double top = client != null ? root.TranslatePoint(new Point(0, 0), client).Y - root.Margin.Top : 0;
                double contentBottom = top + contentHeight;
                double emptyBelow = clientHeight - contentBottom;
                double want = Math.Ceiling(window.ActualHeight - emptyBelow);
                if (!double.IsInfinity(window.MaxHeight)) want = Math.Min(want, window.MaxHeight);
                // WPF-UI's FluentWindow style has MinHeight 320; dialogs set MinHeight="0" so short ones aren't held at that.
                want = Math.Max(want, window.MinHeight);

                if (log)
                    Log.Info(string.Format(CultureInfo.InvariantCulture,
                        "Dialog '{0}' fit: window {1:0.#}, client {2:0.#}, content top {3:0.#} + height {4:0.#} (laid out {5:0.#}), min {7:0.#} → height {6:0.#}.",
                        window.Title, window.ActualHeight, clientHeight, top, contentHeight, root.ActualHeight, want, window.MinHeight));
                if (want <= 0 || Math.Abs(want - window.ActualHeight) < 1) return;

                double centreY = window.Top + window.ActualHeight / 2;
                window.SizeToContent = SizeToContent.Manual;
                window.Height = want;
                if (!double.IsNaN(centreY)) window.Top = centreY - want / 2;
            }
            catch (Exception ex)
            {
                Log.Warn("Fitting a dialog to its content failed: " + ex.Message);
            }
        }
    }
}
