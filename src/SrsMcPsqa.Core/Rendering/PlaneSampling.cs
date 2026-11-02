using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.Core.Rendering
{
    /// <summary>
    /// Scalar image on an array plane. Pixel (i, l) is centred at plane coordinates (U0 + i·PixelSize, V0 + l·PixelSize);
    /// values are row-major (index i + Width·l), l increasing with v (superior).
    /// </summary>
    public sealed class PlaneImage
    {
        public PlaneImage(ArrayOrientation orientation, double depth, double u0, double v0, double pixelSize, int width, int height, float[] values)
        {
            if (values.Length != width * height) throw new ArgumentException("Size mismatch.");
            Orientation = orientation; Depth = depth; U0 = u0; V0 = v0; PixelSize = pixelSize; Width = width; Height = height; Values = values;
        }

        public ArrayOrientation Orientation { get; }
        public double Depth { get; }
        public double U0 { get; }
        public double V0 { get; }
        public double PixelSize { get; }
        public int Width { get; }
        public int Height { get; }
        public float[] Values { get; }

        public float this[int i, int l] => Values[i + Width * l];

        public double Max()
        {
            float m = 0;
            foreach (var v in Values) if (v > m) m = v;
            return m;
        }
    }

    /// <summary>2D line segment in plane coordinates (mm).</summary>
    public readonly struct Segment2
    {
        public Segment2(double u1, double v1, double u2, double v2) { U1 = u1; V1 = v1; U2 = u2; V2 = v2; }
        public double U1 { get; }
        public double V1 { get; }
        public double U2 { get; }
        public double V2 { get; }
        public double Length => Math.Sqrt((U2 - U1) * (U2 - U1) + (V2 - V1) * (V2 - V1));
    }

    /// <summary>Samples dose and masks on an array plane, and extracts isolines (for the 2D plane view).</summary>
    public static class PlaneSampler
    {
        /// <summary>Dose (Gy) on the plane at <paramref name="depth"/>, over a square of half-size <paramref name="halfSize"/> around (cu, cv).</summary>
        public static PlaneImage SampleDose(DoseGrid dose, ArrayOrientation o, double depth, double cu, double cv, double halfSize, double pixel) =>
            Sample(o, depth, cu, cv, halfSize, pixel, p => (float)dose.Sample(p));

        /// <summary>
        /// Mask on the plane (1 inside, 0 outside), same layout as <see cref="SampleDose"/>. With <paramref name="smooth"/>,
        /// a box filter about 1 mm wide is applied so the 0.5 isoline is a smooth outline rather than a voxel staircase (display only).
        /// </summary>
        public static PlaneImage SampleMask(StructureMask mask, ArrayOrientation o, double depth, double cu, double cv, double halfSize, double pixel, bool smooth = true)
        {
            var img = Sample(o, depth, cu, cv, halfSize, pixel, p => mask.Contains(p) ? 1f : 0f);
            return smooth ? BoxFilter(img, Math.Max(1, (int)Math.Round(0.5 / pixel))) : img;
        }

        private static PlaneImage BoxFilter(PlaneImage img, int r)
        {
            int w = img.Width, h = img.Height;
            var outv = new float[w * h];
            for (int l = 0; l < h; l++)
                for (int i = 0; i < w; i++)
                {
                    float sum = 0; int n = 0;
                    for (int dl = -r; dl <= r; dl++)
                        for (int di = -r; di <= r; di++)
                        {
                            int ii = i + di, ll = l + dl;
                            if (ii < 0 || ll < 0 || ii >= w || ll >= h) continue;
                            sum += img[ii, ll]; n++;
                        }
                    outv[i + w * l] = sum / n;
                }
            return new PlaneImage(img.Orientation, img.Depth, img.U0, img.V0, img.PixelSize, w, h, outv);
        }

        private static PlaneImage Sample(ArrayOrientation o, double depth, double cu, double cv, double halfSize, double pixel, Func<Vec3, float> f)
        {
            int n = Math.Max(2, (int)Math.Round(2 * halfSize / pixel) + 1);
            double u0 = cu - halfSize, v0 = cv - halfSize;
            var values = new float[n * n];
            Parallel.For(0, n, l =>
            {
                double v = v0 + l * pixel;
                for (int i = 0; i < n; i++) values[i + n * l] = f(o.FromPlane(u0 + i * pixel, v, depth));
            });
            return new PlaneImage(o, depth, u0, v0, pixel, n, n, values);
        }

        /// <summary>Isoline segments at <paramref name="level"/> by marching squares (with linear interpolation along cell edges).</summary>
        public static List<Segment2> Isolines(PlaneImage img, double level)
        {
            var segs = new List<Segment2>();
            int w = img.Width, h = img.Height;
            double px = img.PixelSize;
            for (int l = 0; l < h - 1; l++)
                for (int i = 0; i < w - 1; i++)
                {
                    double a = img[i, l], b = img[i + 1, l], c = img[i + 1, l + 1], d = img[i, l + 1];
                    int code = (a >= level ? 1 : 0) | (b >= level ? 2 : 0) | (c >= level ? 4 : 0) | (d >= level ? 8 : 0);
                    if (code == 0 || code == 15) continue;
                    double u = img.U0 + i * px, v = img.V0 + l * px;
                    // Edge crossing points: bottom (a-b), right (b-c), top (d-c), left (a-d).
                    (double, double) Bottom() => (u + px * T(a, b), v);
                    (double, double) Right() => (u + px, v + px * T(b, c));
                    (double, double) Top() => (u + px * T(d, c), v + px);
                    (double, double) Left() => (u, v + px * T(a, d));
                    double T(double p, double q) => Math.Abs(q - p) < 1e-12 ? 0.5 : (level - p) / (q - p);
                    void Add((double, double) p, (double, double) q) => segs.Add(new Segment2(p.Item1, p.Item2, q.Item1, q.Item2));

                    switch (code)
                    {
                        case 1: case 14: Add(Left(), Bottom()); break;
                        case 2: case 13: Add(Bottom(), Right()); break;
                        case 3: case 12: Add(Left(), Right()); break;
                        case 4: case 11: Add(Right(), Top()); break;
                        case 6: case 9: Add(Bottom(), Top()); break;
                        case 7: case 8: Add(Left(), Top()); break;
                        case 5: Add(Left(), Top()); Add(Bottom(), Right()); break;   // saddle
                        case 10: Add(Left(), Bottom()); Add(Right(), Top()); break; // saddle
                    }
                }
            return segs;
        }
    }
}
