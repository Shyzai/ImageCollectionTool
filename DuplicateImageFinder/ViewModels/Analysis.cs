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

        private static string[] GetAllImageFiles(string folder) =>
            Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => s_imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToArray();

        // Performs file discovery, numbering checks, and duplicate detection.
        // Runs on a background thread; must not touch UI or observable properties.
        private static (string SearchSummary, string NumberingText, string NumberingNumbers,
            List<KeywordNumberingResult> KeywordNumberings,
            List<(string Path1, string Path2, int GoodMatches)> Duplicates,
            List<(string OldPath, int NewNumber)> NumberingFixes)
            RunAnalysis(string targetFolder, string keyword, bool scanSubfolders, bool checkNumbering, IProgress<string>? progress = null)
        {
            List<(string Path1, string Path2, int GoodMatches)> duplicates = [];
            string numberingText = "";
            string numberingNumbers = "";
            var keywordNumberings = new List<KeywordNumberingResult>();
            List<(string OldPath, int NewNumber)> numberingFixes = [];

            string[] files;
            string searchSummary;

            if (scanSubfolders)
            {
                files = GetAllImageFiles(targetFolder);

                int subfolderCount = files.Select(f => Path.GetDirectoryName(f)).Distinct().Count();
                searchSummary = $"Searching in: {targetFolder} (including subfolders)\nFound {files.Length} image(s) across {subfolderCount} folder(s)";

                if (checkNumbering)
                    (keywordNumberings, numberingFixes) = EvaluateNumberingByKeyword(files);
            }
            else
            {
                // Include both numbered files (keyword_1.jpg) and exact-name files (keyword.jpg).
                var numberedFiles = Directory.GetFiles(targetFolder, keyword + "_*.*");
                var exactFiles    = Directory.GetFiles(targetFolder, keyword + ".*")
                    .Where(f => Path.GetFileNameWithoutExtension(f).Equals(keyword, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                files = numberedFiles.Concat(exactFiles).OrderBy(f => f).ToArray();
                searchSummary = $"Searching in: {targetFolder}\nFound {files.Length} files with the word '{keyword}'";

                if (files.Length > 0)
                    (numberingText, numberingNumbers, numberingFixes) = EvaluateNumbering(files);
            }

            if (files.Length > 1)
                duplicates = FindDuplicateImages(files, progress);

            return (searchSummary, numberingText, numberingNumbers, keywordNumberings, duplicates, numberingFixes);
        }

        // Groups files by (folder, keyword stem) and runs EvaluateNumbering per group.
        // Files that don't follow the keyword_number pattern are skipped.
        // Each group is labelled as "folder\keyword" so the containing folder is always visible.
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
                    string stem = idx >= 0 ? name[..idx] : name;
                    // Normalize to lowercase so files that differ only in capitalization
                    // (e.g. Pikachu_1.jpg / pikachu_2.jpg) are treated as the same group,
                    // matching the case-insensitive behaviour of Directory.GetFiles in keyword mode.
                    return (Dir: (Path.GetDirectoryName(f) ?? "").ToLowerInvariant(), Stem: stem.ToLowerInvariant());
                });

            foreach (var group in groups.OrderBy(g => g.Key.Dir).ThenBy(g => g.Key.Stem))
            {
                // Sort once — reused for both EvaluateNumbering and the display label.
                // The first file in sorted order is the lowest-numbered file (e.g. keyword_1),
                // which gives a predictable casing for the label regardless of filesystem order.
                var    sortedGroup = group.OrderBy(f => f).ToArray();
                string firstFile   = sortedGroup[0];
                string firstName   = Path.GetFileNameWithoutExtension(firstFile);
                int    firstIdx    = firstName.LastIndexOf('_');
                string displayStem = firstIdx >= 0 ? firstName[..firstIdx] : firstName;
                string displayDir  = Path.GetFileName(Path.GetDirectoryName(firstFile) ?? "");
                string keyword     = displayDir + Path.DirectorySeparatorChar + displayStem;

                var (label, numbers, fixes) = EvaluateNumbering(sortedGroup);
                bool hasIssues = fixes.Count > 0;
                string text = hasIssues ? label + "\n" + numbers : label;
                results.Add(new KeywordNumberingResult(keyword, text, hasIssues));
                allFixes.AddRange(fixes);
            }

            results.Sort((a, b) =>
            {
                int byIssues = b.HasIssues.CompareTo(a.HasIssues);
                return byIssues != 0 ? byIssues : string.Compare(a.Keyword, b.Keyword, StringComparison.OrdinalIgnoreCase);
            });
            return (results, allFixes);
        }

        // Scans files in the folder and computes the numbering state and any fixes needed.
        // Returns a label ("Missing number(s):" or "All images are correctly numbered"),
        // a separate numbers string ("1, 2, 3" or ""), and the list of rename fixes.
        internal static (string Label, string Numbers, List<(string OldPath, int NewNumber)> NumberingFixes)
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
                    int num = ImageMatcher.GetImageNumber(fileName.Remove(fileName.IndexOf(".") - 1, 1));
                    imageNameNums[i] = num;
                    if (i > 0 && num == imageNameNums[i - 1])
                    {
                        // The plain-numbered file (e.g. kw_1.jpg) already occupies this slot;
                        // treat the 'a' variant as a sequence follower rather than a new entry.
                        referenceNums[i] = referenceNums[i - 1];
                        numIgnoredImages++;
                    }
                    else
                    {
                        referenceNums[i] = i + 1 - numIgnoredImages;
                    }
                }
                else if (s_sequenceFollowingRegex.IsMatch(fileName))
                {
                    imageNameNums[i] = imageNameNums[i - 1];
                    referenceNums[i] = referenceNums[i - 1];
                    numIgnoredImages++;
                }
                else if (ImageMatcher.GetImageNumber(fileName) < 0)
                {
                    // No number (e.g. keyword.jpg) — counts as a sequence entry that needs renaming.
                    imageNameNums[i] = -1;
                    referenceNums[i] = i + 1 - numIgnoredImages;
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
                if (imageNameNums[i] > maxRef || imageNameNums[i] < 0)
                    extras.Add((files[i], imageNameNums[i]));

            extras.Sort((a, b) => a.Num.CompareTo(b.Num));
            var sortedMissing = missingNums.OrderBy(n => n).ToList();

            var numberingFixes = new List<(string OldPath, int NewNumber)>();
            for (int i = 0; i < Math.Min(extras.Count, sortedMissing.Count); i++)
                numberingFixes.Add((extras[i].Path, sortedMissing[i]));

            string label, numbers;
            if (missingNums.Count > 0)
            {
                label   = "Missing number(s):";
                numbers = string.Join(", ", missingNums.OrderBy(n => n));
            }
            else
            {
                label   = "All images are correctly numbered";
                numbers = "";
            }

            return (label, numbers, numberingFixes);
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

        // Filters sequence variants from duplicate results and returns the remaining pairs.
        private static List<(string Path1, string Path2, int GoodMatches)> FindDuplicateImages(
            string[] files, IProgress<string>? progress = null)
        {
            return ImageMatcher.FindDuplicates(files, progress: progress)
                .Where(r => !(Path.GetDirectoryName(r.Path1) == Path.GetDirectoryName(r.Path2) &&
                              GetSequenceBase(Path.GetFileName(r.Path1)) == GetSequenceBase(Path.GetFileName(r.Path2))))
                .ToList();
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
