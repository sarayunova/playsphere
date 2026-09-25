using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlaySphere.Application.AI;
using PlaySphere.Domain.AI;

namespace PlaySphere.AI;

/// <summary>
/// OpenAI-backed implementation of the <see cref="IAiMediaService"/> boundary (PICKLE.md §38-§39).
/// All provider HTTP access lives here and only here; the API key is resolved from the injected
/// <see cref="IAiCredentialStore"/> (Windows secure storage, §40) and is never persisted or logged.
/// Every call is cancellable and time-boxed; failures surface as <see cref="AiException"/>.
///
/// The service is transport-agnostic over the OpenAI REST protocol (chat/completions and
/// audio/transcriptions) so it can be exercised fully headlessly via an injected
/// <see cref="HttpMessageHandler"/>.
/// </summary>
public sealed class OpenAiMediaService : IAiMediaService
{
    private readonly HttpClient _http;
    private readonly IAiCredentialStore _credentials;
    private readonly AiServiceOptions _options;
    private readonly ILogger<OpenAiMediaService>? _logger;

    public OpenAiMediaService(
        HttpClient http,
        IAiCredentialStore credentials,
        AiServiceOptions options,
        ILogger<OpenAiMediaService>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    private Uri BaseUrl => _options.BaseUrl ?? new Uri("https://api.openai.com/");

    private string ChatModel => string.IsNullOrWhiteSpace(_options.ChatModel) ? "gpt-4o-mini" : _options.ChatModel;

    private string TranscriptionModel => string.IsNullOrWhiteSpace(_options.TranscriptionModel) ? "whisper-1" : _options.TranscriptionModel;

    private TimeSpan Timeout => _options.Timeout ?? TimeSpan.FromMinutes(5);

    public async Task<Transcript> TranscribeAudioAsync(string audioFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath))
        {
            throw new AiException("A local audio file path is required to transcribe.");
        }

        if (!File.Exists(audioFilePath))
        {
            throw new AiException($"Audio file not found: {audioFilePath}");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(Timeout);
        var ct = linkedCts.Token;

        var uri = AppendPath(BaseUrl, "v1/audio/transcriptions");
        using var content = new MultipartFormDataContent("PlaySphereBoundary");

        var fileName = Path.GetFileName(audioFilePath);
        var fileBytes = await File.ReadAllBytesAsync(audioFilePath, ct).ConfigureAwait(false);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        content.Add(fileContent, "file", fileName);
        AddFormField(content, "model", TranscriptionModel);
        AddFormField(content, "response_format", "verbose_json");
        AddFormField(content, "timestamp_granularities[]", "segment");

        using var request = BuildRequest(HttpMethod.Post, uri, content);
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);
        ThrowForNonSuccess(response, body);

