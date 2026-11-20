using System;

namespace SrsMcPsqa.Core.Geometry
{
    /// <summary>
    /// Rounds small shifts to zero, per axis: a component smaller in size than the threshold (mm) becomes 0, so the QA
    /// isocentre isn't moved by fractions of a millimetre that don't matter at measurement.
    /// </summary>
    public static class ShiftRounding
    {
        public static double Snap(double value, double thresholdMm) => thresholdMm > 0 && Math.Abs(value) < thresholdMm ? 0 : value;

        public static Vec3 Snap(Vec3 v, double thresholdMm) => new Vec3(Snap(v.X, thresholdMm), Snap(v.Y, thresholdMm), Snap(v.Z, thresholdMm));

        /// <summary>
        /// Array centre (patient coordinates) whose QA isocentre shift from the MapCHECK user origin (clinical iso − centre)
        /// has its small components rounded to zero.
        /// </summary>
        public static Vec3 SnapArrayCentre(Vec3 clinicalIso, Vec3 arrayCentre, double thresholdMm) =>
            clinicalIso - Snap(clinicalIso - arrayCentre, thresholdMm);
    }
}
