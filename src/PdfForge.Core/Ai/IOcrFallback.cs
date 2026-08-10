using PdfForge.Core.PdfEngine;

namespace PdfForge.Core.Ai;

public interface IOcrFallback
{
    Task<string?> TryOcrAsync(
        PageRenderResult renderedPage,
        CancellationToken cancellationToken = default);
}
