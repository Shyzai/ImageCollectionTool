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
                    FolderText = ShortenPath(fbd.SelectedPath);

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
            CancellationTokenSource? ellipsisCts = null;
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
                HasResults = false;
                SearchSummary = "";
                NumberingText = "";
                NumberingNumbers = "";
                KeywordNumberings.Clear();
                ReplacePairs([]);
                CanFixNumbering = false;
                CanDeleteDuplicates = false;
                IsRunEnabled = false;

                ellipsisCts = new CancellationTokenSource();
                _ = CycleProgressTextAsync("Scanning files", ellipsisCts.Token);

                var progress = new Progress<string>(msg =>
                {
                    ellipsisCts.Cancel();
                    ProgressText = msg;
                });

                var (searchSummary, numberingText, numberingNumbers, keywordNumberings, duplicates, numberingFixes) =
                    await Task.Run(() => RunAnalysis(folder, keyword, scanSubfolders, checkNumbering, progress));

                SearchSummary = searchSummary;
                NumberingText = numberingText;
                NumberingNumbers = numberingNumbers;
                _lastScanSubfolders = scanSubfolders;
                _lastCheckSubfolderNumbering = checkNumbering;
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
                ellipsisCts?.Cancel();
                ProgressText = "";
                IsRunEnabled = true;
            }
        }

        // Deletes the higher-numbered image from each selected pair.
        [RelayCommand]
        private void DeleteDuplicates()
        {
            try
            {
                foreach (var pair in DuplicatePairs.Where(p => p.IsSelected).ToList())
                {
                    int num1 = ImageMatcher.GetImageNumber(pair.FileName1);
                    int num2 = ImageMatcher.GetImageNumber(pair.FileName2);

                    // Keep the lower-numbered image; send the higher-numbered one to the recycle bin.
                    string pathToDelete = num1 >= num2 ? pair.Path1 : pair.Path2;
                    if (File.Exists(pathToDelete))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            pathToDelete,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }

                // Unsubscribe all pairs and clear the collection in one operation.
                foreach (var pair in DuplicatePairs)
                    pair.PropertyChanged -= OnPairSelectionChanged;
                DuplicatePairs.Clear();

                UpdateDeleteState();

                // Re-evaluate numbering against the current file state after deletions.
                // Use _last* snapshots so the branch matches what was shown when Run completed,
                // regardless of whether the user toggled checkboxes since then.
                if (_lastScanSubfolders && _lastCheckSubfolderNumbering)
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
                else if (!_lastScanSubfolders && !string.IsNullOrEmpty(_lastKeyword))
                {
                    var numberedFiles = Directory.GetFiles(_targetFolder, _lastKeyword + "_*.*");
                    var exactFiles    = Directory.GetFiles(_targetFolder, _lastKeyword + ".*")
                        .Where(f => Path.GetFileNameWithoutExtension(f).Equals(_lastKeyword, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var files = numberedFiles.Concat(exactFiles).OrderBy(f => f).ToArray();
                    var (numberingText, numberingNumbers, numberingFixes) = EvaluateNumbering(files);
                    NumberingText = numberingText;
                    NumberingNumbers = numberingNumbers;
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
                var fixes = _lastNumberingFixes;
                _lastNumberingFixes = [];
                CanFixNumbering = false;

                foreach (var (oldPath, newNumber) in fixes)
                {
                    string dir  = Path.GetDirectoryName(oldPath)!;
                    string name = Path.GetFileNameWithoutExtension(oldPath);
                    string ext  = Path.GetExtension(oldPath);

                    // Replace everything after the last underscore with the new number.
                    int underscoreIdx = name.LastIndexOf('_');
                    string newName = name.Substring(0, underscoreIdx + 1) + newNumber + ext;
                    File.Move(oldPath, Path.Combine(dir, newName));
                }

                // Filenames changed — any stored duplicate paths are now stale.
                ReplacePairs([]);

                if (_lastScanSubfolders)
                {
                    // Refresh the subfolder numbering panel to reflect the renamed files.
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
                else
                {
                    NumberingText    = $"> Renamed {fixes.Count} file(s) to fix numbering.";
                    NumberingNumbers = "";
                }
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
