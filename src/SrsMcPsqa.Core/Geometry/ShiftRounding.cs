using System;

namespace SrsMcPsqa.Core.Geometry
{
    /// <summary>
    /// Rounding of QA isocentre shifts, per axis (Settings):
    /// <list type="bullet">
    /// <item><b>Round to</b> (<c>roundToMm</c>): every shift from the MapCHECK user origin is rounded to the nearest multiple
    /// (default 1 mm; 0.5 mm etc. possible; 0 = off), so the shift set at the linac is the shift the plan was calculated with.</item>
    /// <item><b>Threshold</b> (<c>thresholdMm</c>): a component smaller than this becomes 0 (in planning, the shift from the
    /// origin; in fine-tune, the change in position). 0 = off.</item>
    /// </list>
    /// </summary>
    public static class ShiftRounding
    {
        private const double Eps = 1e-9;

        /// <summary>Nearest multiple of <paramref name="roundToMm"/> (halves away from zero); unchanged when ≤ 0.</summary>
        public static double Round(double value, double roundToMm)
        {
            if (roundToMm <= 0) return value;
            double r = Math.Round(value / roundToMm, MidpointRounding.AwayFromZero) * roundToMm;
            return Math.Round(r, 6) + 0.0; // tidy binary noise (0.30000000000000004); + 0.0 turns −0 into 0
        }

        public static Vec3 Round(Vec3 v, double roundToMm) => new Vec3(Round(v.X, roundToMm), Round(v.Y, roundToMm), Round(v.Z, roundToMm));

        /// <summary>Zero when smaller than the threshold, otherwise unchanged.</summary>
        public static double Threshold(double value, double thresholdMm) => thresholdMm > 0 && Math.Abs(value) < thresholdMm - Eps ? 0 : value;

        public static Vec3 Threshold(Vec3 v, double thresholdMm) => new Vec3(Threshold(v.X, thresholdMm), Threshold(v.Y, thresholdMm), Threshold(v.Z, thresholdMm));

        /// <summary>A shift from the MapCHECK origin: rounded, then zero if (as rounded) it's below the threshold.</summary>
        public static double Snap(double value, double thresholdMm, double roundToMm = 0) => Threshold(Round(value, roundToMm), thresholdMm);

        public static Vec3 Snap(Vec3 v, double thresholdMm, double roundToMm = 0) =>
            new Vec3(Snap(v.X, thresholdMm, roundToMm), Snap(v.Y, thresholdMm, roundToMm), Snap(v.Z, thresholdMm, roundToMm));

        /// <summary>
        /// Array centre (patient coordinates) whose QA isocentre shift from the MapCHECK user origin (clinical iso − centre)
        /// is snapped (<see cref="Snap(Vec3,double,double)"/>).
        /// </summary>
        public static Vec3 SnapArrayCentre(Vec3 clinicalIso, Vec3 arrayCentre, double thresholdMm, double roundToMm = 0) =>
            clinicalIso - Snap(clinicalIso - arrayCentre, thresholdMm, roundToMm);

        /// <summary>
        /// Snapped shift for a small step from <paramref name="current"/> towards <paramref name="target"/> (arrow buttons).
        /// A step that would snap straight back to the current shift goes one rounding step further, or out to the
        /// threshold, so the arrows never get stuck.
        /// </summary>
        public static Vec3 Step(Vec3 current, Vec3 target, double thresholdMm, double roundToMm) =>
            new Vec3(StepAxis(current.X, target.X, thresholdMm, roundToMm), StepAxis(current.Y, target.Y, thresholdMm, roundToMm),
                     StepAxis(current.Z, target.Z, thresholdMm, roundToMm));

        private static double StepAxis(double current, double target, double thresholdMm, double roundToMm)
        {
            double s = Snap(target, thresholdMm, roundToMm);
            if (Math.Abs(target - current) < Eps || Math.Abs(s - current) > Eps) return s;
            int dir = Math.Sign(target - current);
            if (roundToMm > 0)
            {
                s = Snap(Round(current, roundToMm) + dir * roundToMm, thresholdMm, roundToMm);
                if (Math.Abs(s - current) > Eps) return s;
            }
            // Still stuck below the threshold: go to the threshold (rounded up to the rounding step).
            double t = roundToMm > 0 ? Math.Ceiling(thresholdMm / roundToMm - Eps) * roundToMm : thresholdMm;
            s = dir * t;
            return Math.Abs(s - current) > Eps ? s : current;
        }

        /// <summary>
        /// Fine-tune: the array move to apply for a wanted move, given the plan's current shift from the MapCHECK origin.
        /// A component of the change smaller than the threshold becomes 0 (that axis stays as it is); otherwise the
        /// resulting shift from the origin is rounded, and the move is whatever gets there. Array moves by m → the shift
        /// from the origin changes by −m.
        /// </summary>
        public static Vec3 FineTuneMove(Vec3 currentShiftFromOrigin, Vec3 wantedMove, double thresholdMm, double roundToMm) =>
            new Vec3(MoveAxis(currentShiftFromOrigin.X, wantedMove.X, thresholdMm, roundToMm),
                     MoveAxis(currentShiftFromOrigin.Y, wantedMove.Y, thresholdMm, roundToMm),
                     MoveAxis(currentShiftFromOrigin.Z, wantedMove.Z, thresholdMm, roundToMm));

        private static double MoveAxis(double shift, double move, double thresholdMm, double roundToMm)
        {
            if (Threshold(move, thresholdMm) == 0) return 0;
            double m = shift - Round(shift - move, roundToMm);
            return Math.Abs(m) < Eps ? 0 : Math.Round(m, 6) + 0.0;
        }
    }
}
