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

        private static readonly string[] s_imageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".tiff"];

        // Performs file discovery, numbering checks, and duplicate detection.
        // Runs on a background thread; must not touch UI or observable properties.
        private static (string SearchSummary, string NumberingText, List<KeywordNumberingResult> KeywordNumberings,
            List<(string Path1, string Path2, int GoodMatches)> Duplicates,
            string DuplicatesFolder, List<(string OldPath, int NewNumber)> NumberingFixes)
            RunAnalysis(string targetFolder, string keyword, bool scanSubfolders, bool checkNumbering, IProgress<string>? progress = null)
        {
            List<(string Path1, string Path2, int GoodMatches)> duplicates = [];
            string numberingText = "";
            var keywordNumberings = new List<KeywordNumberingResult>();
            List<(string OldPath, int NewNumber)> numberingFixes = [];

            string[] files;
            string searchSummary;

            if (scanSubfolders)
            {
                files = Directory.GetFiles(targetFolder, "*.*", SearchOption.AllDirectories)
                    .Where(f => s_imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToArray();

                int subfolderCount = files.Select(f => Path.GetDirectoryName(f)).Distinct().Count();
                searchSummary = $"Searching in: {targetFolder} (including subfolders)\nFound {files.Length} image(s) across {subfolderCount} folder(s)";

                if (checkNumbering)
                    (keywordNumberings, numberingFixes) = EvaluateNumberingByKeyword(files);
            }
            else
            {
                files = Directory.GetFiles(targetFolder, keyword + "_*.*");
                searchSummary = $"Searching in: {targetFolder}\nFound {files.Length} files with the word '{keyword}'";

                (numberingText, numberingFixes) = EvaluateNumbering(files);
            }

            // Clear any leftover staging folder from a previous run before creating a fresh one.
            string duplicatesFolder = Path.Combine(targetFolder, scanSubfolders ? "Potential_Duplicates" : $"Potential_{keyword}_Duplicates");
            if (Directory.Exists(duplicatesFolder))
                Directory.Delete(duplicatesFolder, true);

            if (files.Length > 1)
            {
                Directory.CreateDirectory(duplicatesFolder);
                duplicates = FindDuplicateImages(files, duplicatesFolder, scanSubfolders, progress);
            }
            else if (!scanSubfolders)
            {
                numberingText += "\nNot enough images to check for duplicates.";
            }

            return (searchSummary, numberingText, keywordNumberings, duplicates, duplicatesFolder, numberingFixes);
        }

        // Groups files by keyword prefix (everything before the last '_') and runs EvaluateNumbering per group.
        // Files that don't follow the keyword_number pattern are skipped.
        internal static (List<KeywordNumberingResult> Results, List<(string OldPath, int NewNumber)> AllFixes)
            EvaluateNumberingByKeyword(string[] files)
        {
            var results = new List<KeywordNumberingResult>();
            var allFixes = new List<(string OldPath, int NewNumber)>();

            var groups = files
                .Where(f => ImageMatcher.GetImageNumber(Path.GetFileName(f)) >= 0)
                .GroupBy(f =>
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    int idx = name.LastIndexOf('_');
                    return idx >= 0 ? name[..idx] : name;
                });

            foreach (var group in groups.OrderBy(g => g.Key))
            {
                var (text, fixes) = EvaluateNumbering(group.ToArray());
                bool hasIssues = fixes.Count > 0;
                results.Add(new KeywordNumberingResult(group.Key, text, hasIssues));
                allFixes.AddRange(fixes);
            }

            return (results, allFixes);
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
                var sb = new StringBuilder("Missing number(s):\n");
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

        // Returns the base name with any trailing sequence letter stripped, e.g. "kw_1a" → "kw_1", "kw_2" → "kw_2".
        // Used to identify sequence variants (a/b/c suffixes) that should not be flagged as duplicates.
        internal static string GetSequenceBase(string imageName)
        {
            string name = Path.GetFileNameWithoutExtension(imageName);
            int underscoreIdx = name.LastIndexOf('_');
            if (underscoreIdx < 0) return name;
            string suffix = name[(underscoreIdx + 1)..];
            if (suffix.Length > 1 && char.IsLetter(suffix[^1]))
                suffix = suffix[..^1];
            return name[..(underscoreIdx + 1)] + suffix;
        }

        // Copies duplicate pairs into the staging folder and returns the list of pairs.
        // In subfolder mode, staging filenames are prefixed with their parent folder name to avoid collisions.
        private static List<(string Path1, string Path2, int GoodMatches)> FindDuplicateImages(
            string[] files, string duplicatesFolder, bool useSubfolderPrefix, IProgress<string>? progress = null)
        {
            var results = ImageMatcher.FindDuplicates(files, progress: progress)
                .Where(r => !(Path.GetDirectoryName(r.Path1) == Path.GetDirectoryName(r.Path2) &&
                              GetSequenceBase(Path.GetFileName(r.Path1)) == GetSequenceBase(Path.GetFileName(r.Path2))))
                .ToList();

            if (results.Count == 0)
            {
                Directory.Delete(duplicatesFolder);
                return results;
            }

            foreach (var (path1, path2, _) in results)
            {
                string staged1 = Path.Combine(duplicatesFolder, GetStagingFileName(path1, useSubfolderPrefix));
                string staged2 = Path.Combine(duplicatesFolder, GetStagingFileName(path2, useSubfolderPrefix));

                if (!File.Exists(staged1)) File.Copy(path1, staged1);
                if (!File.Exists(staged2)) File.Copy(path2, staged2);
            }

            return results;
        }

        // Returns the filename to use in the staging folder.
        // In subfolder mode, prefixes with the immediate parent folder name to avoid collisions.
        internal static string GetStagingFileName(string originalPath, bool useSubfolderPrefix)
        {
            string fileName = Path.GetFileName(originalPath);
            if (!useSubfolderPrefix) return fileName;
            string parentFolder = Path.GetFileName(Path.GetDirectoryName(originalPath) ?? "") ?? "";
            return string.IsNullOrEmpty(parentFolder) ? fileName : parentFolder + "__" + fileName;
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
