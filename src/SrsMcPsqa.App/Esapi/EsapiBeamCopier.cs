using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace SrsMcPsqa.Esapi
{
    /// <summary>
    /// Copies treatment beams into a QA plan at a new isocentre, keeping machine, energy/fluence mode, dose rate,
    /// technique, MLC, gantry/collimator/couch angles, per-control-point jaws and leaves, meterset weights and beam IDs.
    /// Verified on Citrix (Phase 0) for SRS ARC VMAT beams (6X-FFF, HD MLC). ESAPI thread only.
    /// </summary>
    internal static class EsapiBeamCopier
    {
        /// <summary>"6X-FFF" → ("6X", "FFF"); "6X" → ("6X", null).</summary>
        public static (string Energy, string? Fluence) SplitEnergyMode(string displayName)
        {
            var i = displayName.IndexOf('-');
            return i < 0 ? (displayName, null) : (displayName.Substring(0, i), displayName.Substring(i + 1));
        }

        /// <summary>Copy all treatment beams in the clinical plan's treatment order and set that order on the QA plan.</summary>
        public static List<Beam> CopyAll(ExternalPlanSetup source, ExternalPlanSetup target, VVector isocenter, Action<string> log)
        {
            var ordered = source.BeamsInTreatmentOrder.Where(b => !b.IsSetupField).ToList();
            if (ordered.Count == 0) throw new InvalidOperationException("The clinical plan has no treatment beams.");
            var created = new List<Beam>();
            foreach (var b in ordered) created.Add(Copy(target, b, isocenter, log));

            target.SetTreatmentOrder(created);
            var want = ordered.Select(b => b.Id).ToList();
            var got = target.BeamsInTreatmentOrder.Where(b => !b.IsSetupField).Select(b => b.Id).ToList();
            if (!want.SequenceEqual(got))
                throw new InvalidOperationException($"Treatment order differs from the clinical plan ({string.Join(", ", got)} vs {string.Join(", ", want)}).");
            log($"Beams copied in clinical treatment order: {string.Join(", ", got)}.");
            return created;
        }

        public static Beam Copy(ExternalPlanSetup target, Beam src, VVector isocenter, Action<string> log)
        {
            var (energy, fluence) = SplitEnergyMode(src.EnergyModeDisplayName);
            var mp = new ExternalBeamMachineParameters(src.TreatmentUnit.Id, energy, src.DoseRate, src.Technique.Id, fluence);
            if (src.MLC != null) mp.MLCId = src.MLC.Id;

            var cps = src.ControlPoints.ToList();
            var cp0 = cps[0];
            var last = cps[cps.Count - 1];
            double coll = cp0.CollimatorAngle, g0 = cp0.GantryAngle, g1 = last.GantryAngle, couch = cp0.PatientSupportAngle;
            var weights = cps.Select(c => c.MetersetWeight).ToList();
            bool isArc = src.GantryDirection != GantryDirection.None;

            Beam nb;
            switch (src.MLCPlanType)
            {
                case MLCPlanType.VMAT:
                    nb = target.AddVMATBeam(mp, weights, coll, g0, g1, src.GantryDirection, couch, isocenter); break;
                case MLCPlanType.ArcDynamic:
                    nb = target.AddConformalArcBeam(mp, coll, cps.Count, g0, g1, src.GantryDirection, couch, isocenter); break;
                case MLCPlanType.DoseDynamic:
                    nb = target.AddSlidingWindowBeam(mp, weights, coll, g0, couch, isocenter); break;
                case MLCPlanType.Static when isArc:
                    nb = target.AddMLCArcBeam(mp, cp0.LeafPositions, cp0.JawPositions, coll, g0, g1, src.GantryDirection, couch, isocenter); break;
                case MLCPlanType.Static when cps.Count > 2:
                    nb = target.AddMultipleStaticSegmentBeam(mp, weights, coll, g0, couch, isocenter); break;
                case MLCPlanType.Static when src.MLC != null:
                    nb = target.AddMLCBeam(mp, cp0.LeafPositions, cp0.JawPositions, coll, g0, couch, isocenter); break;
                default:
                    nb = isArc
                        ? target.AddArcBeam(mp, cp0.JawPositions, coll, g0, g1, src.GantryDirection, couch, isocenter)
                        : target.AddStaticBeam(mp, cp0.JawPositions, coll, g0, couch, isocenter);
                    break;
            }

            var ep = nb.GetEditableParameters();
            var dst = ep.ControlPoints.ToList();
            if (dst.Count != cps.Count)
                throw new InvalidOperationException($"Beam '{src.Id}': the copy has {dst.Count} control points, the clinical beam {cps.Count}.");
            for (int i = 0; i < cps.Count; i++)
            {
                dst[i].JawPositions = cps[i].JawPositions;
                if (src.MLC != null && cps[i].LeafPositions != null && cps[i].LeafPositions.Length > 0)
                    dst[i].LeafPositions = cps[i].LeafPositions;
            }
            nb.ApplyParameters(ep);

            var newWeights = nb.ControlPoints.Select(c => c.MetersetWeight).ToList();
            double maxDiff = newWeights.Zip(weights, (a, b) => Math.Abs(a - b)).DefaultIfEmpty(0).Max();
            if (maxDiff > 1e-4)
            {
                // Conformal arcs get Eclipse-generated weights; anything else must match exactly.
                if (src.MLCPlanType != MLCPlanType.ArcDynamic)
                    throw new InvalidOperationException($"Beam '{src.Id}': meterset weights differ from the clinical beam (max {maxDiff:G3}).");
                log($"Beam '{src.Id}': conformal-arc meterset weights differ slightly from the clinical beam (max {maxDiff:G3}).");
            }

            nb.Id = src.Id;
            log($"Beam '{src.Id}' copied ({src.MLCPlanType}, {cps.Count} control points, couch {couch:0.#}°, collimator {coll:0.#}°).");
            return nb;
        }
    }
}
