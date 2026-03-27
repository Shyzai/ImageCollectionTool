using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DuplicateImageFinder.Tests")]

namespace ImageCollectionTool
{
    public static class ImageMatcher
    {
        // Precomputed cosine lookup table for the fixed 32×32 DCT — eliminates repeated MathF.Cos calls in the inner loop
        private static readonly float[,] s_cosTable = BuildCosTable(32);

        private static float[,] BuildCosTable(int n)
        {
            var table = new float[n, n];
            for (int x = 0; x < n; x++)
                for (int u = 0; u < n; u++)
                    table[x, u] = MathF.Cos((2 * x + 1) * u * MathF.PI / (2 * n));
            return table;
        }

        public static List<(string Path1, string Path2, int GoodMatches)> FindDuplicates(
            string[] imagePaths,
            int hammingThreshold = 10,
            int minFeatureMatches = 40,
            IProgress<string>? progress = null)
        {
            // Phase 1: compute pHash for all images in parallel
            var hashes = new ConcurrentDictionary<string, ulong>();
            Parallel.ForEach(imagePaths, path =>
            {
                try { hashes[path] = ComputePHash(path); }
                catch { /* skip unreadable files */ }
            });

            // Phase 1: pHash screening — cheap O(n²) filter
            List<string> paths = [..hashes.Keys];
            int n = paths.Count;
            List<(string, string)> candidates = new(n * (n - 1) / 2);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (HammingDistance(hashes[paths[i]], hashes[paths[j]]) <= hammingThreshold)
                        candidates.Add((paths[i], paths[j]));

            progress?.Report($"Checking {candidates.Count} candidate pair(s)...");

            // Phase 2: ORB feature matching on candidates only, in parallel
            var duplicates = new ConcurrentBag<(string Path1, string Path2, int GoodMatches)>();
            int completed = 0;
            Parallel.ForEach(candidates, pair =>
            {
                int matches = CountFeatureMatches(pair.Item1, pair.Item2);
                if (matches >= minFeatureMatches)
                    duplicates.Add((pair.Item1, pair.Item2, matches));

                int done = Interlocked.Increment(ref completed);
                progress?.Report($"Checking pair {done} / {candidates.Count}...");
            });

            List<(string Path1, string Path2, int GoodMatches)> result = [..duplicates];
            result.Sort((a, b) => b.GoodMatches.CompareTo(a.GoodMatches));
            return result;
        }

        private static ulong ComputePHash(string imagePath)
        {
            // Load with CacheOption.OnLoad so all decoding happens on the calling thread (safe for Parallel.ForEach)
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(imagePath);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();

            var grayscale = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
            grayscale.Freeze();

            // Scale factors to resize the image down to a fixed 32×32 grid
            var scale = new ScaleTransform(
                32.0 / grayscale.PixelWidth,
                32.0 / grayscale.PixelHeight);
            scale.Freeze();
            var resized = new TransformedBitmap(grayscale, scale);
            resized.Freeze();

            byte[] pixels = new byte[32 * 32];
            resized.CopyPixels(pixels, 32, 0);

            float[,] matrix = new float[32, 32];
            // Convert 2D pixel coordinates to a 1D flat array index (row-major order)
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    matrix[y, x] = pixels[y * 32 + x];

            float[,] dct = ComputeDCT(matrix, 32);

            // Mean of the top-left 8×8 low-frequency coefficients, excluding the DC component at (0,0) to avoid brightness bias
            float sum = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (y != 0 || x != 0) sum += dct[y, x];
            float mean = sum / 63f; // 63 AC coefficients (64 total minus the DC component at index 0)

            // Set the i-th bit if this coefficient exceeds the mean; encodes relative frequency structure as a 64-bit fingerprint
            ulong hash = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (dct[y, x] > mean)
                        hash |= (1UL << (y * 8 + x));

            return hash;
        }

        // XOR produces a 1 for every bit position where the hashes differ; PopCount counts those bits.
        // Result is the number of differing bits — lower means more similar images.
        internal static int HammingDistance(ulong a, ulong b)
            => System.Numerics.BitOperations.PopCount(a ^ b);

        // Extracts the numeric suffix from a filename of the form "Name_<number>.ext". Returns -1 if no underscore is found.
        internal static int GetImageNumber(string imageName)
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(imageName);
            int underscoreIdx = nameWithoutExt.LastIndexOf('_');
            if (underscoreIdx < 0) return -1;
            int.TryParse(nameWithoutExt[(underscoreIdx + 1)..], out int ans);
            return ans;
        }

        // 2D DCT-II implemented as two passes of the 1D DCT (separable property).
        // Formula per element: sum of input[x] * cos((2x+1) * u * π / (2n))
        private static float[,] ComputeDCT(float[,] input, int n)
        {
            float[,] temp   = new float[n, n];
            float[,] output = new float[n, n];

            // Pass 1: apply 1D DCT across each row (captures horizontal frequency content)
            for (int y = 0; y < n; y++)
                for (int u = 0; u < n; u++)
                {
                    float s = 0;
                    for (int x = 0; x < n; x++)
                        s += input[y, x] * s_cosTable[x, u];
                    temp[y, u] = s;
                }

            // Pass 2: apply 1D DCT down each column of the row-transformed result (captures vertical frequency content)
            for (int v = 0; v < n; v++)
                for (int u = 0; u < n; u++)
                {
                    float s = 0;
                    for (int y = 0; y < n; y++)
                        s += temp[y, u] * s_cosTable[y, v];
                    output[v, u] = s;
                }

            return output;
        }

        private static int CountFeatureMatches(string path1, string path2)
        {
            using var img1 = Cv2.ImRead(path1, ImreadModes.Grayscale);
            using var img2 = Cv2.ImRead(path2, ImreadModes.Grayscale);

            // Detect up to 500 keypoints per image and compute their binary descriptors
            using var orb = ORB.Create(nFeatures: 500);
            using var desc1 = new Mat();
            using var desc2 = new Mat();

            orb.DetectAndCompute(img1, null, out _, desc1);
            orb.DetectAndCompute(img2, null, out _, desc2);

            if (desc1.Empty() || desc2.Empty()) return 0;

            using var matcher = new BFMatcher(NormTypes.Hamming, crossCheck: false); // ORB descriptors are binary strings; Hamming distance counts differing bits
            var knnMatches = matcher.KnnMatch(desc1, desc2, k: 2);                   // Find the 2 closest descriptor matches for each keypoint

            // Lowe's ratio test
            int goodMatches = 0;
            foreach (var m in knnMatches)
                if (m.Length == 2 && m[0].Distance < 0.75f * m[1].Distance)
                    goodMatches++;

            return goodMatches;
        }
    }
}
