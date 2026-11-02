using System;
using System.Collections.Generic;
using System.Linq;

namespace SrsMcPsqa.Core.Prescription
{
    /// <summary>Which structures count as PTVs, and which of those are planning helpers (unticked by default).</summary>
    public static class PtvFilter
    {
        /// <summary>Department default: structures starting with "z" are helpers (case-insensitive).</summary>
        public const string DefaultHelperPrefixes = "z";

        /// <summary>Structure IDs containing "PTV" (case-insensitive).</summary>
        public static bool IsPtv(string? id) => id != null && id.IndexOf("PTV", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>True when the ID starts with any of the comma-separated prefixes (case-insensitive).</summary>
        public static bool IsHelper(string? id, string? helperPrefixes)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(helperPrefixes)) return false;
            return Prefixes(helperPrefixes!).Any(p => id!.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> Prefixes(string list) =>
            list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0);
    }
}
