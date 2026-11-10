using System.Linq;
using SrsMcPsqa.Core.Collision;
using SrsMcPsqa.Core.Geometry;
using Xunit;

namespace SrsMcPsqa.Core.Tests
{
    public class CouchCollisionTests
    {
        // Head face 380 mm from iso, radius 400; couch 530 wide, 60 thick, top 110 mm below the origin, head end 200 mm sup.
        private static CollisionSettings S() => new CollisionSettings { OriginAboveCouchMm = 110 };

        private static BeamGeometry Static(double gantry, double couch = 0) => new BeamGeometry("B", couch, new[] { gantry });

        [Fact]
        public void LateralFieldClearsCouchEdge()
        {
            // Iso at the origin, gantry 270: head face at x = −380, couch edge at x = −265 → 115 mm.
            var r = CouchCollision.Check(Vec3.Zero, new[] { Static(270) }, S());
            Assert.Equal(115, r.MinClearanceMm, 0);
            Assert.Equal(ClearanceLevel.Clear, r.Level);
        }

        [Fact]
        public void IsoShiftedLeftBringsCouchCloseOnTheRight()
        {
            // Iso 100 mm to the phantom's left: couch right edge at x = −365 relative to iso → 15 mm at gantry 270.
            var r = CouchCollision.Check(new Vec3(100, 0, 0), new[] { Static(270) }, S());
            Assert.Equal(15, r.MinClearanceMm, 0);
            Assert.Equal(ClearanceLevel.Risk, r.Level);
            Assert.Equal(270, r.Worst!.GantryAtMin, 0);
        }

        [Fact]
        public void PosteriorFieldAndPosteriorShift()
        {
            // Gantry 180: head face 380 mm below iso; couch underside 170 mm below the origin.
            Assert.Equal(210, CouchCollision.Check(Vec3.Zero, new[] { Static(180) }, S()).MinClearanceMm, 0);
            // Iso 100 mm posterior: couch underside only 70 mm below iso → 310 mm.
            Assert.Equal(310, CouchCollision.Check(new Vec3(0, 100, 0), new[] { Static(180) }, S()).MinClearanceMm, 0);
        }

        [Fact]
        public void PosteriorObliqueWithLateralAndPosteriorShiftIsTheProblemCase()
        {
            var arc = new BeamGeometry("ARC", 0, Enumerable.Range(0, 181).Select(i => (181 + 2.0 * i) % 360).ToList());
            var centred = CouchCollision.Check(Vec3.Zero, new[] { arc }, S());
            var shifted = CouchCollision.Check(new Vec3(-60, 60, 0), new[] { arc }, S());
            // Centred: the head's edge passes the couch edge at ~6.5 cm (gantry ~120°/240°). Shifted right and posterior: < 5 cm.
            Assert.Equal(ClearanceLevel.Clear, centred.Level);
            Assert.Equal(ClearanceLevel.Warning, shifted.Level);
            Assert.InRange(shifted.Worst!.GantryAtMin, 90, 180); // the couch's left edge, near the head on that side
        }

        [Fact]
        public void CouchKickRotatesTheCouchUnderTheHead()
        {
            // Couch 90 (counter-clockwise from above): the couch body points to +x, its head end 200 mm towards −x.
            Assert.True(CouchCollision.Check(Vec3.Zero, new[] { Static(90, 90) }, S()).MinClearanceMm < 0);
            Assert.Equal(180, CouchCollision.Check(Vec3.Zero, new[] { Static(270, 90) }, S()).MinClearanceMm, 0);
        }

        [Fact]
        public void SuggestsTheSmallestLateralOffsetThatClears()
        {
            var arc = new BeamGeometry("ARC", 0, Enumerable.Range(0, 181).Select(i => (181 + 2.0 * i) % 360).ToList());
            var iso = new Vec3(100, 0, 0);
            Assert.Equal(ClearanceLevel.Risk, CouchCollision.Check(iso, new[] { arc }, S()).Level);
            // Within ±50 mm nothing fully clears (−1.5 cm at gantry 240°): the best is the full 50 mm to the right.
            var limited = CouchCollision.SuggestLateralOffset(iso, new[] { arc }, S())!;
            Assert.False(limited.Clears);
            Assert.Equal(-50, limited.OffsetMm, 0);
            Assert.Contains("Right", limited.Describe());
            // Allowing 100 mm, the smallest offset that clears 5 cm is towards the phantom's right, short of re-centring.
            var wide = S(); wide.MaxLateralOffsetMm = 100;
            var s = CouchCollision.SuggestLateralOffset(iso, new[] { arc }, wide)!;
            Assert.True(s.Clears);
            Assert.InRange(s.OffsetMm, -100, -55);
            Assert.True(s.Result.MinClearanceMm >= 50);
            Assert.Null(CouchCollision.SuggestLateralOffset(Vec3.Zero, new[] { arc }, S()));
        }

        [Fact]
        public void NotConfiguredUntilTheOriginHeightIsSet()
        {
            Assert.False(new CollisionSettings().IsConfigured);
            Assert.True(S().IsConfigured);
        }
    }
}
