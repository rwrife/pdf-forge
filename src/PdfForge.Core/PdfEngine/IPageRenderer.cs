namespace PdfForge.Core.PdfEngine;

public interface IPageRenderer
{
    Task<PageRenderResult> RenderPageAsync(
        IPdfDocument document,
        int pageNumber,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken = default);
}
