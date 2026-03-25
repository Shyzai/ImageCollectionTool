using System.IO;
using ImageCollectionTool;
using Xunit;

namespace DuplicateImageFinder.Tests
{
    public class PHashTests : IClassFixture<PHashTests.Fixture>
    {
        public class Fixture
        {
            public string TempDir         { get; } = Path.Combine(Path.GetTempPath(), "ImageMatcherTests_PHash");
            public string GradientImage   { get; }
            public string GradientCopy    { get; }
            public string HStripesImage   { get; }
            public string CheckerImage    { get; }

            public Fixture()
            {
                Directory.CreateDirectory(TempDir);
                GradientImage  = Path.Combine(TempDir, "gradient.png");
                GradientCopy   = Path.Combine(TempDir, "gradient_copy.png");
                HStripesImage  = Path.Combine(TempDir, "hstripes.png");
                CheckerImage   = Path.Combine(TempDir, "checker.png");

                TestImageFactory.WriteGradient(GradientImage);
                TestImageFactory.WriteGradient(GradientCopy);
                TestImageFactory.WriteHorizontalStripes(HStripesImage);
                TestImageFactory.WriteCheckerboard(CheckerImage);
            }
        }

        private readonly Fixture _fixture;
        public PHashTests(Fixture fixture) => _fixture = fixture;

        [Fact]
        public void SameImageHashedTwice_ProducesIdenticalHash()
        {
            var results = ImageMatcher.FindDuplicates([_fixture.GradientImage, _fixture.GradientCopy], hammingThreshold: 64, minFeatureMatches: 0);
            Assert.Single(results);
            Assert.Equal(_fixture.GradientImage, results[0].Path1);
            Assert.Equal(_fixture.GradientCopy,  results[0].Path2);
        }

        [Fact]
        public void ClearlyDifferentImages_NotReturnedAsDuplicates()
        {
            // Gradient vs horizontal stripes vs checkerboard — distinct frequency content
            var results = ImageMatcher.FindDuplicates(
                [_fixture.GradientImage, _fixture.HStripesImage, _fixture.CheckerImage],
                hammingThreshold: 5,
                minFeatureMatches: 0);
            Assert.Empty(results);
        }
    }
}
