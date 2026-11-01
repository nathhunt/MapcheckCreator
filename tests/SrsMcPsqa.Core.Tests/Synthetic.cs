using System;
using System.Collections.Generic;
using System.Linq;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Optimisation;

namespace SrsMcPsqa.Core.Tests
{
    /// <summary>Synthetic spherical PTVs with an analytic dose: Rx·1.1 inside each sphere, Gaussian fall-off (σ = 3 mm) outside.</summary>
    internal static class Synthetic
    {
        public sealed record Sphere(string Id, Vec3 Centre, double Radius, double RxGy);

        public static readonly Vec3 MaskSpacing = new Vec3(0.5, 0.5, 1.0);

        public static PlanningTarget Target(Sphere s) =>
            new PlanningTarget(s.Id, s.RxGy, StructureMask.FromPredicate(
                new Box3(s.Centre, s.Centre).Expand(s.Radius + 1), MaskSpacing, p => (p - s.Centre).Length <= s.Radius));

        public static double AnalyticDose(IEnumerable<Sphere> spheres, Vec3 p, double hotFactor = 1.1)
        {
            double d = 0;
            foreach (var s in spheres)
            {
                double r = (p - s.Centre).Length;
                double f = r <= s.Radius ? 1 : Math.Exp(-(r - s.Radius) * (r - s.Radius) / (2 * 9.0));
                d = Math.Max(d, s.RxGy * hotFactor * f);
            }
            return d;
        }

        /// <summary>Dose grid (1 mm) around the spheres; outside it the dose is 0, which is fine because only PTVs are sampled.</summary>
        public static DoseGrid Dose(IReadOnlyList<Sphere> spheres, double hotFactor = 1.1, double spacing = 1.0)
        {
            var box = spheres.Select(s => new Box3(s.Centre, s.Centre).Expand(s.Radius + 4)).Aggregate(Box3.Union);
            int nx = (int)Math.Ceiling(box.Size.X / spacing) + 1, ny = (int)Math.Ceiling(box.Size.Y / spacing) + 1, nz = (int)Math.Ceiling(box.Size.Z / spacing) + 1;
            var g = new GridGeometry(box.Min, new Vec3(spacing, spacing, spacing), nx, ny, nz);
            return DoseGrid.FromFunction(g, p => AnalyticDose(spheres, p, hotFactor));
        }

        public static PlanningModel Model(params Sphere[] spheres) =>
            new PlanningModel(spheres.Select(Target).ToList(), Dose(spheres));

        public static Sphere S(string id, double x, double y, double z, double r, double rx = 20) => new Sphere(id, new Vec3(x, y, z), r, rx);

        public static PlaneCandidate Candidate(ArrayOrientation o, ulong covered, params double[] scores) =>
            new PlaneCandidate(o, 0, 0, 0, Vec3.Zero, scores, covered);
    }
}
