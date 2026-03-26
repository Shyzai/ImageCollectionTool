using ImageCollectionTool.ViewModels;
using Xunit;

namespace DuplicateImageFinder.Tests
{
    public class ShortenPathTests
    {
        [Fact]
        public void EmptyString_ReturnsEmpty()
        {
            Assert.Equal("", MainViewModel.ShortenPath(""));
        }

        [Fact]
        public void NullString_ReturnsNull()
        {
            Assert.Null(MainViewModel.ShortenPath(null!));
        }

        [Fact]
        public void ThreeParts_ReturnsUnchanged()
        {
            Assert.Equal(@"C:\Users\Illya", MainViewModel.ShortenPath(@"C:\Users\Illya"));
        }

        [Fact]
        public void FourParts_Shortens()
        {
            Assert.Equal(@"C:\...\Pictures", MainViewModel.ShortenPath(@"C:\Users\Illya\Pictures"));
        }

        [Fact]
        public void ManyParts_KeepsOnlyRootAndLast()
        {
            Assert.Equal(@"C:\...\Fire Emblem", MainViewModel.ShortenPath(@"C:\Users\Illya\Pictures\Saved Pictures\Fire Emblem"));
        }

        [Fact]
        public void RootOnly_ReturnsUnchanged()
        {
            Assert.Equal(@"C:\", MainViewModel.ShortenPath(@"C:\"));
        }
    }
}
