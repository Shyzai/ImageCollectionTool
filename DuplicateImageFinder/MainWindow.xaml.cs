using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Linq;
using System.Configuration;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace ImageCollectionTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Configuration configFile;
        string targetFolder;
        List<(string Path1, string Path2, int GoodMatches)> _lastDuplicates = [];
        string _lastDuplicatesFolder;
        List<(string OldPath, int NewNumber)> _lastNumberingFixes = [];

        private static readonly Regex s_sequenceFirstRegex     = new Regex(@"\w*_\d*a.*",     RegexOptions.Compiled);
        private static readonly Regex s_sequenceFollowingRegex = new Regex(@"\w*_\d*[b-z].*", RegexOptions.Compiled);

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                targetFolder = configFile.AppSettings.Settings["Search Directory"].Value ?? "";
                folderText.Text += targetFolder;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Error reading configurations: " + ex.Message);
            }
        }

        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var fbd = new FolderBrowserDialog();
                DialogResult result = fbd.ShowDialog();

                if (!string.IsNullOrEmpty(fbd.SelectedPath) && result == System.Windows.Forms.DialogResult.OK)
                {
                    configFile.AppSettings.Settings["Search Directory"].Value = fbd.SelectedPath;
                    targetFolder = fbd.SelectedPath;
                    folderText.Text = "Current Folder: " + fbd.SelectedPath;

                    configFile.Save(ConfigurationSaveMode.Modified);
                    ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Error selecting folder: " + ex.Message);
            }
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(targetFolder)) throw new Exception("Folder was not selected.");

                string keyword = commonWordsTextBox.Text;
                if (string.IsNullOrWhiteSpace(keyword)) throw new Exception("Common word required");

                bool findDuplicates = findDuplicatesCheckBox.IsChecked == true;
                string folder = targetFolder; // capture before leaving the UI thread

                RunButton.IsEnabled = false;

                var (output, duplicates, duplicatesFolder, numberingFixes) = await Task.Run(() => RunAnalysis(folder, keyword, findDuplicates));
                mainTextBox.AppendText(output);
                _lastDuplicates = duplicates;
                _lastDuplicatesFolder = duplicatesFolder;
                _lastNumberingFixes = numberingFixes;
                DeleteDuplicatesButton.IsEnabled = duplicates.Count > 0;
                FixNumberingButton.IsEnabled = numberingFixes.Count > 0;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Run_Click exception: " + ex.Message);
            }
            finally
            {
                RunButton.IsEnabled = true;
            }
        }

        private static (string Output, List<(string Path1, string Path2, int GoodMatches)> Duplicates, string DuplicatesFolder,
        List<(string OldPath, int NewNumber)> NumberingFixes) RunAnalysis(string targetFolder, string keyword, bool findDuplicates)
        {
            var sb = new StringBuilder();
            List<(string Path1, string Path2, int GoodMatches)> duplicates = [];

            #region Finding relevant files

            sb.Append("Searching in: ").Append(targetFolder).Append('\n');

            string[] files = Directory.GetFiles(targetFolder, keyword + "_*.*");
            sb.Append("Found ").Append(files.Length).Append(" files with the word '").Append(keyword).Append("'\n\n");

            #endregion

            #region Checking image numbering

            int[] referenceNums  = new int[files.Length];
            int[] imageNameNums  = new int[files.Length];
            int numIgnoredImages = 0;

            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);

                // Accounting for similar images following pattern 'Name_1a.jpg', 'Name_1b.jpg', etc.
                if (s_sequenceFirstRegex.IsMatch(fileName))
                {
                    imageNameNums[i] = GetImageNumber(fileName.Remove(fileName.IndexOf(".") - 1, 1));
                    referenceNums[i] = i + 1 - numIgnoredImages;
                }
                else if (s_sequenceFollowingRegex.IsMatch(fileName))
                {
                    imageNameNums[i] = imageNameNums[i - 1];
                    referenceNums[i] = referenceNums[i - 1];
                    numIgnoredImages++;
                }
                else
                {
                    imageNameNums[i] = GetImageNumber(fileName);
                    referenceNums[i] = i + 1 - numIgnoredImages;
                }
            }

            /* Finds numbers that are in the "correct" numbering list, and are not in the file names */
            var missingNums = referenceNums.Except(imageNameNums).ToList();

            /* Build rename plan: files whose number exceeds the expected range are "extras" to be renumbered */
            int maxRef = files.Length - numIgnoredImages;
            var extras = new List<(string Path, int Num)>();
            for (int i = 0; i < files.Length; i++)
                if (imageNameNums[i] > maxRef)
                    extras.Add((files[i], imageNameNums[i]));

            extras.Sort((a, b) => a.Num.CompareTo(b.Num));
            var sortedMissing = missingNums.OrderBy(n => n).ToList();

            var numberingFixes = new List<(string OldPath, int NewNumber)>();
            for (int i = 0; i < Math.Min(extras.Count, sortedMissing.Count); i++)
                numberingFixes.Add((extras[i].Path, sortedMissing[i]));

            if (missingNums.Count > 0)
            {
                sb.Append("Images are missing number(s): \n");
                foreach (int n in missingNums)
                    sb.Append("> ").Append(n).Append('\n');
            }
            else
            {
                sb.Append("> All images are correctly numbered\n");
            }
            sb.Append('\n');

            #endregion

            #region Finding duplicate images

            string duplicatesFolder = targetFolder + "\\Potential_" + keyword + "_Duplicates";
            if (Directory.Exists(duplicatesFolder))
                Directory.Delete(duplicatesFolder, true);

            if (findDuplicates)
            {
                if (files.Length > 1)
                {
                    Directory.CreateDirectory(duplicatesFolder);
                    duplicates = FindDuplicateImages(files, duplicatesFolder, sb);
                }
                else
                {
                    sb.Append("Not enough images to do comparisons\n");
                }
            }

            sb.Append("=======================================================\n");

            #endregion

            return (sb.ToString(), duplicates, duplicatesFolder, numberingFixes);
        }

        private static List<(string Path1, string Path2, int GoodMatches)> FindDuplicateImages(string[] files, string duplicatesFolder, StringBuilder sb)
        {
            var results = ImageMatcher.FindDuplicates(files);

            if (results.Count == 0)
            {
                sb.Append("> No duplicates found\n");
                Directory.Delete(duplicatesFolder);
                return results;
            }

            foreach (var (path1, path2, goodMatches) in results)
            {
                string name1 = Path.GetFileName(path1);
                string name2 = Path.GetFileName(path2);

                sb.Append("> Potential match found (").Append(goodMatches).Append(" feature matches): ")
                  .Append(name1).Append(" and ").Append(name2).Append('\n');

                if (!File.Exists(duplicatesFolder + "\\" + name1)) File.Copy(path1, duplicatesFolder + "\\" + name1);
                if (!File.Exists(duplicatesFolder + "\\" + name2)) File.Copy(path2, duplicatesFolder + "\\" + name2);
            }

            return results;
        }

        private static int GetImageNumber(string imageName)
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(imageName);
            int underscoreIdx = nameWithoutExt.LastIndexOf('_');
            if (underscoreIdx < 0) return -1;
            int.TryParse(nameWithoutExt.Substring(underscoreIdx + 1), out int ans);
            return ans;
        }

        private void DeleteDuplicates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var (path1, path2, _) in _lastDuplicates)
                {
                    int num1 = GetImageNumber(Path.GetFileName(path1));
                    int num2 = GetImageNumber(Path.GetFileName(path2));
                    string pathToDelete = num1 >= num2 ? path1 : path2;
                    string nameToDelete = Path.GetFileName(pathToDelete);

                    string pathToKeep = pathToDelete == path1 ? path2 : path1;
                    string nameToKeep = Path.GetFileName(pathToKeep);

                    string duplicatesCopyToDelete = Path.Combine(_lastDuplicatesFolder, nameToDelete);
                    string duplicatesCopyToKeep = Path.Combine(_lastDuplicatesFolder, nameToKeep);
                    if (File.Exists(duplicatesCopyToDelete))
                    {
                        File.Delete(duplicatesCopyToDelete);
                        if (File.Exists(pathToDelete)) File.Delete(pathToDelete);
                    }
                    if (File.Exists(duplicatesCopyToKeep)) File.Delete(duplicatesCopyToKeep);
                }

                if (Directory.Exists(_lastDuplicatesFolder) && !Directory.EnumerateFiles(_lastDuplicatesFolder).Any())
                    Directory.Delete(_lastDuplicatesFolder);

                mainTextBox.AppendText("> Deleted duplicate files.\n");
                DeleteDuplicatesButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("DeleteDuplicates_Click exception: " + ex.Message);
            }
        }

        private void FixNumbering_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var (oldPath, newNumber) in _lastNumberingFixes)
                {
                    string dir  = Path.GetDirectoryName(oldPath);
                    string name = Path.GetFileNameWithoutExtension(oldPath);
                    string ext  = Path.GetExtension(oldPath);
                    int underscoreIdx = name.LastIndexOf('_');
                    string newName = name.Substring(0, underscoreIdx + 1) + newNumber + ext;
                    File.Move(oldPath, Path.Combine(dir, newName));
                }

                mainTextBox.AppendText("> Renamed " + _lastNumberingFixes.Count + " file(s) to fix numbering.\n");
                _lastNumberingFixes = [];
                FixNumberingButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("FixNumbering_Click exception: " + ex.Message);
            }
        }

        private void commonWordsTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Return)
                Run_Click(this, new RoutedEventArgs());
        }
    }
}
