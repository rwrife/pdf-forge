using PdfForge.Core;
using Xunit;

namespace PdfForge.Core.Tests;

public class CoreHealthTests
{
    [Fact]
    public void GetVersionBanner_ReturnsExpectedText()
    {
        Assert.Equal("PdfForge.Core ready", CoreHealth.GetVersionBanner());
    }
}
