using System;
using System.IO;
using SrsMcPsqa.App.Infrastructure;
using Xunit;

namespace SrsMcPsqa.Core.Tests
{
    /// <summary>The settings file lives next to the test assembly, so these tests must not run in parallel with each other.</summary>
    [CollectionDefinition(nameof(SettingsStoreTests), DisableParallelization = true)]
    public sealed class SettingsStoreCollection { }

    [Collection(nameof(SettingsStoreTests))]
    public sealed class SettingsStoreTests : IDisposable
    {
        private static readonly string Dept = SettingsStore.DepartmentPath;
        private static readonly string Bak = Dept + ".bak";
        private static readonly string Tmp = Dept + ".tmp";

        public SettingsStoreTests() => Clean();
        public void Dispose() => Clean();

        private static void Clean()
        {
            foreach (var f in new[] { Dept, Bak, Tmp }) if (File.Exists(f)) File.Delete(f);
        }

        [Fact]
        public void NoFileGivesDefaultsWithoutError()
        {
            var r = SettingsStore.Load();
            Assert.Null(r.DepartmentError);
            Assert.StartsWith("built-in defaults (no site settings file)", r.Source);
            Assert.Equal("PHYS_QA", r.Settings.QaCourseId);
        }

        [Theory]
        [InlineData("{ \"QaCourseId\": \"X\", ")] // truncated (an interrupted copy)
        [InlineData("{ \"QaCourseId\": \"X\" ")]
        [InlineData("{ \"QaCourseId\": \"X\", }")] // trailing comma after a hand edit
        [InlineData("{ QaCourseId: \"X\" }")]       // unquoted name
        [InlineData("{ \"Tau\": \"high\" }")]       // wrong type
        [InlineData("null")]
        [InlineData("")]
        public void UnreadableFileIsReportedNotSilentlyReplaced(string content)
        {
            File.WriteAllText(Dept, content);
            var r = SettingsStore.Load();
            Assert.NotNull(r.DepartmentError);
            Assert.Contains(Dept, r.DepartmentError);
            Assert.StartsWith("built-in defaults: the site settings file could not be read", r.Source);
            Assert.Contains(r.Warnings, w => w.Contains("Could not read the site settings file"));
            Assert.Equal("PHYS_QA", r.Settings.QaCourseId); // built-in defaults in use
        }

        [Theory]
        [InlineData("src/SrsMcPsqa.App/SrsMcPsqa.settings.json")]
        [InlineData("deploy/SrsMcPsqa.settings.TEST.json")]
        public void ShippedSettingsFilesLoad(string relative)
        {
            var root = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(root, "SrsMcPsqa.sln"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar))!;
            File.Copy(Path.Combine(root, relative), Dept);
            var r = SettingsStore.Load();
            Assert.Null(r.DepartmentError);
            Assert.Equal("PHYS_QA", r.Settings.QaCourseId);
        }

        [Fact]
        public void PartialFileKeepsDefaultsForMissingMembers()
        {
            File.WriteAllText(Dept, "{ \"QaCourseId\": \"QA_TEST\", \"CollisionWarningMm\": 40 }");
            var r = SettingsStore.Load();
            Assert.Null(r.DepartmentError);
            Assert.StartsWith("site settings from ", r.Source);
            Assert.Equal("QA_TEST", r.Settings.QaCourseId);
            Assert.Equal(40, r.Settings.CollisionWarningMm);
            Assert.Equal(15, r.Settings.CollisionRiskMm);
            Assert.Equal(2.0, r.Settings.ShiftThresholdMm);
            Assert.Equal(1.0, r.Settings.ShiftRoundToMm);
        }

        [Fact]
        public void SaveCreatesTheFileWhenThereIsNone()
        {
            SettingsStore.SaveDepartment(new AppSettings { QaCourseId = "NEW" });
            Assert.Equal("NEW", SettingsStore.Load().Settings.QaCourseId);
            Assert.False(File.Exists(Bak));
            Assert.False(File.Exists(Tmp));
        }

