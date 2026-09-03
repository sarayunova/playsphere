using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PlaySphere.AI;
using PlaySphere.Application.AI;
using PlaySphere.Domain.AI;
using Xunit;

namespace PlaySphere.UnitTests.AI;

public class OpenAiMediaServiceTests
{
    private static OpenAiMediaService Create(HttpMessageHandler handler, IAiCredentialStore? store = null)
    {
        var creds = store ?? new MemoryCredentialStore();
        if (store is null)
        {
            creds.SetApiKey("sk-test");
        }
        return new OpenAiMediaService(
            new HttpClient(handler),
            creds,
            new AiServiceOptions { BaseUrl = new Uri("https://ai.test/") });
    }

    [Fact]
    public async Task TranscribeAudioAsync_parses_verbose_segments()
    {
        var handler = ScriptedHttpHandler.Json(
            """
            {"language":"en","segments":[
              {"start":0.0,"end":2.5,"text":"Hello world."},
              {"start":2.5,"end":5.0,"text":"Second sentence."}
            ]}
            """);
        var svc = Create(handler);

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 }).ConfigureAwait(false);
            var transcript = await svc.TranscribeAudioAsync(path).ConfigureAwait(false);

            Assert.False(transcript.IsEmpty);
            Assert.Equal("en", transcript.Language);
            Assert.Equal(2, transcript.Segments.Count);
            Assert.Equal("Hello world.", transcript.Segments[0].Text);
            Assert.Equal(0.0, transcript.Segments[0].StartSeconds);
            Assert.Equal(5.0, transcript.Segments[1].EndSeconds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TranscribeAudioAsync_auth_header_uses_bearer_key()
    {
        var handler = ScriptedHttpHandler.Json("{\"language\":\"en\",\"segments\":[]}");
        var svc = Create(handler);

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 9 }).ConfigureAwait(false);
            await svc.TranscribeAudioAsync(path).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(path);
        }

        var auth = handler.LastRequest!.Headers.Authorization!;
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal("sk-test", auth.Parameter);
    }

    [Fact]
    public async Task TranscribeAudioAsync_missing_file_throws_AiException()
    {
        var svc = Create(ScriptedHttpHandler.Json("{}"));
        await Assert.ThrowsAsync<AiException>(() => svc.TranscribeAudioAsync(@"C:\nonexistent\missing.mp3"));
    }

    [Fact]
    public async Task TranscribeAudioAsync_http_error_throws_AiException()
    {
        var handler = ScriptedHttpHandler.Json("{\"error\":{\"message\":\"bad key\"}}", HttpStatusCode.Unauthorized);
        var svc = Create(handler);

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1 }).ConfigureAwait(false);
            await Assert.ThrowsAsync<AiException>(() => svc.TranscribeAudioAsync(path)).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task No_api_key_throws_AiException_without_sending_request()
    {
        var handler = ScriptedHttpHandler.Json("{}");
        var svc = Create(handler, new MemoryCredentialStore()); // no key set

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1 }).ConfigureAwait(false);
            var ex = await Assert.ThrowsAsync<AiException>(() => svc.TranscribeAudioAsync(path)).ConfigureAwait(false);
            Assert.Contains("API key", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(handler.LastRequest); // the request must not have been sent
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GenerateChaptersAsync_parses_chapters()
    {
        var handler = ScriptedHttpHandler.Json(
            """{"choices":[{"message":{"role":"assistant","content":"[{\"title\":\"Intro\",\"start\":0},{\"title\":\"Middle\",\"start\":12.5}]"}}]}""");
        var svc = Create(handler);

        var transcript = new Transcript(
            new[] { new TranscriptSegment(0, 10, "a"), new TranscriptSegment(10, 20, "b") }, "en");

        var chapters = await svc.GenerateChaptersAsync(transcript).ConfigureAwait(false);

        Assert.Equal(2, chapters.Count);
        Assert.Equal("Intro", chapters[0].Title);
        Assert.Equal(0, chapters[0].StartSeconds);
        Assert.Equal(12.5, chapters[1].StartSeconds);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith("/v1/chat/completions", handler.LastRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task GenerateChaptersAsync_ignores_malformed_entries()
    {
        // A valid object plus one missing "title" -> only the valid chapter is kept.
        var handler = ScriptedHttpHandler.Json(
            """{"choices":[{"message":{"role":"assistant","content":"[{\"start\":5},{\"title\":\"OK\",\"start\":5}]"}}]}""");
        var svc = Create(handler);

        var chapters = await svc.GenerateChaptersAsync(new Transcript(Array.Empty<TranscriptSegment>(), null)).ConfigureAwait(false);

        var chapter = Assert.Single(chapters);
        Assert.Equal("OK", chapter.Title);
    }

    [Fact]
    public async Task SummarizeAsync_returns_text()
    {
        var handler = ScriptedHttpHandler.Json(
            """{"choices":[{"message":{"role":"assistant","content":"This is a three word summary."}}]}""");
        var svc = Create(handler);

        var summary = await svc.SummarizeAsync(new Transcript(Array.Empty<TranscriptSegment>(), null)).ConfigureAwait(false);

        Assert.Equal("This is a three word summary.", summary.Text);
        Assert.Equal(6, summary.EstimatedWordCount);
    }

    [Fact]
    public async Task AskAsync_parses_findings_with_timestamps()
    {
        var handler = ScriptedHttpHandler.Json(
            """{"choices":[{"message":{"role":"assistant","content":"[{\"text\":\"Launch in 2026.\",\"start\":44.0}]"}}]}""");
        var svc = Create(handler);

        var points = await svc.AskAsync(new Transcript(Array.Empty<TranscriptSegment>(), null), "When was launch?").ConfigureAwait(false);

        var point = Assert.Single(points);
        Assert.Equal("Launch in 2026.", point.Text);
        Assert.Equal(44.0, point.StartSeconds);
    }

    [Fact]
    public async Task AskAsync_empty_question_throws()
    {
        var svc = Create(ScriptedHttpHandler.Json("{}"));
        await Assert.ThrowsAsync<AiException>(
            () => svc.AskAsync(new Transcript(Array.Empty<TranscriptSegment>(), null), "   "));
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        var svc = Create(HangingHttpHandler.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.SummarizeAsync(new Transcript(Array.Empty<TranscriptSegment>(), null), cts.Token)).ConfigureAwait(false);
    }
}
