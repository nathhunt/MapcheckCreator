using System;
using System.Collections.Generic;
using System.Linq;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.Fake
{
    /// <summary>Ellipsoidal synthetic PTV in patient coordinates.</summary>
    public sealed class FakePtv
    {
        public FakePtv(string id, Vec3 centre, Vec3 radii, double rxGy)
        {
            Id = id; Centre = centre; Radii = radii; RxGy = rxGy;
        }

        public string Id { get; }
        public Vec3 Centre { get; }
        public Vec3 Radii { get; }

        /// <summary>Dose the synthetic plan delivers (the parser reads the intended Rx from the ID).</summary>
        public double RxGy { get; }

        /// <summary>Normalised radius: ≤ 1 inside the ellipsoid.</summary>
        public double ScaledRadius(Vec3 p)
        {
            var d = p - Centre;
            return Math.Sqrt(d.X * d.X / (Radii.X * Radii.X) + d.Y * d.Y / (Radii.Y * Radii.Y) + d.Z * d.Z / (Radii.Z * Radii.Z));
        }

        public bool Contains(Vec3 p) => ScaledRadius(p) <= 1.0;

        public double MeanRadius => (Radii.X + Radii.Y + Radii.Z) / 3;
    }

    /// <summary>
    /// The synthetic patient: a five-metastasis single-isocentre SRS plan with an analytic dose
    /// (slightly hot centre, Gaussian fall-off outside each PTV, low-dose bath around the isocentre).
    /// </summary>
    public sealed class FakePatient
    {
        public string PatientId { get; } = "FAKE001";
        public string DisplayName { get; } = "Phantom, Test (synthetic)";
        public string ClinicalCourseId { get; } = "C1";
        public string ClinicalPlanId { get; } = "SRS_5Mets";
        public string PlanUid { get; } = "1.2.826.0.1.3680043.9999.1";
        public double PlanTotalDoseGy { get; } = 20.0;
        public Vec3 Isocentre { get; } = new Vec3(10.0, -20.0, 35.0);

        public IReadOnlyList<FakePtv> Ptvs { get; }

        public FakePatient()
        {
            var iso = Isocentre;
            Ptvs = new[]
            {
                new FakePtv("PTV1_24Gy", iso + new Vec3(-18, 2, 10), new Vec3(6, 5, 7), 24),
                new FakePtv("PTV2_2000cGy", iso + new Vec3(12, -1, 22), new Vec3(4.5, 4.5, 4.5), 20),
                new FakePtv("PTV_R_Front_2700cGy", iso + new Vec3(30, -28, -15), new Vec3(8, 7, 9), 27),
                new FakePtv("PTV4_18Gy", iso + new Vec3(28, 15, -30), new Vec3(5, 5, 5), 18),
                new FakePtv("PTV5", iso + new Vec3(-35, 25, -45), new Vec3(3.5, 3.5, 4), 20), // no Rx in name -> plan Rx
            };
        }

        /// <summary>Clinical dose (Gy) at patient point <paramref name="p"/>.</summary>
        public double Dose(Vec3 p, double sigma = 2.5, double scale = 1.0)
        {
            double d = 0;
            foreach (var t in Ptvs)
            {
                double r = t.ScaledRadius(p);
                double v = r <= 1
                    ? t.RxGy * (1.02 + 0.08 * (1 - r * r))
                    : t.RxGy * 1.02 * Math.Exp(-Math.Pow((r - 1) * t.MeanRadius, 2) / (2 * sigma * sigma));
                d = Math.Max(d, v);
            }
            double bath = 2.0 * Math.Exp(-(p - Isocentre).Length / 40.0);
            return scale * Math.Max(d, bath);
        }

        /// <summary>Box around all PTVs plus <paramref name="margin"/> mm.</summary>
        public Box3 Bounds(double margin) =>
            Ptvs.Select(t => new Box3(t.Centre - t.Radii, t.Centre + t.Radii)).Aggregate(Box3.Union).Expand(margin);
    }
}
