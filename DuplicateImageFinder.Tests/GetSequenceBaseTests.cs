using ImageCollectionTool.ViewModels;
using Xunit;

namespace DuplicateImageFinder.Tests
{
    public class GetSequenceBaseTests
    {
        [Fact]
        public void PlainNumberedFile_ReturnsUnchanged()
        {
            Assert.Equal("kw_2", MainViewModel.GetSequenceBase("kw_2.jpg"));
        }

        [Fact]
        public void SequenceFirstFile_StripsA()
        {
            Assert.Equal("kw_1", MainViewModel.GetSequenceBase("kw_1a.jpg"));
        }

        [Fact]
        public void SequenceFollowingFile_StripsLetter()
        {
            Assert.Equal("kw_1", MainViewModel.GetSequenceBase("kw_1b.jpg"));
            Assert.Equal("kw_1", MainViewModel.GetSequenceBase("kw_1z.jpg"));
        }

        [Fact]
        public void SequenceFiles_SameBase()
        {
            string a = MainViewModel.GetSequenceBase("kw_1a.jpg");
            string b = MainViewModel.GetSequenceBase("kw_1b.jpg");
            Assert.Equal(a, b);
        }

        [Fact]
        public void DifferentNumbers_DifferentBase()
        {
            string one = MainViewModel.GetSequenceBase("kw_1.jpg");
            string two = MainViewModel.GetSequenceBase("kw_2.jpg");
            Assert.NotEqual(one, two);
        }

        [Fact]
        public void NoUnderscore_ReturnsNameWithoutExtension()
        {
            Assert.Equal("image", MainViewModel.GetSequenceBase("image.jpg"));
        }

        [Fact]
        public void SingleLetterSuffix_NotStripped()
        {
            // "kw_a" has suffix "a" which is length 1 — should not be stripped (no leading digit)
            Assert.Equal("kw_a", MainViewModel.GetSequenceBase("kw_a.jpg"));
        }
    }
}
