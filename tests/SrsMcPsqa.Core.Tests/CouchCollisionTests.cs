using System.Linq;
using SrsMcPsqa.Core.Collision;
using SrsMcPsqa.Core.Geometry;
using Xunit;

namespace SrsMcPsqa.Core.Tests
{
    public class CouchCollisionTests
    {
        // Defaults: head face 415 mm from iso, radius 385; couch 530 wide, 60 thick, head end 200 mm sup.
        // Test setup: couch top 110 mm below the MapCHECK user origin.
        private static CollisionSettings S() => new CollisionSettings { OriginAboveCouchMm = 110, CouchHeadEndSupMm = 200 };

        private static BeamGeometry Static(double gantry, double couch = 0) => new BeamGeometry("B", couch, new[] { gantry });
        private static BeamGeometry FullArc() => new BeamGeometry("ARC", 0, Enumerable.Range(0, 181).Select(i => (181 + 2.0 * i) % 360).ToList());

        [Fact]
        public void LateralFieldClearsCouchEdge()
        {
            // Iso at the origin, gantry 270: head face at x = −415, couch edge at x = −265 → 150 mm.
            var r = CouchCollision.Check(Vec3.Zero, new[] { Static(270) }, S());
            Assert.Equal(150, r.MinClearanceMm, 0);
            Assert.Equal(ClearanceLevel.Clear, r.Level);
        }

        [Fact]
        public void IsoShiftedLeftBringsCouchCloseOnTheRight()
        {
            // Iso 140 mm to the phantom's left: couch right edge at x = −405 relative to iso → 10 mm at gantry 270.
            var r = CouchCollision.Check(new Vec3(140, 0, 0), new[] { Static(270) }, S());
            Assert.Equal(10, r.MinClearanceMm, 0);
            Assert.Equal(ClearanceLevel.Risk, r.Level);
            Assert.Equal(270, r.Worst!.GantryAtMin, 0);
        }

        [Fact]
        public void PosteriorFieldAndPosteriorShift()
        {
            // Gantry 180: head face 415 mm below iso; couch underside 170 mm below the origin.
            Assert.Equal(245, CouchCollision.Check(Vec3.Zero, new[] { Static(180) }, S()).MinClearanceMm, 0);
            // Iso 100 mm posterior: couch underside only 70 mm below iso → 345 mm.
            Assert.Equal(345, CouchCollision.Check(new Vec3(0, 100, 0), new[] { Static(180) }, S()).MinClearanceMm, 0);
        }

        [Fact]
        public void LateralAndPosteriorShiftIsTheProblemCase()
        {
            var centred = CouchCollision.Check(Vec3.Zero, new[] { FullArc() }, S());
            var shifted = CouchCollision.Check(new Vec3(120, 60, 0), new[] { FullArc() }, S());
            // Centred: ~10 cm at the posterior obliques. 12 cm left and 6 cm posterior: the head edge meets the couch's right edge.
            Assert.Equal(ClearanceLevel.Clear, centred.Level);
            Assert.Equal(ClearanceLevel.Risk, shifted.Level);
            Assert.InRange(shifted.Worst!.GantryAtMin, 180, 270);
        }

        [Fact]
        public void CouchKickRotatesTheCouchUnderTheHead()
        {
            // Couch 90 (counter-clockwise from above): the couch body points to +x, its head end 200 mm towards −x.
            Assert.True(CouchCollision.Check(Vec3.Zero, new[] { Static(90, 90) }, S()).MinClearanceMm < 0);
            Assert.Equal(215, CouchCollision.Check(Vec3.Zero, new[] { Static(270, 90) }, S()).MinClearanceMm, 0);
        }

        [Fact]
        public void SuggestsTheSmallestLateralOffsetThatClears()
        {
            var iso = new Vec3(100, 0, 0);
            Assert.Equal(ClearanceLevel.Risk, CouchCollision.Check(iso, new[] { FullArc() }, S()).Level);
            var s = CouchCollision.SuggestLateralOffset(iso, new[] { FullArc() }, S())!;
            Assert.True(s.Clears);
            Assert.Equal(-45, s.OffsetMm, 0); // 45 mm towards the phantom's right
            Assert.True(s.Result.MinClearanceMm >= 50);
            Assert.Contains("Right", s.Describe());
            Assert.Null(CouchCollision.SuggestLateralOffset(Vec3.Zero, new[] { FullArc() }, S()));
        }

