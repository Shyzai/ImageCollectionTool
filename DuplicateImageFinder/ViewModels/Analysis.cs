using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ImageCollectionTool.ViewModels
{
    public partial class MainViewModel
    {
        // Matches files following a sequence pattern, e.g. "Name_1a.jpg" (first in group) and "Name_1b.jpg" (following).
        // These are treated as a single logical image for numbering purposes.
        private static readonly Regex s_sequenceFirstRegex     = new Regex(@"\w*_\d*a.*",     RegexOptions.Compiled);
        private static readonly Regex s_sequenceFollowingRegex = new Regex(@"\w*_\d*[b-z].*", RegexOptions.Compiled);

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

            var (numberingText, numberingFixes) = EvaluateNumbering(files);

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

        // Scans files in the folder and computes the numbering state and any fixes needed.
        internal static (string NumberingText, List<(string OldPath, int NewNumber)> NumberingFixes)
            EvaluateNumbering(string[] files)
        {
            int[] referenceNums  = new int[files.Length];
            int[] imageNameNums  = new int[files.Length];
            int numIgnoredImages = 0;

            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);

                if (s_sequenceFirstRegex.IsMatch(fileName))
                {
                    imageNameNums[i] = ImageMatcher.GetImageNumber(fileName.Remove(fileName.IndexOf(".") - 1, 1));
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
                    imageNameNums[i] = ImageMatcher.GetImageNumber(fileName);
                    referenceNums[i] = i + 1 - numIgnoredImages;
                }
            }

            var missingNums = referenceNums.Except(imageNameNums).ToList();

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

            return (numberingText, numberingFixes);
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
