using System;
using System.Collections.Generic;
using System.Linq;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.Core.Models
{
    /// <summary>
    /// The 5×5 constancy field plan made with each PSQA: per array orientation used, one plan on that phantom image with a
    /// 5 × 5 cm jaw field (MLC retracted, collimator 0°, couch 0°) per energy of the clinical plan, 100 MU each, at the
    /// clinical dose rate, isocentre at the MapCHECK user origin (no shift). Gantry 0° on the 0° image (array horizontal),
    /// 90° on the 270° image (array vertical). Couch clearance isn't checked: the position is fixed and always clear.
    /// </summary>
    public static class ReferenceField
    {
        public const double FieldSizeMm = 50;
        public const double MonitorUnits = 100;
        public const string DefaultIdPattern = "MC_5x5_{orient}";

        /// <summary>Gantry angle (IEC 61217) of the field for an array orientation.</summary>
        public static double GantryAngle(ArrayOrientation o) => o == ArrayOrientation.Deg270 ? 90 : 0;

        /// <summary>
        /// Plan ID from the pattern (<c>{orient}</c> → 0 or 270), e.g. <c>MC_5x5_0</c>. If taken, a letter is added
        /// (<c>MC_5x5_0a</c>, <c>MC_5x5_0b</c>, …). Cut to 13 characters; case-insensitive, like Eclipse.
        /// </summary>
        public static string PlanId(string? pattern, ArrayOrientation o, IEnumerable<string> existingIds)
        {
            var existing = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
            string p = string.IsNullOrWhiteSpace(pattern) ? DefaultIdPattern : pattern!.Trim();
            string id = p.Replace("{orient}", o.ShortLabel()).Replace("{n}", "");
            if (id.Length > QaPlanNaming.MaxIdLength) id = id.Substring(0, QaPlanNaming.MaxIdLength);
            if (!existing.Contains(id)) return id;
            string stem = id.Length >= QaPlanNaming.MaxIdLength ? id.Substring(0, QaPlanNaming.MaxIdLength - 1) : id;
            for (char c = 'a'; c <= 'z'; c++)
                if (!existing.Contains(stem + c)) return stem + c;
            throw new InvalidOperationException($"Could not find a free plan ID for '{id}'.");
        }

        /// <summary>Beam ID of the field for an energy, e.g. "5x5 6X-FFF" (Eclipse limit 16 characters).</summary>
        public static string BeamId(string energyDisplayName)
        {
            string id = "5x5 " + energyDisplayName.Trim();
            return id.Length <= 16 ? id : id.Substring(0, 16);
        }

        /// <summary>Request for the orientation's 5×5 plan: isocentre at the user origin, same dose region as the QA plans.</summary>
        public static QaPlanRequest Request(PhantomImageInfo phantom, string courseId, string planId) => new QaPlanRequest
        {
            QaCourseId = courseId,
            PlanId = planId,
            Orientation = phantom.Orientation,
            StructureSetUid = phantom.StructureSetUid,
            AssignedPtvIds = Array.Empty<string>(),
            Isocentre = phantom.UserOrigin,
            DoseRegion = QaPlanRequests.DoseRegion(phantom.Orientation, phantom.UserOrigin),
            IsReferenceField = true,
        };

        /// <summary>Orientations needing a 5×5 plan, in a fixed order (0° then 270°).</summary>
        public static IReadOnlyList<ArrayOrientation> Orientations(IEnumerable<ArrayOrientation> qaPlanOrientations) =>
            qaPlanOrientations.Distinct().OrderBy(o => o == ArrayOrientation.Deg270 ? 1 : 0).ToList();
    }
}
