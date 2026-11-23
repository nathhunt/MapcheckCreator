using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SrsMcPsqa.App.Infrastructure;
using SrsMcPsqa.Core.Collision;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.App.ViewModels
{
    /// <summary>
    /// Gantry–couch clearance of one QA isocentre position (warning only, never blocks), with a suggested lateral phantom
    /// offset on the couch when the clearance is below the warning level. Computed in the background.
    /// </summary>
    public sealed partial class ClearanceViewModel : ObservableObject
    {
        private readonly PlanningSession _session;
        private int _version, _completedVersion;
        private Vec3? _latestIso;

        public ClearanceViewModel(PlanningSession session) { _session = session; }

        /// <summary>Clear, Warning, Risk, NotConfigured or Pending.</summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(Brush), nameof(HasProblem))] private string _state = "Pending";
        [ObservableProperty] private string _summary = "";
        [ObservableProperty] private string _details = "";
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSuggestion))] private string _suggestion = "";
        public CollisionResult? Result { get; private set; }

        public bool HasProblem => State == "Warning" || State == "Risk";
        public bool HasSuggestion => Suggestion.Length > 0;
        public Brush Brush => State == "Clear" ? StatusBrushConverter.Good : State == "Risk" ? StatusBrushConverter.Bad
            : State == "Warning" ? StatusBrushConverter.Caution : Frozen(Color.FromRgb(0x90, 0x90, 0x90));

        /// <summary>Recompute for a QA isocentre relative to the MapCHECK user origin (fire and forget).</summary>
        public async void Update(Vec3 isoFromOrigin)
        {
            int version = ++_version;
            _latestIso = isoFromOrigin;
            var settings = _session.Settings.ToCollision();
            var beams = _session.ClinicalPlan?.Beams ?? Array.Empty<BeamGeometry>();
            if (!CheckPossible(settings, beams)) { _completedVersion = version; return; }
            try
            {
                var (result, suggestion) = await Task.Run(() => Compute(isoFromOrigin, beams, settings));
                if (version != _version) return; // a newer position is being checked
                Apply(result, suggestion, settings);
            }
            catch (Exception ex)
            {
                if (version != _version) return;
                Fail(ex);
            }
            _completedVersion = version;
        }

        /// <summary>The shown result is for the latest position (no check still running).</summary>
        public bool IsCurrent => _completedVersion == _version;

        /// <summary>
        /// Brings the result up to date now if a check is still running (e.g. the user clicked Create right after moving
        /// the array), so confirmations never quote the previous position's clearance. UI thread.
        /// </summary>
        public void EnsureCurrent()
        {
            if (IsCurrent || _latestIso is not Vec3 iso) return;
            int version = _version;
            var settings = _session.Settings.ToCollision();
            var beams = _session.ClinicalPlan?.Beams ?? Array.Empty<BeamGeometry>();
            if (CheckPossible(settings, beams))
            {
                try { var (result, suggestion) = Compute(iso, beams, settings); Apply(result, suggestion, settings); }
                catch (Exception ex) { Fail(ex); }
            }
            _completedVersion = version; // the background check for this version, when it ends, finds nothing newer to show
        }

        private bool CheckPossible(CollisionSettings settings, IReadOnlyList<BeamGeometry> beams)
        {
            if (!settings.IsConfigured)
            {
                Set("NotConfigured", "Couch clearance not checked: enter the MapCHECK origin height above the couch in Settings.", "", "", null);
                return false;
            }
            if (beams.Count == 0) { Set("NotConfigured", "Couch clearance not checked: no beam angles.", "", "", null); return false; }
            return true;
        }

        private static (CollisionResult, LateralSuggestion?) Compute(Vec3 isoFromOrigin, IReadOnlyList<BeamGeometry> beams, CollisionSettings settings)
        {
            var r = CouchCollision.Check(isoFromOrigin, beams, settings);
            var sug = r.Level == ClearanceLevel.Clear ? null : CouchCollision.SuggestLateralOffset(isoFromOrigin, beams, settings);
            return (r, sug);
        }

        private void Apply(CollisionResult result, LateralSuggestion? suggestion, CollisionSettings settings)
        {
            var worst = result.Worst!;
            string summary = result.Level switch
            {
                ClearanceLevel.Clear => string.Format(CultureInfo.InvariantCulture, "✓ Couch clearance ≥ {0:0.0} cm", result.MinClearanceMm / 10),
                ClearanceLevel.Warning => "⚠ Couch close to the gantry: " + worst.Describe(),
                _ => "✗ Couch collision risk: " + worst.Describe(),
            };
            string details = string.Join(Environment.NewLine, result.Beams.Select(b => (b.Level == ClearanceLevel.Clear ? "✓ " : b.Level == ClearanceLevel.Warning ? "⚠ " : "✗ ") + b.Describe()));
            string sugText = suggestion == null
                ? (result.Level == ClearanceLevel.Clear ? "" : string.Format(CultureInfo.InvariantCulture,
                    "No lateral phantom offset within ±{0:0} mm improves this; consider another array position.", settings.MaxLateralOffsetMm))
                : (suggestion.Clears ? "Suggestion: " : "Best within the limit: ") + suggestion.Describe() + " at measurement → " +
                  CollisionResult.FormatClearance(suggestion.Result.MinClearanceMm) +
                  (suggestion.Clears ? "." : string.Format(CultureInfo.InvariantCulture, " (still below {0:0.0} cm).", settings.WarningMm / 10));
            Set(result.Level.ToString(), summary, details, sugText, result);
        }

        private void Fail(Exception ex)
        {
            Log.Error("Clearance check failed", ex);
            Set("NotConfigured", "Couch clearance check failed: " + ex.Message, "", "", null);
        }

        private void Set(string state, string summary, string details, string suggestion, CollisionResult? result)
        {
            Result = result; State = state; Summary = summary; Details = details; Suggestion = suggestion;
        }

        /// <summary>One line for confirmation dialogs; null when clear or not checked.</summary>
        public string? WarningLine(string planLabel)
        {
            EnsureCurrent();
            return HasProblem ? $"  {planLabel}: {Summary.TrimStart('⚠', '✗', ' ')}" + (HasSuggestion ? $"{Environment.NewLine}    {Suggestion}" : "") : null;
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
