using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PdfForge.Core.PdfEngine;

public sealed class PdfSharpDocument : IPdfDocument
{
    private readonly byte[] _sourceBytes;
    private readonly PdfDocument _pdfDocument;
    private bool _disposed;

    private PdfSharpDocument(string sourcePath, byte[] sourceBytes, PdfDocument pdfDocument)
    {
        SourcePath = sourcePath;
        _sourceBytes = sourceBytes;
        _pdfDocument = pdfDocument;
    }

    public string SourcePath { get; }

    public int PageCount
    {
        get
        {
            ThrowIfDisposed();
            return _pdfDocument.PageCount;
        }
    }

    public static async Task<PdfSharpDocument> OpenAsync(string sourcePath, CancellationToken cancellationToken = default)
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

        var sourceBytes = await File.ReadAllBytesAsync(fullSourcePath, cancellationToken).ConfigureAwait(false);
        var pdfDocument = PdfReader.Open(fullSourcePath, PdfDocumentOpenMode.Import);

        return new PdfSharpDocument(fullSourcePath, sourceBytes, pdfDocument);
    }

    public Task SaveAsAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(SourcePath, fullOutputPath, pathComparison))
        {
            throw new InvalidOperationException("SaveAs must target a different path than the source PDF.");
        }

        var outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        return File.WriteAllBytesAsync(fullOutputPath, _sourceBytes, cancellationToken);
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
}
