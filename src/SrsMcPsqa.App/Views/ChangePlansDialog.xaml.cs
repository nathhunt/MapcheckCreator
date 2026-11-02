using System.Windows;
using Wpf.Ui.Controls;

namespace SrsMcPsqa.App.Views
{
    /// <summary>Dialog for user constraints on the planning screen (DataContext: PlanningViewModel).</summary>
    public partial class ChangePlansDialog : FluentWindow
    {
        public ChangePlansDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
