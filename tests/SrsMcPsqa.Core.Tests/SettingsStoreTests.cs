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
