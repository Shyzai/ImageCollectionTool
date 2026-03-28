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
            var (label, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_2.jpg", "kw_3.jpg"));
            Assert.Equal("All images are correctly numbered", label);
            Assert.Empty(numbers);
            Assert.Empty(fixes);
        }

        [Fact]
        public void SingleFile_ReturnsNoFixes()
        {
            var (label, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg"));
            Assert.Equal("All images are correctly numbered", label);
            Assert.Empty(numbers);
            Assert.Empty(fixes);
        }

        [Fact]
        public void EmptyArray_ReturnsNoFixes()
        {
            var (label, numbers, fixes) = MainViewModel.EvaluateNumbering([]);
            Assert.Equal("All images are correctly numbered", label);
            Assert.Empty(numbers);
            Assert.Empty(fixes);
        }

        [Fact]
        public void MissingNumber_ReportedInNumbers()
        {
            // Files: 1, 3 — missing 2
            var (_, numbers, _) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_3.jpg"));
            Assert.Contains("2", numbers);
        }

        [Fact]
        public void ExtraFile_MappedToMissingSlot()
        {
            // Files: 1, 3 — maxRef=2, so 3 is an extra; missing slot is 2
            var (_, _, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_3.jpg"));
            Assert.Single(fixes);
            Assert.EndsWith("kw_3.jpg", fixes[0].OldPath);
            Assert.Equal(2, fixes[0].NewNumber);
        }

        [Fact]
        public void MultipleGaps_AllReported()
        {
            // Files: 1, 4, 5 — reference range is 1–3, so 2 and 3 are missing; both extras get mapped
            var (_, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_4.jpg", "kw_5.jpg"));
            Assert.Contains("2", numbers);
            Assert.Contains("3", numbers);
            Assert.Equal(2, fixes.Count);
            Assert.Equal(2, fixes[0].NewNumber);
            Assert.Equal(3, fixes[1].NewNumber);
        }

        [Fact]
        public void SequenceFiles_CountedAsSingleEntry()
        {
            // kw_1a and kw_1b share number 1; kw_2 is number 2 — no gaps
            var (label, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1a.jpg", "kw_1b.jpg", "kw_2.jpg"));
            Assert.Equal("All images are correctly numbered", label);
            Assert.Empty(numbers);
            Assert.Empty(fixes);
        }

        [Fact]
        public void SequenceFiles_GapAfterSequence_Reported()
        {
            // kw_1a and kw_1b share 1; kw_3 is present but 2 is missing
            var (_, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1a.jpg", "kw_1b.jpg", "kw_3.jpg"));
            Assert.Contains("2", numbers);
            Assert.Single(fixes);
            Assert.Equal(2, fixes[0].NewNumber);
        }

        [Fact]
        public void UnnumberedFile_AloneWithNumbered_NoFixes()
        {
            // kw.jpg has no number; kw_1.jpg and kw_2.jpg are correctly numbered — no gaps
            var (label, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw.jpg", "kw_1.jpg", "kw_2.jpg"));
            Assert.Equal("All images are correctly numbered", label);
            Assert.Empty(numbers);
            Assert.Empty(fixes);
        }

        [Fact]
        public void UnnumberedFile_DoesNotCountAsMissingNumber()
        {
            // kw.jpg should not be flagged as a missing numbered entry
            var (label, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw.jpg", "kw_1.jpg"));
            Assert.Equal("All images are correctly numbered", label);
            Assert.Empty(numbers);
            Assert.Empty(fixes);
        }

        [Fact]
        public void UnnumberedFile_GapStillDetected()
        {
            // kw.jpg is ignored for numbering; kw_1.jpg and kw_3.jpg have a gap at 2
            var (_, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw.jpg", "kw_1.jpg", "kw_3.jpg"));
            Assert.Contains("2", numbers);
            Assert.Single(fixes);
            Assert.Equal(2, fixes[0].NewNumber);
        }

        [Fact]
        public void UnnumberedFileOnly_NoFixes()
        {
            // Only an unnumbered file — nothing to number-check
            var (label, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw.jpg"));
            Assert.Equal("All images are correctly numbered", label);
            Assert.Empty(numbers);
            Assert.Empty(fixes);
        }

        [Fact]
        public void PlainAndSequenceFirst_SameNumber_NoFalseGap()
        {
            // kw_1.jpg and kw_1a.jpg coexist — kw_1a should be treated as a follower, not a new slot.
            var (label, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_1a.jpg"));
            Assert.Equal("All images are correctly numbered", label);
            Assert.Empty(numbers);
            Assert.Empty(fixes);
        }

        [Fact]
        public void PlainAndSequenceFirst_WithFollower_NoFalseGap()
        {
            // kw_1.jpg, kw_1a.jpg, kw_1b.jpg — all share number 1; no gaps.
            var (label, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_1a.jpg", "kw_1b.jpg"));
            Assert.Equal("All images are correctly numbered", label);
            Assert.Empty(numbers);
            Assert.Empty(fixes);
        }

        [Fact]
        public void PlainAndSequenceFirst_GapAfter_StillDetected()
        {
            // kw_1.jpg + kw_1a.jpg share slot 1; kw_3.jpg has a gap at 2.
            var (_, numbers, fixes) = MainViewModel.EvaluateNumbering(Paths("kw_1.jpg", "kw_1a.jpg", "kw_3.jpg"));
            Assert.Contains("2", numbers);
            Assert.Single(fixes);
            Assert.Equal(2, fixes[0].NewNumber);
        }
    }
}
