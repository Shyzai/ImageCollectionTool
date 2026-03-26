using ImageCollectionTool.ViewModels;
using Xunit;

namespace DuplicateImageFinder.Tests
{
    public class PairSelectionTests
    {
        // --- DuplicatePairViewModel ---

        [Fact]
        public void NewPair_IsSelectedByDefault()
        {
            var pair = new DuplicatePairViewModel("a.jpg", "b.jpg", 50);
            Assert.True(pair.IsSelected);
        }

        [Fact]
        public void ToggleSelection_FlipsToFalse()
        {
            var pair = new DuplicatePairViewModel("a.jpg", "b.jpg", 50);
            pair.ToggleSelectionCommand.Execute(null);
            Assert.False(pair.IsSelected);
        }

        [Fact]
        public void ToggleSelection_FlipsBackToTrue()
        {
            var pair = new DuplicatePairViewModel("a.jpg", "b.jpg", 50);
            pair.ToggleSelectionCommand.Execute(null);
            pair.ToggleSelectionCommand.Execute(null);
            Assert.True(pair.IsSelected);
        }

        [Fact]
        public void ToggleSelection_RaisesPropertyChanged()
        {
            var pair = new DuplicatePairViewModel("a.jpg", "b.jpg", 50);
            string? changedProperty = null;
            pair.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

            pair.ToggleSelectionCommand.Execute(null);

            Assert.Equal(nameof(pair.IsSelected), changedProperty);
        }

        // --- MainViewModel selection state ---

        [Fact]
        public void ReplacePairs_AllSelectedByDefault_CanDeleteIsTrue()
        {
            var vm = new MainViewModel();
            vm.ReplacePairs([new("a.jpg", "b.jpg", 50), new("c.jpg", "d.jpg", 80)]);
            Assert.True(vm.CanDeleteDuplicates);
        }

        [Fact]
        public void ReplacePairs_TwoPairs_LabelShowsTwo()
        {
            var vm = new MainViewModel();
            vm.ReplacePairs([new("a.jpg", "b.jpg", 50), new("c.jpg", "d.jpg", 80)]);
            Assert.Equal("Delete 2 Selected", vm.DeleteSelectedLabel);
        }

        [Fact]
        public void DeselectingAllPairs_CanDeleteIsFalse()
        {
            var vm = new MainViewModel();
            var pair1 = new DuplicatePairViewModel("a.jpg", "b.jpg", 50);
            var pair2 = new DuplicatePairViewModel("c.jpg", "d.jpg", 80);
            vm.ReplacePairs([pair1, pair2]);

            pair1.ToggleSelectionCommand.Execute(null);
            pair2.ToggleSelectionCommand.Execute(null);

            Assert.False(vm.CanDeleteDuplicates);
        }

        [Fact]
        public void DeselectingOnePair_LabelDecrements()
        {
            var vm = new MainViewModel();
            var pair1 = new DuplicatePairViewModel("a.jpg", "b.jpg", 50);
            var pair2 = new DuplicatePairViewModel("c.jpg", "d.jpg", 80);
            vm.ReplacePairs([pair1, pair2]);

            pair1.ToggleSelectionCommand.Execute(null);

            Assert.Equal("Delete 1 Selected", vm.DeleteSelectedLabel);
        }

        [Fact]
        public void ReplacePairs_ClearsOldPairs_LabelResets()
        {
            var vm = new MainViewModel();
            vm.ReplacePairs([new("a.jpg", "b.jpg", 50), new("c.jpg", "d.jpg", 80)]);
            vm.ReplacePairs([new("e.jpg", "f.jpg", 60)]);
            Assert.Equal("Delete 1 Selected", vm.DeleteSelectedLabel);
        }
    }
}
