using ImageCollectionTool.ViewModels;
using Xunit;

namespace DuplicateImageFinder.Tests
{
    public class SubfolderScanTests
    {
        private static string[] Paths(params string[] fullPaths) => fullPaths;

        // --- EvaluateNumberingByKeyword ---

        [Fact]
        public void EvaluateNumberingByKeyword_SingleKeyword_CorrectlyNumbered_NoIssues()
        {
            var files = Paths(
                @"C:\root\pikachu_1.jpg",
                @"C:\root\pikachu_2.jpg",
                @"C:\root\pikachu_3.jpg");

            var (results, fixes) = MainViewModel.EvaluateNumberingByKeyword(files);

            Assert.Single(results);
            Assert.False(results[0].HasIssues);
            Assert.Empty(fixes);
        }

        [Fact]
        public void EvaluateNumberingByKeyword_TwoKeywords_EachCheckedSeparately()
        {
            var files = Paths(
                @"C:\root\pikachu_1.jpg",
                @"C:\root\pikachu_2.jpg",
                @"C:\root\charizard_1.jpg",
                @"C:\root\charizard_3.jpg"); // missing 2

            var (results, fixes) = MainViewModel.EvaluateNumberingByKeyword(files);

            Assert.Equal(2, results.Count);
            var pikachu   = results.Find(r => r.Keyword == "pikachu")!;
            var charizard = results.Find(r => r.Keyword == "charizard")!;

            Assert.False(pikachu.HasIssues);
            Assert.True(charizard.HasIssues);
            Assert.Single(fixes);
        }

        [Fact]
        public void EvaluateNumberingByKeyword_FilesWithoutPattern_Skipped()
        {
            var files = Paths(
                @"C:\root\pikachu_1.jpg",
                @"C:\root\randomimage.jpg"); // no underscore-number pattern

            var (results, _) = MainViewModel.EvaluateNumberingByKeyword(files);

            Assert.Single(results); // only pikachu group
            Assert.Equal("pikachu", results[0].Keyword);
        }

        [Fact]
        public void EvaluateNumberingByKeyword_EmptyArray_ReturnsEmpty()
        {
            var (results, fixes) = MainViewModel.EvaluateNumberingByKeyword([]);
            Assert.Empty(results);
            Assert.Empty(fixes);
        }

        [Fact]
        public void EvaluateNumberingByKeyword_TwoKeywords_FixPointsToCorrectFile()
        {
            var files = Paths(
                @"C:\root\pikachu_1.jpg",
                @"C:\root\pikachu_2.jpg",
                @"C:\root\charizard_1.jpg",
                @"C:\root\charizard_3.jpg"); // missing 2 — charizard_3 should be renamed to 2

            var (_, fixes) = MainViewModel.EvaluateNumberingByKeyword(files);

            Assert.Single(fixes);
            Assert.EndsWith("charizard_3.jpg", fixes[0].OldPath);
            Assert.Equal(2, fixes[0].NewNumber);
        }

        [Fact]
        public void EvaluateNumberingByKeyword_AllFilesWithoutPattern_ReturnsEmpty()
        {
            var files = Paths(
                @"C:\root\randomimage.jpg",
                @"C:\root\anotherimage.png");

            var (results, fixes) = MainViewModel.EvaluateNumberingByKeyword(files);

            Assert.Empty(results);
            Assert.Empty(fixes);
        }

        // --- Visibility helpers ---

        [Fact]
        public void ShowKeywordSection_TrueByDefault()
        {
            var vm = new MainViewModel();
            Assert.True(vm.ShowKeywordSection);
        }

        [Fact]
        public void ShowKeywordSection_FalseWhenScanSubfoldersEnabled()
        {
            var vm = new MainViewModel();
            vm.ScanSubfolders = true;
            Assert.False(vm.ShowKeywordSection);
        }

        [Fact]
        public void ShowSubfolderNumberingOption_FalseByDefault()
        {
            var vm = new MainViewModel();
            Assert.False(vm.ShowSubfolderNumberingOption);
        }

        [Fact]
        public void ShowSubfolderNumberingOption_TrueWhenScanSubfoldersEnabled()
        {
            var vm = new MainViewModel();
            vm.ScanSubfolders = true;
            Assert.True(vm.ShowSubfolderNumberingOption);
        }
    }
}
