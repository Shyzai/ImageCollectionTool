using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.Input;

namespace ImageCollectionTool.ViewModels
{
    public partial class MainViewModel
    {
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

                    _configFile.Save(System.Configuration.ConfigurationSaveMode.Modified);
                    System.Configuration.ConfigurationManager.RefreshSection(_configFile.AppSettings.SectionInformation.Name);
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
                if (!ScanSubfolders && string.IsNullOrWhiteSpace(CommonWords)) throw new Exception("Common word required");

                // Capture UI-bound values before switching to the background thread.
                string keyword       = CommonWords;
                string folder        = _targetFolder;
                bool scanSubfolders  = ScanSubfolders;
                bool checkNumbering  = CheckSubfolderNumbering;

                _lastKeyword = keyword;
                ErrorMessage = "";
                IsRunEnabled = false;

                // Clean up the staging folder from the previous run if the target folder changed.
                if (Directory.Exists(_lastDuplicatesFolder))
                    Directory.Delete(_lastDuplicatesFolder, true);

                using var ellipsisCts = new CancellationTokenSource();
                _ = CycleProgressTextAsync("Scanning files", ellipsisCts.Token);

                var progress = new Progress<string>(msg =>
                {
                    ellipsisCts.Cancel();
                    ProgressText = msg;
                });

                var (searchSummary, numberingText, keywordNumberings, duplicates, duplicatesFolder, numberingFixes) =
                    await Task.Run(() => RunAnalysis(folder, keyword, scanSubfolders, checkNumbering, progress));

                SearchSummary = searchSummary;
                NumberingText = numberingText;
                _lastScanSubfolders = scanSubfolders;
                _lastCheckSubfolderNumbering = checkNumbering;
                _lastDuplicatesFolder = duplicatesFolder;
                HasResults = true;
                NotifyVisibility(); // re-evaluate in case HasResults was already true
                _lastNumberingFixes = numberingFixes;
                CanFixNumbering = numberingFixes.Count > 0;

                KeywordNumberings.Clear();
                foreach (var r in keywordNumberings)
                    KeywordNumberings.Add(r);

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
                    string pathToKeep   = pathToDelete == pair.Path1 ? pair.Path2 : pair.Path1;

                    string stagedToDelete = Path.Combine(_lastDuplicatesFolder, GetStagingFileName(pathToDelete, ScanSubfolders));
                    string stagedToKeep   = Path.Combine(_lastDuplicatesFolder, GetStagingFileName(pathToKeep,   ScanSubfolders));

                    // Delete from the staging folder and the original source folder.
                    if (File.Exists(stagedToDelete))
                    {
                        File.Delete(stagedToDelete);
                        if (File.Exists(pathToDelete)) File.Delete(pathToDelete);
                    }
                    if (File.Exists(stagedToKeep)) File.Delete(stagedToKeep);
                }

                // Remove staging copies for unselected pairs — originals are left untouched.
                foreach (var pair in toKeep)
                {
                    string copy1 = Path.Combine(_lastDuplicatesFolder, GetStagingFileName(pair.Path1, ScanSubfolders));
                    string copy2 = Path.Combine(_lastDuplicatesFolder, GetStagingFileName(pair.Path2, ScanSubfolders));
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

                // Re-evaluate numbering against the current file state after deletions.
                if (ScanSubfolders && CheckSubfolderNumbering)
                {
                    var allFiles = Directory.GetFiles(_targetFolder, "*.*", SearchOption.AllDirectories)
                        .Where(f => s_imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToArray();
                    var (newResults, newFixes) = EvaluateNumberingByKeyword(allFiles);
                    KeywordNumberings.Clear();
                    foreach (var r in newResults)
                        KeywordNumberings.Add(r);
                    _lastNumberingFixes = newFixes;
                    CanFixNumbering = newFixes.Count > 0;
                }
                else if (!ScanSubfolders)
                {
                    var files = Directory.GetFiles(_targetFolder, _lastKeyword + "_*.*");
                    var (numberingText, numberingFixes) = EvaluateNumbering(files);
                    NumberingText = numberingText;
                    _lastNumberingFixes = numberingFixes;
                    CanFixNumbering = numberingFixes.Count > 0;
                }
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

                if (ScanSubfolders)
                {
                    // Update the last entry in each affected keyword group to show the rename confirmation.
                    // For simplicity, append a summary to NumberingText (reused as a status line here).
                    NumberingText = $"> Renamed {_lastNumberingFixes.Count} file(s) to fix numbering.";
                }
                else
                {
                    NumberingText += $"\n> Renamed {_lastNumberingFixes.Count} file(s) to fix numbering.";
                }

                _lastNumberingFixes = [];
                CanFixNumbering = false;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Fix numbering failed: " + ex.Message;
            }
        }
        // Cycles "baseText.", "baseText..", "baseText..." on the UI thread until cancelled.
        private async Task CycleProgressTextAsync(string baseText, CancellationToken ct)
        {
            int frame = 0;
            while (!ct.IsCancellationRequested)
            {
                ProgressText = baseText + new string('.', frame % 3 + 1);
                frame++;
                try { await Task.Delay(400, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
