using System.Linq;
using SrsMcPsqa.Core.Services;
using Xunit;

namespace SrsMcPsqa.Core.Tests
{
    public class EclipseTraceTests
    {
        [Theory]
        [InlineData("Progress 43%", 43)]
        [InlineData("Progress: Calculating dose 12.5 %", 12.5)]
        [InlineData("  progress (field 2 of 4) 80%  ", 80)]
        [InlineData("Progress 7,5%", 7.5)]
        public void ReadsProgressLines(string line, double expected) => Assert.Equal(expected, EclipseTrace.ProgressPercent(line));

        [Theory]
        [InlineData("Information: normalised to 95% isodose")]
        [InlineData("Iteration 2")]
        [InlineData("Progress started")]
        [InlineData("Progress 150%")]
        [InlineData("The thread 0x1a2b has exited with code 0")]
        [InlineData("")]
        [InlineData(null)]
        public void IgnoresOtherLines(string? line) => Assert.Null(EclipseTrace.ProgressPercent(line));

        [Fact]
        public void DisplayMessageSkipsInformationAndNoise()
        {
            Assert.Null(EclipseTrace.DisplayMessage("Information: something"));
            Assert.Null(EclipseTrace.DisplayMessage("The thread 42 has exited"));
            Assert.Equal("Calculating field Arc1", EclipseTrace.DisplayMessage("  Calculating field Arc1 "));
            Assert.Equal(141, EclipseTrace.DisplayMessage(new string('x', 300))!.Length);
        }
    }
}

namespace SrsMcPsqa.Core.Tests
{
    public class TraceLineBufferTests
    {
        [Fact]
        public void JoinsPartialWritesUntilWriteLine()
        {
            var b = new TraceLineBuffer();
            Assert.Empty(b.Write("Calculating "));
            Assert.Empty(b.Write("field Arc1"));
            Assert.Equal(new[] { "Calculating field Arc1 done" }, b.WriteLine(" done"));
        }

        [Fact]
        public void PassesProgressWrittenWithWriteAtOnce()
        {
            var b = new TraceLineBuffer();
            Assert.Equal(new[] { "Progress 10%" }, b.Write("Progress 10%"));
            Assert.Equal(new[] { "Progress 20%" }, b.Write("Progress 20%"));
            Assert.Empty(b.Write("Progress 3"));
            Assert.Equal(new[] { "Progress 30%" }, b.Write("0%"));
        }

        [Fact]
        public void SplitsOnLineBreaksAndCarriageReturns()
        {
            var b = new TraceLineBuffer();
            Assert.Equal(new[] { "a", "b" }, b.Write("a\r\nb\rc"));
            Assert.Equal(new[] { "c" }, b.WriteLine(null));
        }
    }
}

namespace SrsMcPsqa.Core.Tests
{
    public class EclipseIssueTests
    {
        // Lines as seen on Citrix (v18.1, AcurosXB 16.1), end-of-calculation summary.
        private const string W1 = "Warnings Warning: [COUCH 0] The field exits the body in a region where the CT data ends. This will reduce the dose calculation accuracy.";
        private const string W2 = "Warning: [COUCH 45] The field exits the body in a region where the CT data ends. This will reduce the dose calculation accuracy.";
        private const string W3 = "Warning: [COUCH 0] The field enters the body in a region where the CT data ends.";

        [Fact]
        public void ParsesWarningsWithBeam()
        {
            var i = EclipseTrace.ParseIssue(W1)!;
            Assert.False(i.IsError);
            Assert.Equal("COUCH 0", i.Beam);
            Assert.StartsWith("The field exits the body", i.Text);
            Assert.Null(EclipseTrace.ParseIssue("Messages Information: [COUCH 0] Imaging Device: ID=Aquilion LB CT"));
            Assert.Null(EclipseTrace.ParseIssue("Progress 40%"));
            Assert.Null(EclipseTrace.ParseIssue("Errors"));
        }

        [Fact]
        public void SummarisesByMessage()
        {
            var s = EclipseTrace.Summarise(new[] { W1, W2, W3 }.Select(l => EclipseTrace.ParseIssue(l)!));
            Assert.Equal(2, s.Count);
            Assert.StartsWith("Eclipse warning (COUCH 0, COUCH 45): The field exits", s[0]);
            Assert.StartsWith("Eclipse warning (COUCH 0): The field enters", s[1]);
        }

        [Theory]
        [InlineData("Errors")]
        [InlineData("Warnings")]
        [InlineData("Success True")]
        [InlineData("Messages Information: [COUCH 0] Imaging Device")]
        public void SummaryHeadersAreNotDisplayed(string line) => Assert.Null(EclipseTrace.DisplayMessage(line));
    }
}
