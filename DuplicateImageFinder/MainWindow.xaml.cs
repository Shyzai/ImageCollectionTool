using System;
using System.IO;
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
        string duplicatesFolder;
        string[] files;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                string tempFolderName = configFile.AppSettings.Settings["Search Directory"].Value ?? "";

                targetFolder = tempFolderName;
                folderText.Text += tempFolderName;
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
                using (var fbd = new FolderBrowserDialog())
                {
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
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Error selecting folder: " + ex.Message);
            }
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(targetFolder)) throw new Exception("Folder was not selected.");

                #region Finding relevant files

                mainTextBox.Text += "Searching in: " + targetFolder + "\n";
                string keyword = commonWordsTextBox.Text;

                files = null;

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    files = Directory.GetFiles(targetFolder, keyword + "_*.*");

                    mainTextBox.Text += "Found " + files.Length + " files with the word '" + keyword + "'\n\n";
                }
                else
                {
                    throw new Exception("Common word required");
                    //files = Directory.GetFiles(targetFolder);
                    //mainTextBox.Text += "Found " + files.Length + " files in " + targetFolder + "\n";
                }

                #endregion

                #region Checking image numbering 

                int[] referenceNums = new int[files.Length];
                int[] imageNameNums = new int[files.Length];

                int numIgnoredImages = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    //string actualName = fileName.Substring(fileName.LastIndexOf("\\") + 1);
                    //File.Copy(fileName, tempComparisonDir + "\\" + actualName);
                    string fileName = files[i].Substring(files[i].LastIndexOf("\\") + 1);

                    /* Accounting for similar images following pattern 'Name_1a.jpg', 'Name_1b.jpg', etc... */
                    /* Case 1: First image in a sequence (eg. Name_1a) */
                    /* Case 2: All following images in a sequence up to 'z' */
                    /* Case 3: Normal numbered images */
                    if (Regex.IsMatch(fileName, @"\w*_\d*a.*"))
                    {
                        imageNameNums[i] = GetImageNumber(fileName.Remove(fileName.IndexOf(".") - 1, 1));
                        referenceNums[i] = i + 1 - numIgnoredImages;
                    }
                    else if (Regex.IsMatch(fileName, @"\w*_\d*[b-z].*"))
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
                IEnumerable<int> differenceQuery = referenceNums.Except(imageNameNums);

                if (differenceQuery.Count() > 0)
                {
                    mainTextBox.Text += "Images are missing number(s): \n";

                    foreach (int n in differenceQuery)
                    {
                        mainTextBox.Text += "> " + n + "\n";
                    }
                }
                else
                {
                    mainTextBox.Text += "> All images are correctly numbered\n";
                }
                mainTextBox.Text += "\n";

                #endregion

                #region Finding duplicate images by comparing average Hue-Saturation-Brightness values

                duplicatesFolder = targetFolder + "\\Potential_" + keyword + "_Duplicates";
                if (Directory.Exists(duplicatesFolder))
                {
                    Directory.Delete(duplicatesFolder, true);
                }

                if (findDuplicatesCheckBox.IsChecked == true)
                {
                    if (files.Length > 1)
                    {
                        Directory.CreateDirectory(duplicatesFolder);

                        FindDuplicateImages();
                    }
                    else
                    {
                        System.Windows.Forms.MessageBox.Show("Not enough images to do comparisons");
                    }
                }

                mainTextBox.Text += "=======================================================\n";

                #endregion
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Run_Click exception: " + ex.Message);
            }
        }

        private void FindDuplicateImages()
        {
            try
            {
                var results = ImageMatcher.FindDuplicates(files);

                if (results.Count == 0)
                {
                    mainTextBox.Text += "> No duplicates found\n";
                    Directory.Delete(duplicatesFolder);
                    return;
                }

                foreach (var (path1, path2, goodMatches) in results)
                {
                    string name1 = GetFileNameFromPath(path1);
                    string name2 = GetFileNameFromPath(path2);

                    mainTextBox.Text += "> Potential match found (" + goodMatches + " feature matches): " + name1 + " and " + name2 + "\n";

                    if (!File.Exists(duplicatesFolder + "\\" + name1)) File.Copy(path1, duplicatesFolder + "\\" + name1);
                    if (!File.Exists(duplicatesFolder + "\\" + name2)) File.Copy(path2, duplicatesFolder + "\\" + name2);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("FindDuplicateImages exception: " + ex.Message);
            }
        }

        private string GetFileNameFromPath(string path)
        {
            string temp = null;

            try
            {
                temp = path.Substring(path.LastIndexOf("\\") + 1);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("GetFileNameFromPath exception: " + ex.Message);
            }

            return temp;
        }

        private int GetImageNumber(string imageName)
        {
            int ans = -1;

            try
            {
                int start = imageName.LastIndexOf("_") + 1;
                int end = imageName.LastIndexOf(".");
                int length = end - start;
                int.TryParse(imageName.Substring(start, length), out ans);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Error parsing imageName: " + ex.Message);
            }

            return ans;
        }

        private void commonWordsTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                Run_Click(this, new RoutedEventArgs());
            }
        }
    }
}
