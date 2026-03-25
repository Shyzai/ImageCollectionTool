using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DuplicateImageFinder.Tests
{
    /// <summary>
    /// Generates simple test images on disk for use in image-based tests.
    /// </summary>
    internal static class TestImageFactory
    {
        /// <summary>Writes a solid-color 64x64 PNG to the given path.</summary>
        internal static void WriteSolidColor(string path, byte r, byte g, byte b)
        {
            int width = 64, height = 64;
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Rgb24, null);
            byte[] pixels = new byte[width * height * 3];
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i]     = r;
                pixels[i + 1] = g;
                pixels[i + 2] = b;
            }
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 3, 0);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.OpenWrite(path);
            encoder.Save(stream);
        }

        /// <summary>Writes a diagonal gradient 64x64 PNG (x+y intensity).</summary>
        internal static void WriteGradient(string path)
        {
            WritePattern(path, (x, y) => (byte)((x + y) * 2));
        }

        /// <summary>Writes a horizontal stripe pattern 64x64 PNG.</summary>
        internal static void WriteHorizontalStripes(string path)
        {
            WritePattern(path, (x, y) => (byte)((y % 8 < 4) ? 220 : 30));
        }

        /// <summary>Writes a vertical stripe pattern 64x64 PNG.</summary>
        internal static void WriteVerticalStripes(string path)
        {
            WritePattern(path, (x, y) => (byte)((x % 8 < 4) ? 220 : 30));
        }

        /// <summary>Writes a checkerboard pattern 64x64 PNG.</summary>
        internal static void WriteCheckerboard(string path)
        {
            WritePattern(path, (x, y) => (byte)(((x / 8 + y / 8) % 2 == 0) ? 220 : 30));
        }

        private static void WritePattern(string path, Func<int, int, byte> valueFunc)
        {
            int width = 64, height = 64;
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
            byte[] pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = valueFunc(x, y);
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width, 0);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.OpenWrite(path);
            encoder.Save(stream);
        }
    }
}
