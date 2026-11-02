using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using SrsMcPsqa.Core.Optimisation;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>
    /// App settings (JSON). Department defaults come from <c>SrsMcPsqa.settings.json</c> next to the exe (read-only, optional);
    /// a per-user override of personal preferences is stored in <c>%APPDATA%\SrsMcPsqa\settings.json</c>.
    /// Template phantom IDs are environment-specific and are only taken from the department file. The app never writes to its own folder.
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public string TemplatePatientId { get; set; } = "";
        [DataMember] public string TemplateStudyId { get; set; } = "";
        [DataMember] public string Template0ImageId { get; set; } = "";
        [DataMember] public string Template270ImageId { get; set; } = "";
        [DataMember] public bool CopyTemplateStructures { get; set; }
        /// <summary>Comma-separated prefixes of helper PTV structures, unticked by default (e.g. "z").</summary>
        [DataMember] public string HelperPrefixes { get; set; } = Core.Prescription.PtvFilter.DefaultHelperPrefixes;
        [DataMember] public string PlanNamingPattern { get; set; } = Core.Models.QaPlanNaming.DefaultPattern;
        [DataMember] public double EdgeMarginMm { get; set; } = 3.0;
        [DataMember] public double Tau { get; set; } = 0.85;
        [DataMember] public int MinDiodes { get; set; } = 5;
        [DataMember] public double Alpha { get; set; } = 0.5;
        [DataMember] public double FineTuneInPlaneRangeMm { get; set; } = 15.0;
        [DataMember] public double FineTuneNormalRangeMm { get; set; } = 10.0;
        [DataMember] public bool AutoSave { get; set; } = true;
        [DataMember] public string LogLevel { get; set; } = "Info";
        /// <summary>System, Light or Dark.</summary>
        [DataMember] public string Theme { get; set; } = "System";

        public bool HasTemplate =>
            !string.IsNullOrWhiteSpace(TemplatePatientId) && !string.IsNullOrWhiteSpace(TemplateStudyId)
            && !string.IsNullOrWhiteSpace(Template0ImageId) && !string.IsNullOrWhiteSpace(Template270ImageId);

        public TemplatePhantomSettings ToTemplate() => new TemplatePhantomSettings
        {
            PatientId = TemplatePatientId, StudyId = TemplateStudyId, Image0Id = Template0ImageId,
            Image270Id = Template270ImageId, CopyTemplateStructures = CopyTemplateStructures,
        };

        public OptimisationSettings ToOptimisation() => new OptimisationSettings
        {
            EdgeMarginMm = EdgeMarginMm, Tau = Tau, MinDiodes = MinDiodes, Alpha = Alpha,
            FineTuneInPlaneRangeMm = FineTuneInPlaneRangeMm, FineTuneNormalRangeMm = FineTuneNormalRangeMm,
        };

        public AppSettings Clone() => (AppSettings)MemberwiseClone();

        // DataContract deserialisation skips constructors and initialisers; restore defaults for missing members.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext _)
        {
            var d = new AppSettings(false);
            TemplatePatientId = d.TemplatePatientId; TemplateStudyId = d.TemplateStudyId; Template0ImageId = d.Template0ImageId;
            Template270ImageId = d.Template270ImageId; HelperPrefixes = d.HelperPrefixes; PlanNamingPattern = d.PlanNamingPattern; EdgeMarginMm = d.EdgeMarginMm;
            Tau = d.Tau; MinDiodes = d.MinDiodes; Alpha = d.Alpha; FineTuneInPlaneRangeMm = d.FineTuneInPlaneRangeMm;
            FineTuneNormalRangeMm = d.FineTuneNormalRangeMm; AutoSave = d.AutoSave; LogLevel = d.LogLevel; Theme = d.Theme;
        }

        public AppSettings() { }
        private AppSettings(bool _) { }
    }

    /// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
    public static class SettingsStore
    {
        public const string DepartmentFileName = "SrsMcPsqa.settings.json";

        public static string DepartmentPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DepartmentFileName);

        public static string UserPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SrsMcPsqa", "settings.json");

        /// <summary>
        /// Personal preferences from the user file (if present) over the department file (or built-in defaults).
        /// The template phantom fields always come from the department file: they differ between environments
        /// (e.g. test and production Citrix), while %APPDATA% may follow the user between them.
        /// </summary>
        public static AppSettings Load(out string source)
        {
            var dept = LoadDepartmentDefaults();
            AppSettings result = dept.Clone();
            source = File.Exists(DepartmentPath) ? $"department defaults ({DepartmentPath})" : "built-in defaults";
            try
            {
                if (File.Exists(UserPath))
                {
                    result = Read(UserPath);
                    source = $"user settings ({UserPath}) over {source}";
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not read user settings from {UserPath}: {ex.Message}");
            }
            CopyEnvironmentFields(dept, result);
            return result;
        }

        /// <summary>Environment-specific fields (template phantom) are owned by the department file.</summary>
        public static void CopyEnvironmentFields(AppSettings from, AppSettings to)
        {
            to.TemplatePatientId = from.TemplatePatientId;
            to.TemplateStudyId = from.TemplateStudyId;
            to.Template0ImageId = from.Template0ImageId;
            to.Template270ImageId = from.Template270ImageId;
            to.CopyTemplateStructures = from.CopyTemplateStructures;
        }

        public static AppSettings LoadDepartmentDefaults()
        {
            try { if (File.Exists(DepartmentPath)) return Read(DepartmentPath); }
            catch (Exception ex) { Log.Warn($"Could not read department defaults: {ex.Message}"); }
            return new AppSettings();
        }

        /// <summary>Saves personal preferences only; template phantom fields are left blank in the user file.</summary>
        public static void SaveUser(AppSettings settings)
        {
            var s = settings.Clone();
            CopyEnvironmentFields(new AppSettings { PlanNamingPattern = s.PlanNamingPattern }, s);
            Directory.CreateDirectory(Path.GetDirectoryName(UserPath)!);
            var ser = new DataContractJsonSerializer(typeof(AppSettings));
            using var ms = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, ownsStream: false, indent: true))
                ser.WriteObject(writer, s);
            File.WriteAllBytes(UserPath, ms.ToArray());
        }

        public static void DeleteUser()
        {
            if (File.Exists(UserPath)) File.Delete(UserPath);
        }

        private static AppSettings Read(string path)
        {
            var ser = new DataContractJsonSerializer(typeof(AppSettings));
            using var fs = File.OpenRead(path);
            return (AppSettings)ser.ReadObject(fs);
        }
    }
}
