using ImageCollectionTool;
using Xunit;

namespace DuplicateImageFinder.Tests
{
    public class HammingDistanceTests
    {
        [Fact]
        public void IdenticalHashes_ReturnsZero()
        {
            Assert.Equal(0, ImageMatcher.HammingDistance(0xDEADBEEFCAFEBABEUL, 0xDEADBEEFCAFEBABEUL));
        }

        [Fact]
        public void OneBitDifferent_ReturnsOne()
        {
            Assert.Equal(1, ImageMatcher.HammingDistance(0b0000UL, 0b0001UL));
        }

        [Fact]
        public void AllBitsDifferent_Returns64()
        {
            Assert.Equal(64, ImageMatcher.HammingDistance(0UL, ulong.MaxValue));
        }

        [Fact]
        public void BothZero_ReturnsZero()
        {
            Assert.Equal(0, ImageMatcher.HammingDistance(0UL, 0UL));
        }
    }
}
