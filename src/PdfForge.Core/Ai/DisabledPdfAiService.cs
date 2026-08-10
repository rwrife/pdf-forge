using PdfForge.Core.PdfEngine;

namespace PdfForge.Core.Ai;

public sealed class DisabledPdfAiService : IPdfAiService
{
    private readonly PdfAiSettings _settings;

    public DisabledPdfAiService(PdfAiSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public Task<PdfAiAvailability> ProbeAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var status = _settings.Enabled
            ? new PdfAiAvailability(true, false, "Local AI endpoint is unavailable.")
            : PdfAiAvailability.Disabled();

        return Task.FromResult(status);
    }

    public Task<OcrPageResult> OcrPageAsync(PageRenderResult renderedPage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Local AI is disabled. Enable it in settings before running OCR.");
    }

    public Task<string> SummarizeAsync(string extractedText, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Local AI is disabled. Enable it in settings before summarizing.");
    }
}
