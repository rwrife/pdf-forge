# pdf-forge — Plan

## Scope

A small, focused Windows desktop utility for common PDF manipulation, plus optional local-AI enrichment. In scope:

- Load PDFs, render page thumbnails, save output (never overwrite originals in place).
- Page operations: reorder, rotate, delete, extract (to PDF or PNG/JPG).
- Document operations: merge multiple PDFs, split by range / every-N / one-per-page.
- Compression (image downsampling + object cleanup) and title/author/subject/keyword metadata editing.
- Visual page board with drag-and-drop and multi-select.
- Optional, off-by-default local-AI: OCR to produce a searchable text layer, and text summarization — via a local OpenAI-compatible endpoint (Ollama/llama.cpp), with graceful fallback.
- Windows-first packaging: portable self-contained zip + MSIX.

## Architecture / tech approach

- **Language/runtime**: C# on **.NET 8**.
- **UI**: **WPF** (MVVM), so we can ship a native, responsive desktop app with a thumbnail page board (virtualized `ItemsControl`/`ListView`).
- **Core library `PdfForge.Core`** (UI-free, unit-testable):
  - PDF model + operations (merge/split/extract/reorder/rotate/delete/compress/metadata).
  - Page rendering to bitmaps for thumbnails and image export.
  - Candidate PDF libraries: **PDFsharp/MigraDoc** (MIT) for structural edits; **PdfPig** (Apache-2.0) for reading/text extraction; **Docnet/PDFium** for high-fidelity page rasterization. Pick per-operation; keep behind interfaces (`IPdfDocument`, `IPageRenderer`).
  - Deterministic, streaming operations where possible to keep memory bounded on large files.
- **App project `PdfForge.App`**: WPF shell, page board, toolbar, settings, drag-drop, Save-As pipeline.
- **AI abstraction `IPdfAiService`**:
  - `OcrPageAsync` — render page → local vision model / `Windows.Media.Ocr` → text layer.
  - `SummarizeAsync` — extracted text → local text model.
  - Endpoint reachability probe + graceful fallback; off by default; settings for URL + model.
- **Settings**: JSON under `%APPDATA%\pdf-forge` (AI endpoint, model, compression defaults, recent files).
- **Tests**: **xUnit** against `PdfForge.Core` with small fixture PDFs (merge count, page order, rotation angle, extract subset, round-trip metadata).
- **CI**: GitHub Actions on `windows-latest` — build + test on push/PR.

## Milestones

1. **M0 Scaffold** — solution (`PdfForge.Core`, `PdfForge.App`, `PdfForge.Core.Tests`), CI workflow, editorconfig.
2. **M1 Core engine** — load PDF, render thumbnails, save; `IPdfDocument`/`IPageRenderer`.
3. **M2 Page/doc ops** — merge, split, extract, reorder, rotate, delete (Core + tests).
4. **M3 Compress + metadata** — image downsample/object cleanup; metadata read/write.
5. **M4 Page board UI** — drag-drop reorder, multi-select, rotate/delete buttons, drag-drop file open, Save-As.
6. **M5 Local-AI** — `IPdfAiService`, OCR searchable layer, summarization, reachability probe + fallback, Settings.
7. **M6 Packaging** — portable self-contained `win-x64` zip + MSIX; first tagged release.

## Non-goals

- No cloud services, accounts, telemetry, or network calls for core features.
- Not a full PDF editor (no vector drawing, form design, or advanced prepress).
- No digital-signature creation/validation (may revisit later).
- Not cross-platform in v1 — Windows 10/11 first (Core stays portable to allow it later).
- No bundled large AI models; users point at their own local endpoint.

## Packaging target for Windows

- **Primary**: portable **self-contained `win-x64`** single-folder zip (`PdfForge.exe`), no runtime install needed.
- **Secondary**: **MSIX** installer for Start-menu integration and clean updates.
- Native PDF rasterization deps (e.g. PDFium) bundled per-arch; verified in CI on `windows-latest`.