        [Fact]
        public void SaveReplacesTheFileAndKeepsThePreviousAsBak()
        {
            SettingsStore.SaveDepartment(new AppSettings { QaCourseId = "OLD" });
            SettingsStore.SaveDepartment(new AppSettings { QaCourseId = "NEW" });
            Assert.Equal("NEW", SettingsStore.Load().Settings.QaCourseId);
            Assert.Contains("\"OLD\"", File.ReadAllText(Bak));
            Assert.False(File.Exists(Tmp));
        }

        [Fact]
        public void SaveOverAnUnreadableFileKeepsItAsBak()
        {
            File.WriteAllText(Dept, "{ broken");
            SettingsStore.SaveDepartment(new AppSettings { QaCourseId = "FIXED" });
            var r = SettingsStore.Load();
            Assert.Null(r.DepartmentError);
            Assert.Equal("FIXED", r.Settings.QaCourseId);
            Assert.Equal("{ broken", File.ReadAllText(Bak));
        }
    }
}

namespace SrsMcPsqa.Core.Tests
{
    public sealed class SettingsProblemsTests
    {
        [Fact]
        public void DefaultsAndShippedValuesAreUsable() => Assert.Empty(new AppSettings().Problems());

        [Fact]
        public void DefaultsMatchTheOptimiserChecks()
        {
            new AppSettings().ToOptimisation().Validate(); // doesn't throw
        }

        public static TheoryData<Action<AppSettings>, string> Bad => new TheoryData<Action<AppSettings>, string>
        {
            { s => s.Tau = 0, "Tau" },
            { s => s.Tau = 1.2, "Tau" },
            { s => s.Alpha = -0.1, "Alpha" },
            { s => s.EdgeMarginMm = 38.5, "Edge margin" },
            { s => s.MinDiodes = -1, "Minimum diodes" },
            { s => s.FineTuneNormalRangeMm = -5, "Fine-tune" },
            { s => s.ShiftThresholdMm = -1, "Shift rounding" },
            { s => s.ShiftRoundToMm = -1, "Round shifts" },
            { s => s.QaCourseId = " ", "QA course" },
            { s => s.QaCourseId = "MORE_THAN_16_CHARS", "QA course" },
            { s => s.CollisionRiskMm = 40, "warning level" },
            { s => s.CollisionHeadRadiusMm = 0, "Head and couch" },
            { s => s.CollisionOriginAboveCouchMm = -1, "origin above the couch" },
            { s => s.CollisionMaxLateralOffsetMm = -1, "lateral phantom offset" },
            { s => s.Tau = double.NaN, "Tau must be a number" },
        };

        [Theory]
        [MemberData(nameof(Bad))]
        public void OutOfRangeValuesAreNamed(Action<AppSettings> change, string expected)
        {
            var s = new AppSettings();
            change(s);
            Assert.Contains(s.Problems(), p => p.Contains(expected));
        }

        [Fact]
        public void EveryValueTheOptimiserRejectsIsAProblem()
        {
            foreach (var (tau, alpha, margin, diodes) in new[] { (0.0, 0.5, 3.0, 5), (0.85, 1.5, 3.0, 5), (0.85, 0.5, 40.0, 5), (0.85, 0.5, 3.0, -1) })
            {
                var s = new AppSettings { Tau = tau, Alpha = alpha, EdgeMarginMm = margin, MinDiodes = diodes };
                Assert.Throws<ArgumentOutOfRangeException>(() => s.ToOptimisation().Validate());
                Assert.NotEmpty(s.Problems());
            }
        }

        [Fact]
        public void OffValuesAreAllowed()
        {
            var s = new AppSettings { ShiftThresholdMm = 0, CollisionOriginAboveCouchMm = 0, CollisionRiskMm = 0, CollisionWarningMm = 0, MinDiodes = 0 };
            Assert.Empty(s.Problems());
        }
    }
}
