namespace PdfForge.Core.Ai;

public sealed record PdfAiSettings(
    bool Enabled = false,
    string EndpointUrl = "http://localhost:11434/v1/",
    string OcrModel = "minicpm-v",
    string SummaryModel = "llama3.2:3b")
{
    public static PdfAiSettings Default { get; } = new();
}
