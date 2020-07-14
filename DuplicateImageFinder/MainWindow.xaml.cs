using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Linq;
using System.Configuration;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Input;

namespace DuplicateImageFinder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Configuration configFile;
        string targetFolder = "";
        string[] files = null;
        const float similarityThreshold = .9997f;

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

                #region Creating the working directory and copying relevant images

                mainTextBox.Text += "Searching in: " + targetFolder + "\n";
                string keyword = commonWordsTextBox.Text;

                string tempComparisonDir = targetFolder + "\\Comparing" + keyword + "Images";
                Directory.CreateDirectory(tempComparisonDir);

                files = null;

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    files = Directory.GetFiles(targetFolder, keyword + "_*.*");

                    mainTextBox.Text += "Found " + files.Length + " files with the word '" + keyword + "'\n";
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show("Common word required");
                    //files = Directory.GetFiles(targetFolder);
                    //mainTextBox.Text += "Found " + files.Length + " files in " + targetFolder + "\n";
                }

                foreach (string fileName in files)
                {
                    string actualName = fileName.Substring(fileName.LastIndexOf("\\") + 1);

                    File.Copy(fileName, tempComparisonDir + "\\" + actualName);
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
                    string actualName = files[i].Substring(files[i].LastIndexOf("\\") + 1);

                    /* Accounting for similar images following pattern 'Name_1a.jpg', 'Name_1b.jpg', etc... */
                    /* Case 1: First image in a sequence (eg. Name_1a) */
                    /* Case 2: All following images in a sequence up to 'z' */
                    /* Case 3: Normal numbered images */
                    if (Regex.IsMatch(actualName, @"\w*_\d*a.*"))
                    {
                        imageNameNums[i] = GetImageNumber(actualName.Remove(actualName.IndexOf(".") - 1, 1));
                        referenceNums[i] = i + 1 - numIgnoredImages;
                    }
                    else if (Regex.IsMatch(actualName, @"\w*_\d*[b-z].*"))
                    {
                        imageNameNums[i] = imageNameNums[i - 1];
                        referenceNums[i] = referenceNums[i - 1];
                        numIgnoredImages++;
                    }
                    else
                    {
                        imageNameNums[i] = GetImageNumber(actualName);
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
                        mainTextBox.Text += n + "\n";
                    }
                }
                else
                {
                    mainTextBox.Text += ">> All images are correctly numbered\n";
                }
                
                #endregion

                #region Finding duplicate images by comparing average Hue-Saturation-Brightness values

                if (findDuplicatesCheckBox.IsChecked == true)
                {
                    if (files.Length > 1)
                    {
                        FindDuplicateImages(tempComparisonDir);
                    }
                    else
                    {
                        System.Windows.Forms.MessageBox.Show("Not enough images to do comparisons");
                    }
                    
                }
                
                #endregion

                Directory.Delete(tempComparisonDir, true);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Run_Click exception: " + ex.Message);
            }
        }

        private void FindDuplicateImages(string dir)
        {
            try
            {
                /* Updating file paths */
                files = Directory.GetFiles(dir);

                /* SortedList sorts by key rather than value. */
                SortedList<float, string> HSBValues = new SortedList<float, string>();

                for (int i = 0; i < files.Length; i++)
                {
                    string tempString = GetFileNameFromPath(files[i]);

                    try
                    {
                        using (Bitmap temp = new Bitmap(files[i]))
                        {
                            float tempFloat = GetAverageBrightness(temp);

                            if (tempFloat > 0f && tempFloat < 1f)
                            {
                                HSBValues.Add(tempFloat, tempString);
                            }
                            else
                            {
                                mainTextBox.Text += "Error with getting" + files[i] + " brightness. Ouside range.\n";
                            }
                        }
                    }
                    catch (Exception)
                    {
                        mainTextBox.Text += "Skipping " + tempString + ". Error creating bitmap from file.\n";
                    }
                }

                int maxNumComparisons = (HSBValues.Count < 6) ? HSBValues.Count - 1 : 6;

                int numComparisons = 0;
                for (int i = 0; i <= HSBValues.Count - 1; i++)
                {
                    mainTextBox.Text += HSBValues.Values[i] + " - " + HSBValues.Keys[i] + "\n";

                    if (maxNumComparisons + i >= HSBValues.Count)
                    {
                        numComparisons = HSBValues.Count - i - 1;
                    }
                    else
                    {
                        numComparisons = maxNumComparisons;
                    }

                    for (int j = 1; j <= numComparisons; j++)
                    {
                        if (IsSameImage(HSBValues.Keys[i], HSBValues.Keys[i + j]))
                        {
                            mainTextBox.Text += "Potential match found: " + HSBValues.Values[i] + " and " + HSBValues.Values[i + j] + "\n";
                        }
                    }
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

        private bool IsSameImage(float brightness1, float brightness2)
        {
            bool equals = false;

            try
            {
                float percentSimilar = 0f;
                if (brightness1 > brightness2)
                {
                    percentSimilar = brightness2 / brightness1;
                }
                else
                {
                    percentSimilar = brightness1 / brightness2;
                }

                if (percentSimilar >= similarityThreshold) equals = true;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("sameImage exception: " + ex.Message);
            }

            return equals;
        }

        public float GetAverageBrightness(Bitmap bmpSource)
        {
            float ans = 0f;
            try
            {
                float sum = 0f;
                using (Bitmap bmpMin = new Bitmap(bmpSource, new System.Drawing.Size(16, 16)))
                {
                    for (int j = 0; j < bmpMin.Height; j++)
                    {
                        for (int i = 0; i < bmpMin.Width; i++)
                        {
                            sum += bmpMin.GetPixel(i, j).GetBrightness();
                        }
                    }
                    ans = sum / 256f;
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("GetAverageBrightness exception: " + ex.Message);
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
