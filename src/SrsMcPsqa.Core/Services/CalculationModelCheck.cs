using System;

namespace SrsMcPsqa.Core.Services
{
    /// <summary>
    /// QA plans are only made for clinical plans calculated with the calculation model the template phantom is set up for
    /// (Settings: template phantom calculation model). The QA plans use the clinical plan's model, which is then that model.
    /// Eclipse doesn't always report a clinical plan's model (empty from every ESAPI route on the test system); then the
    /// check is made on the model each new QA plan inherits from it (<see cref="QaPlanBlockedReason"/>), and the plan is
    /// removed if it's wrong.
    /// </summary>
    public static class CalculationModelCheck
    {
        /// <summary>
        /// Why QA plans can't be made or recalculated for this clinical model, or null when they can (also when Eclipse didn't
        /// report the clinical model: <see cref="IsUnknown"/>; the QA plan's inherited model is checked instead).
        /// </summary>
        public static string? BlockedReason(string? phantomModel, string? clinicalModel)
        {
            phantomModel = phantomModel?.Trim() ?? "";
            clinicalModel = clinicalModel?.Trim() ?? "";
            if (phantomModel.Length == 0)
                return "The template phantom's calculation model isn't set for this installation. Open Settings, choose it under " +
                       "Template phantom patient and Save.";
            if (clinicalModel.Length == 0) return null;
            if (!string.Equals(phantomModel, clinicalModel, StringComparison.OrdinalIgnoreCase))
                return $"This plan is calculated with {clinicalModel}, but the template phantom is set up for {phantomModel}. " +
                       $"QA plans use the clinical plan's calculation model, so they can only be made for plans calculated with {phantomModel}. " +
                       $"Recalculate the clinical plan with {phantomModel}, or, if the phantom has been set up for {clinicalModel}, change it in Settings.";
            return null;
        }

        /// <summary>Eclipse didn't report the clinical model: it's checked on each QA plan's inherited model instead.</summary>
        public static bool IsUnknown(string? clinicalModel) => string.IsNullOrWhiteSpace(clinicalModel);

        /// <summary>Why a QA plan with this (inherited) model can't be kept or recalculated, or null.</summary>
        public static string? QaPlanBlockedReason(string? phantomModel, string? qaPlanModel, string planId)
        {
            phantomModel = phantomModel?.Trim() ?? "";
            qaPlanModel = qaPlanModel?.Trim() ?? "";
            if (phantomModel.Length == 0) return BlockedReason(phantomModel, qaPlanModel);
            if (qaPlanModel.Length == 0)
                return $"Eclipse didn't report the calculation model of '{planId}', so the app can't check it's {phantomModel}, " +
                       "the model the template phantom is set up for.";
            if (!string.Equals(phantomModel, qaPlanModel, StringComparison.OrdinalIgnoreCase))
                return $"'{planId}' got the clinical plan's calculation model, {qaPlanModel}, but the template phantom is set up for {phantomModel}. " +
                       $"QA plans can only be made for plans calculated with {phantomModel}. Recalculate the clinical plan with {phantomModel}, " +
                       $"or, if the phantom has been set up for {qaPlanModel}, change it in Settings.";
            return null;
        }
    }
}
