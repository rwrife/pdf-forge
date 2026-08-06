using PdfForge.Core.PdfEngine;
using PdfSharpCore.Pdf;
using Xunit;

namespace PdfForge.Core.Tests;

public class PdfEngineTests
{
    [Fact]
    public async Task OpenAsync_ReturnsCorrectPageCount_ForMultiPagePdf()
    {
        var sourcePath = CreateTemporaryPdf(pageCount: 3);
        try
        {
            var engine = new PdfCoreEngine();
            await using var document = await engine.OpenAsync(sourcePath);

            Assert.Equal(3, document.PageCount);
        }
        finally
        {
            DeleteIfExists(sourcePath);
        }
    }

    [Fact]
    public async Task RenderPageAsync_ReturnsPngWithRequestedDimensions()
    {
        var sourcePath = CreateTemporaryPdf(pageCount: 2);
        try
        {
            var engine = new PdfCoreEngine();
            await using var document = await engine.OpenAsync(sourcePath);

            var result = await engine.RenderPageAsync(document, pageNumber: 2, targetWidth: 240, targetHeight: 160);

            Assert.Equal(240, result.Width);
            Assert.Equal(160, result.Height);
            Assert.Equal("image/png", result.MimeType);
            Assert.NotNull(result.ImageBytes);
            Assert.NotEmpty(result.ImageBytes);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, result.ImageBytes.Take(4).ToArray());
        }
        finally
        {
            DeleteIfExists(sourcePath);
        }
    }

    [Fact]
    public async Task SaveAsAsync_WritesToNewPath_AndDoesNotMutateSourceBytes()
    {
        var sourcePath = CreateTemporaryPdf(pageCount: 2);
        var outputPath = Path.Combine(Path.GetTempPath(), $"pdf-forge-save-{Guid.NewGuid():N}.pdf");

        try
        {
            var originalBytes = await File.ReadAllBytesAsync(sourcePath);

            var engine = new PdfCoreEngine();
            await using var document = await engine.OpenAsync(sourcePath);
            await document.SaveAsAsync(outputPath);

            var sourceBytesAfterSave = await File.ReadAllBytesAsync(sourcePath);
            var outputBytes = await File.ReadAllBytesAsync(outputPath);

            Assert.Equal(originalBytes, sourceBytesAfterSave);
            Assert.Equal(originalBytes, outputBytes);
        }
        finally
        {
            DeleteIfExists(sourcePath);
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsAsync_Throws_WhenOutputPathMatchesSourcePath()
    {
        var sourcePath = CreateTemporaryPdf(pageCount: 1);

        try
        {
            var engine = new PdfCoreEngine();
            await using var document = await engine.OpenAsync(sourcePath);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => document.SaveAsAsync(sourcePath));
        }
        finally
        {
            DeleteIfExists(sourcePath);
        }
    }

    private static string CreateTemporaryPdf(int pageCount)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pdf-forge-{Guid.NewGuid():N}.pdf");

        using var document = new PdfDocument();
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
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
}
