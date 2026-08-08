namespace PdfForge.Core.PdfEngine;

public sealed record PdfCompressionSettings(
    int TargetDpi = 150,
    int JpegQuality = 65)
{
    public static PdfCompressionSettings Default { get; } = new();

    public void Validate()
    {
        if (TargetDpi is < 36 or > 1200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TargetDpi),
                TargetDpi,
                "Target DPI must be between 36 and 1200.");
        }

        if (JpegQuality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(JpegQuality),
                JpegQuality,
                "JPEG quality must be between 1 and 100.");
        }
    }
}
