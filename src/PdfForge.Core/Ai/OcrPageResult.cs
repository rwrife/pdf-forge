namespace PdfForge.Core.Ai;

public sealed record OcrPageResult(
    string Text,
    bool UsedRemoteModel,
    bool UsedFallback);
