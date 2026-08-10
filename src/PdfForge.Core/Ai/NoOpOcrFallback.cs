using PdfForge.Core.PdfEngine;

namespace PdfForge.Core.Ai;

public sealed class NoOpOcrFallback : IOcrFallback
{
    public static NoOpOcrFallback Instance { get; } = new();

    public Task<string?> TryOcrAsync(
        PageRenderResult renderedPage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderedPage);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
