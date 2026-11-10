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
    /// Template phantom IDs differ between environments (test, production), so a user's template values are stored
    /// per installation folder (<c>%APPDATA%\SrsMcPsqa\template-&lt;folder hash&gt;.json</c>), falling back to the
    /// department file of that installation. The app never writes to its own folder.
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public string TemplatePatientId { get; set; } = "";
        /// <summary>Study of the 0° image (empty = search all studies).</summary>
        [DataMember] public string Template0StudyId { get; set; } = "";
        /// <summary>Study of the 270° image (empty = search all studies).</summary>
        [DataMember] public string Template270StudyId { get; set; } = "";
        [DataMember] public string Template0ImageId { get; set; } = "";
        [DataMember] public string Template270ImageId { get; set; } = "";
        [DataMember] public bool CopyTemplateStructures { get; set; }
        /// <summary>Comma-separated patterns of helper PTV structures, listed but unticked by default ("*" = any text).</summary>
        [DataMember] public string HelperPatterns { get; set; } = Core.Prescription.PtvFilter.DefaultHelperPatterns;
        [DataMember] public string PlanNamingPattern { get; set; } = Core.Models.QaPlanNaming.DefaultPattern;
        [DataMember] public double EdgeMarginMm { get; set; } = 3.0;
        [DataMember] public double Tau { get; set; } = 0.85;
        [DataMember] public int MinDiodes { get; set; } = 5;
        [DataMember] public double Alpha { get; set; } = 0.5;
        [DataMember] public double FineTuneInPlaneRangeMm { get; set; } = 15.0;
        [DataMember] public double FineTuneNormalRangeMm { get; set; } = 10.0;
        [DataMember] public bool AutoSave { get; set; } = true;

        // Gantry–couch collision check (mm). Head: 41.5 cm iso to face, 76.5 cm across (open-source ESAPI collision scripts);
        // couch: Exact IGRT 53 cm wide, thickness estimated. See docs/COLLISION_CHECK.md.
        [DataMember] public double CollisionHeadClearanceMm { get; set; } = 415;
        [DataMember] public double CollisionHeadRadiusMm { get; set; } = 385;
        [DataMember] public double CollisionCouchWidthMm { get; set; } = 530;
        [DataMember] public double CollisionCouchThicknessMm { get; set; } = 60;
        /// <summary>MapCHECK user origin height above the couch top; 0 = not set (check disabled).</summary>
        [DataMember] public double CollisionOriginAboveCouchMm { get; set; }
        [DataMember] public double CollisionCouchHeadEndSupMm { get; set; } = 200;
        [DataMember] public double CollisionPhantomLateralOffsetMm { get; set; }
        [DataMember] public double CollisionMaxLateralOffsetMm { get; set; } = 50;
        [DataMember] public double CollisionWarningMm { get; set; } = 50;
        [DataMember] public double CollisionRiskMm { get; set; } = 20;
        [DataMember] public string LogLevel { get; set; } = "Info";
        /// <summary>System, Light or Dark.</summary>
        [DataMember] public string Theme { get; set; } = "System";

        public bool HasTemplate =>
            !string.IsNullOrWhiteSpace(TemplatePatientId)
            && !string.IsNullOrWhiteSpace(Template0ImageId) && !string.IsNullOrWhiteSpace(Template270ImageId);

        public TemplatePhantomSettings ToTemplate() => new TemplatePhantomSettings
        {
            PatientId = TemplatePatientId, Study0Id = Template0StudyId, Study270Id = Template270StudyId, Image0Id = Template0ImageId,
            Image270Id = Template270ImageId, CopyTemplateStructures = CopyTemplateStructures,
        };

        public OptimisationSettings ToOptimisation() => new OptimisationSettings
        {
            EdgeMarginMm = EdgeMarginMm, Tau = Tau, MinDiodes = MinDiodes, Alpha = Alpha,
            FineTuneInPlaneRangeMm = FineTuneInPlaneRangeMm, FineTuneNormalRangeMm = FineTuneNormalRangeMm,
        };

        public Core.Collision.CollisionSettings ToCollision() => new Core.Collision.CollisionSettings
        {
            HeadClearanceMm = CollisionHeadClearanceMm, HeadRadiusMm = CollisionHeadRadiusMm,
            CouchWidthMm = CollisionCouchWidthMm, CouchThicknessMm = CollisionCouchThicknessMm,
            OriginAboveCouchMm = CollisionOriginAboveCouchMm, CouchHeadEndSupMm = CollisionCouchHeadEndSupMm,
            PhantomLateralOffsetMm = CollisionPhantomLateralOffsetMm, MaxLateralOffsetMm = CollisionMaxLateralOffsetMm,
            WarningMm = CollisionWarningMm, RiskMm = CollisionRiskMm,
        };

        public AppSettings Clone() => (AppSettings)MemberwiseClone();

        // DataContract deserialisation skips constructors and initialisers; restore defaults for missing members.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext _)
        {
            var d = new AppSettings(false);
            TemplatePatientId = d.TemplatePatientId; Template0StudyId = d.Template0StudyId; Template270StudyId = d.Template270StudyId; Template0ImageId = d.Template0ImageId;
            Template270ImageId = d.Template270ImageId; HelperPatterns = d.HelperPatterns; PlanNamingPattern = d.PlanNamingPattern; EdgeMarginMm = d.EdgeMarginMm;
            Tau = d.Tau; MinDiodes = d.MinDiodes; Alpha = d.Alpha; FineTuneInPlaneRangeMm = d.FineTuneInPlaneRangeMm;
            FineTuneNormalRangeMm = d.FineTuneNormalRangeMm; AutoSave = d.AutoSave; LogLevel = d.LogLevel; Theme = d.Theme;
            CollisionHeadClearanceMm = d.CollisionHeadClearanceMm; CollisionHeadRadiusMm = d.CollisionHeadRadiusMm;
            CollisionCouchWidthMm = d.CollisionCouchWidthMm; CollisionCouchThicknessMm = d.CollisionCouchThicknessMm;
            CollisionOriginAboveCouchMm = d.CollisionOriginAboveCouchMm; CollisionCouchHeadEndSupMm = d.CollisionCouchHeadEndSupMm;
            CollisionPhantomLateralOffsetMm = d.CollisionPhantomLateralOffsetMm; CollisionMaxLateralOffsetMm = d.CollisionMaxLateralOffsetMm;
            CollisionWarningMm = d.CollisionWarningMm; CollisionRiskMm = d.CollisionRiskMm;
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
        /// Template phantom fields: this installation's template file if present, else the department file.
        /// (%APPDATA% may follow the user between test and production, so template values are kept per installation.)
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
                    result = Read<AppSettings>(UserPath);
                    source = $"user settings ({UserPath}) over {source}";
                    // The default naming changed from SMC{n}_{orient} to MC{n}_{orient}; a stored old default follows the department value.
                    if (result.PlanNamingPattern == Core.Models.QaPlanNaming.PreviousDefaultPattern) result.PlanNamingPattern = dept.PlanNamingPattern;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not read user settings from {UserPath}: {ex.Message}");
            }
            CopyEnvironmentFields(dept, result);
            try
            {
                if (File.Exists(TemplatePath))
                {
                    var t = Read<InstallationTemplate>(TemplatePath);
                    result.TemplatePatientId = t.TemplatePatientId ?? ""; result.Template0StudyId = t.Template0StudyId ?? ""; result.Template270StudyId = t.Template270StudyId ?? "";
                    result.Template0ImageId = t.Template0ImageId ?? ""; result.Template270ImageId = t.Template270ImageId ?? "";
                    result.CopyTemplateStructures = t.CopyTemplateStructures;
                    source += $"; template phantom from {TemplatePath}";
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not read template settings from {TemplatePath}: {ex.Message}");
            }
            return result;
        }

        /// <summary>Folder of this installation (test and production installations differ).</summary>
        public static string InstallationFolder => Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\', '/');

        /// <summary>Per-user, per-installation template phantom file.</summary>
        public static string TemplatePath
        {
            get
            {
                using var sha = System.Security.Cryptography.SHA1.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(InstallationFolder.ToUpperInvariant()));
                var key = BitConverter.ToString(hash, 0, 6).Replace("-", "").ToLowerInvariant();
                return Path.Combine(Path.GetDirectoryName(UserPath)!, $"template-{key}.json");
            }
        }

        /// <summary>Environment-specific fields (template phantom) are owned by the department file.</summary>
        public static void CopyEnvironmentFields(AppSettings from, AppSettings to)
        {
            to.TemplatePatientId = from.TemplatePatientId;
            to.Template0StudyId = from.Template0StudyId;
            to.Template270StudyId = from.Template270StudyId;
            to.Template0ImageId = from.Template0ImageId;
            to.Template270ImageId = from.Template270ImageId;
            to.CopyTemplateStructures = from.CopyTemplateStructures;
        }

        public static AppSettings LoadDepartmentDefaults()
        {
            try { if (File.Exists(DepartmentPath)) return Read<AppSettings>(DepartmentPath); }
            catch (Exception ex) { Log.Warn($"Could not read department defaults: {ex.Message}"); }
            return new AppSettings();
        }

        /// <summary>
        /// Saves personal preferences to the user file (template fields blank there) and the template phantom
        /// fields to this installation's template file.
        /// </summary>
        public static void SaveUser(AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserPath)!);
            var s = settings.Clone();
            CopyEnvironmentFields(new AppSettings { PlanNamingPattern = s.PlanNamingPattern }, s);
            Write(UserPath, s);
            Write(TemplatePath, new InstallationTemplate
            {
                InstallationFolder = InstallationFolder,
                TemplatePatientId = settings.TemplatePatientId, Template0StudyId = settings.Template0StudyId, Template270StudyId = settings.Template270StudyId,
                Template0ImageId = settings.Template0ImageId, Template270ImageId = settings.Template270ImageId,
                CopyTemplateStructures = settings.CopyTemplateStructures,
            });
        }

        private static void Write<T>(string path, T value)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using var ms = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, ownsStream: false, indent: true))
                ser.WriteObject(writer, value);
            File.WriteAllBytes(path, ms.ToArray());
        }

        /// <summary>Deletes personal preferences and this installation's template values.</summary>
        public static void DeleteUser()
        {
            if (File.Exists(UserPath)) File.Delete(UserPath);
            if (File.Exists(TemplatePath)) File.Delete(TemplatePath);
        }

        private static T Read<T>(string path)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using var fs = File.OpenRead(path);
            return (T)ser.ReadObject(fs);
        }
    }
}

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>A user's template phantom values for one installation folder.</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class InstallationTemplate
    {
        [System.Runtime.Serialization.DataMember] public string? InstallationFolder { get; set; }
        [System.Runtime.Serialization.DataMember] public string? TemplatePatientId { get; set; }
        [System.Runtime.Serialization.DataMember] public string? Template0StudyId { get; set; }
        [System.Runtime.Serialization.DataMember] public string? Template270StudyId { get; set; }
        [System.Runtime.Serialization.DataMember] public string? Template0ImageId { get; set; }
        [System.Runtime.Serialization.DataMember] public string? Template270ImageId { get; set; }
        [System.Runtime.Serialization.DataMember] public bool CopyTemplateStructures { get; set; }
    }
}
