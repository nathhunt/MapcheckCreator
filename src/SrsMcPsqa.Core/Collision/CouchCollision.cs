using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.Core.Collision
{
    /// <summary>
    /// Geometry for the gantry–couch clearance check (mm). Head: the machine's clearance circle, 39.5 cm radius around the
    /// isocentre (department value), as the head face distance; head 76.5 cm across (open-source ESAPI collision scripts
    /// zackmorelli/CollisionCheck and LDClark/PlanCheck). Couch: Exact IGRT top profile measured on the Eclipse couch
    /// structure (53.0 cm wide, 5.0 cm thick).
    /// </summary>
    public sealed class CollisionSettings
    {
        /// <summary>Distance from the isocentre to the face of the treatment head (collimator cover).</summary>
        public double HeadClearanceMm { get; set; } = 395;
        /// <summary>Radius of the head, modelled as a cylinder around the beam axis.</summary>
        public double HeadRadiusMm { get; set; } = 385;
        /// <summary>Length of the head cylinder from its face towards the source (only the part near the couch matters).</summary>
        public double HeadDepthMm { get; set; } = 200;

        /// <summary>Couch top width at its widest (about 1 cm below the top surface).</summary>
        public double CouchWidthMm { get; set; } = 530;
        public double CouchThicknessMm { get; set; } = 50;

        /// <summary>
        /// Exact IGRT couch top cross-section, one side, from the top surface down: (depth as a fraction of the thickness,
        /// half-width as a fraction of half the maximum width). Measured on the Eclipse couch structure (53.0 cm wide,
        /// 5.0 cm thick): rounded top corner, widest ~1.1 cm below the top, sides curving in to a ~43 cm bottom. Convex.
        /// </summary>
        public static IReadOnlyList<(double Depth, double HalfWidth)> CouchProfile { get; } = new[]
        {
            (0.0, 0.962), (0.076, 0.983), (0.219, 1.0), (0.437, 0.975), (0.676, 0.925), (0.855, 0.875), (1.0, 0.808),
        };
        /// <summary>Length of the couch top from its head end towards the feet.</summary>
        public double CouchLengthMm { get; set; } = 2000;
        /// <summary>
        /// Vertical distance from the MapCHECK user origin down to the couch top surface (department setup: 200 mm).
        /// 0 = not set: the check doesn't run.
        /// </summary>
        public double OriginAboveCouchMm { get; set; } = 200;
        /// <summary>
        /// How far the couch top extends superior of the MapCHECK user origin (towards the gantry at couch 0);
        /// negative when the phantom overhangs the head end.
        /// </summary>
        public double CouchHeadEndSupMm { get; set; } = -60;
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

    /// <summary>Arc rotation direction (IEC 61217: clockwise = increasing gantry angle).</summary>
    public enum ArcDirection { None, Clockwise, CounterClockwise }

    /// <summary>Couch angle and the gantry angles (control points) of one beam, IEC 61217 scales.</summary>
    public sealed class BeamGeometry
    {
        public BeamGeometry(string id, double couchAngle, IReadOnlyList<double> gantryAngles, ArcDirection direction = ArcDirection.None)
        {
            Id = id; CouchAngle = couchAngle; GantryAngles = gantryAngles; Direction = direction;
        }

        public string Id { get; }
        public double CouchAngle { get; }
        public IReadOnlyList<double> GantryAngles { get; }
        /// <summary>
        /// Needed for arcs with few control points (a conformal arc can have just start and stop): the gantry travels this
        /// way between them. None = static field, or take the shorter way between control points.
        /// </summary>
        public ArcDirection Direction { get; }
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
    /// <item>The couch top is a prism with the Exact IGRT cross-section (<see cref="CollisionSettings.CouchProfile"/>) below the
    /// phantom (top surface <c>OriginAboveCouchMm</c> below the user origin), centred
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
            var couch = BuildCouch(isoFromOrigin, s, offset);
            var edges = HeadArrays.From(EdgeSamples(couch, s));
            // Bounding sphere of the head (centre on the beam axis) for a quick lower bound per gantry angle.
            double centreDepth = s.HeadClearanceMm + s.HeadDepthMm / 2;
            double sphere = Math.Sqrt(s.HeadRadiusMm * s.HeadRadiusMm + s.HeadDepthMm * s.HeadDepthMm / 4);
            var centrePoint = HeadArrays.From(new[] { new Vec3(0, -centreDepth, 0) });
            var results = new List<BeamClearance>();
            foreach (var b in beams)
            {
                double best = double.PositiveInfinity, gantryAtBest = double.NaN;
                foreach (var g in Densify(b.GantryAngles, b.Direction))
                {
                    // Skip angles where even the bounding sphere is farther from the couch than the best so far.
                    double lowerBound = Clearance(centrePoint, g, b.CouchAngle, couch) - sphere;
                    if (lowerBound >= best) continue;
                    // Head surface samples → couch box, and couch edge samples → head cylinder (exact for the cylinder):
                    // together they catch face-to-face, rim-to-face and edge-to-face contacts.
                    double c = Math.Min(Clearance(head, g, b.CouchAngle, couch), EdgeClearance(edges, g, b.CouchAngle, s));
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

        /// <summary>
        /// Couch top in the couch frame (isocentre at the origin, couch 0): a prism along z whose cross-section (x, y) is the
        /// convex Exact IGRT profile (<see cref="CollisionSettings.CouchProfile"/>), from zMin to zMax.
        /// </summary>
        private sealed class CouchShape
        {
            public double[] Vx = Array.Empty<double>(), Vy = Array.Empty<double>(); // polygon vertices, in order
            public double ZMin, ZMax, BoxMinX, BoxMaxX, BoxMinY, BoxMaxY;

            /// <summary>Signed distance from (x, y, z) to the prism: negative inside.</summary>
            public double SignedDistance(double x, double y, double z)
            {
                double d2 = SignedDistance2D(x, y);
                double dz = Math.Max(ZMin - z, z - ZMax);
                if (d2 <= 0 && dz <= 0) return Math.Max(d2, dz);
                double a = d2 > 0 ? d2 : 0, b = dz > 0 ? dz : 0;
                return Math.Sqrt(a * a + b * b);
            }

            /// <summary>Lower bound (outside the bounding box) used to skip points quickly; 0 when inside the box.</summary>
            public double BoxDistanceSquared(double x, double y, double z)
            {
                double dx = Math.Max(Math.Max(BoxMinX - x, x - BoxMaxX), 0), dy = Math.Max(Math.Max(BoxMinY - y, y - BoxMaxY), 0);
                double dz = Math.Max(Math.Max(ZMin - z, z - ZMax), 0);
                return dx * dx + dy * dy + dz * dz;
            }

            private double SignedDistance2D(double x, double y)
            {
                int n = Vx.Length;
                double best = double.PositiveInfinity;
                bool inside = true;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double ax = Vx[j], ay = Vy[j], ex = Vx[i] - ax, ey = Vy[i] - ay, px = x - ax, py = y - ay;
                    double t = (px * ex + py * ey) / (ex * ex + ey * ey);
                    t = t < 0 ? 0 : t > 1 ? 1 : t;
                    double qx = px - t * ex, qy = py - t * ey, d = qx * qx + qy * qy;
                    if (d < best) best = d;
                    if (ex * py - ey * px < 0) inside = false; // positive signed area (see BuildCouch) → interior on the left of each edge
                }
                double dist = Math.Sqrt(best);
                return inside ? -dist : dist;
            }
        }

        private static CouchShape BuildCouch(Vec3 iso, CollisionSettings s, double lateralOffset)
        {
            // Phantom frame relative to the user origin, then relative to the isocentre.
            double centreX = -lateralOffset - iso.X; // phantom moved towards +x on the couch = couch centre at −x of the phantom
            double top = s.OriginAboveCouchMm - iso.Y; // +y is posterior (down)
            double half = s.CouchWidthMm / 2, t = s.CouchThicknessMm;
            var prof = CollisionSettings.CouchProfile;
            var xs = new List<double>(); var ys = new List<double>();
            // Right side from the top down, then the left side from the bottom up: a closed convex polygon.
            for (int i = 0; i < prof.Count; i++) { xs.Add(centreX + half * prof[i].HalfWidth); ys.Add(top + t * prof[i].Depth); }
            for (int i = prof.Count - 1; i >= 0; i--) { xs.Add(centreX - half * prof[i].HalfWidth); ys.Add(top + t * prof[i].Depth); }
            // Orientation for the inside test: make the vertex order consistent (positive signed area in x-right, y-down).
            double area = 0;
            for (int i = 0, j = xs.Count - 1; i < xs.Count; j = i++) area += xs[j] * ys[i] - xs[i] * ys[j];
            if (area < 0) { xs.Reverse(); ys.Reverse(); }
            return new CouchShape
            {
                Vx = xs.ToArray(), Vy = ys.ToArray(),
                ZMin = s.CouchHeadEndSupMm - s.CouchLengthMm - iso.Z, ZMax = s.CouchHeadEndSupMm - iso.Z,
                BoxMinX = xs.Min(), BoxMaxX = xs.Max(), BoxMinY = ys.Min(), BoxMaxY = ys.Max(),
            };
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

        /// <summary>Head sample points as flat arrays (the hot loop avoids enumerators and struct copies).</summary>
        private sealed class HeadArrays
        {
            public double[] X = Array.Empty<double>(), Y = Array.Empty<double>(), Z = Array.Empty<double>();
            public static HeadArrays From(IReadOnlyList<Vec3> pts) => new HeadArrays
            {
                X = pts.Select(p => p.X).ToArray(), Y = pts.Select(p => p.Y).ToArray(), Z = pts.Select(p => p.Z).ToArray(),
            };
        }

        private static double Clearance(HeadArrays head, double gantryDeg, double couchDeg, CouchShape couch)
        {
            // Head point at gantry 0 → room: rotate about z by θ (y-down frame: source at (sin θ, −cos θ, 0)).
            double g = gantryDeg * Math.PI / 180, cg = Math.Cos(g), sg = Math.Sin(g);
            // Room → couch frame: undo the couch rotation (counter-clockwise from above by φ, about the vertical y axis).
            // Couch rotation R(φ) maps couch → room in the top view (x right, z towards the gantry):
            // (x, z) → (x cos φ − z sin φ, x sin φ + z cos φ); its inverse is applied below.
            double c = couchDeg * Math.PI / 180, cc = Math.Cos(c), sc = Math.Sin(c);
            double best = double.PositiveInfinity;
            var hx = head.X; var hy = head.Y; var hz = head.Z;
            for (int i = 0; i < hx.Length; i++)
            {
                double x = hx[i] * cg - hy[i] * sg, y = hx[i] * sg + hy[i] * cg, z = hz[i];
                double xc = x * cc + z * sc, zc = -x * sc + z * cc;
                if (best > 0 && couch.BoxDistanceSquared(xc, y, zc) >= best * best) continue; // can't beat the current best
                double d = couch.SignedDistance(xc, y, zc);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// Points along the couch's long edges (at every profile vertex) and around its end profiles, 10 mm apart, that the
        /// head could reach, in the couch frame.
        /// </summary>
        private static List<Vec3> EdgeSamples(CouchShape b, CollisionSettings s)
        {
            double reach = Math.Sqrt(Math.Pow(s.HeadClearanceMm + s.HeadDepthMm, 2) + s.HeadRadiusMm * s.HeadRadiusMm) + 2 * s.WarningMm;
            var pts = new List<Vec3>();
            void Line(Vec3 p, Vec3 q)
            {
                int n = Math.Max(1, (int)Math.Ceiling((q - p).Length / 10.0));
                for (int k = 0; k <= n; k++)
                {
                    var v = p + (q - p) * ((double)k / n);
                    if (v.Length <= reach) pts.Add(v);
                }
            }
            int m = b.Vx.Length;
            for (int i = 0; i < m; i++)
            {
                Line(new Vec3(b.Vx[i], b.Vy[i], b.ZMin), new Vec3(b.Vx[i], b.Vy[i], b.ZMax));
                int j = (i + 1) % m;
                foreach (var z in new[] { b.ZMin, b.ZMax }) Line(new Vec3(b.Vx[i], b.Vy[i], z), new Vec3(b.Vx[j], b.Vy[j], z));
            }
            return pts;
        }

        /// <summary>Smallest signed distance from couch edge samples (couch frame) to the solid head cylinder.</summary>
        private static double EdgeClearance(HeadArrays pts, double gantryDeg, double couchDeg, CollisionSettings s)
        {
            double g = gantryDeg * Math.PI / 180, cg = Math.Cos(g), sg = Math.Sin(g);
            double c = couchDeg * Math.PI / 180, cc = Math.Cos(c), sc = Math.Sin(c);
            double d0 = s.HeadClearanceMm, d1 = s.HeadClearanceMm + s.HeadDepthMm, r0 = s.HeadRadiusMm;
            double best = double.PositiveInfinity;
            for (int i = 0; i < pts.X.Length; i++)
            {
                // Couch frame → room (couch rotation R(φ)), then room → head frame at gantry 0 (inverse gantry rotation).
                double xr = pts.X[i] * cc - pts.Z[i] * sc, zr = pts.X[i] * sc + pts.Z[i] * cc, yr = pts.Y[i];
                double hx = xr * cg + yr * sg, hy = -xr * sg + yr * cg;
                double a = -hy; // distance from the isocentre along the beam axis towards the source
                double r = Math.Sqrt(hx * hx + zr * zr);
                double d;
                if (a >= d0 && a <= d1 && r <= r0) d = -Math.Min(r0 - r, Math.Min(a - d0, d1 - a));
                else
                {
                    double dr = Math.Max(r - r0, 0), da = a < d0 ? d0 - a : a > d1 ? a - d1 : 0;
                    d = Math.Sqrt(dr * dr + da * da);
                }
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// Control-point gantry angles with gaps filled at ≤ 1°: in the arc's direction when known (clockwise = increasing
        /// angle), otherwise the shorter way between control points.
        /// </summary>
        private static IEnumerable<double> Densify(IReadOnlyList<double> angles, ArcDirection direction)
        {
            if (angles.Count == 0) yield break;
            yield return angles[0];
            for (int i = 1; i < angles.Count; i++)
            {
                double a = angles[i - 1], fwd = ((angles[i] - a) % 360 + 360) % 360;
                double delta = direction == ArcDirection.Clockwise ? fwd
                    : direction == ArcDirection.CounterClockwise ? (fwd == 0 ? 0 : fwd - 360)
                    : ((angles[i] - a) % 360 + 540) % 360 - 180;
                int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / GantryStepDeg));
                for (int k = 1; k <= n; k++) yield return a + delta * k / n;
            }
        }

        private static double Normalise(double deg) => ((deg % 360) + 360) % 360;
    }
}
