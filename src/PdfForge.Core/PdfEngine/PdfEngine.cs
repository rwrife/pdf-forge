namespace PdfForge.Core.PdfEngine;

public sealed class PdfEngine
{
    public PdfEngine(IPageRenderer? pageRenderer = null)
    {
        PageRenderer = pageRenderer ?? new PlaceholderPageRenderer();
    }

    public IPageRenderer PageRenderer { get; }

    public async Task<IPdfDocument> OpenAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var document = await PdfSharpDocument.OpenAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return document;
    }

    public Task<PageRenderResult> RenderPageAsync(
        IPdfDocument document,
        int pageNumber,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken = default)
    {
        return PageRenderer.RenderPageAsync(document, pageNumber, targetWidth, targetHeight, cancellationToken);
    }
}
