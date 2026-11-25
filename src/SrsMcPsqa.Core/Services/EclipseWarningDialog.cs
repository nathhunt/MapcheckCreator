using System;
using System.Linq;

namespace SrsMcPsqa.Core.Services
{
    /// <summary>
    /// Decides whether a message box Eclipse opened is a warnings-only notice the app may close for the user: a single
    /// button, and every non-empty line starts with "Warning:" (e.g. "Warning: [COUCH 0] The calibration protocol status
    /// is not Commissioning or Approved."). Anything else (errors, questions, several buttons) is left for the user.
    /// </summary>
    public static class EclipseWarningDialog
    {
        public static bool IsWarningsOnly(string? text, int buttonCount)
        {
            if (buttonCount != 1 || string.IsNullOrWhiteSpace(text)) return false;
            var lines = text!.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            return lines.Count > 0 && lines.All(l => l.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase));
        }
    }
}
