using System;

namespace SrsMcPsqa.Core.Geometry
{
    /// <summary>
    /// Axis-aligned regular grid in patient coordinates. <see cref="Origin"/> is the centre of voxel (0,0,0);
    /// voxel (i,j,k) is centred at <c>Origin + (i·Sx, j·Sy, k·Sz)</c>. Data are stored X-fastest.
    /// </summary>
    public sealed class GridGeometry
    {
        public GridGeometry(Vec3 origin, Vec3 spacing, int nx, int ny, int nz)
        {
            if (nx <= 0 || ny <= 0 || nz <= 0) throw new ArgumentException("Grid dimensions must be positive.");
            if (spacing.X <= 0 || spacing.Y <= 0 || spacing.Z <= 0) throw new ArgumentException("Grid spacing must be positive.");
            Origin = origin; Spacing = spacing; Nx = nx; Ny = ny; Nz = nz;
        }

        public Vec3 Origin { get; }
        public Vec3 Spacing { get; }
        public int Nx { get; }
        public int Ny { get; }
        public int Nz { get; }
        public int Count => Nx * Ny * Nz;

        public int Index(int i, int j, int k) => i + Nx * (j + Ny * k);

        public Vec3 VoxelCentre(int i, int j, int k) =>
            new Vec3(Origin.X + i * Spacing.X, Origin.Y + j * Spacing.Y, Origin.Z + k * Spacing.Z);

        /// <summary>Outer edges of the grid.</summary>
        public Box3 Bounds => new Box3(Origin - Spacing * 0.5,
            new Vec3(Origin.X + (Nx - 0.5) * Spacing.X, Origin.Y + (Ny - 0.5) * Spacing.Y, Origin.Z + (Nz - 0.5) * Spacing.Z));

        public GridGeometry Translate(Vec3 offset) => new GridGeometry(Origin + offset, Spacing, Nx, Ny, Nz);
    }

    /// <summary>Absolute dose in Gy on a regular grid.</summary>
    public sealed class DoseGrid
    {
        public DoseGrid(GridGeometry geometry, float[] valuesGy)
        {
            if (valuesGy.Length != geometry.Count) throw new ArgumentException("Dose array length does not match the grid.");
            Geometry = geometry; Values = valuesGy;
        }

        public GridGeometry Geometry { get; }

        /// <summary>Dose values in Gy, X-fastest.</summary>
        public float[] Values { get; }

        /// <summary>
        /// Trilinear interpolation. Points more than half a voxel outside the grid return 0 Gy; points within half a
        /// voxel of the edge are clamped to the edge voxels.
        /// </summary>
        public double Sample(Vec3 p)
        {
            var g = Geometry;
            double fx = (p.X - g.Origin.X) / g.Spacing.X;
            double fy = (p.Y - g.Origin.Y) / g.Spacing.Y;
            double fz = (p.Z - g.Origin.Z) / g.Spacing.Z;
            if (fx < -0.5 || fy < -0.5 || fz < -0.5 || fx > g.Nx - 0.5 || fy > g.Ny - 0.5 || fz > g.Nz - 0.5) return 0;

            Split(fx, g.Nx, out int x0, out int x1, out double tx);
            Split(fy, g.Ny, out int y0, out int y1, out double ty);
            Split(fz, g.Nz, out int z0, out int z1, out double tz);

            int nx = g.Nx, nxy = g.Nx * g.Ny;
            var v = Values;
            double c00 = v[x0 + nx * y0 + nxy * z0] * (1 - tx) + v[x1 + nx * y0 + nxy * z0] * tx;
            double c10 = v[x0 + nx * y1 + nxy * z0] * (1 - tx) + v[x1 + nx * y1 + nxy * z0] * tx;
            double c01 = v[x0 + nx * y0 + nxy * z1] * (1 - tx) + v[x1 + nx * y0 + nxy * z1] * tx;
            double c11 = v[x0 + nx * y1 + nxy * z1] * (1 - tx) + v[x1 + nx * y1 + nxy * z1] * tx;
            double c0 = c00 * (1 - ty) + c10 * ty;
            double c1 = c01 * (1 - ty) + c11 * ty;
            return c0 * (1 - tz) + c1 * tz;
        }

        private static void Split(double f, int n, out int i0, out int i1, out double t)
        {
            if (f <= 0) { i0 = i1 = 0; t = 0; return; }
            if (f >= n - 1) { i0 = i1 = n - 1; t = 0; return; }
            i0 = (int)f; i1 = i0 + 1; t = f - i0;
        }

