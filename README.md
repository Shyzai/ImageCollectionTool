# ImageCollectionTool

Tool for helping organize collections of images.

## Features

### Numbering Check
Verifies images follow the naming pattern `Name_1`, `Name_2`, etc. Sequence variants (`Name_1a`, `Name_1b`, ... up to `Name_1z`) are also supported and treated as a single numbered entry. Reports any missing numbers and can automatically rename files to fill the gaps.

### Duplicate Detection
Detects duplicate images using perceptual hashing (pHash) for fast candidate filtering, followed by ORB feature matching (Hamming distance) for verification. Detected pairs are shown as cards in the output panel with thumbnail previews and a match count.

All pairs are selected by default. Click a card to toggle its selection. The **Delete Selected** button removes the higher-numbered file from each selected pair. If a pair has already been manually removed from the staging folder, it is skipped and the original is left untouched. The staging folder is cleaned up automatically when the app is closed.

## Platform

This application targets Windows only. Builds on non-Windows platforms will produce CA1416 warnings about Windows-specific APIs (WPF, WinForms, `FolderBrowserDialog`).

## Testing

Tests are in the `DuplicateImageFinder.Tests` project and use xUnit. Run them from the repo root:

```
dotnet test DuplicateImageFinder.Tests
```

No test images need to be provided — the test fixtures generate patterned images (gradients, stripes, checkerboard) programmatically at runtime.
