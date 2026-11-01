using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace SrsMcPsqa.Spike
{
    /// <summary>
    /// Copies one treatment beam (machine, energy, dose rate, technique, MLC, gantry/collimator/couch,
    /// jaws, leaf positions per control point, meterset weights) into a target plan at a new isocentre.
    /// Runs on the ESAPI thread only.
    /// </summary>
    internal static class BeamCopier
    {
        /// <summary>"6X-FFF" -> ("6X", "FFF"); "6X" -> ("6X", null). VERIFY: null vs "" for standard fluence.</summary>
        public static (string Energy, string? Fluence) SplitEnergyMode(string displayName)
        {
            var i = displayName.IndexOf('-');
            return i < 0 ? (displayName, null) : (displayName.Substring(0, i), displayName.Substring(i + 1));
        }

        public static Beam Copy(ExternalPlanSetup target, Beam src, VVector isocenter, Action<string> log)
        {
            var (energy, fluence) = SplitEnergyMode(src.EnergyModeDisplayName);
            // VERIFY: ExternalBeamMachineParameters(machineId, energyModeId, doseRate, techniqueId, primaryFluenceModeId)
            var mp = new ExternalBeamMachineParameters(src.TreatmentUnit.Id, energy, src.DoseRate, src.Technique.Id, fluence);
            if (src.MLC != null && Reflect.TrySetProperty(mp, "MLCId", src.MLC.Id, log))
                log($"[INFO]   MachineParameters.MLCId set to '{src.MLC.Id}'.");

            var cps = src.ControlPoints.ToList();
            var cp0 = cps[0];
            var last = cps[cps.Count - 1];
            double coll = cp0.CollimatorAngle, g0 = cp0.GantryAngle, g1 = last.GantryAngle, couch = cp0.PatientSupportAngle;
            var weights = cps.Select(c => c.MetersetWeight).ToList();
            bool isArc = src.GantryDirection != GantryDirection.None;

            log($"[INFO]   Source beam '{src.Id}': machine={src.TreatmentUnit.Id}, energy={energy}, fluence={fluence ?? "(standard)"}, " +
                $"doseRate={src.DoseRate}, technique={src.Technique.Id}, MLCPlanType={src.MLCPlanType}, MLC={src.MLC?.Id ?? "none"}, " +
                $"CPs={cps.Count}, gantry {g0:F1}->{g1:F1} {src.GantryDirection}, coll={coll:F1}, couch={couch:F1}");

            Beam nb;
            string method;
            switch (src.MLCPlanType)
            {
                case MLCPlanType.VMAT:
                    method = "AddVMATBeam";
                    nb = target.AddVMATBeam(mp, weights, coll, g0, g1, src.GantryDirection, couch, isocenter);
                    break;
                case MLCPlanType.ArcDynamic:
                    method = "AddConformalArcBeam";
                    nb = target.AddConformalArcBeam(mp, coll, cps.Count, g0, g1, src.GantryDirection, couch, isocenter);
                    break;
                case MLCPlanType.DoseDynamic:
                    method = "AddSlidingWindowBeam";
                    nb = target.AddSlidingWindowBeam(mp, weights, coll, g0, couch, isocenter);
                    break;
                case MLCPlanType.Static when isArc:
                    method = "AddMLCArcBeam";
                    nb = target.AddMLCArcBeam(mp, cp0.LeafPositions, cp0.JawPositions, coll, g0, g1, src.GantryDirection, couch, isocenter);
                    break;
                case MLCPlanType.Static when cps.Count > 2:
                    method = "AddMultipleStaticSegmentBeam";
                    nb = target.AddMultipleStaticSegmentBeam(mp, weights, coll, g0, couch, isocenter);
                    break;
                case MLCPlanType.Static when src.MLC != null:
                    method = "AddMLCBeam";
                    nb = target.AddMLCBeam(mp, cp0.LeafPositions, cp0.JawPositions, coll, g0, couch, isocenter);
                    break;
                default:
                    method = isArc ? "AddArcBeam" : "AddStaticBeam";
                    nb = isArc
                        ? target.AddArcBeam(mp, cp0.JawPositions, coll, g0, g1, src.GantryDirection, couch, isocenter)
                        : target.AddStaticBeam(mp, cp0.JawPositions, coll, g0, couch, isocenter);
                    break;
            }
            log($"[PASS]   {method} created beam '{nb.Id}' with {nb.ControlPoints.Count()} control points.");

            // Copy jaws and leaves control point by control point.
            var ep = nb.GetEditableParameters();
            var dst = ep.ControlPoints.ToList();
            if (dst.Count != cps.Count)
                log($"[WARN]   Control point count differs: source {cps.Count}, new {dst.Count}. Copying the first {Math.Min(dst.Count, cps.Count)}.");
            int n = Math.Min(dst.Count, cps.Count);
            for (int i = 0; i < n; i++)
            {
                dst[i].JawPositions = cps[i].JawPositions;
                if (src.MLC != null && cps[i].LeafPositions != null && cps[i].LeafPositions.Length > 0)
                    dst[i].LeafPositions = cps[i].LeafPositions;
            }
            nb.ApplyParameters(ep);
            log($"[PASS]   Jaws/leaves applied to {n} control points.");

            // Check meterset weights match (AddConformalArcBeam generates its own).
            var newWeights = nb.ControlPoints.Select(c => c.MetersetWeight).ToList();
            double maxDiff = 0;
            for (int i = 0; i < Math.Min(newWeights.Count, weights.Count); i++)
                maxDiff = Math.Max(maxDiff, Math.Abs(newWeights[i] - weights[i]));
            log(maxDiff < 1e-4
                ? "[PASS]   Meterset weights match source."
                : $"[WARN]   Meterset weights differ from source (max |diff| = {maxDiff:G4}).");

            try
            {
                nb.Id = src.Id;
                log($"[PASS]   Beam ID set to '{nb.Id}'.");
            }
            catch (Exception ex)
            {
                log($"[FAIL]   Setting beam ID '{src.Id}' threw: {ex.Message}");
            }
            return nb;
        }

        /// <summary>Preset MUs keyed by beam ID, taken from the clinical plan's beams.</summary>
        public static List<KeyValuePair<string, MetersetValue>> PresetMus(IEnumerable<Beam> clinicalBeams, IEnumerable<string> beamIds)
        {
            var wanted = new HashSet<string>(beamIds);
            return clinicalBeams
                .Where(b => !b.IsSetupField && wanted.Contains(b.Id))
                .Select(b => new KeyValuePair<string, MetersetValue>(b.Id, b.Meterset))
                .ToList();
        }
    }
}
