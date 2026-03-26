# ImageCollectionTool

Tool for helping organize collections of images.

## Features

### Numbering Check
Verifies images follow the naming pattern `Name_1`, `Name_2`, etc. Sequence variants (`Name_1a`, `Name_1b`, ... up to `Name_1z`) are also supported and treated as a single numbered entry. Reports any missing numbers and can automatically rename files to fill the gaps.

### Duplicate Detection
Detects duplicate images using perceptual hashing (pHash) for fast candidate filtering, followed by ORB feature matching (Hamming distance) for verification. Duplicate pairs are copied into a `Potential_<keyword>_Duplicates` subfolder for manual review.

Once reviewed, the **Delete Duplicates** button removes the higher-numbered file from each pair. If a pair has already been manually removed from the duplicates folder, it is skipped and the original is left untouched.

## Platform

This application targets Windows only. Builds on non-Windows platforms will produce CA1416 warnings about Windows-specific APIs (WPF, WinForms, `FolderBrowserDialog`).

## Testing

Tests are in the `DuplicateImageFinder.Tests` project and use xUnit. Run them from the repo root:

```
dotnet test DuplicateImageFinder.Tests
```

No test images need to be provided — the test fixtures generate patterned images (gradients, stripes, checkerboard) programmatically at runtime.

### Test coverage

| File | What's covered |
|---|---|
| `HammingDistanceTests.cs` | Bit distance calculations — identical, one-bit, all-bits, zero inputs |
| `GetImageNumberTests.cs` | Filename number parsing — normal, large, multi-underscore, missing, non-numeric |
| `PHashTests.cs` | Same image hashes identically; distinct patterns have high Hamming distance |
| `FindDuplicatesTests.cs` | Duplicate pair detected; all-unique returns empty; single image; empty array |
