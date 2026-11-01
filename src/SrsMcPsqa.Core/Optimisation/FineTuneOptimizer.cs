using System;
using System.Collections.Generic;
using System.Linq;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;

namespace SrsMcPsqa.Core.Optimisation
{
    /// <summary>Inputs for fine-tuning one calculated QA plan (section 6.6).</summary>
    public sealed class FineTuneRequest
    {
        public ArrayOrientation Orientation { get; set; }

        /// <summary>Phantom image user origin = array centre (phantom DICOM coordinates).</summary>
        public Vec3 PhantomUserOrigin { get; set; }

        public Vec3 ClinicalIsocentre { get; set; }

        /// <summary>Current QA plan isocentre (phantom DICOM coordinates).</summary>
        public Vec3 CurrentQaIsocentre { get; set; }

        /// <summary>Calculated QA dose in phantom coordinates (Gy).</summary>
        public DoseGrid QaDose { get; set; } = null!;

        /// <summary>PTVs assigned to this plan, in patient coordinates, each with its own Rx.</summary>
        public IReadOnlyList<PlanningTarget> Targets { get; set; } = Array.Empty<PlanningTarget>();

        /// <summary>Optional J_k,best for <see cref="Targets"/> (same order) from the pre-calculation analysis, for coverage flags.</summary>
        public double[]? BestScores { get; set; }
    }

    /// <summary>Proposed isocentre shift for one QA plan. Predicted metrics are approximations (translated dose).</summary>
    public sealed class FineTuneProposal
    {
        public ArrayPlane CurrentPlane { get; set; }
        public ArrayPlane ProposedPlane { get; set; }

        /// <summary>Change of QA isocentre, Δ = I_ph,new − I_ph (phantom coordinates).</summary>
        public Vec3 IsoShift { get; set; }

        public Vec3 CurrentQaIsocentre { get; set; }
        public Vec3 ProposedQaIsocentre { get; set; }

        /// <summary>Array move relative to the patient, P_new − P (= −Δ).</summary>
        public Vec3 ArrayMoveRelativeToPatient => ProposedPlane.Centre - CurrentPlane.Centre;

        /// <summary>e.g. "Left 2.0 mm, Ant 0.0 mm, Sup 1.5 mm" (array relative to patient).</summary>
        public string ArrayMoveDescription => ShiftLabels.Describe(ArrayMoveRelativeToPatient);

        /// <summary>Proposed QA isocentre relative to the phantom user origin.</summary>
        public Vec3 ProposedIsoRelativeToUserOrigin { get; set; }

        public PlaneEvaluation Current { get; set; } = null!;
        public PlaneEvaluation Predicted { get; set; } = null!;
        public double CurrentObjective { get; set; }
        public double PredictedObjective { get; set; }
        public bool Improves => PredictedObjective > CurrentObjective + 1e-6;
    }

