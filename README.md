# pdf-forge

**Windows PDF toolkit** — merge, split, reorder, rotate, delete, compress, and extract pages from PDFs with a fast, friendly desktop UI. Optional local-AI adds OCR for scanned PDFs and page/document summarization using tiny local models. Offline & privacy-first — your documents never leave your machine.

## Overview

`pdf-forge` is a lightweight Windows desktop app (Windows 10/11 first) for the everyday PDF chores that normally push people toward sketchy online converters:

- **Merge** several PDFs into one, in any order.
- **Split** a PDF into multiple files (by page ranges, every N pages, or one-per-page).
- **Reorder / rotate / delete** pages via a visual page-thumbnail board (drag & drop).
- **Extract** selected pages to a new document or to images (PNG/JPG).
- **Compress** to shrink file size (downsample images, strip unused objects).
- **Metadata** — view/edit title, author, subject, keywords.
- **Optional local-AI**: OCR a scanned/image-only PDF into a searchable text layer, and summarize a page or whole document — all via a local model endpoint. Fully optional and **off by default**.

Everything works **100% offline**. There is no account, no cloud upload, no telemetry.

## Motivation

Basic PDF editing on Windows is a pain: the built-in tools can't merge or reorder, desktop suites are heavy/paid, and free online tools mean uploading private documents to a stranger's server. `pdf-forge` keeps all of it local, fast, and free — a single small app that does the 90% of PDF tasks people actually need.

## Use cases

- Combine scanned receipts / invoices into one file for expenses.
- Pull a few pages out of a big report to share.
- Rotate pages that were scanned sideways.
- Reorder a document that came out of the scanner backwards.
- Shrink a huge image-heavy PDF so it fits an email attachment limit.
- Make a scanned contract **searchable** with local OCR (no cloud).
- Get a quick local-AI **summary** of a long PDF before reading it.

## How to use (Windows-first quickstart)

> Status: early scaffolding. Build steps below are the target workflow; see milestones.

1. Download the latest portable `pdf-forge-win-x64.zip` from Releases (or the MSIX installer).
2. Unzip and run `PdfForge.exe` — no install required for the portable build.
3. Drag one or more PDFs onto the window (or **File → Open**).
4. Use the page board to reorder / rotate / delete pages, or the toolbar for **Merge / Split / Extract / Compress**.
5. Click **Save As** to write the result. Originals are never modified in place.

### Build from source (developers)

```powershell
git clone https://github.com/rwrife/pdf-forge.git
cd pdf-forge
dotnet build -c Release
dotnet run --project src/PdfForge.App
```

## Example workflow

**Merge two PDFs and extract pages 3–5 of the result:**

1. Drag `a.pdf` then `b.pdf` onto the page board — pages append in order.
2. Select pages 3, 4, 5 on the board (Ctrl+click).
3. **Extract → Selected pages → New document.**
4. **Save As** `pages-3-5.pdf`.

**OCR a scanned PDF (local-AI on):**

1. Open the scanned PDF.
2. **Tools → Make searchable (OCR)** — pdf-forge renders each page and runs the local OCR model, then writes an invisible text layer.
3. Save. The output is now selectable/searchable in any reader.

## Local-AI integration

pdf-forge runs fully without AI. When you opt in, it talks to a **local, OpenAI-compatible endpoint** (e.g. [Ollama](https://ollama.com/) or [llama.cpp](https://github.com/ggerganov/llama.cpp) server) on `http://localhost:11434` — nothing is sent to the cloud.

- **OCR / scanned-PDF reading**: a tiny local vision model (MiniCPM-V class) or a native OCR fallback (`Windows.Media.Ocr`) turns page images into text.
- **Summarization**: a small local text model (Llama 3.2 3B / Qwen2.5 / Phi-3-mini class) summarizes a page or document; only extracted text is sent to the local endpoint.
- A **reachability probe** checks the endpoint on demand; if it's unavailable, AI features gray out gracefully and the rest of the app is unaffected.
- All AI features are **off by default** and configurable (endpoint URL, model name) in Settings.

## Current status / milestones

- [ ] M0 — Repo scaffold, solution layout, CI on `windows-latest`
- [x] M1 — Core PDF engine: load, render thumbnails, save (initial implementation)
- [ ] M2 — Merge / split / extract / reorder / rotate / delete
- [ ] M3 — Compression + metadata editor
- [ ] M4 — Page board UI (drag & drop, multi-select) + drag-drop file open
- [ ] M5 — Optional local-AI: OCR searchable layer + summarization
- [ ] M6 — Packaging: portable win-x64 zip + MSIX, first release

See [PLAN.md](./PLAN.md) for scope, architecture, and non-goals.

---

*Part of the auto-tool-lab Windows utilities series. Privacy-first, local-only, no telemetry.*
