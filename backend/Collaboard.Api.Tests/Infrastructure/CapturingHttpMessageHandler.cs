using System.Collections.Concurrent;
using System.Net;

namespace Collaboard.Api.Tests.Infrastructure;

// A stub HttpMessageHandler that captures every outbound webhook POST (method, URI, headers,
// the EXACT raw body bytes) and returns a programmable response. The Phase 2 webhook delivery
// tests point the dispatcher's typed HttpClient at this so they assert on the precise bytes and
// headers sent — no real socket, no flake. The raw body is captured ONCE per request so the
// HMAC-signature round-trip can run both the signature-check and the body-assert against the
// SAME captured array (D3 — the footgun hides in re-serializing the parsed body). (#320.)
public sealed class CapturingHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<CapturedRequest> _requests = new();

    // The status code each request returns. Default 200 (success). Flip to 500 to exercise retry,
    // or use a hanging delay to exercise timeout. Read on each Send so a test can change it mid-run
    // (e.g. scenario 8: 500 until exhausted, then 200 on a fresh create).
    public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;

    // Optional artificial delay before responding — simulates a slow endpoint for the
    // never-blocks-the-mutation and timeout edge cases.
    public TimeSpan ResponseDelay { get; set; } = TimeSpan.Zero;

    public IReadOnlyList<CapturedRequest> Requests => [.. _requests];

    public int RequestCount => _requests.Count;

    public void Clear() => _requests.Clear();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        // Snapshot the headers (content + request) into a flat lookup the test can read.
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in request.Headers)
        {
            headers[key] = string.Join(',', values);
        }

        if (request.Content is not null)
        {
            foreach (var (key, values) in request.Content.Headers)
            {
                headers[key] = string.Join(',', values);
            }
        }

        _requests.Enqueue(new CapturedRequest(request.Method.Method, request.RequestUri, headers, body));

        if (ResponseDelay > TimeSpan.Zero)
        {
            await Task.Delay(ResponseDelay, cancellationToken);
        }

        return new HttpResponseMessage(ResponseStatusCode);
    }
}

public sealed record CapturedRequest(string Method, Uri? Uri, IReadOnlyDictionary<string, string> Headers, byte[] Body);
