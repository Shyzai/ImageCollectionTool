using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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

        // Deletes the staging folder on app close so it doesn't linger between sessions.
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_lastDuplicatesFolder))
                    Directory.Delete(_lastDuplicatesFolder, true);
            }
            catch { /* Best-effort — don't block shutdown. */ }
        }
    }
}
