using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace ImageCollectionTool.ViewModels
{
    public partial class MainViewModel
    {
        // Replaces the duplicate pair collection, wiring up selection-change listeners on each new pair.
        internal void ReplacePairs(IEnumerable<DuplicatePairViewModel> newPairs)
        {
            foreach (var pair in DuplicatePairs)
                pair.PropertyChanged -= OnPairSelectionChanged;

            DuplicatePairs.Clear();

            foreach (var pair in newPairs)
            {
                pair.PropertyChanged += OnPairSelectionChanged;
                DuplicatePairs.Add(pair);
            }

            UpdateDeleteState();
        }

        private void OnPairSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DuplicatePairViewModel.IsSelected))
                UpdateDeleteState();
        }

        // Recomputes delete button state and label based on current pair selections.
        private void UpdateDeleteState()
        {
            HasDuplicates = DuplicatePairs.Count > 0;
            CanDeleteDuplicates = DuplicatePairs.Any(p => p.IsSelected);
            OnPropertyChanged(nameof(DeleteSelectedLabel));
        }
    }
}
