using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PdfForge.Core.PdfEngine;

public sealed class PlaceholderPageRenderer : IPageRenderer
{
    public async Task<PageRenderResult> RenderPageAsync(
        IPdfDocument document,
        int pageNumber,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (targetWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Target width must be greater than zero.");
        }

        if (targetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetHeight), "Target height must be greater than zero.");
        }

        if (pageNumber < 1 || pageNumber > document.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page number is out of range.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var image = new Image<Rgba32>(targetWidth, targetHeight, Color.White);

        await using var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream, cancellationToken).ConfigureAwait(false);

        return new PageRenderResult(targetWidth, targetHeight, "image/png", stream.ToArray());
    }
}
