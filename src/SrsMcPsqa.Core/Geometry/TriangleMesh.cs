using System;
using System.Collections.Generic;

namespace SrsMcPsqa.Core.Geometry
{
    /// <summary>Triangle mesh in patient coordinates (mm) for 3D display.</summary>
    public sealed class TriangleMesh
    {
        public TriangleMesh(IReadOnlyList<Vec3> positions, IReadOnlyList<int> triangleIndices)
        {
            if (triangleIndices.Count % 3 != 0) throw new ArgumentException("Triangle index count must be a multiple of 3.");
            Positions = positions; TriangleIndices = triangleIndices;
        }

        public IReadOnlyList<Vec3> Positions { get; }
        public IReadOnlyList<int> TriangleIndices { get; }

        /// <summary>UV-sphere mesh of an axis-aligned ellipsoid (synthetic data).</summary>
        public static TriangleMesh Ellipsoid(Vec3 centre, Vec3 radii, int slices = 24, int stacks = 16)
        {
            var pos = new List<Vec3>();
            var idx = new List<int>();
            for (int i = 0; i <= stacks; i++)
            {
                double phi = Math.PI * i / stacks;
                for (int j = 0; j <= slices; j++)
                {
                    double th = 2 * Math.PI * j / slices;
                    pos.Add(new Vec3(centre.X + radii.X * Math.Sin(phi) * Math.Cos(th),
                                     centre.Y + radii.Y * Math.Sin(phi) * Math.Sin(th),
                                     centre.Z + radii.Z * Math.Cos(phi)));
                }
            }
            int row = slices + 1;
            for (int i = 0; i < stacks; i++)
                for (int j = 0; j < slices; j++)
                {
                    int a = i * row + j, b = a + row;
                    idx.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            return new TriangleMesh(pos, idx);
        }
    }
}
