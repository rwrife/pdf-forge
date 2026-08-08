using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;

namespace PdfForge.Core.PdfEngine;

public sealed class PdfCoreEngine
{
    public PdfCoreEngine(IPageRenderer? pageRenderer = null)
    {
        PageRenderer = pageRenderer ?? new PlaceholderPageRenderer();
    }

    public IPageRenderer PageRenderer { get; }

    public async Task<IPdfDocument> OpenAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var document = await PdfSharpDocument.OpenAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return document;
    }

    public async Task<IPdfDocument> MergeAsync(IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        var normalizedSourcePaths = new List<string>();
        foreach (var sourcePath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                continue;
            }

            normalizedSourcePaths.Add(Path.GetFullPath(sourcePath));
        }

        if (normalizedSourcePaths.Count == 0)
        {
            throw new ArgumentException("At least one source PDF path is required.", nameof(sourcePaths));
        }

        var mergedDocument = new PdfDocument();

        foreach (var sourcePath in normalizedSourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("PDF source file does not exist.", sourcePath);
            }

            using var importedDocument = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            foreach (var page in importedDocument.Pages)
            {
                mergedDocument.AddPage(page);
            }
        }

        return await Task.FromResult<IPdfDocument>(
                PdfSharpDocument.FromPdfDocument(mergedDocument, normalizedSourcePaths))
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<IPdfDocument>> SplitAsync(
        IPdfDocument document,
        PdfSplitMode splitMode,
        CancellationToken cancellationToken = default)
    {
        var pdfDocument = AsPdfSharpDocument(document);
        var outputs = new List<IPdfDocument>();

        using var importedSnapshot = pdfDocument.OpenImportSnapshot();
        var splitChunks = BuildSplitChunks(importedSnapshot.PageCount, splitMode);

        foreach (var splitChunk in splitChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var splitDocument = CreateDocumentFromSelection(importedSnapshot, splitChunk);
            outputs.Add(PdfSharpDocument.FromPdfDocument(splitDocument, pdfDocument.ProtectedSourcePaths));
        }

        return Task.FromResult<IReadOnlyList<IPdfDocument>>(outputs);
    }

    public Task<IPdfDocument> ExtractAsync(
        IPdfDocument document,
        IReadOnlyList<int> pageIndices,
        CancellationToken cancellationToken = default)
    {
        var pdfDocument = AsPdfSharpDocument(document);

        using var importedSnapshot = pdfDocument.OpenImportSnapshot();
        ValidatePageIndices(pageIndices, importedSnapshot.PageCount, nameof(pageIndices));

        cancellationToken.ThrowIfCancellationRequested();
        var extractedDocument = CreateDocumentFromSelection(importedSnapshot, pageIndices);
        var output = PdfSharpDocument.FromPdfDocument(extractedDocument, pdfDocument.ProtectedSourcePaths);

        return Task.FromResult<IPdfDocument>(output);
    }

    public async Task<IReadOnlyList<string>> ExtractPagesAsImagesAsync(
        IPdfDocument document,
        IReadOnlyList<int> pageIndices,
        string outputDirectory,
        PdfImageFormat imageFormat,
        int targetWidth = 1024,
        int targetHeight = 1448,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        if (targetWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Target width must be greater than zero.");
        }

        if (targetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetHeight), "Target height must be greater than zero.");
        }

        ValidatePageIndices(pageIndices, document.PageCount, nameof(pageIndices));

        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        var outputPaths = new List<string>(pageIndices.Count);
        for (var index = 0; index < pageIndices.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageNumber = pageIndices[index];
            var renderResult = await RenderPageAsync(
                    document,
                    pageNumber,
                    targetWidth,
                    targetHeight,
                    cancellationToken)
                .ConfigureAwait(false);

            var extension = imageFormat == PdfImageFormat.Png ? "png" : "jpg";
            var outputPath = Path.Combine(
                fullOutputDirectory,
                $"extract-{index + 1:D3}-page-{pageNumber:D4}.{extension}");

            if (imageFormat == PdfImageFormat.Png)
            {
                await File.WriteAllBytesAsync(outputPath, renderResult.ImageBytes, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await using var imageStream = new MemoryStream(renderResult.ImageBytes);
                using var image = await Image.LoadAsync(imageStream, cancellationToken).ConfigureAwait(false);
                image.SaveAsJpeg(outputPath);
            }

            outputPaths.Add(outputPath);
        }

        return outputPaths;
    }

    public Task<IPdfDocument> ReorderAsync(
        IPdfDocument document,
        IReadOnlyList<int> pageIndicesInOrder,
        CancellationToken cancellationToken = default)
    {
        var pdfDocument = AsPdfSharpDocument(document);

        using var importedSnapshot = pdfDocument.OpenImportSnapshot();
        ValidatePageIndices(pageIndicesInOrder, importedSnapshot.PageCount, nameof(pageIndicesInOrder));

        if (pageIndicesInOrder.Count != importedSnapshot.PageCount)
        {
            throw new ArgumentException("Reorder list must include every page exactly once.", nameof(pageIndicesInOrder));
        }

        var hasDuplicates = pageIndicesInOrder.Distinct().Count() != pageIndicesInOrder.Count;
        if (hasDuplicates)
        {
            throw new ArgumentException("Reorder list must not contain duplicate page indices.", nameof(pageIndicesInOrder));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var reordered = CreateDocumentFromSelection(importedSnapshot, pageIndicesInOrder);
        var output = PdfSharpDocument.FromPdfDocument(reordered, pdfDocument.ProtectedSourcePaths);

        return Task.FromResult<IPdfDocument>(output);
    }

    public Task<IPdfDocument> RotateAsync(
        IPdfDocument document,
        int pageIndex,
        int degrees,
        CancellationToken cancellationToken = default)
    {
        if (degrees % 90 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), "Rotation degrees must be a multiple of 90.");
        }

        var pdfDocument = AsPdfSharpDocument(document);

        using var importedSnapshot = pdfDocument.OpenImportSnapshot();
        ValidatePageIndex(pageIndex, importedSnapshot.PageCount, nameof(pageIndex));

        cancellationToken.ThrowIfCancellationRequested();

        var allPages = Enumerable.Range(1, importedSnapshot.PageCount).ToArray();
        var rotatedDocument = CreateDocumentFromSelection(importedSnapshot, allPages);
        var targetPage = rotatedDocument.Pages[pageIndex - 1];
        targetPage.Rotate = NormalizeRotationDegrees(targetPage.Rotate + degrees);

        var output = PdfSharpDocument.FromPdfDocument(rotatedDocument, pdfDocument.ProtectedSourcePaths);
        return Task.FromResult<IPdfDocument>(output);
    }

    public Task<IPdfDocument> DeleteAsync(
        IPdfDocument document,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        var pdfDocument = AsPdfSharpDocument(document);

        using var importedSnapshot = pdfDocument.OpenImportSnapshot();
        ValidatePageIndex(pageIndex, importedSnapshot.PageCount, nameof(pageIndex));

        cancellationToken.ThrowIfCancellationRequested();

        var remainingPages = Enumerable.Range(1, importedSnapshot.PageCount)
            .Where(pageNumber => pageNumber != pageIndex)
            .ToArray();

        var outputDocument = CreateDocumentFromSelection(importedSnapshot, remainingPages);
        var output = PdfSharpDocument.FromPdfDocument(outputDocument, pdfDocument.ProtectedSourcePaths);

        return Task.FromResult<IPdfDocument>(output);
    }

    public Task<PageRenderResult> RenderPageAsync(
        IPdfDocument document,
        int pageNumber,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken = default)
    {
        return PageRenderer.RenderPageAsync(document, pageNumber, targetWidth, targetHeight, cancellationToken);
    }

    public Task<PdfDocumentMetadata> ReadMetadataAsync(
        IPdfDocument document,
        CancellationToken cancellationToken = default)
    {
        var pdfDocument = AsPdfSharpDocument(document);
        cancellationToken.ThrowIfCancellationRequested();

        using var importedSnapshot = pdfDocument.OpenImportSnapshot();
        var metadata = new PdfDocumentMetadata(
            NormalizeMetadataValue(importedSnapshot.Info.Title),
            NormalizeMetadataValue(importedSnapshot.Info.Author),
            NormalizeMetadataValue(importedSnapshot.Info.Subject),
            NormalizeMetadataValue(importedSnapshot.Info.Keywords));

        return Task.FromResult(metadata);
    }

    public Task<IPdfDocument> WriteMetadataAsync(
        IPdfDocument document,
        PdfDocumentMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var pdfDocument = AsPdfSharpDocument(document);
        cancellationToken.ThrowIfCancellationRequested();

        using var importedSnapshot = pdfDocument.OpenImportSnapshot();
        var allPages = Enumerable.Range(1, importedSnapshot.PageCount).ToArray();
        var outputDocument = CreateDocumentFromSelection(importedSnapshot, allPages);

        CopyMetadata(importedSnapshot, outputDocument);
        ApplyMetadata(outputDocument, metadata);

        var output = PdfSharpDocument.FromPdfDocument(outputDocument, pdfDocument.ProtectedSourcePaths);
        return Task.FromResult<IPdfDocument>(output);
    }

    public Task<IPdfDocument> CompressAsync(
        IPdfDocument document,
        PdfCompressionSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveSettings = settings ?? PdfCompressionSettings.Default;
        effectiveSettings.Validate();

        var pdfDocument = AsPdfSharpDocument(document);
        cancellationToken.ThrowIfCancellationRequested();

        using var importedSnapshot = pdfDocument.OpenImportSnapshot();
        var allPages = Enumerable.Range(1, importedSnapshot.PageCount).ToArray();
        var outputDocument = CreateDocumentFromSelection(importedSnapshot, allPages);

        CopyMetadata(importedSnapshot, outputDocument);
        ApplyCompressionSettings(outputDocument, effectiveSettings);

        var output = PdfSharpDocument.FromPdfDocument(outputDocument, pdfDocument.ProtectedSourcePaths);
        return Task.FromResult<IPdfDocument>(output);
    }

    private static PdfSharpDocument AsPdfSharpDocument(IPdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document is not PdfSharpDocument pdfDocument)
        {
            throw new NotSupportedException("Only PdfSharpDocument instances are supported.");
        }

        return pdfDocument;
    }

    private static IReadOnlyList<IReadOnlyList<int>> BuildSplitChunks(int pageCount, PdfSplitMode splitMode)
    {
        ArgumentNullException.ThrowIfNull(splitMode);

        if (pageCount <= 0)
        {
            throw new InvalidOperationException("Cannot split an empty PDF document.");
        }

        return splitMode switch
        {
            PdfSplitMode.OnePerPage => Enumerable.Range(1, pageCount)
                .Select(page => (IReadOnlyList<int>)new[] { page })
                .ToArray(),

            PdfSplitMode.EveryNPages everyNPages => BuildEveryNChunks(pageCount, everyNPages.PagesPerSplit),

            PdfSplitMode.PageRanges pageRanges => BuildRangeChunks(pageCount, pageRanges.Ranges),

            _ => throw new NotSupportedException($"Unsupported split mode: {splitMode.GetType().Name}")
        };
    }

    private static IReadOnlyList<IReadOnlyList<int>> BuildEveryNChunks(int pageCount, int pagesPerSplit)
    {
        if (pagesPerSplit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pagesPerSplit), "Pages per split must be greater than zero.");
        }

        var chunks = new List<IReadOnlyList<int>>();

        for (var startPage = 1; startPage <= pageCount; startPage += pagesPerSplit)
        {
            var chunkLength = Math.Min(pagesPerSplit, pageCount - startPage + 1);
            var chunk = Enumerable.Range(startPage, chunkLength).ToArray();
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static IReadOnlyList<IReadOnlyList<int>> BuildRangeChunks(
        int pageCount,
        IReadOnlyList<PdfPageRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        if (ranges.Count == 0)
        {
            throw new ArgumentException("At least one page range is required.", nameof(ranges));
        }

        var chunks = new List<IReadOnlyList<int>>(ranges.Count);

        foreach (var range in ranges)
        {
            if (range.StartPageNumber < 1 || range.EndPageNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(ranges), "Page ranges must start at page 1 or greater.");
            }

            if (range.EndPageNumber < range.StartPageNumber)
            {
                throw new ArgumentException("Range end must be greater than or equal to the range start.", nameof(ranges));
            }

            if (range.EndPageNumber > pageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(ranges), "Page range exceeds document page count.");
            }

            var chunk = Enumerable.Range(
                    range.StartPageNumber,
                    range.EndPageNumber - range.StartPageNumber + 1)
                .ToArray();

            chunks.Add(chunk);
        }

        return chunks;
    }

    private static void ValidatePageIndices(IReadOnlyList<int> pageIndices, int pageCount, string paramName)
    {
        ArgumentNullException.ThrowIfNull(pageIndices);

        if (pageIndices.Count == 0)
        {
            throw new ArgumentException("At least one page index is required.", paramName);
        }

        foreach (var pageIndex in pageIndices)
        {
            ValidatePageIndex(pageIndex, pageCount, paramName);
        }
    }

    private static void ValidatePageIndex(int pageIndex, int pageCount, string paramName)
    {
        if (pageIndex < 1 || pageIndex > pageCount)
        {
            throw new ArgumentOutOfRangeException(paramName, pageIndex, "Page index is out of range.");
        }
    }

    private static PdfDocument CreateDocumentFromSelection(PdfDocument importedDocument, IReadOnlyList<int> oneBasedPageIndices)
    {
        var outputDocument = new PdfDocument();
        foreach (var pageIndex in oneBasedPageIndices)
        {
            outputDocument.AddPage(importedDocument.Pages[pageIndex - 1]);
        }

        return outputDocument;
    }

    private static void ApplyCompressionSettings(PdfDocument document, PdfCompressionSettings settings)
    {
        document.Options.NoCompression = false;
        document.Options.CompressContentStreams = true;

        var useBestCompression = settings.JpegQuality <= 70 || settings.TargetDpi <= 150;
        SetEnumOptionByName(
            document.Options,
            propertyName: "FlateEncodeMode",
            preferredValueName: useBestCompression ? "BestCompression" : "Default");

        if (settings.JpegQuality <= 90)
        {
            SetEnumOptionByName(
                document.Options,
                propertyName: "UseFlateDecoderForJpegImages",
                preferredValueName: "Automatic");
        }
    }

    private static void SetEnumOptionByName(object options, string propertyName, string preferredValueName)
    {
        var property = options.GetType().GetProperty(propertyName);
        if (property is null || !property.PropertyType.IsEnum || !property.CanWrite)
        {
            return;
        }

        var matchingValueName = Enum.GetNames(property.PropertyType)
            .FirstOrDefault(valueName => string.Equals(valueName, preferredValueName, StringComparison.OrdinalIgnoreCase));

        if (matchingValueName is null)
        {
            return;
        }

        var enumValue = Enum.Parse(property.PropertyType, matchingValueName, ignoreCase: true);
        property.SetValue(options, enumValue);
    }

    private static void CopyMetadata(PdfDocument fromDocument, PdfDocument toDocument)
    {
        toDocument.Info.Title = fromDocument.Info.Title;
        toDocument.Info.Author = fromDocument.Info.Author;
        toDocument.Info.Subject = fromDocument.Info.Subject;
        toDocument.Info.Keywords = fromDocument.Info.Keywords;
    }

    private static void ApplyMetadata(PdfDocument document, PdfDocumentMetadata metadata)
    {
        document.Info.Title = metadata.Title ?? string.Empty;
        document.Info.Author = metadata.Author ?? string.Empty;
        document.Info.Subject = metadata.Subject ?? string.Empty;
        document.Info.Keywords = metadata.Keywords ?? string.Empty;
    }

    private static string? NormalizeMetadataValue(string? metadataValue)
    {
        return string.IsNullOrWhiteSpace(metadataValue)
            ? null
            : metadataValue;
    }

    private static int NormalizeRotationDegrees(int degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
