using System;
using System.Collections.Generic;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Prescription;

namespace SrsMcPsqa.Core.Models
{
    /// <summary>A PTV as used by the optimiser: ID, its own prescription and its mask in patient coordinates.</summary>
    public sealed class PlanningTarget
    {
        public PlanningTarget(string id, double rxGy, StructureMask mask)
        {
            if (rxGy <= 0) throw new ArgumentOutOfRangeException(nameof(rxGy), $"PTV '{id}' needs a positive prescription.");
            if (mask.IsEmpty) throw new ArgumentException($"PTV '{id}' mask is empty.", nameof(mask));
            Id = id; RxGy = rxGy; Mask = mask;
        }

        public string Id { get; }
        public double RxGy { get; }
        public StructureMask Mask { get; }

        public override string ToString() => $"{Id} ({RxGy:0.##} Gy)";
    }

    /// <summary>
    /// An array plane, stored as its orientation and the patient point on the array centre (<c>P</c>).
    /// Depth = P along the normal; in-plane centre = P's in-plane components (see <see cref="PlaneAxes"/>).
    /// </summary>
    public readonly struct ArrayPlane : IEquatable<ArrayPlane>
    {
        public ArrayPlane(ArrayOrientation orientation, Vec3 centre) { Orientation = orientation; Centre = centre; }

        public ArrayOrientation Orientation { get; }

        /// <summary>Patient point on the array centre (DICOM, mm).</summary>
        public Vec3 Centre { get; }

        public double Depth => Orientation.N(Centre);
        public double U => Orientation.U(Centre);
        public double V => Orientation.V(Centre);

        public ArrayPlane Moved(Vec3 delta) => new ArrayPlane(Orientation, Centre + delta);

        public bool Equals(ArrayPlane other) => Orientation == other.Orientation && Centre.Equals(other.Centre);
        public override bool Equals(object? obj) => obj is ArrayPlane p && Equals(p);
        public override int GetHashCode() => ((int)Orientation * 397) ^ Centre.GetHashCode();
        public override string ToString() => $"{Orientation.Label()} @ {Centre}";
    }

    /// <summary>Coverage status of a PTV in a recommendation or evaluation.</summary>
    public enum CoverageStatus
    {
        /// <summary>Meets the coverage rule on at least one plane and isn't clipped there.</summary>
        Covered,
        /// <summary>Best plane clips the PTV cross-section at the usable-square edge (e.g. PTV bigger than the array).</summary>
        Partial,
        /// <summary>Covered by score, but no plane gives N_min diodes inside the PTV (rule waived).</summary>
        LowDiodeCount,
        /// <summary>No chosen plane meets the coverage rule.</summary>
        NotCovered,
    }

    /// <summary>Metrics for one PTV on one array plane.</summary>
    public sealed class PtvPlaneMetrics
    {
        public int TargetIndex { get; set; }
        public string TargetId { get; set; } = "";
        public double RxGy { get; set; }
        /// <summary>A_k: cross-section area inside the usable square (mm²).</summary>
        public double AreaInSquare { get; set; }
        /// <summary>Whole cross-section area at this depth, inside or outside the square (mm²).</summary>
        public double SliceArea { get; set; }
        /// <summary>A_k,max for this orientation (mm²).</summary>
        public double MaxArea { get; set; }
        /// <summary>H_k (mm²).</summary>
        public double HighDoseArea { get; set; }
        /// <summary>M_k.</summary>
        public double MeanDoseRatio { get; set; }
        /// <summary>J_k.</summary>
        public double Score { get; set; }
        /// <summary>J_k,best (best achievable for this PTV alone).</summary>
        public double BestScore { get; set; }
        /// <summary>N_k: diodes inside the PTV.</summary>
        public int DiodesInPtv { get; set; }
        /// <summary>Diodes inside the PTV with dose ≥ Rx_k.</summary>
        public int DiodesAtOrAboveRx { get; set; }
        public bool Covered { get; set; }
        public bool Clipped { get; set; }

        public double HighDoseFraction => MaxArea > 0 ? HighDoseArea / MaxArea : 0;
        public double RelativeScore => BestScore > 0 ? Score / BestScore : 0;
    }

    /// <summary>Full evaluation of one array plane for all PTVs in the model.</summary>
    public sealed class PlaneEvaluation
    {
        public PlaneEvaluation(ArrayPlane plane, IReadOnlyList<PtvPlaneMetrics> targets)
        {
            Plane = plane; Targets = targets;
        }

        public ArrayPlane Plane { get; }
        public IReadOnlyList<PtvPlaneMetrics> Targets { get; }
    }

    /// <summary>A PTV as shown in the UI: ID, volume, prescription and its source.</summary>
    public sealed class PtvInfo
    {
        public string Id { get; set; } = "";
        public double VolumeCc { get; set; }
        public double RxGy { get; set; }
        public RxSource RxSource { get; set; }
        public StructureMask? Mask { get; set; }

        /// <summary>Surface mesh for the 3D view (optional).</summary>
        public TriangleMesh? Mesh { get; set; }
    }
}
