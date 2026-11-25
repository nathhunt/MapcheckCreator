using System;

namespace SrsMcPsqa.Core.Services
{
    /// <summary>
    /// QA plans are only made for clinical plans calculated with the calculation model the template phantom is set up for
    /// (Settings: template phantom calculation model). The QA plans use the clinical plan's model, which is then that model.
    /// </summary>
    public static class CalculationModelCheck
    {
        /// <summary>Why QA plans can't be made or recalculated for this clinical model, or null when they can.</summary>
        public static string? BlockedReason(string? phantomModel, string? clinicalModel)
        {
            phantomModel = phantomModel?.Trim() ?? "";
            clinicalModel = clinicalModel?.Trim() ?? "";
            if (phantomModel.Length == 0)
                return "The template phantom's calculation model isn't set for this installation. Open Settings, choose it under " +
                       "Template phantom patient and Save.";
            if (clinicalModel.Length == 0)
                return $"Eclipse didn't report the clinical plan's calculation model, so the app can't check it's {phantomModel}, " +
                       "the model the template phantom is set up for. Check the plan's calculation model in Eclipse.";
            if (!string.Equals(phantomModel, clinicalModel, StringComparison.OrdinalIgnoreCase))
                return $"This plan is calculated with {clinicalModel}, but the template phantom is set up for {phantomModel}. " +
                       $"QA plans use the clinical plan's calculation model, so they can only be made for plans calculated with {phantomModel}. " +
                       $"Recalculate the clinical plan with {phantomModel}, or, if the phantom has been set up for {clinicalModel}, change it in Settings.";
            return null;
        }
    }
}
