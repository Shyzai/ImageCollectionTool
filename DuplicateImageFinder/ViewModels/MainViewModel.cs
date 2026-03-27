using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
        private List<(string OldPath, int NewNumber)> _lastNumberingFixes = [];

        // Matches files following a sequence pattern, e.g. "Name_1a.jpg" (first in group) and "Name_1b.jpg" (following).
        // These are treated as a single logical image for numbering purposes.
        private static readonly Regex s_sequenceFirstRegex     = new Regex(@"\w*_\d*a.*",     RegexOptions.Compiled);
        private static readonly Regex s_sequenceFollowingRegex = new Regex(@"\w*_\d*[b-z].*", RegexOptions.Compiled);

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
        public bool ShowDuplicatesSection  => HasResults && DuplicatesWereChecked;
        public bool ShowNoDuplicatesMessage => HasResults && DuplicatesWereChecked && !HasDuplicates;

        partial void OnHasResultsChanged(bool value)          => NotifyVisibility();
        partial void OnDuplicatesWereCheckedChanged(bool value) => NotifyVisibility();
        partial void OnHasDuplicatesChanged(bool value)        => NotifyVisibility();
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

        // --- Commands ---

        // Opens a folder picker and persists the selected path to App.config.
        [RelayCommand]
        private void SelectFolder()
        {
            try
            {
                using var fbd = new FolderBrowserDialog();
                DialogResult result = fbd.ShowDialog();

                if (!string.IsNullOrEmpty(fbd.SelectedPath) && result == DialogResult.OK)
                {
                    _configFile.AppSettings.Settings["Search Directory"].Value = fbd.SelectedPath;
                    _targetFolder = fbd.SelectedPath;
                    FolderText = "Current Folder: " + ShortenPath(fbd.SelectedPath);

                    _configFile.Save(ConfigurationSaveMode.Modified);
                    ConfigurationManager.RefreshSection(_configFile.AppSettings.SectionInformation.Name);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error selecting folder: " + ex.Message;
            }
        }

        // Runs the analysis on a background thread to keep the UI responsive.
        // Replaces the output panel contents and enables action buttons if results are found.
        [RelayCommand]
        private async Task Run()
        {
            try
            {
                if (string.IsNullOrEmpty(_targetFolder)) throw new Exception("Folder was not selected.");
                if (string.IsNullOrWhiteSpace(CommonWords)) throw new Exception("Common word required");

                // Capture UI-bound values before switching to the background thread.
                string keyword = CommonWords;
                string folder = _targetFolder;
                bool findDuplicates = FindDuplicates;

                ErrorMessage = "";
                IsRunEnabled = false;
                ProgressText = "Scanning files...";

                var progress = new Progress<string>(msg => ProgressText = msg);

                var (searchSummary, numberingText, duplicates, duplicatesFolder, numberingFixes) =
                    await Task.Run(() => RunAnalysis(folder, keyword, findDuplicates, progress));

                SearchSummary = searchSummary;
                NumberingText = numberingText;
                HasResults = true;
                DuplicatesWereChecked = findDuplicates;
                _lastDuplicatesFolder = duplicatesFolder;
                _lastNumberingFixes = numberingFixes;
                CanFixNumbering = numberingFixes.Count > 0;

                var pairs = duplicates.Select(d => new DuplicatePairViewModel(d.Path1, d.Path2, d.GoodMatches));
                ReplacePairs(pairs);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                ProgressText = "";
                IsRunEnabled = true;
            }
        }

        // Deletes the higher-numbered image from each selected pair.
        // Unselected pairs are kept in the main folder; their staging copies are removed.
        [RelayCommand]
        private void DeleteDuplicates()
        {
            try
            {
                var toDelete = DuplicatePairs.Where(p => p.IsSelected).ToList();
                var toKeep   = DuplicatePairs.Where(p => !p.IsSelected).ToList();

                foreach (var pair in toDelete)
                {
                    int num1 = ImageMatcher.GetImageNumber(pair.FileName1);
                    int num2 = ImageMatcher.GetImageNumber(pair.FileName2);

                    // Keep the lower-numbered image; delete the higher-numbered one.
                    string pathToDelete = num1 >= num2 ? pair.Path1 : pair.Path2;
                    string nameToDelete = Path.GetFileName(pathToDelete);

                    string pathToKeep = pathToDelete == pair.Path1 ? pair.Path2 : pair.Path1;
                    string nameToKeep = Path.GetFileName(pathToKeep);

                    string duplicatesCopyToDelete = Path.Combine(_lastDuplicatesFolder, nameToDelete);
                    string duplicatesCopyToKeep   = Path.Combine(_lastDuplicatesFolder, nameToKeep);

                    // Delete from the staging folder and the original source folder.
                    if (File.Exists(duplicatesCopyToDelete))
                    {
                        File.Delete(duplicatesCopyToDelete);
                        if (File.Exists(pathToDelete)) File.Delete(pathToDelete);
                    }
                    if (File.Exists(duplicatesCopyToKeep)) File.Delete(duplicatesCopyToKeep);
                }

                // Remove staging copies for unselected pairs — originals are left untouched.
                foreach (var pair in toKeep)
                {
                    string copy1 = Path.Combine(_lastDuplicatesFolder, pair.FileName1);
                    string copy2 = Path.Combine(_lastDuplicatesFolder, pair.FileName2);
                    if (File.Exists(copy1)) File.Delete(copy1);
                    if (File.Exists(copy2)) File.Delete(copy2);
                }

                // Unsubscribe all pairs and clear the collection in one operation.
                foreach (var pair in DuplicatePairs)
                    pair.PropertyChanged -= OnPairSelectionChanged;
                DuplicatePairs.Clear();

                // Remove the staging folder if it's now empty.
                if (Directory.Exists(_lastDuplicatesFolder) && !Directory.EnumerateFiles(_lastDuplicatesFolder).Any())
                    Directory.Delete(_lastDuplicatesFolder);

                UpdateDeleteState();
            }
            catch (Exception ex)
            {
                ErrorMessage = "Delete failed: " + ex.Message;
            }
        }

        // Renames files to fill gaps in the numbering sequence (e.g. 1, 2, 4 → 1, 2, 3).
        [RelayCommand]
        private void FixNumbering()
        {
            try
            {
                foreach (var (oldPath, newNumber) in _lastNumberingFixes)
                {
                    string dir  = Path.GetDirectoryName(oldPath)!;
                    string name = Path.GetFileNameWithoutExtension(oldPath);
                    string ext  = Path.GetExtension(oldPath);

                    // Replace everything after the last underscore with the new number.
                    int underscoreIdx = name.LastIndexOf('_');
                    string newName = name.Substring(0, underscoreIdx + 1) + newNumber + ext;
                    File.Move(oldPath, Path.Combine(dir, newName));
                }

                NumberingText += $"\n> Renamed {_lastNumberingFixes.Count} file(s) to fix numbering.";
                _lastNumberingFixes = [];
                CanFixNumbering = false;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Fix numbering failed: " + ex.Message;
            }
        }

        // --- Pair collection management ---

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

        // --- Analysis logic ---

        // Performs file discovery, numbering checks, and optionally duplicate detection.
        // Runs on a background thread; must not touch UI or observable properties.
        private static (string SearchSummary, string NumberingText, List<(string Path1, string Path2, int GoodMatches)> Duplicates,
            string DuplicatesFolder, List<(string OldPath, int NewNumber)> NumberingFixes)
            RunAnalysis(string targetFolder, string keyword, bool findDuplicates, IProgress<string>? progress = null)
        {
            List<(string Path1, string Path2, int GoodMatches)> duplicates = [];

            string searchSummary = $"Searching in: {targetFolder}\nFound {{0}} files with the word '{keyword}'";

            string[] files = Directory.GetFiles(targetFolder, keyword + "_*.*");
            searchSummary = string.Format(searchSummary, files.Length);

            // Build parallel arrays: the number extracted from each filename, and what it should be.
            int[] referenceNums  = new int[files.Length];
            int[] imageNameNums  = new int[files.Length];
            int numIgnoredImages = 0;

            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);

                if (s_sequenceFirstRegex.IsMatch(fileName))
                {
                    // Strip the trailing letter (e.g. 'a') before extracting the number.
                    imageNameNums[i] = ImageMatcher.GetImageNumber(fileName.Remove(fileName.IndexOf(".") - 1, 1));
                    referenceNums[i] = i + 1 - numIgnoredImages;
                }
                else if (s_sequenceFollowingRegex.IsMatch(fileName))
                {
                    // This file shares a number with the previous one; don't advance the reference counter.
                    imageNameNums[i] = imageNameNums[i - 1];
                    referenceNums[i] = referenceNums[i - 1];
                    numIgnoredImages++;
                }
                else
                {
                    imageNameNums[i] = ImageMatcher.GetImageNumber(fileName);
                    referenceNums[i] = i + 1 - numIgnoredImages;
                }
            }

            // Numbers that appear in the expected sequence but are absent from the file names.
            var missingNums = referenceNums.Except(imageNameNums).ToList();

            // Files whose number exceeds the expected range are candidates to be renumbered into the gaps.
            int maxRef = files.Length - numIgnoredImages;
            var extras = new List<(string Path, int Num)>();
            for (int i = 0; i < files.Length; i++)
                if (imageNameNums[i] > maxRef)
                    extras.Add((files[i], imageNameNums[i]));

            extras.Sort((a, b) => a.Num.CompareTo(b.Num));
            var sortedMissing = missingNums.OrderBy(n => n).ToList();

            // Pair each out-of-range file with the lowest missing number.
            var numberingFixes = new List<(string OldPath, int NewNumber)>();
            for (int i = 0; i < Math.Min(extras.Count, sortedMissing.Count); i++)
                numberingFixes.Add((extras[i].Path, sortedMissing[i]));

            string numberingText;
            if (missingNums.Count > 0)
            {
                var sb = new StringBuilder("Missing number(s): ");
                foreach (int n in missingNums)
                    sb.Append(n).Append(", ");
                numberingText = sb.ToString().TrimEnd(',', ' ');
            }
            else
            {
                numberingText = "All images are correctly numbered";
            }

            // Clear any leftover staging folder from a previous run before creating a fresh one.
            string duplicatesFolder = targetFolder + "\\Potential_" + keyword + "_Duplicates";
            if (Directory.Exists(duplicatesFolder))
                Directory.Delete(duplicatesFolder, true);

            if (findDuplicates)
            {
                if (files.Length > 1)
                {
                    Directory.CreateDirectory(duplicatesFolder);
                    duplicates = FindDuplicateImages(files, duplicatesFolder, progress);
                }
                else
                {
                    numberingText += "\nNot enough images to check for duplicates.";
                }
            }

            return (searchSummary, numberingText, duplicates, duplicatesFolder, numberingFixes);
        }

        // Runs image similarity matching, filters out same-numbered pairs, and copies matches
        // into the staging folder so the user can review them before deleting.
        private static List<(string Path1, string Path2, int GoodMatches)> FindDuplicateImages(string[] files, string duplicatesFolder, IProgress<string>? progress = null)
        {
            var results = ImageMatcher.FindDuplicates(files, progress: progress)
                .Where(r => ImageMatcher.GetImageNumber(Path.GetFileName(r.Path1)) !=
                            ImageMatcher.GetImageNumber(Path.GetFileName(r.Path2)))
                .ToList();

            if (results.Count == 0)
            {
                Directory.Delete(duplicatesFolder);
                return results;
            }

            foreach (var (path1, path2, _) in results)
            {
                string name1 = Path.GetFileName(path1);
                string name2 = Path.GetFileName(path2);

                // Copy both files to the staging folder (guard against duplicate file names across pairs).
                if (!File.Exists(duplicatesFolder + "\\" + name1)) File.Copy(path1, duplicatesFolder + "\\" + name1);
                if (!File.Exists(duplicatesFolder + "\\" + name2)) File.Copy(path2, duplicatesFolder + "\\" + name2);
            }

            return results;
        }

        // Keeps the drive root and the final folder name, replacing intermediate segments with "...".
        // E.g. "C:\Users\Illya\Pictures\Saved Pictures\Folder" → "C:\...\Folder"
        internal static string ShortenPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 3) return path;
            return parts[0] + Path.DirectorySeparatorChar + "..." + Path.DirectorySeparatorChar + parts[^1];
        }
    }
}
