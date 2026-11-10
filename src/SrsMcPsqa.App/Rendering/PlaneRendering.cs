using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SrsMcPsqa.Core.Detector;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Rendering;

namespace SrsMcPsqa.App.Rendering
{
    /// <summary>Outline or isodose line set for one PTV.</summary>
    public sealed class ContourLayer
    {
        public ContourLayer(Color colour, IReadOnlyList<Segment2> segments, bool dashed, string label)
        {
            Colour = colour; Segments = segments; Dashed = dashed; Label = label;
        }

        public Color Colour { get; }
        public IReadOnlyList<Segment2> Segments { get; }
        public bool Dashed { get; }
        public string Label { get; }
    }

    /// <summary>
    /// Static part of the 2D array-plane view at one depth: dose colourwash, PTV outlines and each PTV's 100%-Rx isodose.
    /// Plane coordinates (u, v) in mm; u to the right, v (superior) up.
    /// </summary>
    public sealed class PlaneScene
    {
        public ArrayOrientation Orientation { get; set; }
        public double Depth { get; set; }
        public double ViewCentreU { get; set; }
        public double ViewCentreV { get; set; }
        public double HalfView { get; set; }
        /// <summary>Dose sampled on the plane (Gy); coloured by the view with the current colour-wash levels.</summary>
        public PlaneImage? DoseImage { get; set; }
        /// <summary>Plane-coordinate rectangle covered by <see cref="DoseImage"/> (outer pixel edges).</summary>
        public Rect DoseRect { get; set; }
        public double DoseMaxGy { get; set; }
        /// <summary>Dose shown as 100 % in the colour wash (plan prescription; per fraction for QA dose).</summary>
        public double ReferenceGy { get; set; }
        public double MaxPercent => ReferenceGy > 0 ? 100 * DoseMaxGy / ReferenceGy : 0;
        public IReadOnlyList<ContourLayer> Layers { get; set; } = Array.Empty<ContourLayer>();
        public bool IsCalculated { get; set; }
        public string Caption { get; set; } = "";
        /// <summary>Dose used for diode colouring (patient frame); may be null.</summary>
        public DoseGrid? Dose { get; set; }
    }

    public readonly struct DiodeDot
    {
        public DiodeDot(double u, double v, double doseGy) { U = u; V = v; DoseGy = doseGy; }
        public double U { get; }
        public double V { get; }
        /// <summary>Dose at the diode (Gy), NaN when there is no dose.</summary>
        public double DoseGy { get; }
    }

    /// <summary>Array position on the plane (moves with nudges and dragging).</summary>
    public sealed class PlaneOverlay
    {
        public double ArrayU { get; set; }
        public double ArrayV { get; set; }
        public double UsableHalf { get; set; }
        public IReadOnlyList<DiodeDot> Diodes { get; set; } = Array.Empty<DiodeDot>();
        public bool ShowDiodes { get; set; } = true;
    }

    /// <summary>Drag gesture from <see cref="Controls.PlaneView"/>: total in-plane movement since the drag started.</summary>
    public sealed class PlaneDragArgs
    {
        public PlaneDragArgs(double du, double dv, bool completed) { DU = du; DV = dv; Completed = completed; }
        public double DU { get; }
        public double DV { get; }
        public bool Completed { get; }
    }

    /// <summary>Colour-wash window in % of the reference dose, like Eclipse's dose colour wash bar.</summary>
    public static class DoseWash
    {
        public const double DefaultLowerPercent = 20;

        /// <summary>Effective upper level: <paramref name="upperPercent"/> ≤ 0 means "maximum dose on the plane".</summary>
        public static double EffectiveUpper(double upperPercent, PlaneScene? scene) =>
            upperPercent > 0 ? upperPercent : Math.Max(scene?.MaxPercent ?? 100, 1);

        /// <summary>Colour for a dose in % of the reference; transparent below the lower level, top colour above the upper.</summary>
        public static Color Colour(double percent, double lower, double upper, byte alpha = 165)
        {
            if (double.IsNaN(percent) || percent < lower) return Colors.Transparent;
            double t = upper > lower ? (percent - lower) / (upper - lower) : 1;
            return ColourMap.Ramp(Math.Max(0, Math.Min(1, t)), alpha);
        }

        public static BitmapSource Render(PlaneImage img, double referenceGy, double lower, double upper)
        {
            int w = img.Width, h = img.Height;
            var pixels = new byte[w * h * 4];
            double toPct = referenceGy > 0 ? 100 / referenceGy : 0;
            for (int l = 0; l < h; l++)
            {
                int row = h - 1 - l; // bitmap row 0 = top = highest v
                for (int i = 0; i < w; i++)
                {
                    var c = Colour(img[i, l] * toPct, lower, upper);
                    int k = (row * w + i) * 4;
                    // Premultiplied BGRA.
                    pixels[k] = (byte)(c.B * c.A / 255);
                    pixels[k + 1] = (byte)(c.G * c.A / 255);
                    pixels[k + 2] = (byte)(c.R * c.A / 255);
                    pixels[k + 3] = c.A;
                }
            }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
            bmp.Freeze();
            return bmp;
        }
    }

