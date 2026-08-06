namespace PdfForge.Core.PdfEngine;

public interface IPdfDocument : IDisposable, IAsyncDisposable
{
    string SourcePath { get; }

    int PageCount { get; }

    Task SaveAsAsync(string outputPath, CancellationToken cancellationToken = default);
}
