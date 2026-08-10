using System.Net;
using System.Net.Http;
using System.Text;
using PdfForge.Core.Ai;
using PdfForge.Core.PdfEngine;
using Xunit;

namespace PdfForge.Core.Tests;

public class PdfAiServiceTests
{
    [Fact]
    public async Task DisabledService_DoesNotCallNetwork_AndReportsUnavailable()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/v1/")
        };

        var service = PdfAiServiceFactory.Create(new PdfAiSettings(Enabled: false), httpClient);

        var availability = await service.ProbeAvailabilityAsync();

        Assert.False(availability.EnabledByUser);
        Assert.False(availability.EndpointReachable);
        Assert.False(availability.CanUseAi);
        Assert.Equal(0, handler.CallCount);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SummarizeAsync("hello world"));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ProbeAvailabilityAsync_ReturnsDown_WhenEndpointThrows()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/v1/")
        };

        using var service = new LocalOpenAiPdfAiService(
            new PdfAiSettings(Enabled: true),
            httpClient,
            NoOpOcrFallback.Instance);

        var availability = await service.ProbeAvailabilityAsync();

        Assert.True(availability.EnabledByUser);
        Assert.False(availability.EndpointReachable);
        Assert.False(availability.CanUseAi);
        Assert.Contains("unavailable", availability.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAvailabilityAsync_ReturnsUp_WhenModelsEndpointIsHealthy()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/v1/")
        };

        using var service = new LocalOpenAiPdfAiService(
            new PdfAiSettings(Enabled: true),
            httpClient,
            NoOpOcrFallback.Instance);

        var availability = await service.ProbeAvailabilityAsync();

        Assert.True(availability.EnabledByUser);
        Assert.True(availability.EndpointReachable);
        Assert.True(availability.CanUseAi);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("/v1/models", handler.Requests[0].RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task SummarizeAsync_ReturnsAssistantText()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"content\":\"Short local summary.\"}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/v1/")
        };

        using var service = new LocalOpenAiPdfAiService(
            new PdfAiSettings(Enabled: true, SummaryModel: "phi3-mini"),
            httpClient,
            NoOpOcrFallback.Instance);

        var summary = await service.SummarizeAsync("Long text that should be summarized.");

        Assert.Equal("Short local summary.", summary);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("/v1/chat/completions", handler.Requests[0].RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task OcrPageAsync_UsesFallback_WhenRemoteModelFails()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("offline"));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/v1/")
        };

        using var service = new LocalOpenAiPdfAiService(
            new PdfAiSettings(Enabled: true),
            httpClient,
            new StaticOcrFallback("fallback ocr text"));

        var page = new PageRenderResult(
            Width: 100,
            Height: 100,
            MimeType: "image/png",
            ImageBytes: new byte[] { 1, 2, 3, 4 });

        var ocr = await service.OcrPageAsync(page);

        Assert.Equal("fallback ocr text", ocr.Text);
        Assert.False(ocr.UsedRemoteModel);
        Assert.True(ocr.UsedFallback);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Constructor_RejectsNonLocalEndpoint()
    {
        using var httpClient = new HttpClient();

        Assert.Throws<ArgumentException>(() =>
            new LocalOpenAiPdfAiService(
                new PdfAiSettings(
                    Enabled: true,
                    EndpointUrl: "https://api.openai.com/v1/",
                    OcrModel: "minicpm-v",
                    SummaryModel: "phi3-mini"),
                httpClient,
                NoOpOcrFallback.Instance));
    }

    private sealed class StaticOcrFallback(string text) : IOcrFallback
    {
        public Task<string?> TryOcrAsync(PageRenderResult renderedPage, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(text);
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        public int CallCount { get; private set; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Requests.Add(request);
            return Task.FromResult(_handler(request));
        }
    }
}
