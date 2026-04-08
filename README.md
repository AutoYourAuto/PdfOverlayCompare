# PDF Overlay Compare

A Windows Forms application that compares two PDF files page-by-page and displays the
differences in an overlay view with color-coded output.

## Color Coding
- **Red** = Content only in PDF A (reference)
- **Blue** = Content only in PDF B (compare)
- **Dark Gray** = Identical content in both
- **Light Gray** = Content within tolerance of matching content

## Requirements
- Windows 10/11
- .NET Framework 4.7.2 or later
- Visual Studio 2019 or later (or `dotnet build` CLI)

## Build

### Option 1: Visual Studio
1. Open `PdfOverlayCompare.csproj` in Visual Studio
2. NuGet packages will auto-restore (PdfiumViewer + native binaries)
3. Build and run (F5)

### Option 2: Command Line
```
cd PdfOverlayCompare
dotnet restore
dotnet build
dotnet run
```

### Note on PdfiumViewer native DLL
The `PdfiumViewer.Native.x86_64.v8-xfa` NuGet package provides the `pdfium.dll` that
PdfiumViewer needs. After building, ensure `pdfium.dll` is in your output directory.
If it is not copied automatically, manually copy it from:
```
packages\PdfiumViewer.Native.x86_64.v8-xfa\...\pdfium.dll
```
to your `bin\Debug\` or `bin\Release\` folder.

## Usage

### Quick Start (No Calibration)
1. **File > Open PDF A** — load the reference PDF
2. **File > Open PDF B** — load the comparison PDF
3. Click **Compare** — generates an overlay using simple page scaling
4. The overlay panel (right side) shows the color-coded differences

### With 3-Point Calibration (Recommended for Different Page Sizes)
When the two PDFs have different page sizes, orientations, or slight misalignment,
use the 3-point calibration for accurate overlay:

1. Load both PDFs
2. Click **Pick Points** in the toolbar (enters picking mode)
3. On **PDF A**, left-click 3 well-separated reference points
   (e.g., three corners of the drawing border, or three easily identifiable
   intersections of grid lines)
4. On **PDF B**, left-click the **same 3 points** in the same order
5. The status bar shows the computed transform (scale, rotation, offset)
6. Click **Compare** to generate the calibrated overlay

### Tips for Picking Good Calibration Points
- Pick points that are far apart (e.g., opposite corners of the drawing)
- Pick points that you can identify precisely on both PDFs
- Grid line intersections and title block corners work well
- Avoid points that are too close together (reduces accuracy)
- The 3 points must NOT be collinear (not all on the same line)

### Navigation
- **Pan**: Right-click drag or middle-click drag
- **Zoom**: Mouse wheel (zooms toward cursor)
- **Zoom to Fit**: View menu or Ctrl+F
- **Page Navigation**: Use the Page A / Page B spinners in the toolbar
- **Sync Pages**: When checked, changing Page A also changes Page B

### Export
- **Export current overlay**: File > Export Overlay as PNG
- **Export all pages as images**: File > Export All Pages as Images (saves PNGs to a folder)
- **Export all pages as PDF**: File > Export All Pages as PDF (saves a single PDF file)
  The PDF export uses JPEG compression at quality 92 internally, keeping the
  file size reasonable while preserving good detail for comparison.

### Settings
- **DPI**: Rendering resolution (higher = more detail but slower). Default 200.
- **Tolerance**: Pixel tolerance for merging nearby content (handles sub-pixel
  shifts from scaling). Default 3. Increase if you see too much noise.

## Architecture

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point |
| `MainForm.cs` | Main window layout, file I/O, workflow |
| `PdfViewPanel.cs` | Custom panel with pan/zoom and point picking |
| `PdfPageRenderer.cs` | Wraps PdfiumViewer for PDF-to-Bitmap rendering |
| `AffineTransform.cs` | 3-point affine transform computation |
| `OverlayEngine.cs` | Image compositing with difference detection |
| `PdfBuilder.cs` | Creates output PDF from overlay images (no extra dependency) |

## Troubleshooting

**"Unable to load DLL 'pdfium'" error**
→ The native pdfium.dll is missing. See the build note above about copying it.

**Overlay shows too much noise**
→ Increase the Tolerance value (try 5-8).
→ Make sure your calibration points are accurate.

**Overlay is misaligned**
→ Re-pick calibration points more carefully.
→ Use easily identifiable points like grid intersections.

**Out of memory on large PDFs**
→ Reduce the DPI setting (try 150 or 100).
