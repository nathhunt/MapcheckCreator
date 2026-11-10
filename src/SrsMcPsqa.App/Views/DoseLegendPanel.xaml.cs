using System.Windows.Controls;
using System.Windows.Input;

namespace SrsMcPsqa.App.Views
{
    /// <summary>Colour-wash bar plus exact-value boxes; Enter applies a typed value.</summary>
    public partial class DoseLegendPanel : UserControl
    {
        public DoseLegendPanel()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, new KeyEventHandler((s, e) =>
            {
                if (e.Key == Key.Enter && e.OriginalSource is TextBox tb)
                    tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }), true);
        }
    }
}
