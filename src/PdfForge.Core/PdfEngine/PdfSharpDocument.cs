using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PdfForge.Core.PdfEngine;

public sealed class PdfSharpDocument : IPdfDocument
{
    private readonly PdfDocument _pdfDocument;
    private readonly IReadOnlyCollection<string> _protectedSourcePaths;
    private readonly StringComparison _pathComparison;
    private bool _disposed;

    private PdfSharpDocument(PdfDocument pdfDocument, IReadOnlyCollection<string> protectedSourcePaths)
    {
        _pdfDocument = pdfDocument ?? throw new ArgumentNullException(nameof(pdfDocument));
        _protectedSourcePaths = protectedSourcePaths;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public string SourcePath => _protectedSourcePaths.FirstOrDefault() ?? string.Empty;

    public int PageCount
    {
        get
        {
            ThrowIfDisposed();
            return _pdfDocument.PageCount;
        }
    }

    internal IReadOnlyCollection<string> ProtectedSourcePaths => _protectedSourcePaths;

    internal PdfDocument InnerDocument
    {
        get
        {
            ThrowIfDisposed();
            return _pdfDocument;
        }
    }

    public static Task<PdfSharpDocument> OpenAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path is required.", nameof(sourcePath));
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("PDF source file does not exist.", fullSourcePath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var importedSource = PdfReader.Open(fullSourcePath, PdfDocumentOpenMode.Import);
        var editableCopy = CloneFromImport(importedSource);

        return Task.FromResult(new PdfSharpDocument(editableCopy, NormalizeProtectedPaths(new[] { fullSourcePath })));
    }

    internal static PdfSharpDocument FromPdfDocument(PdfDocument pdfDocument, IEnumerable<string>? protectedSourcePaths = null)
    {
        var normalizedSourcePaths = NormalizeProtectedPaths(protectedSourcePaths);
        return new PdfSharpDocument(pdfDocument, normalizedSourcePaths);
    }

    internal PdfDocument OpenImportSnapshot()
    {
        ThrowIfDisposed();

        using var snapshot = new MemoryStream();
        _pdfDocument.Save(snapshot, false);

        var bytes = snapshot.ToArray();
        return PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
    }

    public Task SaveAsAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var fullOutputPath = Path.GetFullPath(outputPath);

        if (_protectedSourcePaths.Any(path => string.Equals(path, fullOutputPath, _pathComparison)))
        {
            throw new InvalidOperationException("SaveAs must target a different path than the source PDF.");
        }

        var outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var outputStream = File.Create(fullOutputPath);
        _pdfDocument.Save(outputStream, false);
        outputStream.Flush();

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _pdfDocument.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static PdfDocument CloneFromImport(PdfDocument importDocument)
    {
        var clonedDocument = new PdfDocument();
        foreach (var page in importDocument.Pages)
        {
            clonedDocument.AddPage(page);
        }

        clonedDocument.Info.Title = importDocument.Info.Title;
        clonedDocument.Info.Author = importDocument.Info.Author;
        clonedDocument.Info.Subject = importDocument.Info.Subject;
        clonedDocument.Info.Keywords = importDocument.Info.Keywords;

        return clonedDocument;
    }

    private static IReadOnlyCollection<string> NormalizeProtectedPaths(IEnumerable<string>? protectedSourcePaths)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var normalized = new HashSet<string>(comparer);

        if (protectedSourcePaths is null)
        {
            return normalized;
        }

        foreach (var sourcePath in protectedSourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                continue;
            }

            normalized.Add(Path.GetFullPath(sourcePath));
        }

        return normalized;
    }
}