    /// <summary>
    /// Proposes an isocentre shift for a calculated QA plan using its own calculated dose.
    /// <para>
    /// Shifting the QA isocentre by Δ moves the anatomy and its dose rigidly in the (homogeneous) phantom:
    /// D_new(r) ≈ D_old(r − Δ). Relative to the patient, that is the same as moving the array centre from P to
    /// P' = P − Δ with the dose fixed to the anatomy. So the search maps the QA dose into patient coordinates
    /// and evaluates the usual J_k on planes around P.
    /// </para>
    /// <para>
    /// Objective: Σ J_k over the assigned PTVs, with H_k from each PTV's own Rx (100% isodose) in the calculated dose.
    /// Constraint: no PTV may end up more clipped by the usable-square edge than now (or the clip tolerance, whichever is larger),
    /// and PTVs intersected now must stay intersected.
    /// Search: ±range in-plane and along the normal, coarse grid then fine refinement (defaults ±15/±10 mm, 1 mm then 0.25 mm).
    /// </para>
    /// </summary>
    public static class FineTuneOptimizer
    {
        public static FineTuneProposal Propose(FineTuneRequest r, OptimisationSettings s)
        {
            s.Validate();
            if (r.Targets.Count == 0) throw new ArgumentException("No PTVs assigned to this plan.");
            var o = r.Orientation;
            var p = IsoGeometry.ArrayCentreInPatient(r.PhantomUserOrigin, r.ClinicalIsocentre, r.CurrentQaIsocentre);

            // Dose in patient coordinates: D_pat(x) = D_qa(x + (I_ph − I_clin)).
            var patientDose = r.QaDose.Translate(-(r.CurrentQaIsocentre - r.ClinicalIsocentre));
            double h = s.FineTuneFineStepMm;
            var model = new PlanningModel(r.Targets, patientDose, pixelSize: h, depthStep: h, gridOffset: p);

            double half = s.UsableHalfWidth;
            int K = r.Targets.Count;
            var cur = new PtvSample[K];
            for (int k = 0; k < K; k++) cur[k] = model.Sample(k, o, 0, o.U(p), o.V(p), half, s.Alpha);

            double Objective(int j, double cu, double cv)
            {
                double sum = 0;
                for (int k = 0; k < K; k++)
                {
                    var smp = model.Sample(k, o, j, cu, cv, half, s.Alpha);
                    if (cur[k].AreaInSquare > 0)
                    {
                        if (smp.AreaInSquare <= 0) return double.NegativeInfinity;
                        double allowed = Math.Max(cur[k].ClippedFraction, s.ClipTolerance);
                        if (smp.ClippedFraction > allowed + 1e-9) return double.NegativeInfinity;
                    }
                    sum += smp.Score;
                }
                return sum;
            }

            double cu0 = o.U(p), cv0 = o.V(p);
            double currentObj = cur.Sum(c => c.Score);
            double best = Objective(0, cu0, cv0);
            if (double.IsNegativeInfinity(best)) best = currentObj; // current position always allowed
            int bj = 0; double bu = cu0, bv = cv0;

            int stepIdx = (int)Math.Round(s.FineTuneCoarseStepMm / h);
            int nIn = (int)Math.Floor(s.FineTuneInPlaneRangeMm / s.FineTuneCoarseStepMm);
            int nN = (int)Math.Floor(s.FineTuneNormalRangeMm / s.FineTuneCoarseStepMm);
            for (int dj = -nN; dj <= nN; dj++)
                for (int a = -nIn; a <= nIn; a++)
                    for (int b = -nIn; b <= nIn; b++)
                    {
                        int j = dj * stepIdx;
                        double cu = cu0 + a * s.FineTuneCoarseStepMm, cv = cv0 + b * s.FineTuneCoarseStepMm;
                        double v = Objective(j, cu, cv);
                        if (IsBetter(v, j, cu, cv, best, bj, bu, bv, cu0, cv0)) { best = v; bj = j; bu = cu; bv = cv; }
                    }

            // Fine refinement within ± one coarse step.
            int cj = bj; double cu1 = bu, cv1 = bv;
            for (int dj = -stepIdx; dj <= stepIdx; dj++)
                for (int a = -stepIdx; a <= stepIdx; a++)
                    for (int b = -stepIdx; b <= stepIdx; b++)
                    {
                        int j = cj + dj;
                        double cu = cu1 + a * h, cv = cv1 + b * h;
                        if (Math.Abs(j * h) > s.FineTuneNormalRangeMm + 1e-9 || Math.Abs(cu - cu0) > s.FineTuneInPlaneRangeMm + 1e-9
                            || Math.Abs(cv - cv0) > s.FineTuneInPlaneRangeMm + 1e-9) continue;
                        double v = Objective(j, cu, cv);
                        if (IsBetter(v, j, cu, cv, best, bj, bu, bv, cu0, cv0)) { best = v; bj = j; bu = cu; bv = cv; }
                    }

            var proposedCentre = o.FromPlane(bu, bv, model.DepthOf(o, bj));
            var current = new ArrayPlane(o, p);
            var proposed = new ArrayPlane(o, proposedCentre);
            var reference = r.BestScores != null && r.BestScores.Length == K ? new CoverageReference(r.BestScores, new bool[K]) : null;
            var shift = IsoGeometry.IsoShiftForArrayMove(p, proposedCentre);
            var newIso = r.CurrentQaIsocentre + shift;

            return new FineTuneProposal
            {
                CurrentPlane = current,
                ProposedPlane = proposed,
                IsoShift = shift,
                CurrentQaIsocentre = r.CurrentQaIsocentre,
                ProposedQaIsocentre = newIso,
                ProposedIsoRelativeToUserOrigin = newIso - r.PhantomUserOrigin,
                Current = model.Evaluate(current, s, reference),
                Predicted = model.Evaluate(proposed, s, reference),
                CurrentObjective = currentObj,
                PredictedObjective = best,
            };
        }

        /// <summary>Higher objective wins; ties go to the smaller move (then a fixed order), so "no change" is preferred.</summary>
        private static bool IsBetter(double v, int j, double cu, double cv, double best, int bj, double bu, double bv, double cu0, double cv0)
        {
            if (double.IsNegativeInfinity(v)) return false;
            if (v > best + 1e-9) return true;
            if (v < best - 1e-9) return false;
            double d = Math.Abs(j) + Math.Abs(cu - cu0) + Math.Abs(cv - cv0);
            double bd = Math.Abs(bj) + Math.Abs(bu - cu0) + Math.Abs(bv - cv0);
            return d < bd - 1e-9;
        }
    }
}
