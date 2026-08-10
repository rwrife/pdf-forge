using PdfForge.Core.PdfEngine;

namespace PdfForge.Core.Ai;

public interface IPdfAiService
{
    Task<PdfAiAvailability> ProbeAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<OcrPageResult> OcrPageAsync(
        PageRenderResult renderedPage,
        CancellationToken cancellationToken = default);

    Task<string> SummarizeAsync(
        string extractedText,
        CancellationToken cancellationToken = default);
}
