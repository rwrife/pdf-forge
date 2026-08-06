namespace PdfForge.Core.PdfEngine;

public sealed record PageRenderResult(
    int Width,
    int Height,
    string MimeType,
    byte[] ImageBytes);
