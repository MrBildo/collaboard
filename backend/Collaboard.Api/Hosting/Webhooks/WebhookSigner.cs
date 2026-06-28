using System.Security.Cryptography;

namespace Collaboard.Api.Hosting.Webhooks;

// HMAC-SHA256 signing of a webhook body. Pure function — no DbContext, no DB —
// so it is unit-testable directly (the project's pure-function carve-out). The input is the
// EXACT raw body bytes the dispatcher writes to the request stream; the dispatcher serializes
// once and signs that same byte array, never re-serializing (the classic HMAC footgun). The
// header value is GitHub's shape: `sha256=<hex-lowercase-digest>` — the `sha256=` prefix is
// algorithm-agile so a future scheme can send `sha512=...` without changing the header name.
internal static class WebhookSigner
{
    public const string SignaturePrefix = "sha256=";

    public static string Sign(byte[] body, string secret)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(secret);

        var key = System.Text.Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(key, body);

        return SignaturePrefix + Convert.ToHexStringLower(hash);
    }
}
