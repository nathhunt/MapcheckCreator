using SrsMcPsqa.Core.Services;
using Xunit;

namespace SrsMcPsqa.Core.Tests
{
    public class EclipseWarningDialogTests
    {
        private const string Seen =
            "Warning: [COUCH 0] The calibration protocol status is not Commissioning or Approved.\r\n" +
            "Warning: [COUCH 45] The calibration protocol status is not Commissioning or Approved.\r\n" +
            "Warning: [COUCH 45] The field exits the body in a region where the CT data ends. This will reduce the dose calculation accuracy because the intersection point of the fanline and the body surface cannot be found.\r\n";

        [Fact] public void WarningsWithOneButtonAreClosed() => Assert.True(EclipseWarningDialog.IsWarningsOnly(Seen, 1));
        [Fact] public void TwoButtonsAreLeft() => Assert.False(EclipseWarningDialog.IsWarningsOnly(Seen, 2));
        [Fact] public void AnErrorLineIsLeft() => Assert.False(EclipseWarningDialog.IsWarningsOnly(Seen + "Error: [COUCH 0] Calculation failed.", 1));
        [Fact] public void OtherTextIsLeft() => Assert.False(EclipseWarningDialog.IsWarningsOnly("Do you want to save changes?", 1));
        [Fact] public void EmptyIsLeft() => Assert.False(EclipseWarningDialog.IsWarningsOnly("  ", 1));
    }
}
