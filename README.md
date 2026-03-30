# ImageCollectionTool

Tool for helping organize collections of images.

## Getting Started

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Clone the repository and run:

```
dotnet run --project DuplicateImageFinder
```

All other dependencies are restored automatically via NuGet.

## Features

### Scan Modes

**Keyword mode** (default): scans a single folder for files matching `Keyword_*.*` and checks that they are sequentially numbered.

**Subfolder mode**: scans all subfolders recursively and compares every image found. No keyword is required. Optionally checks numbering per keyword group across all subfolders.

### Output Panel
Results are split across two tabs:

- **Summary** — search details and numbering results
- **Duplicates** — detected duplicate pairs

### Numbering Check
Verifies images follow the naming pattern `Name_1`, `Name_2`, etc. Sequence variants (`Name_1a`, `Name_1b`, ... up to `Name_1z`) are supported and treated as a single numbered entry. Reports any missing numbers and can automatically rename files to fill the gaps.

In subfolder mode, numbering is checked per keyword group. Groups with issues are listed first and highlighted; if all groups are correctly numbered a single summary message is shown above the keyword list.

Applying a numbering fix clears any pending duplicate results — re-run after fixing to get fresh duplicate detection against the renamed files.

### Duplicate Detection
Detects duplicate images using perceptual hashing (pHash) for fast candidate filtering, followed by ORB feature matching (Hamming distance) for verification. Detected pairs are shown as cards in the Duplicates tab with thumbnail previews and a match count.

All pairs are selected by default. Click a card to toggle its selection. The **Delete Selected** button removes the higher-numbered file from each selected pair.

Click the scale icon on a pair card to open a **side-by-side comparison window** showing both images at full quality. The window is sized proportionally to the images, up to a capped size.

Sequence variants (e.g. `Name_1a` and `Name_1b`) in the same folder are never flagged as duplicates.

## Platform

This application targets Windows only. Builds on non-Windows platforms will produce CA1416 warnings about Windows-specific APIs (WPF, WinForms, `FolderBrowserDialog`).

## Testing

Tests are in the `DuplicateImageFinder.Tests` project and use xUnit. Run them from the repo root:

```
dotnet test DuplicateImageFinder.Tests
```

No test images need to be provided — the test fixtures generate patterned images (gradients, stripes, checkerboard) programmatically at runtime.
