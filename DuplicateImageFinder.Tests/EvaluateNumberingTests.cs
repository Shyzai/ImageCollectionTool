using ImageCollectionTool.ViewModels;
using Xunit;

namespace DuplicateImageFinder.Tests
{
    public class EvaluateNumberingTests
    {
        // Builds fake file paths — the method only inspects filenames, not file contents.
        private static string[] Paths(params string[] names) =>
            names.Select(n => $"C:\\folder\\{n}").ToArray();

        [Fact]
        public void CorrectlyNumbered_ReturnsNoFixes()
        {
            var (text, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_2.jpg", "kw_3.jpg"));
            Assert.Equal("All images are correctly numbered", text);
            Assert.Empty(fixes);
        }

        [Fact]
        public void SingleFile_ReturnsNoFixes()
        {
            var (text, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg"));
            Assert.Equal("All images are correctly numbered", text);
            Assert.Empty(fixes);
        }

        [Fact]
        public void EmptyArray_ReturnsNoFixes()
        {
            var (text, fixes) = MainViewModel.EvaluateNumbering([]);
            Assert.Equal("All images are correctly numbered", text);
            Assert.Empty(fixes);
        }

        [Fact]
        public void MissingNumber_ReportedInText()
        {
            // Files: 1, 3 — missing 2
            var (text, _) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_3.jpg"));
            Assert.Contains("2", text);
        }

        [Fact]
        public void ExtraFile_MappedToMissingSlot()
        {
            // Files: 1, 3 — maxRef=2, so 3 is an extra; missing slot is 2
            var (_, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_3.jpg"));
            Assert.Single(fixes);
            Assert.EndsWith("kw_3.jpg", fixes[0].OldPath);
            Assert.Equal(2, fixes[0].NewNumber);
        }

        [Fact]
        public void MultipleGaps_AllReported()
        {
            // Files: 1, 4, 5 — reference range is 1–3, so 2 and 3 are missing; both extras get mapped
            var (text, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_4.jpg", "kw_5.jpg"));
            Assert.Contains("2", text);
            Assert.Contains("3", text);
            Assert.Equal(2, fixes.Count);
            Assert.Equal(2, fixes[0].NewNumber);
            Assert.Equal(3, fixes[1].NewNumber);
        }

        [Fact]
        public void SequenceFiles_CountedAsSingleEntry()
        {
            // kw_1a and kw_1b share number 1; kw_2 is number 2 — no gaps
            var (text, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1a.jpg", "kw_1b.jpg", "kw_2.jpg"));
            Assert.Equal("All images are correctly numbered", text);
            Assert.Empty(fixes);
        }

        [Fact]
        public void SequenceFiles_GapAfterSequence_Reported()
        {
            // kw_1a and kw_1b share 1; kw_3 is present but 2 is missing
            var (text, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1a.jpg", "kw_1b.jpg", "kw_3.jpg"));
            Assert.Contains("2", text);
            Assert.Single(fixes);
            Assert.Equal(2, fixes[0].NewNumber);
        }
    }
}
