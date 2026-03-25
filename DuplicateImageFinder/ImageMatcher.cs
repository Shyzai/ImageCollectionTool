using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace ImageCollectionTool
{
    public static class ImageMatcher
    {
        public static List<(string Path1, string Path2, int GoodMatches)> FindDuplicates(
            string[] imagePaths,
            int hammingThreshold = 10,
            int minFeatureMatches = 15)
        {
            // Pre-compute pHash for all images
            var hashes = new Dictionary<string, ulong>();
            foreach (var path in imagePaths)
            {
                try { hashes[path] = ComputePHash(path); }
                catch { /* skip unreadable files */ }
            }

            // Phase 1: pHash screening — cheap O(n²) filter
            var paths = new List<string>(hashes.Keys);
            var candidates = new List<(string, string)>();
            for (int i = 0; i < paths.Count; i++)
                for (int j = i + 1; j < paths.Count; j++)
                    if (HammingDistance(hashes[paths[i]], hashes[paths[j]]) <= hammingThreshold)
                        candidates.Add((paths[i], paths[j]));

            // Phase 2: ORB feature matching on candidates only
            var duplicates = new List<(string, string, int)>();
            foreach (var (a, b) in candidates)
            {
                int matches = CountFeatureMatches(a, b);
                if (matches >= minFeatureMatches)
                    duplicates.Add((a, b, matches));
            }

            return duplicates;
        }

        private static ulong ComputePHash(string imagePath)
        {
            var image = new BitmapImage(new Uri(imagePath));
            var grayscale = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);

            // Scale factors to resize the image down to a fixed 32x32 grid
            var scale = new System.Windows.Media.ScaleTransform(
                32.0 / grayscale.PixelWidth,
                32.0 / grayscale.PixelHeight);
            var resized = new TransformedBitmap(grayscale, scale);

            byte[] pixels = new byte[32 * 32];
            resized.CopyPixels(pixels, 32, 0);

            float[,] matrix = new float[32, 32];
            // Convert 2D pixel coordinates to a 1D flat array index (row-major order)
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    matrix[y, x] = pixels[y * 32 + x];

            float[,] dct = ComputeDCT(matrix, 32);

            // Take the top-left 8x8 low-frequency coefficients
            float[] lowFreq = new float[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    lowFreq[y * 8 + x] = dct[y, x];

            // Mean excluding the DC component (index 0) to avoid brightness bias
            float sum = 0;
            for (int i = 1; i < 64; i++) sum += lowFreq[i];
            float mean = sum / 63f; // 63 AC coefficients (64 total minus the DC component at index 0)

            // Set the i-th bit if this coefficient exceeds the mean; encodes relative frequency structure as a 64-bit fingerprint
            ulong hash = 0;
            for (int i = 0; i < 64; i++)
                if (lowFreq[i] > mean)
                    hash |= (1UL << i);

            return hash;
        }

        // XOR produces a 1 for every bit position where the hashes differ; PopCount counts those bits.
        // Result is the number of differing bits — lower means more similar images.
        private static int HammingDistance(ulong a, ulong b)
            => System.Numerics.BitOperations.PopCount(a ^ b);

        // 2D DCT-II implemented as two passes of the 1D DCT (separable property).
        // Formula per element: sum of input[x] * cos((2x+1) * u * π / (2n))
        private static float[,] ComputeDCT(float[,] input, int n)
        {
            float[,] temp = new float[n, n];
            float[,] output = new float[n, n];

            // Pass 1: apply 1D DCT across each row (captures horizontal frequency content)
            for (int y = 0; y < n; y++)
                for (int u = 0; u < n; u++)
                {
                    float s = 0;
                    for (int x = 0; x < n; x++)
                        s += input[y, x] * MathF.Cos((2 * x + 1) * u * MathF.PI / (2 * n));
                    temp[y, u] = s;
                }

            // Pass 2: apply 1D DCT down each column of the row-transformed result (captures vertical frequency content)
            for (int v = 0; v < n; v++)
                for (int u = 0; u < n; u++)
                {
                    float s = 0;
                    for (int y = 0; y < n; y++)
                        s += temp[y, u] * MathF.Cos((2 * y + 1) * v * MathF.PI / (2 * n));
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

            using var matcher = new BFMatcher(NormTypes.Hamming, crossCheck: false);    // ORB descriptors are binary strings; Hamming distance counts differing bits
            var knnMatches = matcher.KnnMatch(desc1, desc2, k: 2);                      // Find the 2 closest descriptor matches for each keypoint

            // Lowe's ratio test
            int goodMatches = 0;
            foreach (var m in knnMatches)
                if (m.Length == 2 && m[0].Distance < 0.75f * m[1].Distance)
                    goodMatches++;

            return goodMatches;
        }
    }
}
