using System.Collections.Concurrent;
using System.IO;
using ImageCollectionTool;
using Xunit;

namespace DuplicateImageFinder.Tests
{
    public class FindDuplicatesTests : IClassFixture<FindDuplicatesTests.Fixture>
    {
        public class Fixture
        {
            public string TempDir    { get; } = Path.Combine(Path.GetTempPath(), "ImageMatcherTests_FindDuplicates");
            public string Image1     { get; }
            public string Image1Copy { get; }
            public string Image2     { get; }
            public string Image3     { get; }

            public Fixture()
            {
                Directory.CreateDirectory(TempDir);
                Image1     = Path.Combine(TempDir, "gradient.png");
                Image1Copy = Path.Combine(TempDir, "gradient_copy.png");
                Image2     = Path.Combine(TempDir, "hstripes.png");
                Image3     = Path.Combine(TempDir, "checker.png");

                TestImageFactory.WriteGradient(Image1);
                TestImageFactory.WriteGradient(Image1Copy);
                TestImageFactory.WriteHorizontalStripes(Image2);
                TestImageFactory.WriteCheckerboard(Image3);
            }
        }

        private readonly Fixture _fixture;
        public FindDuplicatesTests(Fixture fixture) => _fixture = fixture;

        [Fact]
        public void TwoCopiesOfSameImage_ReturnedAsDuplicatePair()
        {
            var results = ImageMatcher.FindDuplicates([_fixture.Image1, _fixture.Image1Copy], hammingThreshold: 64, minFeatureMatches: 0);
            Assert.Single(results);
        }

        [Fact]
        public void AllUniqueImages_ReturnsEmpty()
        {
            var results = ImageMatcher.FindDuplicates(
                [_fixture.Image1, _fixture.Image2, _fixture.Image3],
                hammingThreshold: 5,
                minFeatureMatches: 0);
            Assert.Empty(results);
        }

        [Fact]
        public void SingleImage_ReturnsEmpty()
        {
            var results = ImageMatcher.FindDuplicates([_fixture.Image1]);
            Assert.Empty(results);
        }

        [Fact]
        public void EmptyArray_ReturnsEmpty()
        {
            var results = ImageMatcher.FindDuplicates([]);
            Assert.Empty(results);
        }

        [Fact]
        public void WithCandidates_ProgressIsReported()
        {
            // Uses a synchronous IProgress<string> to avoid threading issues with Progress<T>.
            var messages = new ConcurrentBag<string>();
            var progress = new SyncProgress(messages);

            ImageMatcher.FindDuplicates(
                [_fixture.Image1, _fixture.Image1Copy],
                hammingThreshold: 64,
                minFeatureMatches: 0,
                progress: progress);

            Assert.NotEmpty(messages);
        }

        private sealed class SyncProgress(ConcurrentBag<string> messages) : IProgress<string>
        {
            public void Report(string value) => messages.Add(value);
        }
    }
}
