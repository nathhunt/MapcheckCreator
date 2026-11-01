using System;
using System.Collections.Generic;

namespace SrsMcPsqa.Core.Detector
{
    /// <summary>In-plane diode position on the array, mm from the array centre.</summary>
    public readonly struct DiodePosition
    {
        public DiodePosition(double u, double v) { U = u; V = v; }
        public double U { get; }
        public double V { get; }
    }

    /// <summary>
    /// Sun Nuclear SRS MapCHECK diode layout model.
    /// <para>
    /// Diodes at (u, v) = (1.75·i, 1.75·j) mm for integers i, j ∈ [−22, 22] with i + j even. This gives 1,013 diodes,
    /// 2.47 mm (1.75·√2) nearest-neighbour spacing and a ±38.5 mm (77 mm) extent: two boards with 3.5 mm pitch offset by
    /// 1.75 mm in both directions.
    /// </para>
    /// <para>VERIFY: confirm against Sun Nuclear documentation for the SRS MapCHECK.</para>
    /// The lattice is symmetric under 90° rotation, so the in-plane rotation between orientations does not matter.
    /// </summary>
    public static class DiodeArray
    {
        public const double LatticeStep = 1.75;
        public const int MaxIndex = 22;

        /// <summary>Half of the 77 mm array extent.</summary>
        public const double HalfExtent = LatticeStep * MaxIndex; // 38.5

        public const double Extent = 2 * HalfExtent; // 77

        public static double NearestNeighbourSpacing => LatticeStep * Math.Sqrt(2);

        public static IReadOnlyList<DiodePosition> Positions { get; } = Build();

        public static int Count => Positions.Count;

        /// <summary>Half-width of the usable square for a given edge margin (mm).</summary>
        public static double UsableHalfWidth(double edgeMargin) => HalfExtent - edgeMargin;

        private static DiodePosition[] Build()
        {
            var list = new List<DiodePosition>(1013);
            for (int j = -MaxIndex; j <= MaxIndex; j++)
                for (int i = -MaxIndex; i <= MaxIndex; i++)
                    if (((i + j) & 1) == 0)
                        list.Add(new DiodePosition(i * LatticeStep, j * LatticeStep));
            return list.ToArray();
        }
    }
}
