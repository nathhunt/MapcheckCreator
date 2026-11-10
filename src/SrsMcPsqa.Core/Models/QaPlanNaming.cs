using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.Core.Models
{
    /// <summary>QA plan IDs from a naming pattern, e.g. <c>MC{n}_{orient}</c> → <c>MC1_0</c>, <c>MC2_270</c>.</summary>
    public static class QaPlanNaming
    {
        /// <summary>Eclipse plan ID limit.</summary>
        public const int MaxIdLength = 13;

        public const string DefaultPattern = "MC{n}_{orient}";
        /// <summary>Default before 2026-11 (stored user settings still holding it are migrated).</summary>
        public const string PreviousDefaultPattern = "SMC{n}_{orient}";

        /// <summary>
        /// First unique ID for plan number <paramref name="n"/> (or higher, if taken). Placeholders: <c>{n}</c>, <c>{orient}</c>.
        /// A pattern without <c>{n}</c> gets the number appended when needed for uniqueness. IDs are cut to 13 characters,
        /// keeping the number. Comparison is case-insensitive, like Eclipse.
        /// </summary>
        public static string Generate(string? pattern, int n, ArrayOrientation orientation, IEnumerable<string> existingIds)
        {
            var existing = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
            var p = string.IsNullOrWhiteSpace(pattern) ? DefaultPattern : pattern!.Trim();
            bool hasNumber = p.Contains("{n}");
            for (int k = Math.Max(1, n); k < n + 1000; k++)
            {
                string id = Fit(p, hasNumber || k != n ? k : (int?)null, orientation);
                if (!existing.Contains(id)) return id;
            }
            throw new InvalidOperationException("Could not find a unique plan ID.");
        }

        private static string Fit(string pattern, int? number, ArrayOrientation o)
        {
            string num = number?.ToString(CultureInfo.InvariantCulture) ?? "";
            string id = pattern.Replace("{orient}", o.ShortLabel());
            id = pattern.Contains("{n}") ? id.Replace("{n}", num) : id + num;
            if (id.Length <= MaxIdLength) return id;
            // Too long: trim the text before the number so the number (which makes it unique) survives.
            int at = id.LastIndexOf(num, StringComparison.Ordinal);
            if (num.Length == 0 || at < 0) return id.Substring(0, MaxIdLength);
            int excess = id.Length - MaxIdLength;
            int cut = Math.Min(excess, at);
            id = id.Remove(at - cut, cut);
            return id.Length <= MaxIdLength ? id : id.Substring(0, MaxIdLength);
        }
    }

    /// <summary>
    /// Machine-readable tag written to the QA plan Comment: <c>SRSMC-PSQA|v1|src=&lt;UID&gt;|orient=0|ptv=PTV1;PTV2</c>.
    /// The app only ever deletes plans that carry this tag.
    /// </summary>
    public sealed class QaPlanTag
    {
        public const string Prefix = "SRSMC-PSQA";
        public const int CurrentVersion = 1;

        public QaPlanTag(string sourcePlanUid, ArrayOrientation orientation, IEnumerable<string> ptvIds)
        {
            SourcePlanUid = sourcePlanUid; Orientation = orientation; PtvIds = ptvIds.ToList();
        }

        public string SourcePlanUid { get; }
        public ArrayOrientation Orientation { get; }
        public IReadOnlyList<string> PtvIds { get; }

        public string Format() =>
            $"{Prefix}|v{CurrentVersion}|src={SourcePlanUid}|orient={(int)Orientation}|ptv={string.Join(";", PtvIds.Select(Escape))}";

        private static string Escape(string id) => id.Replace("|", "_").Replace(";", "_");

        public static bool TryParse(string? comment, out QaPlanTag? tag)
        {
            tag = null;
            if (string.IsNullOrEmpty(comment)) return false;
            int start = comment!.IndexOf(Prefix + "|", StringComparison.Ordinal);
            if (start < 0) return false;
            var parts = comment.Substring(start).Split('|');
            string? src = null, ptv = null;
            ArrayOrientation? orient = null;
            foreach (var part in parts.Skip(1))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string key = part.Substring(0, eq), val = part.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "src": src = val; break;
                    case "orient": orient = val == "0" ? ArrayOrientation.Deg0 : val == "270" ? ArrayOrientation.Deg270 : (ArrayOrientation?)null; break;
                    case "ptv": ptv = val; break;
                }
            }
            if (src == null || orient == null) return false;
            tag = new QaPlanTag(src, orient.Value, (ptv ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            return true;
        }
    }
}
