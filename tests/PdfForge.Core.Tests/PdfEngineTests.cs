using PdfForge.Core.PdfEngine;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
            var sourcePageWidths = ReadPageWidths(sourcePath);
            var outputPageWidths = ReadPageWidths(outputPath);

            Assert.Equal(originalBytes, sourceBytesAfterSave);
            Assert.NotEmpty(outputBytes);
            Assert.Equal(sourcePageWidths, outputPageWidths);
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

    [Fact]
    public async Task MergeAsync_PreservesInputOrder_AndPageCountSum()
    {
        var sourceA = CreateTemporaryPdfWithWidths(310, 320);
        var sourceB = CreateTemporaryPdfWithWidths(410);
        var outputPath = Path.Combine(Path.GetTempPath(), $"pdf-forge-merge-{Guid.NewGuid():N}.pdf");

        try
        {
            var engine = new PdfCoreEngine();
            await using var merged = await engine.MergeAsync(new[] { sourceA, sourceB });

            Assert.Equal(3, merged.PageCount);

            await merged.SaveAsAsync(outputPath);
            Assert.Equal(new[] { 310, 320, 410 }, ReadPageWidths(outputPath));
        }
        finally
        {
            DeleteIfExists(sourceA);
            DeleteIfExists(sourceB);
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task SplitAsync_EveryNPages_DistributesPagesCorrectly()
    {
        var sourcePath = CreateTemporaryPdfWithWidths(101, 102, 103, 104, 105);

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);
            var splits = await engine.SplitAsync(sourceDocument, new PdfSplitMode.EveryNPages(2));

            try
            {
                Assert.Equal(3, splits.Count);
                Assert.Equal(new[] { 2, 2, 1 }, splits.Select(split => split.PageCount).ToArray());

                var firstSplit = await SaveAndReadWidthsAsync(splits[0]);
                var secondSplit = await SaveAndReadWidthsAsync(splits[1]);
                var thirdSplit = await SaveAndReadWidthsAsync(splits[2]);

                Assert.Equal(new[] { 101, 102 }, firstSplit);
                Assert.Equal(new[] { 103, 104 }, secondSplit);
                Assert.Equal(new[] { 105 }, thirdSplit);
            }
            finally
            {
                foreach (var split in splits)
                {
                    await split.DisposeAsync();
                }
            }
        }
        finally
        {
            DeleteIfExists(sourcePath);
        }
    }

    [Fact]
    public async Task SplitAsync_OnePerPage_ReturnsOneDocumentPerPage()
    {
        var sourcePath = CreateTemporaryPdfWithWidths(701, 702, 703);

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);
            var splits = await engine.SplitAsync(sourceDocument, new PdfSplitMode.OnePerPage());

            try
            {
                Assert.Equal(3, splits.Count);
                Assert.All(splits, split => Assert.Equal(1, split.PageCount));

                var widths = new List<int>();
                foreach (var split in splits)
                {
                    widths.Add((await SaveAndReadWidthsAsync(split))[0]);
                }

                Assert.Equal(new[] { 701, 702, 703 }, widths);
            }
            finally
            {
                foreach (var split in splits)
                {
                    await split.DisposeAsync();
                }
            }
        }
        finally
        {
            DeleteIfExists(sourcePath);
        }
    }

    [Fact]
    public async Task SplitAsync_PageRanges_UsesRequestedRanges()
    {
        var sourcePath = CreateTemporaryPdfWithWidths(201, 202, 203, 204);

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);

            var splits = await engine.SplitAsync(
                sourceDocument,
                new PdfSplitMode.PageRanges(new[]
                {
                    new PdfPageRange(2, 3),
                    new PdfPageRange(4, 4)
                }));

            try
            {
                Assert.Equal(2, splits.Count);
                Assert.Equal(new[] { 202, 203 }, await SaveAndReadWidthsAsync(splits[0]));
                Assert.Equal(new[] { 204 }, await SaveAndReadWidthsAsync(splits[1]));
            }
            finally
            {
                foreach (var split in splits)
                {
                    await split.DisposeAsync();
                }
            }
        }
        finally
        {
            DeleteIfExists(sourcePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_ReturnsSelectedPages_InSelectionOrder()
    {
        var sourcePath = CreateTemporaryPdfWithWidths(11, 22, 33, 44);

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);
            await using var extracted = await engine.ExtractAsync(sourceDocument, new[] { 4, 2, 2 });

            Assert.Equal(3, extracted.PageCount);
            Assert.Equal(new[] { 44, 22, 22 }, await SaveAndReadWidthsAsync(extracted));
        }
        finally
        {
            DeleteIfExists(sourcePath);
        }
    }

    [Fact]
    public async Task ReorderAsync_ReordersPages()
    {
        var sourcePath = CreateTemporaryPdfWithWidths(901, 902, 903);

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);
            await using var reordered = await engine.ReorderAsync(sourceDocument, new[] { 3, 1, 2 });

            Assert.Equal(new[] { 903, 901, 902 }, await SaveAndReadWidthsAsync(reordered));
        }
        finally
        {
            DeleteIfExists(sourcePath);
        }
    }

    [Fact]
    public async Task RotateAsync_AppliesExpectedPageRotation()
    {
        var sourcePath = CreateTemporaryPdfWithWidths(501, 502);
        var outputPath = Path.Combine(Path.GetTempPath(), $"pdf-forge-rotate-{Guid.NewGuid():N}.pdf");

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);
            await using var rotated = await engine.RotateAsync(sourceDocument, pageIndex: 2, degrees: 90);
            await rotated.SaveAsAsync(outputPath);

            Assert.Equal(new[] { 0, 90 }, ReadPageRotations(outputPath));
        }
        finally
        {
            DeleteIfExists(sourcePath);
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesRequestedPage()
    {
        var sourcePath = CreateTemporaryPdfWithWidths(801, 802, 803);

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);
            await using var withoutSecondPage = await engine.DeleteAsync(sourceDocument, pageIndex: 2);

            Assert.Equal(2, withoutSecondPage.PageCount);
            Assert.Equal(new[] { 801, 803 }, await SaveAndReadWidthsAsync(withoutSecondPage));
        }
        finally
        {
            DeleteIfExists(sourcePath);
        }
    }

    [Fact]
    public async Task ExtractPagesAsImagesAsync_WritesPngAndJpegOutputs()
    {
        var sourcePath = CreateTemporaryPdf(pageCount: 2);
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"pdf-forge-images-{Guid.NewGuid():N}");

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);

            var pngPaths = await engine.ExtractPagesAsImagesAsync(
                sourceDocument,
                new[] { 2, 1 },
                outputDirectory,
                PdfImageFormat.Png,
                targetWidth: 64,
                targetHeight: 64);

            var jpgPaths = await engine.ExtractPagesAsImagesAsync(
                sourceDocument,
                new[] { 1 },
                outputDirectory,
                PdfImageFormat.Jpeg,
                targetWidth: 64,
                targetHeight: 64);

            Assert.Equal(2, pngPaths.Count);
            Assert.Single(jpgPaths);
            Assert.All(pngPaths, path => Assert.EndsWith(".png", path, StringComparison.OrdinalIgnoreCase));
            Assert.All(pngPaths, path => Assert.True(File.Exists(path)));
            Assert.All(jpgPaths, path => Assert.EndsWith(".jpg", path, StringComparison.OrdinalIgnoreCase));
            Assert.All(jpgPaths, path => Assert.True(File.Exists(path)));

            var jpegHeader = await File.ReadAllBytesAsync(jpgPaths[0]);
            Assert.Equal(0xFF, jpegHeader[0]);
            Assert.Equal(0xD8, jpegHeader[1]);
        }
        finally
        {
            DeleteIfExists(sourcePath);

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Metadata_RoundTrip_ReadsAndWritesExpectedFields()
    {
        var sourcePath = CreateTemporaryPdfWithWidths(600, 601);
        var outputPath = Path.Combine(Path.GetTempPath(), $"pdf-forge-metadata-{Guid.NewGuid():N}.pdf");

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);

            var updatedMetadata = new PdfDocumentMetadata(
                Title: "Quarterly Planning Packet",
                Author: "pdf-forge tests",
                Subject: "Metadata round-trip",
                Keywords: "pdf,forge,metadata");

            await using var withMetadata = await engine.WriteMetadataAsync(sourceDocument, updatedMetadata);
            await withMetadata.SaveAsAsync(outputPath);

            await using var reopened = await engine.OpenAsync(outputPath);
            var readBack = await engine.ReadMetadataAsync(reopened);

            Assert.Equal(updatedMetadata.Title, readBack.Title);
            Assert.Equal(updatedMetadata.Author, readBack.Author);
            Assert.Equal(updatedMetadata.Subject, readBack.Subject);
            Assert.Equal(updatedMetadata.Keywords, readBack.Keywords);
        }
        finally
        {
            DeleteIfExists(sourcePath);
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task CompressAsync_ReducesFileSize_AndPreservesPageCount()
    {
        var sourcePath = CreateImageHeavyPdfFixture(pageCount: 3);
        var outputPath = Path.Combine(Path.GetTempPath(), $"pdf-forge-compressed-{Guid.NewGuid():N}.pdf");

        try
        {
            var engine = new PdfCoreEngine();
            await using var sourceDocument = await engine.OpenAsync(sourcePath);

            var settings = new PdfCompressionSettings(TargetDpi: 120, JpegQuality: 55);
            await using var compressed = await engine.CompressAsync(sourceDocument, settings);
            await compressed.SaveAsAsync(outputPath);

            var sourceFileSize = new FileInfo(sourcePath).Length;
            var compressedFileSize = new FileInfo(outputPath).Length;

            Assert.Equal(sourceDocument.PageCount, compressed.PageCount);
            Assert.True(
                compressedFileSize < sourceFileSize,
                $"Expected compressed file to be smaller. Source={sourceFileSize}, Compressed={compressedFileSize}");
        }
        finally
        {
            DeleteIfExists(sourcePath);
            DeleteIfExists(outputPath);
        }
    }

    private static string CreateTemporaryPdf(int pageCount)
    {
        var widths = Enumerable.Range(0, pageCount).Select(index => 595 + index).ToArray();
        return CreateTemporaryPdfWithWidths(widths);
    }

    private static string CreateTemporaryPdfWithWidths(params int[] pageWidths)
    {
        if (pageWidths is null || pageWidths.Length == 0)
        {
            throw new ArgumentException("At least one page width is required.", nameof(pageWidths));
        }

        var filePath = Path.Combine(Path.GetTempPath(), $"pdf-forge-{Guid.NewGuid():N}.pdf");

        using var document = new PdfDocument();
        foreach (var width in pageWidths)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(842);
        }

        document.Save(filePath);
        return filePath;
    }

    private static string CreateImageHeavyPdfFixture(int pageCount)
    {
        var imagePath = Path.Combine(Path.GetTempPath(), $"pdf-forge-image-{Guid.NewGuid():N}.png");
        var pdfPath = Path.Combine(Path.GetTempPath(), $"pdf-forge-image-heavy-{Guid.NewGuid():N}.pdf");

        try
        {
            using (var image = new Image<Rgba32>(1024, 1024))
            {
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        var red = (byte)((x * 17 + y * 11) % 255);
                        var green = (byte)((x * 7 + y * 13) % 255);
                        var blue = (byte)((x * y) % 255);
                        image[x, y] = new Rgba32(red, green, blue);
                    }
                }

                image.SaveAsPng(imagePath);
            }

            using var document = new PdfDocument();
            document.Options.NoCompression = true;
            document.Options.CompressContentStreams = false;

            using var embeddedImage = XImage.FromFile(imagePath);
            for (var i = 0; i < pageCount; i++)
            {
                var page = document.AddPage();
                page.Width = XUnit.FromPoint(612);
                page.Height = XUnit.FromPoint(792);

                using var graphics = XGraphics.FromPdfPage(page);
                graphics.DrawImage(embeddedImage, 0, 0, page.Width.Point, page.Height.Point);
            }

            document.Save(pdfPath);
            return pdfPath;
        }
        finally
        {
            DeleteIfExists(imagePath);
        }
    }

    private static int[] ReadPageWidths(string sourcePath)
    {
        using var document = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        var widths = new List<int>(document.PageCount);

        foreach (var page in document.Pages)
        {
            widths.Add((int)Math.Round(page.Width.Point));
        }

        return widths.ToArray();
    }

    private static int[] ReadPageRotations(string sourcePath)
    {
        using var document = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        var rotations = new List<int>(document.PageCount);

        foreach (var page in document.Pages)
        {
            rotations.Add(page.Rotate);
        }

        return rotations.ToArray();
    }

    private static async Task<int[]> SaveAndReadWidthsAsync(IPdfDocument document)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pdf-forge-out-{Guid.NewGuid():N}.pdf");

        try
        {
            await document.SaveAsAsync(outputPath);
            return ReadPageWidths(outputPath);
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
