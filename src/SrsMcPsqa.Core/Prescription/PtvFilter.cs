using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SrsMcPsqa.Core.Prescription
{
    /// <summary>Which structures count as PTVs, and which of those are planning helpers (listed but unticked by default).</summary>
    public static class PtvFilter
    {
        /// <summary>Department default: z-prefixed helpers and evaluation structures.</summary>
        public const string DefaultHelperPatterns = "z*, *eval*";

        /// <summary>Structure IDs containing "PTV" (case-insensitive).</summary>
        public static bool IsPtv(string? id) => id != null && id.IndexOf("PTV", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// True when the ID matches any comma-separated pattern (case-insensitive). <c>*</c> matches any text.
        /// A pattern without <c>*</c> is a prefix, so "z" behaves like "z*".
        /// </summary>
        public static bool IsHelper(string? id, string? helperPatterns)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(helperPatterns)) return false;
            return Patterns(helperPatterns!).Any(r => r.IsMatch(id));
        }

        private static IEnumerable<Regex> Patterns(string list) =>
            list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Select(p => p.Contains("*") ? p : p + "*")
                .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }
}
