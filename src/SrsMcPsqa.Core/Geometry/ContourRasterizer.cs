using System;
using System.Collections.Generic;
using System.Linq;

namespace SrsMcPsqa.Core.Geometry
{
    /// <summary>2D point in an axial plane (patient X, Y in mm).</summary>
    public readonly struct Point2
    {
        public Point2(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }
    }

    /// <summary>All contour polygons of one structure on one axial image plane.</summary>
    public sealed class ContourSlice
    {
        public ContourSlice(double z, IReadOnlyList<IReadOnlyList<Point2>> polygons) { Z = z; Polygons = polygons; }

        /// <summary>Patient Z of the image plane (mm).</summary>
        public double Z { get; }

        /// <summary>Closed polygons (last point need not repeat the first). Holes and islands are resolved by the even-odd rule.</summary>
        public IReadOnlyList<IReadOnlyList<Point2>> Polygons { get; }
    }

    /// <summary>
    /// Rasterises axial contours into a <see cref="StructureMask"/>: the in-plane resolution in X/Y,
    /// slice spacing in Z, restricted to the contour bounding box plus a margin. A voxel is inside when its centre is
    /// inside the slice's polygons by the even-odd rule (so holes and multiple islands work).
    /// </summary>
    public static class ContourRasterizer
    {
        public static StructureMask Rasterize(IReadOnlyList<ContourSlice> slices, double sliceSpacing,
            double inPlaneResolution = 0.5, double margin = 2.0)
        {
            if (sliceSpacing <= 0) throw new ArgumentOutOfRangeException(nameof(sliceSpacing));
            var withPoints = slices.Where(s => s.Polygons.Any(p => p.Count >= 3)).ToList();
            if (withPoints.Count == 0) throw new ArgumentException("No contours with at least 3 points.", nameof(slices));

            var allPoints = withPoints.SelectMany(s => s.Polygons).SelectMany(p => p).ToList();
            double minX = allPoints.Min(p => p.X) - margin, maxX = allPoints.Max(p => p.X) + margin;
            double minY = allPoints.Min(p => p.Y) - margin, maxY = allPoints.Max(p => p.Y) + margin;
            double minZ = withPoints.Min(s => s.Z), maxZ = withPoints.Max(s => s.Z);

            double h = inPlaneResolution;
            int nx = (int)Math.Ceiling((maxX - minX) / h);
            int ny = (int)Math.Ceiling((maxY - minY) / h);
            int marginSlices = (int)Math.Ceiling(margin / sliceSpacing);
            int nz = (int)Math.Round((maxZ - minZ) / sliceSpacing) + 1 + 2 * marginSlices;
            var origin = new Vec3(minX + h / 2, minY + h / 2, minZ - marginSlices * sliceSpacing);
            var g = new GridGeometry(origin, new Vec3(h, h, sliceSpacing), nx, ny, nz);
            var inside = new bool[g.Count];

            // Group polygons by slice index so duplicate entries for one plane are combined under even-odd.
            var bySlice = withPoints
                .GroupBy(s => (int)Math.Round((s.Z - origin.Z) / sliceSpacing))
                .Where(grp => grp.Key >= 0 && grp.Key < nz);

            foreach (var grp in bySlice)
            {
                int k = grp.Key;
                var polys = grp.SelectMany(s => s.Polygons).Where(p => p.Count >= 3).ToList();
                var xs = new List<double>();
                for (int j = 0; j < ny; j++)
                {
                    double y = origin.Y + j * h;
                    xs.Clear();
                    foreach (var poly in polys)
                    {
                        for (int e = 0; e < poly.Count; e++)
                        {
                            var a = poly[e];
                            var b = poly[(e + 1) % poly.Count];
                            // Half-open rule so a vertex exactly on the scanline is counted once.
                            if ((a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y))
                                xs.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                        }
                    }
                    if (xs.Count < 2) continue;
                    xs.Sort();
                    for (int m = 0; m + 1 < xs.Count; m += 2)
                    {
                        int i0 = Math.Max(0, (int)Math.Ceiling((xs[m] - origin.X) / h));
                        int i1 = Math.Min(nx - 1, (int)Math.Ceiling((xs[m + 1] - origin.X) / h) - 1);
                        // Sorted crossings pair up into disjoint spans, so this is the even-odd fill.
                        for (int i = i0; i <= i1; i++)
                            inside[g.Index(i, j, k)] = true;
                    }
                }
            }
            return new StructureMask(g, inside);
        }
    }
}
