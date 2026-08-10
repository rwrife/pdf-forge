using PdfForge.Core.Ai;
using PdfForge.Core.PdfEngine;
using PdfSharpCore.Pdf;
using Xunit;

namespace PdfForge.Core.Tests;

public class PdfCoreEngineAiTests
{
    [Fact]
    public async Task OcrPageToSearchableAsync_UsesAiService_AndPreservesPageCount()
    {
        var sourcePath = CreateTemporaryPdf(pageCount: 2);
        var outputPath = Path.Combine(Path.GetTempPath(), $"pdf-forge-ocr-{Guid.NewGuid():N}.pdf");

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);

            var aiService = new FakePdfAiService(
                ocrText: "Searchable OCR text",
                summary: "n/a");

            await using var searchable = await engine.OcrPageToSearchableAsync(
                sourceDocument,
                pageNumber: 1,
                aiService: aiService);

            await searchable.SaveAsAsync(outputPath);

            Assert.Equal(sourceDocument.PageCount, searchable.PageCount);
            Assert.Equal(1, aiService.OcrCallCount);
            Assert.True(File.Exists(outputPath));
            Assert.NotEmpty(await File.ReadAllBytesAsync(outputPath));
        }
        finally
        {
            DeleteIfExists(sourcePath);
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task SummarizeTextAsync_DelegatesToAiService()
    {
        var engine = new PdfCoreEngine();
        var aiService = new FakePdfAiService(
            ocrText: "unused",
            summary: "Short summary from local model.");

        var summary = await engine.SummarizeTextAsync("Long source text", aiService);

        Assert.Equal("Short summary from local model.", summary);
        Assert.Equal(1, aiService.SummaryCallCount);
    }

    private static string CreateTemporaryPdf(int pageCount)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pdf-forge-ai-{Guid.NewGuid():N}.pdf");

        using var document = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
        {
            document.AddPage();
        }

        document.Save(filePath);
        return filePath;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class FakePdfAiService(string ocrText, string summary) : IPdfAiService
    {
        public int OcrCallCount { get; private set; }

        public int SummaryCallCount { get; private set; }

        public Task<PdfAiAvailability> ProbeAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PdfAiAvailability(true, true, "reachable"));
        }

        public Task<OcrPageResult> OcrPageAsync(PageRenderResult renderedPage, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OcrCallCount++;
            return Task.FromResult(new OcrPageResult(ocrText, UsedRemoteModel: true, UsedFallback: false));
        }

        public Task<string> SummarizeAsync(string extractedText, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SummaryCallCount++;
            return Task.FromResult(summary);
        }
    }
}
