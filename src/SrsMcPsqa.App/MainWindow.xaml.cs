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
            FillWorkArea();
        }

        /// <summary>
        /// Start filling the screen's work area (above the taskbar). Starting with WindowState = Maximized makes the
        /// borderless FluentWindow overhang the screen edges, cutting off the content and the title bar buttons.
        /// </summary>
        private void FillWorkArea()
        {
            var wa = SystemParameters.WorkArea;
            if (wa.Width < MinWidth || wa.Height < MinHeight)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }
            Left = wa.Left;
            Top = wa.Top;
            Width = wa.Width;
            Height = wa.Height;
        }
    }
}
