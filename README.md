# DViewer
<img width="1917" height="1158" alt="image" src="https://github.com/user-attachments/assets/b9f09333-0c99-4e93-b5f3-e4f95ad13c19" />

A lightweight, cross-platform **DICOM viewer** built with **.NET MAUI**.

Focus areas:

* fast metadata inspection
* side-by-side comparison
* basic metadata editing
* simple conversions (e.g., extracting encapsulated video)
* highlighting of non-conformant values

---

## Features

* Cross-platform UI (Windows, macOS/MacCatalyst, Android, iOS)
* Two-pane workflow: load a DICOM on the left/right and compare tag-by-tag
* Fast metadata browser with search, tag filter, sorting, and a “differences only” toggle
* Inline editing of left/right values with instant highlighting
* Validation cues for obviously invalid or mismatched values
* Multi-frame support: frame slider + simple timer-based playback
* Encapsulated video (H.264/MPEG-4) extraction to a temp file and inline playback
* Pragmatic preview: renders a first frame when it is not a video
* File association friendly (open from OS into a side)

---

## Requirements

* .NET SDK 8.0 (or the version used in the solution)
* .NET MAUI workloads for your target platforms
* NuGet packages:

  * `FellowOakDicom`
  * `CommunityToolkit.Maui`
  * `CommunityToolkit.Maui.MediaElement`
  * `SixLabors.ImageSharp`

---

## Setup

### Enable the Community Toolkit (including MediaElement) in `MauiProgram.cs`

```csharp
using CommunityToolkit.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        return builder.Build();
    }
}
```

### XAML namespace for `MediaElement`

```xml
xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit"
```

### (Optional) Fellow Oak DICOM setup

If you use native codecs/transcoding, initialize once at startup:

```csharp
using FellowOakDicom;
using FellowOakDicom.Imaging;

new DicomSetupBuilder()
    .RegisterServices(s => s.AddFellowOakDicom())
    .SkipValidation()
    .Build();
```

---

## Build and Run

```bash
# Restore and build
dotnet restore
dotnet build

# Example: run on Windows
dotnet run -f net8.0-windows10.0.19041.0
```

Use Visual Studio to select and run Android, iOS, or MacCatalyst targets.

---

## How It Works

* Loader (`DicomLoader`)

  * Opens DICOM via Fellow Oak DICOM
  * Builds safe metadata (skips large/binary tags)
  * Detects encapsulated video; if present, extracts the elementary stream (H.264/MPEG-4) to the app cache and exposes a path
  * For images, renders a first frame and exposes a delegate to render arbitrary frames for multi-frame studies

* Per-file ViewModel (`DicomFileViewModel`)

  * Holds `FileName`, `Image` (preview), `Metadata` (read-only), `Rows`, and `RowMap`
  * Video case: `VideoPath` set (e.g., `.mp4`), `FrameCount = 0`, `GetFrameImageSource = null`
  * Multi-frame case: `VideoPath = null`, `FrameCount = NumberOfFrames`, `GetFrameImageSource = idx => RenderFrameAsImageSource(ds, idx)`

* Main ViewModel (`MainViewModel`)

  * Holds Left and Right files, combined comparison table, filtering/sorting state
  * Multi-frame playback via `IDispatcherTimer` (videos play in `MediaElement`)
  * Exposes `LeftHasVideo`, `Left/RightVideoSource`, `Left/RightHasMultiFrame`, `FrameCount`, `FrameIndex`, etc.

* UI (XAML)

  * Each side shows either an `Image` (no video) or a `MediaElement` (video)
  * Compact overlay shows key patient/study fields
  * Bottom half shows the combined metadata grid with inline editing

---

## Usage

1. Open files
   Click the **L** (left) or **R** (right) button to pick a DICOM file for that side.

2. Preview

   * Video: `MediaElement` with playback controls (via `CommunityToolkit.Maui.MediaElement`)
   * Multi-frame: frame slider and play/pause
   * Single-frame: static preview of frame 0

3. Browse and filter metadata

   * Free-text filter (tag, name, or value)
   * Tag-Filter list to focus on a single tag
   * Toggles: “Only differences” and “Only invalid”

4. Edit values

   * Edit left/right values inline; highlighting updates immediately

---

## Conversions

* Encapsulated video extraction
  If the DICOM contains H.264/MPEG-4, the elementary stream is written to the app cache (e.g., `.mp4`) and bound to the player.

* Future ideas
  Transfer syntax normalization, anonymization, bulk export.

---

## Validation and Non-Conformance

* Basic heuristics flag suspicious or invalid values and differences between sides.
* Extend validators as needed (e.g., stricter VR/VM checks, modality-specific rules).

---

## Troubleshooting

* Video does not play

  * Ensure `CommunityToolkit.Maui.MediaElement` is installed and enabled in `MauiProgram.cs`.
  * Confirm platform codec support (H.264 recommended).
  * Check that `LeftVideoSource`/`RightVideoSource` points to a valid file (app cache).

* No preview or wrong colors

  * Some uncommon photometric interpretations or high bit depths may need extra handling.

* Large files slow UI

  * Large/binary tags are skipped; consider virtualization for extremely large tag lists.

---

## Roadmap

* Optional write-back of edited metadata
* Expanded validators and conformance profiles
* Bulk conversion/export tools
* Keyboard shortcuts and advanced tag editors

---

## License

Apache-2.0
