using System.Collections.Generic;
using System.Linq;
using SrsMcPsqa.Core.Detector;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Optimisation;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.Core.Models
{
    /// <summary>Builds service requests for a planned array plane, using the sign convention in <see cref="IsoGeometry"/>.</summary>
    public static class QaPlanRequests
    {
        /// <summary>Margin beyond the array edge to cover the fine-tune search (mm).</summary>
        public const double DoseInPlaneMargin = 20.0;

        /// <summary>Half-thickness of the returned dose slab along the array normal (mm).</summary>
        public const double DoseNormalHalfThickness = 15.0;

        /// <summary>QA plan request for <paramref name="plane"/>: isocentre O + (I_clin − P) and a dose region around the array.</summary>
        public static QaPlanRequest Create(ArrayPlane plane, Vec3 clinicalIso, PhantomImageInfo phantom, string courseId, string planId,
            IEnumerable<string> assignedPtvIds) => new QaPlanRequest
            {
                QaCourseId = courseId,
                PlanId = planId,
                Orientation = plane.Orientation,
                StructureSetUid = phantom.StructureSetUid,
                AssignedPtvIds = assignedPtvIds.ToList(),
                Isocentre = IsoGeometry.QaIsocentre(phantom.UserOrigin, clinicalIso, plane.Centre),
                DoseRegion = DoseRegion(plane.Orientation, phantom.UserOrigin),
            };

        /// <summary>Phantom region to return dose for: array ± margin in-plane, ± 15 mm along the normal, centred on the user origin.</summary>
        public static Box3 DoseRegion(ArrayOrientation o, Vec3 userOrigin)
        {
            double inPlane = DiodeArray.HalfExtent + DoseInPlaneMargin;
            var half = o.FromPlane(inPlane, inPlane, DoseNormalHalfThickness);
            return new Box3(userOrigin - half, userOrigin + half);
        }

        /// <summary>Fine-tune request for a calculated QA plan.</summary>
        public static FineTuneRequest FineTune(QaPlanResult plan, Vec3 clinicalIso, PhantomImageInfo phantom,
            IReadOnlyList<PlanningTarget> assignedTargets, double[]? bestScores = null) => new FineTuneRequest
            {
                Orientation = plan.Orientation,
                PhantomUserOrigin = phantom.UserOrigin,
                ClinicalIsocentre = clinicalIso,
                CurrentQaIsocentre = plan.Isocentre,
                QaDose = plan.Dose ?? throw new System.InvalidOperationException($"Plan '{plan.PlanId}' has no calculated dose."),
                Targets = assignedTargets,
                BestScores = bestScores,
            };

        /// <summary>The calculated QA dose expressed in patient coordinates: D_pat(p) = D_qa(p + (I_ph − I_clin)).</summary>
        public static DoseGrid DoseInPatientFrame(DoseGrid qaDose, Vec3 qaIso, Vec3 clinicalIso) =>
            qaDose.Translate(-(qaIso - clinicalIso));
    }
}
