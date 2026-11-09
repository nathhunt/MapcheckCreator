using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SrsMcPsqa.Spike
{
    /// <summary>Snapshot of the UI inputs, taken on the UI thread and handed to the ESAPI thread by value.</summary>
    internal sealed class SpikeInputs
    {
        public string PatientId { get; set; } = "";
        public string ClinicalCourseId { get; set; } = "";
        public string ClinicalPlanId { get; set; } = "";
        public string QaCourseId { get; set; } = "";
        public string TemplatePatientId { get; set; } = "";
        public string TemplateStudyId { get; set; } = "";
        public string TemplateImageId { get; set; } = "";
        /// <summary>Structure set of an already-copied phantom image to use for step 3 (filled by step 2).</summary>
        public string PhantomStructureSetId { get; set; } = "";
        /// <summary>Beam to copy; empty = first treatment beam (or all, if <see cref="CopyAllBeams"/>).</summary>
        public string BeamId { get; set; } = "";
        public bool CopyAllBeams { get; set; }
        public bool ForceCopyImage { get; set; }
        public bool SetPrescription { get; set; }
        /// <summary>Set PlanNormalizationValue = 100 before calculating (MU-preservation test).</summary>
        public bool NormalizeTo100 { get; set; }
        public bool SaveAfterWrite { get; set; } = true;
        /// <summary>QA isocentre for step 3, in mm relative to the phantom user origin (DICOM axes).</summary>
        public double[] IsoOffset1 { get; set; } = { 10, 0, 5 };
        /// <summary>QA isocentre for step 3b (move), in mm relative to the phantom user origin (DICOM axes).</summary>
        public double[] IsoOffset2 { get; set; } = { 12, 0, 3 };

        private static string StorePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SrsMcPsqa", "spike-inputs.txt");

        /// <summary>Remember the template/course IDs between runs (per user, %APPDATA%). Patient-specific plan IDs are not stored.</summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllLines(StorePath, new[]
                {
                    "TemplatePatientId=" + TemplatePatientId,
                    "TemplateStudyId=" + TemplateStudyId,
                    "TemplateImageId=" + TemplateImageId,
                    "IsoOffset1=" + string.Join(",", IsoOffset1.Select(v => v.ToString(CultureInfo.InvariantCulture))),
                    "IsoOffset2=" + string.Join(",", IsoOffset2.Select(v => v.ToString(CultureInfo.InvariantCulture))),
                });
            }
            catch
            {
                // Remembering inputs is a convenience only.
            }
        }

        public static SpikeInputs LoadOrDefault()
        {
            var s = new SpikeInputs();
            try
            {
                if (!File.Exists(StorePath)) return s;
                var d = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(StorePath))
                {
                    var i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                }
                if (d.TryGetValue("TemplatePatientId", out var v)) s.TemplatePatientId = v;
                if (d.TryGetValue("TemplateStudyId", out v)) s.TemplateStudyId = v;
                if (d.TryGetValue("TemplateImageId", out v)) s.TemplateImageId = v;
                if (d.TryGetValue("IsoOffset1", out v) && TryParseVector(v, out var o1)) s.IsoOffset1 = o1;
                if (d.TryGetValue("IsoOffset2", out v) && TryParseVector(v, out var o2)) s.IsoOffset2 = o2;
            }
            catch
            {
                // Ignore a corrupt file; defaults are fine.
            }
            return s;
        }

        public static bool TryParseVector(string text, out double[] v)
        {
            v = Array.Empty<double>();
            var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return false;
            var r = new double[3];
            for (int i = 0; i < 3; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i])) return false;
            v = r;
            return true;
        }

        public static string FormatVector(double[] v) =>
            string.Join(", ", v.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)));
    }
}
