using System;
using System.Collections.Generic;
using System.Globalization;

namespace SrsMcPsqa.Core.Geometry
{
    /// <summary>
    /// The single place that defines how a QA plan isocentre positions the patient's anatomy relative to the array.
    /// <para>
    /// Notation (all DICOM coordinates, mm):
    /// <c>O</c> = phantom image user origin (= centre of the detector array);
    /// <c>I_clin</c> = clinical plan isocentre (patient image);
    /// <c>I_ph</c> = QA plan isocentre (phantom image);
    /// <c>P</c> = patient point that lands on the array centre.
    /// </para>
    /// <para>
    /// A QA plan with isocentre <c>I_ph</c> delivers the clinical beams relative to <c>I_ph</c> exactly as they were relative to
    /// <c>I_clin</c>, so patient point <c>p</c> maps to phantom point <c>p + (I_ph − I_clin)</c>.
    /// Requiring <c>P</c> to map to <c>O</c> gives <c>I_ph = O + (I_clin − P)</c>.
    /// </para>
    /// </summary>
    public static class IsoGeometry
    {
        /// <summary>QA isocentre that puts patient point <paramref name="arrayCentreInPatient"/> on the array centre: <c>O + (I_clin − P)</c>.</summary>
        public static Vec3 QaIsocentre(Vec3 phantomUserOrigin, Vec3 clinicalIso, Vec3 arrayCentreInPatient) =>
            phantomUserOrigin + (clinicalIso - arrayCentreInPatient);

        /// <summary>Patient point on the array centre for a QA isocentre: <c>P = O + I_clin − I_ph</c>.</summary>
        public static Vec3 ArrayCentreInPatient(Vec3 phantomUserOrigin, Vec3 clinicalIso, Vec3 qaIso) =>
            phantomUserOrigin + clinicalIso - qaIso;

        /// <summary>Phantom position of patient point <paramref name="p"/>: <c>p + (I_ph − I_clin)</c>.</summary>
        public static Vec3 PatientToPhantom(Vec3 p, Vec3 clinicalIso, Vec3 qaIso) => p + (qaIso - clinicalIso);

        /// <summary>Patient position of phantom point <paramref name="q"/>: <c>q − (I_ph − I_clin)</c>.</summary>
        public static Vec3 PhantomToPatient(Vec3 q, Vec3 clinicalIso, Vec3 qaIso) => q - (qaIso - clinicalIso);

        /// <summary>
        /// Change of QA isocentre needed to move the array centre from patient point <paramref name="from"/> to
        /// <paramref name="to"/>: <c>ΔI_ph = from − to</c> (moving the array one way relative to the patient
        /// means moving the isocentre the other way).
        /// </summary>
        public static Vec3 IsoShiftForArrayMove(Vec3 from, Vec3 to) => from - to;
    }

    /// <summary>
    /// Human-readable shift labels. A shift is always described as the
    /// <b>array position relative to the patient</b>, in patient directions (HFS patient coordinates):
    /// +X = Left, −X = Right, +Y = Post, −Y = Ant, +Z = Sup, −Z = Inf.
    /// </summary>
    public static class ShiftLabels
    {
        /// <summary>Direction word for a signed component along a patient axis.</summary>
        public static string Direction(int axis, double value) => axis switch
        {
            0 => value >= 0 ? "Left" : "Right",
            1 => value >= 0 ? "Post" : "Ant",
            2 => value >= 0 ? "Sup" : "Inf",
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

        /// <summary>
        /// e.g. "Left 2.0 mm, Ant 1.5 mm, Sup 0.0 mm" for an array move of (+2, −1.5, 0).
        /// Components smaller than <paramref name="zeroTol"/> are shown as 0.0 without a direction flip.
        /// </summary>
        public static string Describe(Vec3 arrayMoveRelativeToPatient, double zeroTol = 0.05)
        {
            var parts = new List<string>(3);
            for (int axis = 0; axis < 3; axis++)
            {
                double v = arrayMoveRelativeToPatient[axis];
                if (Math.Abs(v) < zeroTol) v = 0;
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0} mm", Direction(axis, v), Math.Abs(v)));
            }
            return string.Join(", ", parts);
        }
    }
}