    public static class ColourMap
    {
        /// <summary>Eclipse-like ramp from blue (t = 0) through cyan, green and yellow to red (t = 1).</summary>
        public static Color Ramp(double t, byte alpha = 165)
        {
            (double, byte, byte, byte)[] stops =
            {
                (0.0, 0, 0, 255), (0.25, 0, 200, 255), (0.5, 0, 220, 0), (0.75, 255, 230, 0), (1.0, 255, 0, 0),
            };
            for (int i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].Item1) continue;
                var a = stops[i - 1]; var b = stops[i];
                double f = (t - a.Item1) / (b.Item1 - a.Item1);
                return Color.FromArgb(alpha, Lerp(a.Item2, b.Item2, f), Lerp(a.Item3, b.Item3, f), Lerp(a.Item4, b.Item4, f));
            }
            return Color.FromArgb(alpha, 255, 0, 0);
        }

        private static byte Lerp(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);
    }

    /// <summary>Builds <see cref="PlaneScene"/> and <see cref="PlaneOverlay"/> from Core data. Thread-safe (bitmaps are frozen).</summary>
    public static class PlaneRenderBuilder
    {
        public const double DefaultHalfView = 50.0;
        private const double Pixel = 0.5;

        public static PlaneScene BuildScene(ArrayOrientation o, double depth, double viewU, double viewV, DoseGrid? dose,
            IReadOnlyList<(PlanningTarget Target, Color Colour)> targets, bool calculated, string caption, double? referenceGy = null,
            double halfView = DefaultHalfView)
        {
            var scene = new PlaneScene
            {
                Orientation = o, Depth = depth, ViewCentreU = viewU, ViewCentreV = viewV, HalfView = halfView,
                IsCalculated = calculated, Caption = caption, Dose = dose,
            };
            PlaneImage? img = null;
            if (dose != null)
            {
                img = PlaneSampler.SampleDose(dose, o, depth, viewU, viewV, halfView, Pixel);
                scene.DoseMaxGy = img.Max();
                scene.DoseImage = img;
                scene.ReferenceGy = referenceGy is double r && r > 0 ? r : scene.DoseMaxGy;
                scene.DoseRect = new Rect(img.U0 - Pixel / 2, img.V0 - Pixel / 2, img.Width * Pixel, img.Height * Pixel);
            }

            var layers = new List<ContourLayer>();
            foreach (var (t, colour) in targets)
            {
                var b = t.Mask.Bounds;
                if (o.N(b.Min) > depth || o.N(b.Max) < depth) continue;
                double cu = (o.U(b.Min) + o.U(b.Max)) / 2, cv = (b.Min.Z + b.Max.Z) / 2;
                double half = Math.Max(o.U(b.Max) - o.U(b.Min), b.Max.Z - b.Min.Z) / 2 + 3;
                var mask = PlaneSampler.SampleMask(t.Mask, o, depth, cu, cv, half, 0.25);
                var outline = PlaneSampler.Isolines(mask, 0.5);
                if (outline.Count > 0) layers.Add(new ContourLayer(colour, outline, false, t.Id));

                if (img != null)
                {
                    double margin = 8;
                    var iso = PlaneSampler.Isolines(img, t.RxGy)
                        .Where(s => Math.Abs((s.U1 + s.U2) / 2 - cu) <= half + margin && Math.Abs((s.V1 + s.V2) / 2 - cv) <= half + margin)
                        .ToList();
                    if (iso.Count > 0) layers.Add(new ContourLayer(colour, iso, true, $"{t.Id} 100% ({t.RxGy:0.##} Gy)"));
                }
            }
            scene.Layers = layers;
            return scene;
        }

        public static PlaneOverlay BuildOverlay(PlaneScene? scene, double arrayU, double arrayV, double usableHalf, bool showDiodes)
        {
            var dots = new List<DiodeDot>(DiodeArray.Count);
            if (showDiodes)
            {
                var o = scene?.Orientation ?? ArrayOrientation.Deg0;
                foreach (var d in DiodeArray.Positions)
                {
                    double u = arrayU + d.U, v = arrayV + d.V;
                    double gy = scene?.Dose != null ? scene.Dose.Sample(o.FromPlane(u, v, scene.Depth)) : double.NaN;
                    dots.Add(new DiodeDot(u, v, gy));
                }
            }
            return new PlaneOverlay { ArrayU = arrayU, ArrayV = arrayV, UsableHalf = usableHalf, Diodes = dots, ShowDiodes = showDiodes };
        }
    }
}
