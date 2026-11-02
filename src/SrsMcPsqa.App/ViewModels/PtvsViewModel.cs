using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Optimisation;
using Wpf.Ui.Controls;

namespace SrsMcPsqa.App.ViewModels
{
    /// <summary>Screen 2: choose PTVs and check/edit each PTV's prescription.</summary>
    public sealed partial class PtvsViewModel : PageViewModel
    {
        public PtvsViewModel(ShellViewModel shell) : base(shell, "PTVs", SymbolRegular.TargetArrow24, 2)
        {
            Session.PlanLoaded += async (_, __) =>
            {
                foreach (var r in Rows) r.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(PtvRowViewModel.Include)) UpdateSummary(); };
                await ComputeCrossSectionsAsync();
            };
        }

        public ObservableCollection<PtvRowViewModel> Rows => Session.Ptvs;

        [ObservableProperty] private string _summary = "";

        public override void OnNavigatedTo() => UpdateSummary();

        private void UpdateSummary() =>
            Summary = $"{Rows.Count(r => r.Include)} of {Rows.Count} PTVs selected.";

        /// <summary>Max cross-section per orientation (independent of Rx), computed in the background.</summary>
        private async Task ComputeCrossSectionsAsync()
        {
            UpdateSummary();
            var plan = Session.ClinicalPlan;
            var rows = Rows.ToList();
            if (plan?.Dose == null || rows.Count == 0) return;
            var areas = await Shell.ComputeAsync("Computing PTV cross-sections", () =>
            {
                var targets = rows.Select(r => new Core.Models.PlanningTarget(r.Id, r.RxGy > 0 ? r.RxGy : 1, r.Info.Mask!)).ToList();
                var model = new PlanningModel(targets, plan.Dose);
                return rows.Select((r, k) => (r, a0: model.MaxArea(k, ArrayOrientation.Deg0), a270: model.MaxArea(k, ArrayOrientation.Deg270))).ToList();
            });
            if (areas == null) return;
            foreach (var (r, a0, a270) in areas) { r.MaxArea0 = a0; r.MaxArea270 = a270; }
        }

        [RelayCommand]
        private void SelectAll() { foreach (var r in Rows) r.Include = true; UpdateSummary(); }

        [RelayCommand]
        private void SelectNone() { foreach (var r in Rows) r.Include = false; UpdateSummary(); }

        [RelayCommand]
        private void Continue()
        {
            UpdateSummary();
            if (!Rows.Any(r => r.Include))
            {
                Session.Dialogs.ShowInfo("No PTVs selected", "Tick at least one PTV.");
                return;
            }
            Shell.Navigate(Shell.Planning);
        }
    }
}
