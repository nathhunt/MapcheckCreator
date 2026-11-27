using System;
using System.Windows;
using Wpf.Ui.Controls;

namespace SrsMcPsqa.App.Views
{
    public enum MessageDialogKind { Info, Question, Warning, Error }

    /// <summary>Simple Fluent message box with optional details and "Copy details". Pure view code.</summary>
    public partial class MessageDialog : FluentWindow
    {
        private readonly string? _details;

        public MessageDialog(string title, string message, string? details, MessageDialogKind kind, string okText, string? cancelText)
        {
            InitializeComponent();
            Title = title;
            TitleBarControl.Title = title;
            MessageText.Text = message;
            _details = details;
            OkButton.Content = okText;
            if (cancelText == null) CancelButton.Visibility = Visibility.Collapsed;
            else CancelButton.Content = cancelText;
            if (!string.IsNullOrEmpty(details) && details != message)
            {
                DetailsExpander.Visibility = Visibility.Visible;
                DetailsText.Text = details;
            }
            if (!string.IsNullOrEmpty(details)) CopyButton.Visibility = Visibility.Visible;
            KindIcon.Symbol = kind switch
            {
                MessageDialogKind.Error => SymbolRegular.ErrorCircle24,
                MessageDialogKind.Warning => SymbolRegular.Warning24,
                MessageDialogKind.Question => SymbolRegular.QuestionCircle24,
                _ => SymbolRegular.Info24,
            };
            if (kind == MessageDialogKind.Warning) OkButton.Appearance = ControlAppearance.Danger;
            Infrastructure.DialogSizing.FitHeightToContent(this, RootGrid, DetailsExpander);
        }

        private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(_details ?? MessageText.Text); }
            catch (Exception) { /* clipboard can be locked on Citrix; the details are also in the log */ }
        }
    }
}
