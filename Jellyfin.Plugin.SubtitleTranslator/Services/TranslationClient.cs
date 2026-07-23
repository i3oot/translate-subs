using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.SubtitleTranslator.Configuration;

namespace Jellyfin.Plugin.SubtitleTranslator.Services;

internal sealed class TranslationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;

    public TranslationClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        string sourceLanguage,
        string targetLanguage,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("The configured translation endpoint must be an absolute HTTP or HTTPS URL.");
        }

        var batchSize = Math.Clamp(configuration.BatchSize, 1, 100_000);
        var result = new List<string>(texts.Count);
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(10);

        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var batch = texts.Skip(offset).Take(batchSize).ToArray();
            var translated = string.Equals(configuration.Provider, "OpenAICompatible", StringComparison.OrdinalIgnoreCase)
                ? await TranslateOpenAiAsync(client, endpoint, batch, sourceLanguage, targetLanguage, configuration, cancellationToken).ConfigureAwait(false)
                : await TranslateLibreAsync(client, endpoint, batch, sourceLanguage, targetLanguage, configuration, cancellationToken).ConfigureAwait(false);

            if (translated.Count != batch.Length)
            {
                throw new InvalidOperationException("The translation service returned a different number of captions than requested.");
            }

            result.AddRange(translated);
        }

        return result;
    }

    private static async Task<IReadOnlyList<string>> TranslateLibreAsync(
        HttpClient client,
        Uri endpoint,
        string[] texts,
        string sourceLanguage,
        string targetLanguage,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var uri = endpoint.AbsolutePath.EndsWith("/translate", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : new Uri(endpoint.ToString().TrimEnd('/') + "/translate", UriKind.Absolute);
        var payload = new
        {
            q = texts,
            source = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage,
            target = targetLanguage,
            format = "text",
            api_key = string.IsNullOrWhiteSpace(configuration.ApiKey) ? null : configuration.ApiKey
        };

        using var response = await client.PostAsJsonAsync(uri, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body);

        using var document = JsonDocument.Parse(body);
        var translated = document.RootElement.GetProperty("translatedText");
        return translated.ValueKind == JsonValueKind.Array
            ? translated.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray()
            : [translated.GetString() ?? string.Empty];
    }

    private static async Task<IReadOnlyList<string>> TranslateOpenAiAsync(
        HttpClient client,
        Uri endpoint,
        string[] texts,
        string sourceLanguage,
        string targetLanguage,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var endpointText = endpoint.ToString().TrimEnd('/');
        var uri = endpoint.AbsolutePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : endpoint.AbsolutePath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? new Uri(endpointText + "/chat/completions", UriKind.Absolute)
                : new Uri(endpointText + "/v1/chat/completions", UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        }

        var instruction = $$"""
            You are a subtitle translation engine.

            Translate every caption from {{(string.IsNullOrWhiteSpace(sourceLanguage) ? "an automatically detected source language" : sourceLanguage)}} to {{targetLanguage}}.

            OUTPUT CONTRACT — ALL RULES ARE MANDATORY:
            1. Return only one valid JSON object. Do not use Markdown or code fences.
            2. The object must have exactly one property named "translations" whose value is an array of strings.
            3. The translations array MUST contain exactly {{texts.Length}} elements: one output for every input caption.
            4. Preserve order and index mapping exactly. Output element N must translate input element N.
            5. Never merge, split, omit, reorder, summarize, or add captions, even when adjacent captions form one sentence.
            6. Treat the captions as spoken dialogue, not formal written prose. The translation must sound natural when spoken by a native speaker.
            7. When several translations are correct, choose the most natural conversational wording for the context, speaker intent, emotion, politeness, and register. Avoid stiff or unnecessarily literal phrasing.
            8. Use surrounding captions to resolve ambiguous or fragmented speech, but keep every caption in its original array position. Do not invent meaning, slang, or details unsupported by the source.
            9. Preserve line breaks and inline subtitle markup such as <i>, <b>, and speaker prefixes.
            10. Return an empty string for an empty input caption; never remove its array position.
            11. Escape all strings as valid JSON.

            FORMAT EXAMPLE ONLY:
            Input: {"captionCount":2,"captions":["Hello!","<i>Wait.</i>\nCome back."]}
            Output: {"translations":["Hallo!","<i>Warte.</i>\nKomm zurück."]}

            Before responding, verify that translations.length equals {{texts.Length}}.
            """;
        request.Content = JsonContent.Create(new
        {
            model = configuration.Model,
            temperature = 0,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = instruction },
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(new { captionCount = texts.Length, captions = texts }, JsonOptions)
                }
            }
        }, options: JsonOptions);

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body);

        var envelope = JsonSerializer.Deserialize<ChatResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("The translation service returned an empty response.");
        var content = envelope.Choices.FirstOrDefault()?.Message.Content
            ?? throw new InvalidOperationException("The translation service returned no translated captions.");
        var translated = JsonSerializer.Deserialize<TranslationEnvelope>(content, JsonOptions);
        return translated?.Translations
            ?? throw new InvalidOperationException("The translation service response did not contain a translations array.");
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = body.Length > 500 ? body[..500] : body;
            throw new HttpRequestException($"Translation service returned {(int)response.StatusCode}: {detail}");
        }
    }

    private sealed record TranslationEnvelope([property: JsonPropertyName("translations")] string[] Translations);

    private sealed record ChatResponse([property: JsonPropertyName("choices")] ChatChoice[] Choices);

    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatMessage Message);

    private sealed record ChatMessage([property: JsonPropertyName("content")] string Content);
}
