using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.Core.Collision
{
    /// <summary>
    /// Geometry for the gantry–couch clearance check (mm). Defaults are deliberately conservative approximations for a
    /// TrueBeam with an Exact IGRT couch top; verify them against the vendor specification and your own setup (Settings).
    /// </summary>
    public sealed class CollisionSettings
    {
        /// <summary>Distance from the isocentre to the face of the treatment head (collimator cover).</summary>
        public double HeadClearanceMm { get; set; } = 380;
        /// <summary>Radius of the head, modelled as a cylinder around the beam axis.</summary>
        public double HeadRadiusMm { get; set; } = 400;
        /// <summary>Length of the head cylinder from its face towards the source (only the part near the couch matters).</summary>
        public double HeadDepthMm { get; set; } = 300;

        public double CouchWidthMm { get; set; } = 530;
        public double CouchThicknessMm { get; set; } = 60;
        /// <summary>Length of the couch top from its head end towards the feet.</summary>
        public double CouchLengthMm { get; set; } = 2000;
        /// <summary>
        /// Vertical distance from the MapCHECK user origin down to the couch top surface. 0 = not set: the check
        /// can't run until this is entered.
        /// </summary>
        public double OriginAboveCouchMm { get; set; }
        /// <summary>
        /// How far the couch top extends superior of the MapCHECK user origin (towards the gantry at couch 0);
        /// negative when the phantom overhangs the head end.
        /// </summary>
        public double CouchHeadEndSupMm { get; set; } = 200;
        /// <summary>Phantom lateral offset on the couch (+ = towards the phantom's left, −x); 0 = centred.</summary>
        public double PhantomLateralOffsetMm { get; set; }
        /// <summary>Largest lateral offset that may be suggested.</summary>
        public double MaxLateralOffsetMm { get; set; } = 50;
        public double LateralStepMm { get; set; } = 5;

        /// <summary>Clearance below this is a warning.</summary>
        public double WarningMm { get; set; } = 50;
        /// <summary>Clearance below this is a collision risk.</summary>
        public double RiskMm { get; set; } = 20;

        public bool IsConfigured => OriginAboveCouchMm > 0 && HeadClearanceMm > 0 && HeadRadiusMm > 0 && CouchWidthMm > 0 && CouchThicknessMm > 0;

        public CollisionSettings Clone() => (CollisionSettings)MemberwiseClone();
    }

    /// <summary>Couch angle and the gantry angles (control points) of one beam, IEC 61217 scales.</summary>
    public sealed class BeamGeometry
    {
        public BeamGeometry(string id, double couchAngle, IReadOnlyList<double> gantryAngles)
        {
            Id = id; CouchAngle = couchAngle; GantryAngles = gantryAngles;
        }

        public string Id { get; }
        public double CouchAngle { get; }
        public IReadOnlyList<double> GantryAngles { get; }
    }

    public enum ClearanceLevel { Clear, Warning, Risk }

    public sealed class BeamClearance
    {
        public BeamClearance(string beamId, double couchAngle, double minClearanceMm, double gantryAtMin, ClearanceLevel level)
        {
            BeamId = beamId; CouchAngle = couchAngle; MinClearanceMm = minClearanceMm; GantryAtMin = gantryAtMin; Level = level;
        }

        public string BeamId { get; }
        public double CouchAngle { get; }
        /// <summary>Smallest head–couch gap over the beam's gantry angles; negative = the model shapes overlap.</summary>
        public double MinClearanceMm { get; }
        public double GantryAtMin { get; }
        public ClearanceLevel Level { get; }

        public string Describe() => string.Format(CultureInfo.InvariantCulture, "{0} (couch {1:0}°): {2} at gantry {3:0}°",
            BeamId, CouchAngle, CollisionResult.FormatClearance(MinClearanceMm), GantryAtMin);
    }

    public sealed class CollisionResult
    {
        public CollisionResult(IReadOnlyList<BeamClearance> beams, double lateralOffsetMm)
        {
            Beams = beams; LateralOffsetMm = lateralOffsetMm;
        }

        public IReadOnlyList<BeamClearance> Beams { get; }
        public double LateralOffsetMm { get; }
        public BeamClearance? Worst => Beams.OrderBy(b => b.MinClearanceMm).FirstOrDefault();
        public double MinClearanceMm => Worst?.MinClearanceMm ?? double.PositiveInfinity;
        public ClearanceLevel Level => Beams.Count == 0 ? ClearanceLevel.Clear : Beams.Max(b => b.Level);

        public static string FormatClearance(double mm) => mm < 0
            ? "overlap (collision)"
            : string.Format(CultureInfo.InvariantCulture, "{0:0.0} cm clearance", mm / 10);
    }

    /// <summary>A lateral phantom offset on the couch that clears the warning level (or improves the clearance most).</summary>
    public sealed class LateralSuggestion
    {
        public LateralSuggestion(double offsetMm, CollisionResult result, bool clears)
        {
            OffsetMm = offsetMm; Result = result; Clears = clears;
        }

        /// <summary>Phantom offset on the couch (+ = towards the phantom's left), relative to centred.</summary>
        public double OffsetMm { get; }
        public CollisionResult Result { get; }
        /// <summary>True when every beam is above the warning level with this offset.</summary>
        public bool Clears { get; }

        public string Describe() => OffsetMm == 0 ? "phantom centred on the couch"
            : string.Format(CultureInfo.InvariantCulture, "move the phantom {0:0} mm towards its {1} on the couch",
                Math.Abs(OffsetMm), OffsetMm > 0 ? "Left (+x)" : "Right (−x)");
    }

    /// <summary>
    /// Gantry–couch clearance for a QA plan. All geometry is in the room frame with the isocentre at the origin, aligned
    /// with the phantom's DICOM axes at couch 0 (x = phantom left, y = posterior/down, z = superior/towards the gantry):
    /// <list type="bullet">
    /// <item>The head is a cylinder of radius <c>HeadRadiusMm</c> along the beam axis, from <c>HeadClearanceMm</c> to
    /// <c>HeadClearanceMm + HeadDepthMm</c> from the isocentre. Gantry angle θ (IEC 61217, increasing clockwise seen from
    /// the foot of the couch) puts the source at (sin θ, −cos θ, 0): 0° above, 90° on the phantom's left (HFS).</item>
    /// <item>The couch top is a box below the phantom (top surface <c>OriginAboveCouchMm</c> below the user origin), centred
    /// laterally on the phantom unless offset, from <c>CouchHeadEndSupMm</c> superior of the origin towards the feet.
    /// It rotates with the couch angle φ about the vertical axis through the isocentre, counter-clockwise seen from above
    /// for positive φ (IEC 61217).</item>
    /// </list>
    /// The head surface is sampled (grid on the face, rings along the side), so clearances are approximate to a few mm;
    /// the warning margins absorb that. The phantom itself is not modelled.
    /// </summary>
    public static class CouchCollision
    {
        private const double FaceGridMm = 25, RingStepDeg = 6, SideStepMm = 60, GantryStepDeg = 1;

        /// <param name="isoFromOrigin">QA isocentre relative to the MapCHECK user origin (phantom DICOM axes).</param>
        /// <param name="lateralOffsetMm">Phantom offset on the couch; null = the setting.</param>
        public static CollisionResult Check(Vec3 isoFromOrigin, IReadOnlyList<BeamGeometry> beams, CollisionSettings s, double? lateralOffsetMm = null)
        {
            double offset = lateralOffsetMm ?? s.PhantomLateralOffsetMm;
            var head = HeadArrays.From(HeadPoints(s));
            var couch = CouchBox(isoFromOrigin, s, offset);
            // Bounding sphere of the head (centre on the beam axis) for a quick lower bound per gantry angle.
            double centreDepth = s.HeadClearanceMm + s.HeadDepthMm / 2;
            double sphere = Math.Sqrt(s.HeadRadiusMm * s.HeadRadiusMm + s.HeadDepthMm * s.HeadDepthMm / 4);
            var results = new List<BeamClearance>();
            foreach (var b in beams)
            {
                double best = double.PositiveInfinity, gantryAtBest = double.NaN;
                foreach (var g in Densify(b.GantryAngles))
                {
                    // Skip angles where even the bounding sphere is farther from the couch than the best so far.
                    double lowerBound = Clearance(new[] { new Vec3(0, -centreDepth, 0) }, g, b.CouchAngle, couch) - sphere;
                    if (lowerBound >= best) continue;
                    double c = Clearance(head, g, b.CouchAngle, couch);
                    if (c < best) { best = c; gantryAtBest = g; }
                }
                if (double.IsNaN(gantryAtBest)) continue;
                var level = best < s.RiskMm ? ClearanceLevel.Risk : best < s.WarningMm ? ClearanceLevel.Warning : ClearanceLevel.Clear;
                results.Add(new BeamClearance(b.Id, b.CouchAngle, best, Normalise(gantryAtBest), level));
            }
            return new CollisionResult(results, offset);
        }

        /// <summary>
        /// Smallest lateral phantom offset (within ±<c>MaxLateralOffsetMm</c>, preferring small offsets) that brings every
        /// beam above the warning level; if none does, the offset with the largest minimum clearance. Null when the
        /// current setup is already clear or no offset improves it.
        /// </summary>
        public static LateralSuggestion? SuggestLateralOffset(Vec3 isoFromOrigin, IReadOnlyList<BeamGeometry> beams, CollisionSettings s)
        {
            var current = Check(isoFromOrigin, beams, s);
            if (current.Level == ClearanceLevel.Clear) return null;
            double step = Math.Max(1, s.LateralStepMm);
            var candidates = new List<double>();
            for (double d = step; d <= s.MaxLateralOffsetMm + 1e-9; d += step)
            {
                candidates.Add(s.PhantomLateralOffsetMm + d);
                candidates.Add(s.PhantomLateralOffsetMm - d);
            }
            var allowed = candidates.Where(o => Math.Abs(o) <= s.MaxLateralOffsetMm + 1e-9).ToList();
            var checks = new CollisionResult[allowed.Count];
            System.Threading.Tasks.Parallel.For(0, allowed.Count, i => checks[i] = Check(isoFromOrigin, beams, s, allowed[i]));
            LateralSuggestion? best = null;
            for (int i = 0; i < allowed.Count; i++) // smallest offsets first
            {
                var r = checks[i];
                if (r.MinClearanceMm >= s.WarningMm) return new LateralSuggestion(allowed[i], r, clears: true);
                if (best == null || r.MinClearanceMm > best.Result.MinClearanceMm + 1e-6) best = new LateralSuggestion(allowed[i], r, clears: false);
            }
            return best != null && best.Result.MinClearanceMm > current.MinClearanceMm + 1 ? best : null;
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>Axis-aligned couch box in the couch frame (isocentre at the origin, couch 0).</summary>
        private readonly struct Box
        {
            public Box(Vec3 min, Vec3 max) { Min = min; Max = max; }
            public Vec3 Min { get; }
            public Vec3 Max { get; }

            /// <summary>Euclidean distance from p to the box; negative (minus the depth) inside.</summary>
            public double SignedDistance(Vec3 p)
            {
                double dx = Math.Max(Min.X - p.X, p.X - Max.X), dy = Math.Max(Min.Y - p.Y, p.Y - Max.Y), dz = Math.Max(Min.Z - p.Z, p.Z - Max.Z);
                double ox = Math.Max(dx, 0), oy = Math.Max(dy, 0), oz = Math.Max(dz, 0);
                double outside = Math.Sqrt(ox * ox + oy * oy + oz * oz);
                return outside > 0 ? outside : Math.Max(dx, Math.Max(dy, dz));
            }
        }

        private static Box CouchBox(Vec3 iso, CollisionSettings s, double lateralOffset)
        {
            // Phantom frame relative to the user origin, then relative to the isocentre.
            double centreX = -lateralOffset; // phantom moved towards +x on the couch = couch centre at −x of the phantom
            double top = s.OriginAboveCouchMm; // +y is posterior (down)
            var min = new Vec3(centreX - s.CouchWidthMm / 2, top, s.CouchHeadEndSupMm - s.CouchLengthMm);
            var max = new Vec3(centreX + s.CouchWidthMm / 2, top + s.CouchThicknessMm, s.CouchHeadEndSupMm);
            return new Box(min - iso, max - iso);
        }

        /// <summary>Sample points on the head at gantry 0 (beam axis along −y: the head is above, at y &lt; 0).</summary>
        private static List<Vec3> HeadPoints(CollisionSettings s)
        {
            var pts = new List<Vec3>();
            double d0 = s.HeadClearanceMm, r = s.HeadRadiusMm;
            // Face: square grid clipped to the disc, plus the rim.
            for (double x = -r; x <= r + 1e-9; x += FaceGridMm)
                for (double z = -r; z <= r + 1e-9; z += FaceGridMm)
                    if (x * x + z * z <= r * r) pts.Add(new Vec3(x, -d0, z));
            // Side: rings from the face towards the source.
            for (double d = d0; d <= d0 + s.HeadDepthMm + 1e-9; d += SideStepMm)
                for (double a = 0; a < 360; a += RingStepDeg)
                {
                    double t = a * Math.PI / 180;
                    pts.Add(new Vec3(r * Math.Cos(t), -d, r * Math.Sin(t)));
                }
            return pts;
        }

        private static double Clearance(IReadOnlyList<Vec3> head, double gantryDeg, double couchDeg, Box couch) =>
            Clearance(HeadArrays.From(head), gantryDeg, couchDeg, couch);

        /// <summary>Head sample points as flat arrays (the hot loop avoids enumerators and struct copies).</summary>
        private sealed class HeadArrays
        {
            public double[] X = Array.Empty<double>(), Y = Array.Empty<double>(), Z = Array.Empty<double>();
            public static HeadArrays From(IReadOnlyList<Vec3> pts) => new HeadArrays
            {
                X = pts.Select(p => p.X).ToArray(), Y = pts.Select(p => p.Y).ToArray(), Z = pts.Select(p => p.Z).ToArray(),
            };
        }

        private static double Clearance(HeadArrays head, double gantryDeg, double couchDeg, Box couch)
        {
            // Head point at gantry 0 → room: rotate about z by θ (y-down frame: source at (sin θ, −cos θ, 0)).
            double g = gantryDeg * Math.PI / 180, cg = Math.Cos(g), sg = Math.Sin(g);
            // Room → couch frame: undo the couch rotation (counter-clockwise from above by φ, about the vertical y axis).
            // Couch rotation R(φ) maps couch → room in the top view (x right, z towards the gantry):
            // (x, z) → (x cos φ − z sin φ, x sin φ + z cos φ); its inverse is applied below.
            double c = couchDeg * Math.PI / 180, cc = Math.Cos(c), sc = Math.Sin(c);
            double minX = couch.Min.X, minY = couch.Min.Y, minZ = couch.Min.Z, maxX = couch.Max.X, maxY = couch.Max.Y, maxZ = couch.Max.Z;
            double best = double.PositiveInfinity;
            var hx = head.X; var hy = head.Y; var hz = head.Z;
            for (int i = 0; i < hx.Length; i++)
            {
                double x = hx[i] * cg - hy[i] * sg, y = hx[i] * sg + hy[i] * cg, z = hz[i];
                double xc = x * cc + z * sc, zc = -x * sc + z * cc;
                double dx = Math.Max(minX - xc, xc - maxX), dy = Math.Max(minY - y, y - maxY), dz = Math.Max(minZ - zc, zc - maxZ);
                double d;
                if (dx <= 0 && dy <= 0 && dz <= 0) d = Math.Max(dx, Math.Max(dy, dz));
                else
                {
                    double ox = dx > 0 ? dx : 0, oy = dy > 0 ? dy : 0, oz = dz > 0 ? dz : 0;
                    double sq = ox * ox + oy * oy + oz * oz;
                    if (sq >= best * best && best > 0) continue; // can't beat the current best
                    d = Math.Sqrt(sq);
                }
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Control-point gantry angles with gaps filled at ≤ 1°, following each step's shorter direction.</summary>
        private static IEnumerable<double> Densify(IReadOnlyList<double> angles)
        {
            if (angles.Count == 0) yield break;
            yield return angles[0];
            for (int i = 1; i < angles.Count; i++)
            {
                double a = angles[i - 1], delta = ((angles[i] - a) % 360 + 540) % 360 - 180;
                int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / GantryStepDeg));
                for (int k = 1; k <= n; k++) yield return a + delta * k / n;
            }
        }

        private static double Normalise(double deg) => ((deg % 360) + 360) % 360;
    }
}
