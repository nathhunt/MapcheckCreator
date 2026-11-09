using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SrsMcPsqa.Core.Services
{
    public enum QaStepState { Running, Done, Failed }

    /// <summary>MU check result for one beam after a QA calculation.</summary>
    public sealed class BeamMuResult
    {
        public BeamMuResult(string beamId, double qaMu, double clinicalMu, double tolerance)
        {
            BeamId = beamId; QaMu = qaMu; ClinicalMu = clinicalMu;
            Matches = !double.IsNaN(qaMu) && Math.Abs(qaMu - clinicalMu) <= tolerance;
        }

        public string BeamId { get; }
        public double QaMu { get; }
        public double ClinicalMu { get; }
        public bool Matches { get; }
    }

    /// <summary>
    /// Structured progress of a write operation, per QA plan and step (for the progress panel). Eclipse doesn't report
    /// progress inside a dose calculation, so a calculation step only says when it starts and finishes, plus what the UI
    /// needs to estimate its duration (model, beams, control points).
    /// </summary>
    public sealed class QaProgressEvent
    {
        /// <summary>Plan ID, or a label such as "Phantom images".</summary>
        public string PlanId { get; set; } = "";
        public string Step { get; set; } = "";
        public QaStepState State { get; set; }
        public string? Detail { get; set; }
        public TimeSpan? Duration { get; set; }

        public bool IsCalculation { get; set; }
        public string? Model { get; set; }
        public int Beams { get; set; }
        public int ControlPoints { get; set; }

        public IReadOnlyList<BeamMuResult>? MuResults { get; set; }

        /// <summary>Progress Eclipse wrote to its trace output during the calculation (0-100), if any.</summary>
        public double? EclipsePercent { get; set; }
        /// <summary>Latest Eclipse trace message during the calculation, if any.</summary>
        public string? EclipseMessage { get; set; }
        /// <summary>Warnings/errors Eclipse reported for the calculation (summarised), on the step's Done event.</summary>
        public IReadOnlyList<string>? Warnings { get; set; }
    }

    /// <summary>Helper for services: report a step as running, then done (with duration) or failed.</summary>
    public static class QaSteps
    {
        public const string PhantomImages = "Phantom images";

        public static T Run<T>(IProgress<QaProgressEvent>? events, string planId, string step, Func<T> work, Func<T, string?>? detail = null)
        {
            events?.Report(new QaProgressEvent { PlanId = planId, Step = step, State = QaStepState.Running });
            var sw = Stopwatch.StartNew();
            try
            {
                var result = work();
                events?.Report(new QaProgressEvent
                {
                    PlanId = planId, Step = step, State = QaStepState.Done, Duration = sw.Elapsed, Detail = detail?.Invoke(result),
                });
                return result;
            }
            catch (Exception ex)
            {
                events?.Report(new QaProgressEvent { PlanId = planId, Step = step, State = QaStepState.Failed, Duration = sw.Elapsed, Detail = ex.Message });
                throw;
            }
        }

        public static void Run(IProgress<QaProgressEvent>? events, string planId, string step, Action work, string? detail = null) =>
            Run<bool>(events, planId, step, () => { work(); return true; }, _ => detail);
    }
}
