using ImageCollectionTool;
using Xunit;

namespace DuplicateImageFinder.Tests
{
    public class GetImageNumberTests
    {
        [Fact]
        public void NormalFile_ReturnsNumber()
        {
            Assert.Equal(5, ImageMatcher.GetImageNumber("Cat_5.jpg"));
        }

        [Fact]
        public void LargeNumber_ReturnsNumber()
        {
            Assert.Equal(123, ImageMatcher.GetImageNumber("Cat_123.png"));
        }

        [Fact]
        public void NoUnderscore_ReturnsMinusOne()
        {
            Assert.Equal(-1, ImageMatcher.GetImageNumber("Cat.jpg"));
        }

        [Fact]
        public void NonNumericSuffix_ReturnsZero()
        {
            Assert.Equal(0, ImageMatcher.GetImageNumber("Cat_abc.jpg"));
        }

        [Fact]
        public void SequenceFirstStripped_ReturnsNumber()
        {
            // Caller strips the 'a' before passing in (e.g. "Cat_1a.jpg" → "Cat_1.jpg")
            Assert.Equal(1, ImageMatcher.GetImageNumber("Cat_1.jpg"));
        }

        [Fact]
        public void MultipleUnderscores_UsesLastOne()
        {
            Assert.Equal(7, ImageMatcher.GetImageNumber("My_Cat_7.jpg"));
        }

        [Fact]
        public void SequenceVariants_ReturnSameValue()
        {
            // Both return the same value — the filter that prevents same-numbered
            // sequence files (e.g. Cat_1a and Cat_1b) from being flagged as duplicates relies on this.
            Assert.Equal(
                ImageMatcher.GetImageNumber("Cat_1a.jpg"),
                ImageMatcher.GetImageNumber("Cat_1b.jpg"));
        }
    }
}
