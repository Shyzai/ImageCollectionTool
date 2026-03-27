using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ImageCollectionTool.ViewModels
{
    public partial class DuplicatePairViewModel : ObservableObject
    {
        public string Path1 { get; }
        public string Path2 { get; }
        public int GoodMatches { get; }
        public string FileName1 => Path.GetFileName(Path1);
        public string FileName2 => Path.GetFileName(Path2);

        // Selected by default so "Delete X Selected" acts on all pairs unless the user opts out.
        [ObservableProperty] private bool _isSelected = true;

        // Loaded asynchronously after construction to avoid blocking the UI.
        [ObservableProperty] private BitmapImage? _thumbnail1;
        [ObservableProperty] private BitmapImage? _thumbnail2;

        public DuplicatePairViewModel(string path1, string path2, int goodMatches)
        {
            Path1 = path1;
            Path2 = path2;
            GoodMatches = goodMatches;
            _ = LoadThumbnailsAsync();
        }

        // Toggles selection when the user clicks anywhere on the card.
        [RelayCommand]
        private void ToggleSelection() => IsSelected = !IsSelected;

        // Opens Windows Explorer with the file selected.
        [RelayCommand]
        private void OpenImage(string path) =>
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        private async Task LoadThumbnailsAsync()
        {
            Thumbnail1 = await LoadThumbnailAsync(Path1);
            Thumbnail2 = await LoadThumbnailAsync(Path2);
        }

        private static async Task<BitmapImage?> LoadThumbnailAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path);
                    bitmap.DecodePixelWidth = 150; // Decode at thumbnail size to save memory.
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load fully so the file handle is released immediately.
                    bitmap.EndInit();
                    bitmap.Freeze(); // Required to use the bitmap on the UI thread after loading on a background thread.
                    return bitmap;
                }
                catch
                {
                    return null;
                }
            });
        }
    }
}
