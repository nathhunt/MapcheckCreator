using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SrsMcPsqa.App.Rendering;

namespace SrsMcPsqa.App.Controls
{
    /// <summary>
    /// Vertical dose colour-wash bar with two draggable levels, like Eclipse's "Dose Color Wash" bar: dose below the lower
    /// level isn't coloured, dose above the upper level gets the top colour. Values are % of the scene's reference dose.
    /// Drag a handle (snaps to 1 %); double-click resets to the defaults (lower 20 %, upper = maximum).
    /// </summary>
    public sealed class DoseLegend : FrameworkElement
    {
        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(nameof(Scene), typeof(PlaneScene), typeof(DoseLegend),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LowerPercentProperty = DependencyProperty.Register(nameof(LowerPercent), typeof(double), typeof(DoseLegend),
            new FrameworkPropertyMetadata(DoseWash.DefaultLowerPercent, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty UpperPercentProperty = DependencyProperty.Register(nameof(UpperPercent), typeof(double), typeof(DoseLegend),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public PlaneScene? Scene { get => (PlaneScene?)GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
        public double LowerPercent { get => (double)GetValue(LowerPercentProperty); set => SetValue(LowerPercentProperty, value); }
        /// <summary>≤ 0 = maximum dose on the plane (auto).</summary>
        public double UpperPercent { get => (double)GetValue(UpperPercentProperty); set => SetValue(UpperPercentProperty, value); }

        private const double Top = 34, Bottom = 30, BarX = 22, BarW = 16;
        private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A)));
        private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));
        private static readonly Brush DimText = Frozen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)));
        private static readonly Brush BelowBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)));
        private static readonly Brush HandleBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)));
        private static readonly Pen HandlePen = Frozen(new Pen(Brushes.Black, 1));
        private static readonly Pen TickPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1));
        private static readonly Typeface Face = new Typeface("Segoe UI");

        private enum Handle { None, Lower, Upper }
        private Handle _dragging;

        public DoseLegend()
        {
            Width = 92;
            MinHeight = 200;
            ToolTip = "Drag the arrows to change the colour wash levels. Double-click to reset.";
        }

        private double Upper => DoseWash.EffectiveUpper(UpperPercent, Scene);

        /// <summary>Top of the scale (%): a little above the maximum dose (fixed while dragging, so the scale doesn't move).</summary>
        private double ScaleMax => Math.Ceiling(Math.Max(Scene?.MaxPercent ?? 100, 100) * 1.1 / 10) * 10;

        private double Y(double percent) => Top + (1 - percent / ScaleMax) * Math.Max(1, ActualHeight - Top - Bottom);
        private double Percent(double y) => (1 - (y - Top) / Math.Max(1, ActualHeight - Top - Bottom)) * ScaleMax;

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(Background, null, bounds);
            DrawText(dc, "Dose [%]", new Point(6, 4), 12, TextBrush, bold: true);
            if (Scene?.DoseImage == null || Scene.ReferenceGy <= 0)
            {
                DrawText(dc, "no dose", new Point(6, 22), 11, DimText);
                return;
            }

            double lower = LowerPercent, upper = Upper, max = ScaleMax;
            double yTop = Y(max), yBottom = Y(0);

            // Below the lower level: grey (not coloured); between: ramp; above the upper level: top colour.
            dc.DrawRectangle(BelowBrush, null, new Rect(BarX, Y(Math.Min(lower, max)), BarW, Math.Max(0, yBottom - Y(Math.Min(lower, max)))));
            var ramp = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
            for (int i = 0; i <= 10; i++) ramp.GradientStops.Add(new GradientStop(ColourMap.Ramp(i / 10.0, 255), i / 10.0));
            ramp.Freeze();
            double yLow = Y(lower), yUp = Y(Math.Min(upper, max));
            if (yLow > yUp) dc.DrawRectangle(ramp, null, new Rect(BarX, yUp, BarW, yLow - yUp));
            var topBrush = new SolidColorBrush(ColourMap.Ramp(1, 255)); topBrush.Freeze();
            if (yUp > yTop) dc.DrawRectangle(topBrush, null, new Rect(BarX, yTop, BarW, yUp - yTop));

            // Scale.
            double step = max > 300 ? 50 : max > 150 ? 20 : 10;
            for (double p = 0; p <= max + 1e-6; p += step)
            {
                double y = Y(p);
                dc.DrawLine(TickPen, new Point(BarX + BarW, y), new Point(BarX + BarW + 4, y));
                DrawText(dc, p.ToString("0", CultureInfo.InvariantCulture), new Point(BarX + BarW + 6, y - 7), 10, DimText);
            }

            DrawHandle(dc, yUp, upper);
            DrawHandle(dc, yLow, lower);

            DrawText(dc, string.Format(CultureInfo.InvariantCulture, "100% = {0:0.##} Gy", Scene.ReferenceGy), new Point(4, ActualHeight - Bottom + 2), 10, DimText);
            DrawText(dc, string.Format(CultureInfo.InvariantCulture, "max {0:0.0}%", Scene.MaxPercent), new Point(4, ActualHeight - Bottom + 15), 10, DimText);
        }

        private void DrawHandle(DrawingContext dc, double y, double value)
        {
            // Triangle pointing at the bar, value label to its left.
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(BarX - 1, y), true, true);
                ctx.LineTo(new Point(BarX - 10, y - 6), true, false);
                ctx.LineTo(new Point(BarX - 10, y + 6), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(HandleBrush, HandlePen, geo);
            dc.DrawLine(HandlePen, new Point(BarX, y), new Point(BarX + BarW, y));
            DrawText(dc, value.ToString("0", CultureInfo.InvariantCulture), new Point(BarX + BarW + 26, y - 7), 11, TextBrush, bold: true);
        }

        private void DrawText(DrawingContext dc, string text, Point at, double size, Brush brush, bool bold = false)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                bold ? new Typeface(Face.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : Face, size, brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, at);
        }

        // ------------------------------------------------------------------ dragging
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (Scene?.DoseImage == null) return;
            if (e.ClickCount == 2)
            {
                LowerPercent = DoseWash.DefaultLowerPercent;
                UpperPercent = 0;
                e.Handled = true;
                return;
            }
            double y = e.GetPosition(this).Y;
            double dUp = Math.Abs(y - Y(Math.Min(Upper, ScaleMax))), dLow = Math.Abs(y - Y(LowerPercent));
            if (Math.Min(dUp, dLow) > 10) return;
            _dragging = dUp <= dLow ? Handle.Upper : Handle.Lower;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var y = e.GetPosition(this).Y;
            bool near = Scene?.DoseImage != null &&
                        (Math.Abs(y - Y(Math.Min(Upper, ScaleMax))) <= 10 || Math.Abs(y - Y(LowerPercent)) <= 10);
            Cursor = _dragging != Handle.None || near ? Cursors.SizeNS : Cursors.Arrow;
            if (_dragging == Handle.None) return;
            double p = Math.Round(Math.Max(0, Math.Min(ScaleMax, Percent(y))));
            if (_dragging == Handle.Lower) LowerPercent = Math.Min(p, Upper - 1);
            else UpperPercent = Math.Max(p, LowerPercent + 1);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_dragging == Handle.None) return;
            _dragging = Handle.None;
            ReleaseMouseCapture();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            _dragging = Handle.None;
        }

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
