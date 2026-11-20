using System;
using System.Linq;
using System.Windows;
using SrsMcPsqa.App.Views;

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>Error, confirmation and information dialogs (Fluent styled, with "copy details").</summary>
    public interface IDialogService
    {
        void ShowError(string title, string message, Exception? exception = null);
        void ShowInfo(string title, string message);
        bool Confirm(string title, string message, string confirmText = "OK", bool danger = false);

        /// <summary>"Change plans" dialog for the planning screen; true = re-optimise.</summary>
        bool ShowChangePlans(object planningViewModel);
    }

    public sealed class DialogService : IDialogService
    {
        /// <summary>Active visible window, else the visible main window, else none (an owner must already be shown).</summary>
        private static Window? Owner
        {
            get
            {
                var app = Application.Current;
                if (app == null) return null;
                return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
                       ?? (app.MainWindow is Window m && m.IsVisible ? m : null);
            }
        }

        public void ShowError(string title, string message, Exception? exception = null)
        {
            Log.Error($"{title}: {message}", exception);
            var details = exception == null ? message : $"{message}{Environment.NewLine}{Environment.NewLine}{exception}";
            new MessageDialog(title, message, details, MessageDialogKind.Error, "OK", null) { Owner = Owner }.ShowDialog();
        }

        public void ShowInfo(string title, string message)
        {
            Log.Info($"Dialog shown: {title}: {message.Replace(Environment.NewLine, " | ")}");
            new MessageDialog(title, message, null, MessageDialogKind.Info, "OK", null) { Owner = Owner }.ShowDialog();
        }

        public bool ShowChangePlans(object planningViewModel) =>
            new ChangePlansDialog { DataContext = planningViewModel, Owner = Owner }.ShowDialog() == true;

        public bool Confirm(string title, string message, string confirmText = "OK", bool danger = false)
        {
            var d = new MessageDialog(title, message, null, danger ? MessageDialogKind.Warning : MessageDialogKind.Question, confirmText, "Cancel") { Owner = Owner };
            bool ok = d.ShowDialog() == true;
            Log.Info($"Confirm '{title}': {(ok ? confirmText : "Cancel")}. Message: {message.Replace(Environment.NewLine, " | ")}");
            return ok;
        }
    }
}
