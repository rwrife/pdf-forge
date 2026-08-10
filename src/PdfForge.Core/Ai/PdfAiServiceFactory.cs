namespace PdfForge.Core.Ai;

public static class PdfAiServiceFactory
{
    public static IPdfAiService Create(
        PdfAiSettings settings,
        HttpClient? httpClient = null,
        IOcrFallback? ocrFallback = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return new DisabledPdfAiService(settings);
        }

        return new LocalOpenAiPdfAiService(settings, httpClient, ocrFallback);
    }
}
