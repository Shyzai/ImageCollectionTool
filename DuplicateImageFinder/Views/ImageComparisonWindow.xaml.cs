using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace ImageCollectionTool.Views
{
    public partial class ImageComparisonWindow : Wpf.Ui.Controls.FluentWindow
    {
        public ImageComparisonWindow(string path1, string path2)
        {
            InitializeComponent();

            var img1 = LoadImage(path1);
            var img2 = LoadImage(path2);

            Image1.Source      = img1;
            Image2.Source      = img2;
            FileName1Text.Text = Path.GetFileName(path1);
            FileName2Text.Text = Path.GetFileName(path2);

            SizeToImages(img1, img2);

            var main = System.Windows.Application.Current.MainWindow;
            Left = main.Left + main.Width + 8;
            Top  = main.Top;
        }

        private void SizeToImages(BitmapImage img1, BitmapImage img2)
        {
            const double titleBar    = 46; // title bar + separator
            const double labelHeight = 32; // filename label + margin
            const double chrome      = 18; // divider + padding

            // Natural window size if images were shown at full resolution.
            double naturalWidth  = img1.PixelWidth + img2.PixelWidth + chrome;
            double naturalHeight = Math.Max(img1.PixelHeight, img2.PixelHeight) + titleBar + labelHeight;

            // Scale down proportionally if either dimension exceeds 50% of the screen.
            var workArea = System.Windows.SystemParameters.WorkArea;
            double scaleW = naturalWidth  > workArea.Width  * 0.5 ? workArea.Width  * 0.5 / naturalWidth  : 1.0;
            double scaleH = naturalHeight > workArea.Height * 0.5 ? workArea.Height * 0.5 / naturalHeight : 1.0;
            double scale  = Math.Min(scaleW, scaleH);

            Width  = naturalWidth  * scale;
            Height = naturalHeight * scale;
        }

        private static BitmapImage LoadImage(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource    = new Uri(path);
            bitmap.CacheOption  = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}
