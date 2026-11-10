using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SrsMcPsqa.App.Rendering;
using SrsMcPsqa.Core.Detector;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.App.Controls
{
    /// <summary>
    /// 2D array-plane view: dose colourwash, PTV outlines (solid), 100%-Rx isodose lines (dashed), the 77 mm array and
    /// usable square, diode dots and mm rulers. The array can be dragged when <see cref="AllowDrag"/> is set.
    /// Pure rendering and mouse handling; all logic lives in the view model.
    /// </summary>
    public sealed class PlaneView : FrameworkElement
    {
        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(nameof(Scene), typeof(PlaneScene), typeof(PlaneView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty OverlayProperty = DependencyProperty.Register(nameof(Overlay), typeof(PlaneOverlay), typeof(PlaneView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty DragCommandProperty = DependencyProperty.Register(nameof(DragCommand), typeof(ICommand), typeof(PlaneView));
        public static readonly DependencyProperty AllowDragProperty = DependencyProperty.Register(nameof(AllowDrag), typeof(bool), typeof(PlaneView),
            new FrameworkPropertyMetadata(false));
        /// <summary>Colour-wash lower level (% of the scene's reference dose); dose below it is not coloured.</summary>
        public static readonly DependencyProperty LowerPercentProperty = DependencyProperty.Register(nameof(LowerPercent), typeof(double), typeof(PlaneView),
            new FrameworkPropertyMetadata(DoseWash.DefaultLowerPercent, FrameworkPropertyMetadataOptions.AffectsRender));
        /// <summary>Colour-wash upper level (%); ≤ 0 = maximum dose on the plane.</summary>
        public static readonly DependencyProperty UpperPercentProperty = DependencyProperty.Register(nameof(UpperPercent), typeof(double), typeof(PlaneView),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public PlaneScene? Scene { get => (PlaneScene?)GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
        public PlaneOverlay? Overlay { get => (PlaneOverlay?)GetValue(OverlayProperty); set => SetValue(OverlayProperty, value); }
        public ICommand? DragCommand { get => (ICommand?)GetValue(DragCommandProperty); set => SetValue(DragCommandProperty, value); }
        public bool AllowDrag { get => (bool)GetValue(AllowDragProperty); set => SetValue(AllowDragProperty, value); }
        public double LowerPercent { get => (double)GetValue(LowerPercentProperty); set => SetValue(LowerPercentProperty, value); }
        public double UpperPercent { get => (double)GetValue(UpperPercentProperty); set => SetValue(UpperPercentProperty, value); }

        // Colour-wash bitmap for the current scene and levels.
        private (PlaneScene Scene, double Lower, double Upper)? _washKey;
        private System.Windows.Media.Imaging.BitmapSource? _wash;

        private System.Windows.Media.Imaging.BitmapSource? Wash(PlaneScene s, double lower, double upper)
        {
            if (s.DoseImage == null) return null;
            if (_washKey is { } k && ReferenceEquals(k.Scene, s) && k.Lower == lower && k.Upper == upper) return _wash;
            _wash = DoseWash.Render(s.DoseImage, s.ReferenceGy, lower, upper);
            _washKey = (s, lower, upper);
            return _wash;
        }

        private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A)));
        private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));
        private static readonly Brush DimText = Frozen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)));
        private static readonly Pen ArrayPen = Frozen(new Pen(Brushes.White, 1.6));
        private static readonly Pen UsablePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1) { DashStyle = DashStyles.Dash });
        private static readonly Pen GhostPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 200, 0)), 1.2) { DashStyle = DashStyles.Dot });
        private static readonly Pen TickPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1));
        private static readonly Typeface Face = new Typeface("Segoe UI");

        private Point? _dragStart;

        private double Scale => Scene == null ? 1 : Math.Max(1e-6, Math.Min(ActualWidth, ActualHeight) / (2 * Scene.HalfView));

        private Point ToScreen(double u, double v)
        {
            var s = Scene!;
            return new Point(ActualWidth / 2 + (u - s.ViewCentreU) * Scale, ActualHeight / 2 - (v - s.ViewCentreV) * Scale);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.PushClip(new RectangleGeometry(bounds));
            dc.DrawRectangle(Background, null, bounds);
            var s = Scene;
            if (s == null)
            {
                DrawText(dc, "Select a plan to see its array plane.", new Point(12, 12), 13, DimText);
                dc.Pop();
                return;
            }

            double lower = LowerPercent, upper = DoseWash.EffectiveUpper(UpperPercent, s);
            var wash = Wash(s, lower, upper);
            if (wash != null)
            {
                var a = ToScreen(s.DoseRect.Left, s.DoseRect.Bottom);
                var b = ToScreen(s.DoseRect.Right, s.DoseRect.Top);
                dc.DrawImage(wash, new Rect(a, b));
            }

            foreach (var layer in s.Layers)
            {
                var pen = new Pen(new SolidColorBrush(layer.Colour), layer.Dashed ? 1.3 : 1.8);
                if (layer.Dashed) pen.DashStyle = new DashStyle(new double[] { 3, 2 }, 0);
                pen.Freeze();
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    foreach (var seg in layer.Segments)
                    {
                        ctx.BeginFigure(ToScreen(seg.U1, seg.V1), false, false);
                        ctx.LineTo(ToScreen(seg.U2, seg.V2), true, false);
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }

            var o = Overlay;
            if (o != null) DrawArray(dc, s, o, lower, upper);
            DrawOrientationLabels(dc, s);

            DrawText(dc, s.Caption, new Point(10, 8), 12, TextBrush);
            DrawText(dc, s.Banner ?? (s.IsCalculated ? "CALCULATED" : "APPROXIMATION (clinical dose, before QA calculation)"),
                new Point(10, 26), 11, s.IsCalculated && s.Banner == null ? StatusBrush(true) : StatusBrush(false));
            if (s.DoseMaxGy > 0)
                DrawText(dc, string.Format(CultureInfo.InvariantCulture, "Colour wash {0:0}–{1:0}% (100% = {2:0.##} Gy) · solid = PTV · dashed = PTV 100% Rx",
                    lower, upper, s.ReferenceGy), new Point(10, ActualHeight - 22), 11, DimText);
            dc.Pop();
        }

        private void DrawArray(DrawingContext dc, PlaneScene s, PlaneOverlay o, double lower, double upper)
        {
            double h = DiodeArray.HalfExtent;
            if (o.GhostArray is { } g)
            {
                dc.DrawRectangle(null, GhostPen, new Rect(ToScreen(g.U - h, g.V + h), ToScreen(g.U + h, g.V - h)));
                var gc = ToScreen(g.U, g.V);
                dc.DrawLine(GhostPen, new Point(gc.X - 5, gc.Y), new Point(gc.X + 5, gc.Y));
                dc.DrawLine(GhostPen, new Point(gc.X, gc.Y - 5), new Point(gc.X, gc.Y + 5));
                DrawText(dc, "current", ToScreen(g.U - h, g.V + h) + new Vector(2, -15), 10, DimText);
            }
            dc.DrawRectangle(null, ArrayPen, new Rect(ToScreen(o.ArrayU - h, o.ArrayV + h), ToScreen(o.ArrayU + h, o.ArrayV - h)));
            dc.DrawRectangle(null, UsablePen, new Rect(ToScreen(o.ArrayU - o.UsableHalf, o.ArrayV + o.UsableHalf), ToScreen(o.ArrayU + o.UsableHalf, o.ArrayV - o.UsableHalf)));

            var c = ToScreen(o.ArrayU, o.ArrayV);
            dc.DrawLine(ArrayPen, new Point(c.X - 6, c.Y), new Point(c.X + 6, c.Y));
            dc.DrawLine(ArrayPen, new Point(c.X, c.Y - 6), new Point(c.X, c.Y + 6));

            if (o.ShowDiodes)
            {
                double r = Math.Max(0.8, 0.45 * Scale);
                double toPct = s.ReferenceGy > 0 ? 100 / s.ReferenceGy : 0;
                foreach (var d in o.Diodes)
                {
                    var colour = DoseWash.Colour(d.DoseGy * toPct, lower, upper, 255);
                    if (colour.A == 0) colour = Color.FromArgb(150, 200, 200, 200);
                    var brush = new SolidColorBrush(colour);
                    brush.Freeze();
                    dc.DrawEllipse(brush, null, ToScreen(d.U, d.V), r, r);
                }
            }

            // mm rulers along the bottom and left array edges, relative to the array centre.
            for (int k = -30; k <= 30; k += 10)
            {
                var pb = ToScreen(o.ArrayU + k, o.ArrayV - h);
                dc.DrawLine(TickPen, pb, new Point(pb.X, pb.Y + 5));
                DrawText(dc, k.ToString(CultureInfo.InvariantCulture), new Point(pb.X - 6, pb.Y + 5), 10, DimText);
                var pl = ToScreen(o.ArrayU - h, o.ArrayV + k);
                dc.DrawLine(TickPen, pl, new Point(pl.X - 5, pl.Y));
                DrawText(dc, k.ToString(CultureInfo.InvariantCulture), new Point(pl.X - 26, pl.Y - 7), 10, DimText);
            }
        }

        private void DrawOrientationLabels(DrawingContext dc, PlaneScene s)
        {
            // 0°: u = patient X (Left = +X on the right). 270°: u = patient Y (Post = +Y on the right). v = Z (Sup up).
            string left = s.Orientation == ArrayOrientation.Deg0 ? "R" : "A";
            string right = s.Orientation == ArrayOrientation.Deg0 ? "L" : "P";
            DrawText(dc, left, new Point(6, ActualHeight / 2 - 8), 14, TextBrush);
            DrawText(dc, right, new Point(ActualWidth - 16, ActualHeight / 2 - 8), 14, TextBrush);
            DrawText(dc, "S", new Point(ActualWidth / 2 - 4, 44), 14, TextBrush);
            DrawText(dc, "I", new Point(ActualWidth / 2 - 2, ActualHeight - 42), 14, TextBrush);
        }

        private void DrawText(DrawingContext dc, string text, Point at, double size, Brush brush)
        {
            if (string.IsNullOrEmpty(text)) return;
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, at);
        }

        private static Brush StatusBrush(bool calculated) => calculated ? Infrastructure.StatusBrushConverter.Good : Infrastructure.StatusBrushConverter.Caution;

        // ------------------------------------------------------------------ dragging
        private bool OverArray(Point p)
        {
            var o = Overlay;
            if (Scene == null || o == null) return false;
            var a = ToScreen(o.ArrayU - DiodeArray.HalfExtent, o.ArrayV + DiodeArray.HalfExtent);
            var b = ToScreen(o.ArrayU + DiodeArray.HalfExtent, o.ArrayV - DiodeArray.HalfExtent);
            return new Rect(a, b).Contains(p);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var p = e.GetPosition(this);
            if (!AllowDrag || !OverArray(p)) return;
            _dragStart = p;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.GetPosition(this);
            Cursor = AllowDrag && (_dragStart != null || OverArray(p)) ? Cursors.SizeAll : Cursors.Arrow;
            if (_dragStart == null) return;
            Send(p, false);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_dragStart == null) return;
            Send(e.GetPosition(this), true);
            _dragStart = null;
            ReleaseMouseCapture();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_dragStart != null) { Send(Mouse.GetPosition(this), true); _dragStart = null; }
        }

        private void Send(Point p, bool completed)
        {
            var start = _dragStart!.Value;
            // Snap to 0.5 mm.
            double du = Math.Round((p.X - start.X) / Scale * 2) / 2;
            double dv = Math.Round(-(p.Y - start.Y) / Scale * 2) / 2;
            var args = new PlaneDragArgs(du, dv, completed);
            if (DragCommand?.CanExecute(args) == true) DragCommand.Execute(args);
        }

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
