using System;
using System.Globalization;

namespace SrsMcPsqa.Core.Geometry
{
    /// <summary>
    /// A point or vector in DICOM patient coordinates, in mm.
    /// For a head-first-supine patient: +X = patient left, +Y = posterior, +Z = superior.
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static Vec3 Zero => new Vec3(0, 0, 0);

        /// <summary>Component by axis index: 0 = X, 1 = Y, 2 = Z.</summary>
        public double this[int axis] => axis switch
        {
            0 => X,
            1 => Y,
            2 => Z,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

        /// <summary>Copy with one component replaced.</summary>
        public Vec3 With(int axis, double value) => axis switch
        {
            0 => new Vec3(value, Y, Z),
            1 => new Vec3(X, value, Z),
            2 => new Vec3(X, Y, value),
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(double s, Vec3 a) => a * s;

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object? obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());
        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);
        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

        /// <summary>True when every component differs by at most <paramref name="tol"/> mm.</summary>
        public bool ApproxEquals(Vec3 other, double tol = 1e-6) =>
            Math.Abs(X - other.X) <= tol && Math.Abs(Y - other.Y) <= tol && Math.Abs(Z - other.Z) <= tol;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", X, Y, Z);
    }

    /// <summary>Axis-aligned box in patient coordinates (mm).</summary>
    public readonly struct Box3
    {
        public Box3(Vec3 min, Vec3 max) { Min = min; Max = max; }
        public Vec3 Min { get; }
        public Vec3 Max { get; }
        public Vec3 Centre => (Min + Max) * 0.5;
        public Vec3 Size => Max - Min;

        public Box3 Expand(double margin) =>
            new Box3(Min - new Vec3(margin, margin, margin), Max + new Vec3(margin, margin, margin));

        public static Box3 Union(Box3 a, Box3 b) => new Box3(
            new Vec3(Math.Min(a.Min.X, b.Min.X), Math.Min(a.Min.Y, b.Min.Y), Math.Min(a.Min.Z, b.Min.Z)),
            new Vec3(Math.Max(a.Max.X, b.Max.X), Math.Max(a.Max.Y, b.Max.Y), Math.Max(a.Max.Z, b.Max.Z)));

        public override string ToString() => $"[{Min} - {Max}]";
    }
}
