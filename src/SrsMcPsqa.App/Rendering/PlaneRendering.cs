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
        public BitmapSource? DoseBitmap { get; set; }
        /// <summary>Plane-coordinate rectangle covered by <see cref="DoseBitmap"/> (outer pixel edges).</summary>
        public Rect DoseRect { get; set; }
        public double DoseMaxGy { get; set; }
        public IReadOnlyList<ContourLayer> Layers { get; set; } = Array.Empty<ContourLayer>();
        public bool IsCalculated { get; set; }
        public string Caption { get; set; } = "";
        /// <summary>Dose used for diode colouring (patient frame); may be null.</summary>
        public DoseGrid? Dose { get; set; }
    }

    public readonly struct DiodeDot
    {
        public DiodeDot(double u, double v, Color colour) { U = u; V = v; Colour = colour; }
        public double U { get; }
        public double V { get; }
        public Color Colour { get; }
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

    public static class ColourMap
    {
        /// <summary>Rainbow colourwash; transparent below 5% of max.</summary>
        public static Color Dose(double fraction, byte alpha = 165)
        {
            if (double.IsNaN(fraction) || fraction < 0.05) return Colors.Transparent;
            double t = Math.Min(1, fraction);
            (double, byte, byte, byte)[] stops =
            {
                (0.0, 20, 20, 140), (0.3, 0, 120, 255), (0.55, 0, 220, 120), (0.8, 255, 230, 0), (1.0, 255, 30, 30),
            };
            for (int i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].Item1) continue;
                var a = stops[i - 1]; var b = stops[i];
                double f = (t - a.Item1) / (b.Item1 - a.Item1);
                return Color.FromArgb(alpha, Lerp(a.Item2, b.Item2, f), Lerp(a.Item3, b.Item3, f), Lerp(a.Item4, b.Item4, f));
            }
            return Color.FromArgb(alpha, 255, 30, 30);
        }

        private static byte Lerp(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);
    }

    /// <summary>Builds <see cref="PlaneScene"/> and <see cref="PlaneOverlay"/> from Core data. Thread-safe (bitmaps are frozen).</summary>
    public static class PlaneRenderBuilder
    {
        public const double DefaultHalfView = 50.0;
        private const double Pixel = 0.5;

        public static PlaneScene BuildScene(ArrayOrientation o, double depth, double viewU, double viewV, DoseGrid? dose,
            IReadOnlyList<(PlanningTarget Target, Color Colour)> targets, bool calculated, string caption, double halfView = DefaultHalfView)
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
                scene.DoseBitmap = ToBitmap(img, scene.DoseMaxGy);
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
                double max = scene?.DoseMaxGy ?? 0;
                foreach (var d in DiodeArray.Positions)
                {
                    double u = arrayU + d.U, v = arrayV + d.V;
                    Color c = Color.FromArgb(150, 200, 200, 200);
                    if (scene?.Dose != null && max > 0)
                    {
                        double frac = scene.Dose.Sample(o.FromPlane(u, v, scene.Depth)) / max;
                        if (frac >= 0.05) c = ColourMap.Dose(frac, 255);
                    }
                    dots.Add(new DiodeDot(u, v, c));
                }
            }
            return new PlaneOverlay { ArrayU = arrayU, ArrayV = arrayV, UsableHalf = usableHalf, Diodes = dots, ShowDiodes = showDiodes };
        }

        private static BitmapSource ToBitmap(PlaneImage img, double max)
        {
            int w = img.Width, h = img.Height;
            var pixels = new byte[w * h * 4];
            for (int l = 0; l < h; l++)
            {
                int row = h - 1 - l; // bitmap row 0 = top = highest v
                for (int i = 0; i < w; i++)
                {
                    var c = ColourMap.Dose(max > 0 ? img[i, l] / max : 0);
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
}
