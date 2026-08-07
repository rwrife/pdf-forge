namespace PdfForge.Core.PdfEngine;

public abstract record PdfSplitMode
{
    private PdfSplitMode()
    {
    }

    public sealed record EveryNPages(int PagesPerSplit) : PdfSplitMode;

    public sealed record OnePerPage : PdfSplitMode;

    public sealed record PageRanges(IReadOnlyList<PdfPageRange> Ranges) : PdfSplitMode;
}

public sealed record PdfPageRange(int StartPageNumber, int EndPageNumber);
