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
