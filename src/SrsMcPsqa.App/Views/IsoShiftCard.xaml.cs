using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.App.Views
{
    /// <summary>One axis of the shift, e.g. "Left", "3.5 mm", "(0.35 cm)".</summary>
    public sealed class IsoShiftRow
    {
        public IsoShiftRow(string direction, string mm, string cm) { Direction = direction; Mm = mm; Cm = cm; }
        public string Direction { get; }
        public string Mm { get; }
        public string Cm { get; }
    }

    /// <summary>Large, axis-by-axis display of a QA isocentre shift relative to the MapCHECK user origin (mm and cm, as in Eclipse).</summary>
    public partial class IsoShiftCard : UserControl
    {
        public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(nameof(Offset), typeof(Vec3?), typeof(IsoShiftCard),
            new PropertyMetadata(null, (d, _) => ((IsoShiftCard)d).Update()));
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(IsoShiftCard),
            new PropertyMetadata("QA isocentre"));
        private static readonly DependencyPropertyKey RowsKey = DependencyProperty.RegisterReadOnly(nameof(Rows), typeof(IReadOnlyList<IsoShiftRow>),
            typeof(IsoShiftCard), new PropertyMetadata(null));
        public static readonly DependencyProperty RowsProperty = RowsKey.DependencyProperty;

        public Vec3? Offset { get => (Vec3?)GetValue(OffsetProperty); set => SetValue(OffsetProperty, value); }
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public IReadOnlyList<IsoShiftRow>? Rows => (IReadOnlyList<IsoShiftRow>?)GetValue(RowsProperty);

        public IsoShiftCard() { InitializeComponent(); }

        private void Update()
        {
            var rows = new List<IsoShiftRow>();
            if (Offset is Vec3 o)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    double v = o[axis];
                    if (System.Math.Abs(v) < 0.05) v = 0;
                    rows.Add(new IsoShiftRow(ShiftLabels.Direction(axis, v),
                        System.Math.Abs(v).ToString("0.0", CultureInfo.InvariantCulture) + " mm",
                        "(" + (System.Math.Abs(v) / 10).ToString("0.00", CultureInfo.InvariantCulture) + " cm)"));
                }
            }
            SetValue(RowsKey, rows);
        }
    }
}
