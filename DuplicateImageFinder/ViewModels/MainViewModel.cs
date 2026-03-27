using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageCollectionTool.ViewModels
{
    // ObservableObject provides INotifyPropertyChanged; the partial keyword is required
    // so the CommunityToolkit source generator can extend this class with property/command boilerplate.
    public partial class MainViewModel : ObservableObject
    {
        // --- State ---

        private Configuration _configFile = null!;
        private string _targetFolder = "";

        // Retained so the action buttons can act on results from the most recent Run.
        private string _lastDuplicatesFolder = "";
        private string _lastKeyword = "";
        private List<(string OldPath, int NewNumber)> _lastNumberingFixes = [];

        // --- Observable properties (source generator emits the public PascalCase counterparts) ---

        [ObservableProperty] private string _folderText = "Current Folder: ";
        [ObservableProperty] private string _commonWords = "";
        [ObservableProperty] private bool _findDuplicates = false;
        [ObservableProperty] private bool _isRunEnabled = true;
        [ObservableProperty] private bool _canDeleteDuplicates = false;
        [ObservableProperty] private bool _canFixNumbering = false;

        // Shown in the sidebar while a run is in progress.
        [ObservableProperty] private string _progressText = "";
        public bool IsRunning => !IsRunEnabled;
        partial void OnIsRunEnabledChanged(bool value) => OnPropertyChanged(nameof(IsRunning));

        // Populated after each Run to drive the structured output panel.
        [ObservableProperty] private string _searchSummary = "";
        [ObservableProperty] private string _numberingText = "";
        [ObservableProperty] private bool _hasResults = false;

        // Set when any operation fails; shown inline in the output panel instead of a popup.
        [ObservableProperty] private string _errorMessage = "";
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

        // True when the last Run included duplicate detection and found at least one pair.
        [ObservableProperty] private bool _hasDuplicates = false;

        // True when the last Run included duplicate detection (regardless of results).
        [ObservableProperty] private bool _duplicatesWereChecked = false;

        // Computed visibility helpers — updated whenever their dependencies change.
        public bool ShowDuplicatesSection   => HasResults && DuplicatesWereChecked;
        public bool ShowNoDuplicatesMessage => HasResults && DuplicatesWereChecked && !HasDuplicates;

        partial void OnHasResultsChanged(bool value)           => NotifyVisibility();
        partial void OnDuplicatesWereCheckedChanged(bool value) => NotifyVisibility();
        partial void OnHasDuplicatesChanged(bool value)         => NotifyVisibility();
        private void NotifyVisibility()
        {
            OnPropertyChanged(nameof(ShowDuplicatesSection));
            OnPropertyChanged(nameof(ShowNoDuplicatesMessage));
        }

        // The duplicate pair cards shown in the output panel.
        // Using ObservableCollection so the ItemsControl updates when pairs are removed after deletion.
        public ObservableCollection<DuplicatePairViewModel> DuplicatePairs { get; } = [];

        // Label for the delete button, e.g. "Delete 2 Selected".
        public string DeleteSelectedLabel =>
            $"Delete {DuplicatePairs.Count(p => p.IsSelected)} Selected";

        // --- Constructor ---

        public MainViewModel()
        {
            try
            {
                // Load the persisted search directory from App.config on startup.
                _configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                _targetFolder = _configFile.AppSettings.Settings["Search Directory"]?.Value ?? "";
                FolderText = "Current Folder: " + ShortenPath(_targetFolder);
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error reading configurations: " + ex.Message;
            }
        }
    }
}
