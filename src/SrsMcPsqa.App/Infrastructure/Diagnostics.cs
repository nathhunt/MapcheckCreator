using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>Text for the log that helps diagnose problems: environment, settings in use, geometry.</summary>
    public static class Diagnostics
    {
        /// <summary>App version and build commit, e.g. 1.0.0-beta+a1b2c3d (from the informational version stamped at build).</summary>
        public static string AppVersion { get; } =
            typeof(Diagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Diagnostics).Assembly.GetName().Version?.ToString(3) ?? "?";

        public static string Environment()
        {
            string Ver(string name)
            {
                var a = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == name);
                if (a != null) return a.GetName().Version?.ToString() ?? "?";
                try { return Assembly.Load(name).GetName().Version?.ToString() ?? "?"; } catch { return "not found"; }
            }
            return $"Environment: user {System.Environment.UserDomainName}\\{System.Environment.UserName} on {System.Environment.MachineName}; " +
                   $"{System.Environment.OSVersion}; CLR {System.Environment.Version}; {(System.Environment.Is64BitProcess ? "64" : "32")}-bit; " +
                   $"exe {Assembly.GetEntryAssembly()?.Location}; ESAPI API {Ver("VMS.TPS.Common.Model.API")}, Types {Ver("VMS.TPS.Common.Model.Types")}; " +
                   $"WPF-UI {Ver("Wpf.Ui")}; culture {CultureInfo.CurrentCulture.Name}; log level {Log.MinLevel}.";
        }

        public static string Settings(AppSettings s) => string.Format(CultureInfo.InvariantCulture,
            "Settings in use: template '{0}' (0° '{1}' study '{2}', 270° '{3}' study '{4}'); QA course '{5}'; naming '{6}'; helpers '{7}'; " +
            "edge {8} mm, tau {9}, min diodes {10}, alpha {11}, fine-tune ±{12}/{13} mm, shift threshold {14} mm; auto-save {15}; " +
            "collision: origin {16} mm above couch, head end {17} mm, lateral {18} mm (max {19}), warn {20}/risk {21} mm, head {22}/{23} mm, couch {24}×{25} mm.",
            s.TemplatePatientId, s.Template0ImageId, s.Template0StudyId, s.Template270ImageId, s.Template270StudyId, s.QaCourseId, s.PlanNamingPattern, s.HelperPatterns,
            s.EdgeMarginMm, s.Tau, s.MinDiodes, s.Alpha, s.FineTuneInPlaneRangeMm, s.FineTuneNormalRangeMm, s.ShiftThresholdMm, s.AutoSave,
            s.CollisionOriginAboveCouchMm, s.CollisionCouchHeadEndSupMm, s.CollisionPhantomLateralOffsetMm, s.CollisionMaxLateralOffsetMm,
            s.CollisionWarningMm, s.CollisionRiskMm, s.CollisionHeadClearanceMm, s.CollisionHeadRadiusMm, s.CollisionCouchWidthMm, s.CollisionCouchThicknessMm);

        public static string Vec(Core.Geometry.Vec3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}, {2:0.0})", v.X, v.Y, v.Z);
    }
}
