using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PdfForge.Core.PdfEngine;

namespace PdfForge.Core.Ai;

public sealed class LocalOpenAiPdfAiService : IPdfAiService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PdfAiSettings _settings;
    private readonly Uri _endpointBaseUri;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IOcrFallback _ocrFallback;

    public LocalOpenAiPdfAiService(
        PdfAiSettings settings,
        HttpClient? httpClient = null,
        IOcrFallback? ocrFallback = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        if (!_settings.Enabled)
        {
            throw new ArgumentException("Local AI must be enabled in settings before creating this service.", nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(_settings.OcrModel))
        {
            throw new ArgumentException("An OCR model name is required.", nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(_settings.SummaryModel))
        {
            throw new ArgumentException("A summary model name is required.", nameof(settings));
        }

        if (!Uri.TryCreate(_settings.EndpointUrl, UriKind.Absolute, out var endpointBaseUri))
        {
            throw new ArgumentException("Endpoint URL must be a valid absolute URI.", nameof(settings));
        }

        ValidateLocalEndpoint(endpointBaseUri);
        _endpointBaseUri = EnsureTrailingSlash(endpointBaseUri);

        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = _endpointBaseUri,
                Timeout = TimeSpan.FromSeconds(20)
            };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;

            if (_httpClient.BaseAddress is null)
            {
                _httpClient.BaseAddress = _endpointBaseUri;
            }
        }

        _ocrFallback = ocrFallback ?? NoOpOcrFallback.Instance;
    }

    public async Task<PdfAiAvailability> ProbeAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "models");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new PdfAiAvailability(
                    EnabledByUser: true,
                    EndpointReachable: true,
                    Message: $"Local AI endpoint reachable at {_endpointBaseUri}.");
            }

            return new PdfAiAvailability(
                EnabledByUser: true,
                EndpointReachable: false,
                Message: $"Local AI endpoint returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PdfAiAvailability(
                EnabledByUser: true,
                EndpointReachable: false,
                Message: "Local AI endpoint probe timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new PdfAiAvailability(
                EnabledByUser: true,
                EndpointReachable: false,
                Message: $"Local AI endpoint unavailable: {ex.Message}");
        }
    }

    public async Task<OcrPageResult> OcrPageAsync(
        PageRenderResult renderedPage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderedPage);

        if (renderedPage.ImageBytes is null || renderedPage.ImageBytes.Length == 0)
        {
            throw new ArgumentException("Rendered page bytes are required for OCR.", nameof(renderedPage));
        }

        Exception? remoteFailure = null;
        try
        {
            var remoteText = await ExecuteVisionOcrAsync(renderedPage, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(remoteText))
            {
                return new OcrPageResult(remoteText.Trim(), UsedRemoteModel: true, UsedFallback: false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            remoteFailure = ex;
        }

        var fallbackText = await _ocrFallback.TryOcrAsync(renderedPage, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(fallbackText))
        {
            return new OcrPageResult(fallbackText.Trim(), UsedRemoteModel: false, UsedFallback: true);
        }

        if (remoteFailure is not null)
        {
            throw new InvalidOperationException("OCR failed against local endpoint and no fallback produced text.", remoteFailure);
        }

        throw new InvalidOperationException("OCR did not return text and no fallback produced text.");
    }

    public async Task<string> SummarizeAsync(
        string extractedText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(extractedText))
        {
            throw new ArgumentException("Extracted text is required for summarization.", nameof(extractedText));
        }

        var payload = new
        {
            model = _settings.SummaryModel,
            temperature = 0.2,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You summarize PDF content. Return concise plaintext only."
                },
                new
                {
                    role = "user",
                    content = $"Summarize the following PDF content:\n\n{extractedText}"
                }
            }
        };

        var responseJson = await SendChatCompletionAsync(payload, cancellationToken).ConfigureAwait(false);
        var summary = ParseAssistantContent(responseJson);

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException("Local summary model returned an empty response.");
        }

        return summary.Trim();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<string> ExecuteVisionOcrAsync(
        PageRenderResult renderedPage,
        CancellationToken cancellationToken)
    {
        var normalizedMimeType = string.IsNullOrWhiteSpace(renderedPage.MimeType)
            ? "image/png"
            : renderedPage.MimeType.Trim();

        var base64Image = Convert.ToBase64String(renderedPage.ImageBytes);
        var dataUrl = $"data:{normalizedMimeType};base64,{base64Image}";

        var payload = new
        {
            model = _settings.OcrModel,
            temperature = 0,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are an OCR engine. Return only extracted plaintext from the image."
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = "Extract all readable text from this PDF page image. Return plaintext only."
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = dataUrl
                            }
                        }
                    }
                }
            }
        };

        var responseJson = await SendChatCompletionAsync(payload, cancellationToken).ConfigureAwait(false);
        return ParseAssistantContent(responseJson);
    }

    private async Task<string> SendChatCompletionAsync(object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Local AI endpoint returned HTTP {(int)response.StatusCode}: {responseBody}");
        }

        return responseBody;
    }

    private static string ParseAssistantContent(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);

        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("AI response did not include any choices.");
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content))
        {
            throw new InvalidOperationException("AI response did not include message content.");
        }

        return ParseContentElement(content);
    }

    private static string ParseContentElement(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => ParseArrayContent(content),
            _ => string.Empty
        };
    }

    private static string ParseArrayContent(JsonElement content)
    {
        var parts = new List<string>();

        foreach (var entry in content.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                var value = entry.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value);
                }

                continue;
            }

            if (entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("text", out var textElement))
            {
                var text = textElement.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }
            }
        }

        return string.Join("\n", parts);
    }

    private static Uri EnsureTrailingSlash(Uri baseUri)
    {
        var value = baseUri.ToString();
        if (!value.EndsWith('/', StringComparison.Ordinal))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }

    private static void ValidateLocalEndpoint(Uri endpoint)
    {
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Local AI endpoint URL must use http or https.", nameof(endpoint));
        }

        var host = endpoint.Host;
        var isLocalHost =
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("127.", StringComparison.OrdinalIgnoreCase);

        if (!isLocalHost)
        {
            throw new ArgumentException("Local AI endpoint must target localhost/loopback only.", nameof(endpoint));
        }
    }
}
