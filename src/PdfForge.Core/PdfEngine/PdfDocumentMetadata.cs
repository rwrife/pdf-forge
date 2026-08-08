namespace PdfForge.Core.PdfEngine;

public sealed record PdfDocumentMetadata(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords)
{
    public static PdfDocumentMetadata Empty { get; } = new(null, null, null, null);
}