        public double Max()
        {
            float m = float.MinValue;
            foreach (var v in Values) if (v > m) m = v;
            return m;
        }

        /// <summary>Same values on a grid moved by <paramref name="offset"/> (values array is shared).</summary>
        public DoseGrid Translate(Vec3 offset) => new DoseGrid(Geometry.Translate(offset), Values);

        /// <summary>Build a dose grid by evaluating a function at every voxel centre (synthetic data and tests).</summary>
        public static DoseGrid FromFunction(GridGeometry g, Func<Vec3, double> doseGy)
        {
            var values = new float[g.Count];
            System.Threading.Tasks.Parallel.For(0, g.Nz, k =>
            {
                for (int j = 0; j < g.Ny; j++)
                    for (int i = 0; i < g.Nx; i++)
                        values[g.Index(i, j, k)] = (float)doseGy(g.VoxelCentre(i, j, k));
            });
            return new DoseGrid(g, values);
        }
    }

    /// <summary>Binary structure mask on a regular grid (typically 0.5 mm in-plane, CT slice spacing in Z).</summary>
    public sealed class StructureMask
    {
        public StructureMask(GridGeometry geometry, bool[] inside)
        {
            if (inside.Length != geometry.Count) throw new ArgumentException("Mask array length does not match the grid.");
            Geometry = geometry; Inside = inside;
            ComputeBounds();
        }

        public GridGeometry Geometry { get; }
        public bool[] Inside { get; }
        public int VoxelCount { get; private set; }

        /// <summary>Tight box around the inside voxels (voxel edges). Undefined when <see cref="IsEmpty"/>.</summary>
        public Box3 Bounds { get; private set; }

        public bool IsEmpty => VoxelCount == 0;

        /// <summary>Volume in cm³.</summary>
        public double VolumeCc => VoxelCount * Geometry.Spacing.X * Geometry.Spacing.Y * Geometry.Spacing.Z / 1000.0;

        /// <summary>Nearest-voxel lookup; false outside the grid.</summary>
        public bool Contains(Vec3 p)
        {
            var g = Geometry;
            int i = (int)Math.Floor((p.X - g.Origin.X) / g.Spacing.X + 0.5);
            int j = (int)Math.Floor((p.Y - g.Origin.Y) / g.Spacing.Y + 0.5);
            int k = (int)Math.Floor((p.Z - g.Origin.Z) / g.Spacing.Z + 0.5);
            if (i < 0 || j < 0 || k < 0 || i >= g.Nx || j >= g.Ny || k >= g.Nz) return false;
            return Inside[g.Index(i, j, k)];
        }

        private void ComputeBounds()
        {
            var g = Geometry;
            int imin = int.MaxValue, jmin = int.MaxValue, kmin = int.MaxValue, imax = -1, jmax = -1, kmax = -1, count = 0;
            for (int k = 0; k < g.Nz; k++)
                for (int j = 0; j < g.Ny; j++)
                    for (int i = 0; i < g.Nx; i++)
                    {
                        if (!Inside[g.Index(i, j, k)]) continue;
                        count++;
                        if (i < imin) imin = i; if (i > imax) imax = i;
                        if (j < jmin) jmin = j; if (j > jmax) jmax = j;
                        if (k < kmin) kmin = k; if (k > kmax) kmax = k;
                    }
            VoxelCount = count;
            if (count == 0) { Bounds = new Box3(g.Origin, g.Origin); return; }
            var half = g.Spacing * 0.5;
            Bounds = new Box3(g.VoxelCentre(imin, jmin, kmin) - half, g.VoxelCentre(imax, jmax, kmax) + half);
        }

        /// <summary>Build a mask over <paramref name="box"/> by testing every voxel centre (synthetic data and tests).</summary>
        public static StructureMask FromPredicate(Box3 box, Vec3 spacing, Func<Vec3, bool> inside)
        {
            int nx = Math.Max(1, (int)Math.Ceiling(box.Size.X / spacing.X));
            int ny = Math.Max(1, (int)Math.Ceiling(box.Size.Y / spacing.Y));
            int nz = Math.Max(1, (int)Math.Ceiling(box.Size.Z / spacing.Z));
            var g = new GridGeometry(box.Min + spacing * 0.5, spacing, nx, ny, nz);
            var data = new bool[g.Count];
            System.Threading.Tasks.Parallel.For(0, nz, k =>
            {
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                        data[g.Index(i, j, k)] = inside(g.VoxelCentre(i, j, k));
            });
            return new StructureMask(g, data);
        }
    }
}