        [Fact]
        public void BestOffsetWhenNothingWithinTheLimitClears()
        {
            var s = CouchCollision.SuggestLateralOffset(new Vec3(140, 0, 0), new[] { FullArc() }, S())!;
            Assert.False(s.Clears);
            Assert.Equal(-50, s.OffsetMm, 0);
        }

        [Theory]
        [InlineData(120, 30)]
        [InlineData(240, 315)]
        [InlineData(60, 90)]
        [InlineData(200, 10)]
        public void HeadPositionMatchesOtherEsapiCollisionScripts(double gantry, double couch)
        {
            // LDClark/PlanCheck (CollisionCalculator.AddCylinderToMesh), in patient coordinates (HFS), with couch angle φ and
            // gantry θ: head face centre = iso + d·(cos φ sin θ, −cos θ, −sin φ sin θ). zackmorelli/CollisionCheck uses the
            // same source position (1000·sin θ, −1000·cos θ) and couch rotation. Put a 2 mm "couch" on that point: it must
            // touch the head face here, and be far away with the couch angle mirrored.
            double d = 415, t = gantry * System.Math.PI / 180, p = couch * System.Math.PI / 180;
            var c = new Vec3(d * System.Math.Cos(p) * System.Math.Sin(t), -d * System.Math.Cos(t), -d * System.Math.Sin(p) * System.Math.Sin(t));
            CollisionSettings Box(Vec3 centre) => new CollisionSettings
            {
                CouchWidthMm = 2, CouchThicknessMm = 2, CouchLengthMm = 2,
                PhantomLateralOffsetMm = -centre.X, OriginAboveCouchMm = centre.Y - 1, CouchHeadEndSupMm = centre.Z + 1,
            };
            // The test box sits above the origin for some angles (negative height); allow that here.
            var s = Box(c);
            var at = CouchCollision.Check(Vec3.Zero, new[] { Static(gantry, couch) }, s, s.PhantomLateralOffsetMm);
            Assert.InRange(at.MinClearanceMm, -1.8, 0.5); // a 2 mm cube straddling the face: overlap at most √3 mm
            var mirrored = CouchCollision.Check(Vec3.Zero, new[] { Static(gantry, -couch) }, s, s.PhantomLateralOffsetMm);
            if (System.Math.Abs(System.Math.Sin(p) * System.Math.Sin(t)) > 0.2) Assert.True(mirrored.MinClearanceMm > 100);
        }

        [Fact]
        public void ConformalArcWithOnlyStartAndStopFollowsItsDirection()
        {
            // 181° → 179° clockwise passes 270° (couch edge 150 mm away); taken the short way it would only see ~180°.
            var cw = new BeamGeometry("DCA", 0, new[] { 181.0, 179.0 }, ArcDirection.Clockwise);
            var r = CouchCollision.Check(new Vec3(140, 0, 0), new[] { cw }, S());
            Assert.Equal(ClearanceLevel.Risk, r.Level);
            var shortWay = new BeamGeometry("DCA", 0, new[] { 181.0, 179.0 });
            Assert.True(CouchCollision.Check(new Vec3(140, 0, 0), new[] { shortWay }, S()).MinClearanceMm > r.MinClearanceMm + 50);
            // Counter-clockwise from 90° to 270° goes over the top (through 0°): never under the couch.
            var ccw = new BeamGeometry("DCA", 0, new[] { 90.0, 270.0 }, ArcDirection.CounterClockwise);
            Assert.InRange(CouchCollision.Check(Vec3.Zero, new[] { ccw }, S()).Worst!.GantryAtMin, 0, 360);
            Assert.Equal(150, CouchCollision.Check(Vec3.Zero, new[] { ccw }, S()).MinClearanceMm, 0);
        }

        [Fact]
        public void NotConfiguredUntilTheOriginHeightIsSet()
        {
            Assert.False(new CollisionSettings { OriginAboveCouchMm = 0 }.IsConfigured);
            Assert.True(S().IsConfigured);
            // Department setup defaults: array centre 200 mm above the couch top, overhanging the couch end by 60 mm.
            var d = new CollisionSettings();
            Assert.True(d.IsConfigured);
            Assert.Equal(200, d.OriginAboveCouchMm);
            Assert.Equal(-60, d.CouchHeadEndSupMm);
        }
    }
}