        return ParseTranscription(body);
    }

    public Task<IReadOnlyList<AiChapter>> GenerateChaptersAsync(Transcript transcript, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var prompt = new StringBuilder("You are a media chapterer. From the transcript below, produce a JSON array of chapters. ")
            .Append("Each chapter MUST have exactly two fields: \"title\" (string) and \"start\" (number, the transcript-provided start second). ")
            .Append("Never invent timestamps not present verbatim in the transcript. ")
            .Append("Return ONLY a JSON array, for example: [{\"title\":\"Intro\",\"start\":0}].\n\nTranscript:\n")
            .Append(FormatTranscript(transcript))
            .ToString();
        return ChatJsonAsync(prompt, ParseChapters, cancellationToken);
    }

    public Task<AiSummary> SummarizeAsync(Transcript transcript, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var prompt = new StringBuilder("Summarize the following transcript in 3-6 concise sentences. ")
            .Append("Return plain text only, no preamble. To verify claims against timestamps, note the second numbers.\n\nTranscript:\n")
            .Append(FormatTranscript(transcript))
            .ToString();
        return ChatTextAsync(prompt, AiSummary.FromText, cancellationToken);
    }

    public Task<IReadOnlyList<AiKeyPoint>> AskAsync(Transcript transcript, string question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new AiException("A question is required to ask the media.");
        }

        var prompt = new StringBuilder("Answer the question using ONLY the transcript below. ")
            .Append("Produce a JSON array of findings. Each finding MUST have \"text\" (string answer) and optionally ")
            .Append("\"start\" (number, the verbatim transcript second supporting the claim). ")
            .Append("Do NOT include a finding if the transcript does not support it. ")
            .Append("Return ONLY a JSON array, for example: [{\"text\":\"...\",\"start\":12.3}].\n\nQuestion: ")
            .Append(question)
            .Append("\n\nTranscript:\n")
            .Append(FormatTranscript(transcript))
            .ToString();
        return ChatJsonAsync(prompt, ParseKeyPoints, cancellationToken);
    }

    public Task<IReadOnlyList<TranscriptSegment>> TranslateAsync(Transcript transcript, string targetLanguage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            throw new AiException("A target language is required to translate.");
        }

        var prompt = new StringBuilder("Translate the following transcript into ")
            .Append(targetLanguage.Trim())
            .Append(". Produce a JSON array of segments. Each segment MUST have exactly three fields: ")
            .Append("\"start\" (number, the EXACT original start second, copied verbatim), ")
            .Append("\"end\" (number, the EXACT original end second, copied verbatim), and ")
            .Append("\"text\" (string, the translated text only). ")
            .Append("Never invent, shift or renumber timestamps — one output segment per input segment, in order. ")
            .Append("Return ONLY a JSON array, for example: [{\"start\":0,\"end\":2.5,\"text\":\"...\"}].\n\nTranscript:\n")
            .Append(FormatTranscript(transcript))
            .ToString();
        return ChatJsonAsync(prompt, ParseTranslatedSegments, cancellationToken);
    }

    private async Task<T> ChatJsonAsync<T>(string prompt, Func<string, T> parser, CancellationToken cancellationToken)
    {
        var text = await ChatTextInternalAsync(prompt, cancellationToken).ConfigureAwait(false);
        return parser(text);
    }

    private async Task<T> ChatTextAsync<T>(string prompt, Func<string, T> parser, CancellationToken cancellationToken)
    {
        var text = await ChatTextInternalAsync(prompt, cancellationToken).ConfigureAwait(false);
        return parser(text);
    }

    private async Task<string> ChatTextInternalAsync(string prompt, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(Timeout);
        var ct = linkedCts.Token;

        var payload = new Dictionary<string, object>
        {
            ["model"] = ChatModel,
            ["messages"] = new object[]
            {
                new Dictionary<string, string> { ["role"] = "system", ["content"] = "You are a helpful media-analysis assistant. Reply only with the requested format." },
                new Dictionary<string, string> { ["role"] = "user", ["content"] = prompt },
            },
            ["temperature"] = 0.2,
        };

        var uri = AppendPath(BaseUrl, "v1/chat/completions");
        using var request = BuildRequest(HttpMethod.Post, uri, JsonContent(payload));
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);
        ThrowForNonSuccess(response, body);

        return ParseChatContent(body);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var key = _credentials.GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            // Do not create/send the request at all when no key is configured.
            request.Dispose();
            throw new AiException("No OpenAI API key configured. Add one in Settings before using AI features.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        _logger?.LogDebug("AI request {Method} {Url}", request.Method, request.RequestUri?.PathAndQuery);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, uri);
        if (content is not null)
        {
            request.Content = content;
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static void AddFormField(MultipartFormDataContent form, string name, string value)
    {
        form.Add(new StringContent(value ?? string.Empty), name);
    }

    private static Uri AppendPath(Uri baseUri, string path)
    {
        var baseUrl = baseUri.ToString();
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }
        return new Uri(new Uri(baseUrl), path);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

    private static void ThrowForNonSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new AiException($"OpenAI request failed ({response.StatusCode}): {Truncate(body, 400)}");
    }

    private static string Truncate(string value, int max)
    {
        if (value is null)
        {
            return string.Empty;
        }
        return value.Length <= max ? value : value[..max];
    }

    private static string FormatTranscript(Transcript transcript)
    {
        var sb = new StringBuilder();
        foreach (var segment in transcript.Segments)
        {
            sb.Append('[')
              .Append(segment.StartSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
              .Append(']')
              .Append(' ')
              .Append(segment.Text)
              .Append('\n');
        }
        return sb.ToString();
    }

    private static Transcript ParseTranscription(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? language = null;
            if (root.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String)
            {
                language = lang.GetString();
            }

            var segments = new List<TranscriptSegment>();
            if (root.TryGetProperty("segments", out var segs) && segs.ValueKind == JsonValueKind.Array)
            {
                foreach (var seg in segs.EnumerateArray())
                {
                    var start = GetDouble(seg, "start", 0);
                    var end = GetDouble(seg, "end", start);
                    var text = GetString(seg, "text", string.Empty);
                    segments.Add(new TranscriptSegment(start, end, text));
                }
            }

            if (segments.Count == 0)
            {
                // Fall back to the flat "text" field when no verbose segments are returned.
                var text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() ?? string.Empty
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(new TranscriptSegment(0, 0, text.Trim()));
                }
            }

            return new Transcript(segments, language);
        }
        catch (JsonException ex)
        {
            throw new AiException("Malformed transcription response from provider.", ex);
        }
    }

    private static string ParseChatContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                throw new AiException("Provider returned no chat choices.");
            }

            var first = choices[0];
            if (first.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }

            throw new AiException("Provider chat response had no message content.");
        }
        catch (JsonException ex)
        {
            throw new AiException("Malformed chat response from provider.", ex);
        }
    }

    private static IReadOnlyList<AiChapter> ParseChapters(string text)
    {
        var chapters = new List<AiChapter>();
        foreach (var item in ParseJsonArray(text))
        {
            var title = GetString(item, "title", string.Empty);
            var start = GetDouble(item, "start", 0);
            if (!string.IsNullOrWhiteSpace(title))
            {
                chapters.Add(new AiChapter(title.Trim(), start));
            }
        }
        return chapters;
    }

    private static IReadOnlyList<AiKeyPoint> ParseKeyPoints(string text)
    {
        var points = new List<AiKeyPoint>();
        foreach (var item in ParseJsonArray(text))
        {
            var content = GetString(item, "text", string.Empty);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }
            double? start = null;
            if (item.TryGetProperty("start", out var s) && s.ValueKind == JsonValueKind.Number)
            {
                start = s.GetDouble();
            }
            points.Add(new AiKeyPoint(content.Trim(), start));
        }
        return points;
    }

    private static IReadOnlyList<TranscriptSegment> ParseTranslatedSegments(string text)
    {
        var segments = new List<TranscriptSegment>();
        foreach (var item in ParseJsonArray(text))
        {
            var translated = GetString(item, "text", string.Empty);
            if (string.IsNullOrWhiteSpace(translated))
            {
                continue;
            }
            var start = GetDouble(item, "start", 0);
            var end = GetDouble(item, "end", start);
            segments.Add(new TranscriptSegment(start, end, translated.Trim()));
        }
        return segments;
    }

    private static List<JsonElement> ParseJsonArray(string text)
    {
        // The model is asked to return ONLY a JSON array, but may wrap it in ```json fences or
        // stray prose. Extract the first balanced [...] block defensively (untrusted input, §43).
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        List<JsonElement> items = new();
        if (start < 0 || end <= start)
        {
            return items;
        }

        try
        {
            using var doc = JsonDocument.Parse(text.Substring(start, end - start + 1));
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    items.Add(el.Clone());
                }
            }
        }
        catch (JsonException)
        {
            // Return whatever was parsed before the malformed block (or empty).
        }
        return items;
    }

    private static string GetString(JsonElement el, string name, string defaultValue)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

    private static double GetDouble(JsonElement el, string name, double defaultValue)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
        {
            return v.GetDouble();
        }
        return defaultValue;
    }
}
