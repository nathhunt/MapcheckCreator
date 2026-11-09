using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SrsMcPsqa.Core.Services
{
    /// <summary>
    /// Reads progress from the text Eclipse writes to .NET trace output while it works (seen for DVH estimation and
    /// optimisation, e.g. "Progress ... 43%"; see gatewayscripts.com, "Automated planning from templates IV").
    /// Whether a dose calculation writes such lines, and in what format, is to be confirmed on Citrix, so this is lenient
    /// and the app falls back to its own estimate when nothing is recognised.
    /// </summary>
    public static class EclipseTrace
    {
        private static readonly Regex Percent = new Regex(@"(\d{1,3}(?:[.,]\d+)?)\s*%", RegexOptions.Compiled);

        /// <summary>True for noise that isn't worth showing ("The thread ... has exited", blank lines).</summary>
        public static bool IsNoise(string? line) =>
            string.IsNullOrWhiteSpace(line) || line!.TrimStart().StartsWith("The thread", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Percentage from a "Progress" line (the last "N%" on it), or null. Only lines starting with "Progress" count,
        /// so percentages in other messages (e.g. dose levels) aren't mistaken for progress.
        /// </summary>
        public static double? ProgressPercent(string? line)
        {
            if (IsNoise(line)) return null;
            var t = line!.Trim();
            if (!t.StartsWith("Progress", StringComparison.OrdinalIgnoreCase)) return null;
            var matches = Percent.Matches(t);
            if (matches.Count == 0) return null;
            var text = matches[matches.Count - 1].Groups[1].Value.Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return null;
            return p >= 0 && p <= 100 ? p : (double?)null;
        }

        private static readonly Regex Issue = new Regex(@"\b(Warning|Error):\s*(?:\[(?<beam>[^\]]+)\]\s*)?(?<text>.+)$", RegexOptions.Compiled);

        /// <summary>
        /// A warning or error from Eclipse's end-of-calculation summary, e.g.
        /// "Warnings Warning: [COUCH 45] The field exits the body in a region where the CT data ends. ...".
        /// </summary>
        public static CalculationIssue? ParseIssue(string? line)
        {
            if (IsNoise(line)) return null;
            var m = Issue.Match(line!.Trim());
            if (!m.Success) return null;
            return new CalculationIssue(m.Groups[1].Value.Equals("Error", StringComparison.OrdinalIgnoreCase),
                m.Groups["beam"].Success ? m.Groups["beam"].Value.Trim() : null, m.Groups["text"].Value.Trim());
        }

        /// <summary>One line per distinct message, listing the beams it applies to.</summary>
        public static System.Collections.Generic.IReadOnlyList<string> Summarise(System.Collections.Generic.IEnumerable<CalculationIssue> issues)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var g in System.Linq.Enumerable.GroupBy(issues, i => (i.IsError, i.Text)))
            {
                var beams = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Distinct(
                    System.Linq.Enumerable.Where(System.Linq.Enumerable.Select(g, i => i.Beam), b => b != null)));
                list.Add($"Eclipse {(g.Key.IsError ? "error" : "warning")}{(beams.Count > 0 ? " (" + string.Join(", ", beams) + ")" : "")}: {g.Key.Text}");
            }
            return list;
        }

        /// <summary>A message worth showing under the calculation bar (not "Information" chatter or noise), trimmed.</summary>
        public static string? DisplayMessage(string? line)
        {
            if (IsNoise(line)) return null;
            var t = line!.Trim();
            if (t.StartsWith("Information", StringComparison.OrdinalIgnoreCase) || t.IndexOf("Information:", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            // Section headers of Eclipse's end-of-calculation summary.
            if (Regex.IsMatch(t, @"^(Errors|Warnings|Messages)$|^Success\b", RegexOptions.IgnoreCase)) return null;
            return t.Length > 140 ? t.Substring(0, 140) + "…" : t;
        }
    }
}

namespace SrsMcPsqa.Core.Services
{
    public sealed class CalculationIssue
    {
        public CalculationIssue(bool isError, string? beam, string text) { IsError = isError; Beam = beam; Text = text; }
        public bool IsError { get; }
        public string? Beam { get; }
        public string Text { get; }
    }

    /// <summary>
    /// Turns TraceListener Write/WriteLine calls into complete lines. Text from Write is held until a line break or
    /// WriteLine, except that a complete "Progress ... N%" message sent through Write alone is passed on at once
    /// (Eclipse may write progress that way; the Gateway Scripts example handles Write messages immediately).
    /// Not thread-safe: the caller locks.
    /// </summary>
    public sealed class TraceLineBuffer
    {
        private readonly System.Text.StringBuilder _partial = new System.Text.StringBuilder();

        public System.Collections.Generic.IReadOnlyList<string> Write(string? text)
        {
            var lines = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            _partial.Append(text);
            TakeCompleteLines(lines);
            if (_partial.Length > 0 && EclipseTrace.ProgressPercent(_partial.ToString()) != null)
            {
                lines.Add(_partial.ToString());
                _partial.Clear();
            }
            return lines;
        }

        public System.Collections.Generic.IReadOnlyList<string> WriteLine(string? text)
        {
            var lines = new System.Collections.Generic.List<string>();
            _partial.Append(text).Append('\n');
            TakeCompleteLines(lines);
            return lines;
        }

        private void TakeCompleteLines(System.Collections.Generic.List<string> lines)
        {
            var s = _partial.ToString();
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\n' && s[i] != '\r') continue;
                var line = s.Substring(start, i - start);
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
                start = i + 1;
            }
            _partial.Clear();
            if (start < s.Length) _partial.Append(s, start, s.Length - start);
        }
    }
}
