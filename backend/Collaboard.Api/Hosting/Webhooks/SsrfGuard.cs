using System.Net;
using System.Net.Sockets;

namespace Collaboard.Api.Hosting.Webhooks;

// SSRF controls for outbound webhook delivery (#326, D3). v1 had no SSRF surface — the single
// endpoint was typed by the human deployer. v2 lets an admin (including a promptable
// AgentAdministrator via MCP) register arbitrary URLs, so the server's outbound connections become
// an attacker-influenceable channel. This is the UNIFORM guard every subscription's deliveries pass
// through — no per-subscription exemption, including the config-migrated seed (D3, the deliberate
// breaking change). The guard is what makes "agents manage webhooks" safe; it is load-bearing FOR
// the locked decision, not optional hardening on top of it.
//
// The 4-control floor (denylist, not allowlist):
//   1. Scheme allowlist — http/https only (reject file:/gopher:/ftp:/...).
//   2. Resolve-and-validate the host's IPs against a blocked-range denylist (loopback, RFC1918,
//      link-local incl. the cloud-metadata 169.254/16, unique-local, unspecified, multicast),
//      unwrapping IPv4-mapped IPv6 first. At registration (fast-fail UX) and at connect (security).
//   3. Connect-time IP pinning — the ConnectCallback resolves, validates EVERY returned IP, and
//      opens the socket to the validated IP, closing the DNS-rebind/TOCTOU window (a host that
//      resolved public at registration cannot rebind to loopback at delivery).
//   4. AllowAutoRedirect = false on the handler (a 302-to-internal would walk around 1-3) — wired
//      in Program.cs alongside the ConnectCallback, not here.
//
// allowPrivate (Webhooks:AllowPrivateNetworkTargets) is the operator override for a legitimately-
// private target (self-hosted n8n on a LAN/Tailscale address). Startup-bound (IOptions), so the
// registration validator and the connect callback read the SAME value — they must agree (#326 S2).
internal static class SsrfGuard
{
    // The pure decision (#326 control 2): is this resolved IP in a blocked range? No DbContext, no
    // DB — the project's pure-function carve-out, table-testable directly.
    public static bool IsBlockedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // Unwrap an IPv4-mapped IPv6 (::ffff:127.0.0.1) so a mapped loopback/private address cannot
        // slip the IPv4 checks below.
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        return IPAddress.IsLoopback(ip) || ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsBlockedV4(ip),
            AddressFamily.InterNetworkV6 => IsBlockedV6(ip),
            // Any other family (AppleTalk, IPX, ...) is not a deliverable internet target — block.
            _ => true,
        };
    }

    // Registration-time validation (#326 controls 1-2): scheme allowlist + resolve-and-deny. Throws
    // WebhookValidationException (a caller-fixable 400, surfaced by the store) on rejection. The
    // public overload resolves via DNS; the internal overload takes an injectable resolver for
    // deterministic tests (a hostname resolving to a private IP, without real DNS).
    public static Task ValidateForRegistrationAsync(string url, bool allowPrivate, CancellationToken ct) =>
        ValidateForRegistrationAsync(url, allowPrivate, DefaultResolveAsync, ct);

    internal static async Task ValidateForRegistrationAsync
    (
        string url,
        bool allowPrivate,
        Func<string, CancellationToken, ValueTask<IPAddress[]>> resolve,
        CancellationToken ct
    )
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new WebhookValidationException($"Webhook URL '{url}' is not a valid absolute URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new WebhookValidationException
            (
                $"Webhook URL scheme '{uri.Scheme}' is not allowed; use http or https."
            );
        }

        if (allowPrivate)
        {
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await resolve(uri.Host, ct);
        }
        catch (SocketException ex)
        {
            throw new WebhookValidationException($"Webhook host '{uri.Host}' could not be resolved.", ex);
        }

        if (addresses.Length == 0)
        {
            throw new WebhookValidationException($"Webhook host '{uri.Host}' resolved to no addresses.");
        }

        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address))
            {
                var blockedAddress = address.ToString();
                throw new WebhookValidationException
                (
                    $"Webhook host '{uri.Host}' resolves to a private or otherwise blocked address ({blockedAddress}); "
                    + "set Webhooks:AllowPrivateNetworkTargets to allow it."
                );
            }
        }
    }

    // The connect-pin core (#326 control 3 + S3). Resolves the host, validates EVERY returned IP,
    // and returns the validated endpoint to dial. Throws WebhookSsrfBlockedException on a blocked
    // address (S3a — the handler wraps it in HttpRequestException → the sender records a Failed
    // attempt). Connecting to the RETURNED IPEndPoint (never re-resolving the hostname) is what
    // closes the rebind window (S3b): the IP we validated is the IP we dial. The resolver is
    // injectable so a test can simulate a rebind (a host resolving to loopback) without real DNS.
    internal static async ValueTask<IPEndPoint> ResolveAndValidateEndpointAsync
    (
        string host,
        int port,
        bool allowPrivate,
        Func<string, CancellationToken, ValueTask<IPAddress[]>> resolve,
        CancellationToken ct
    )
    {
        IPAddress[] addresses;
        try
        {
            addresses = await resolve(host, ct);
        }
        catch (SocketException ex)
        {
            throw new WebhookSsrfBlockedException($"Webhook host '{host}' could not be resolved.", ex);
        }

        if (addresses.Length == 0)
        {
            throw new WebhookSsrfBlockedException($"Webhook host '{host}' resolved to no addresses.");
        }

        if (!allowPrivate)
        {
            foreach (var address in addresses)
            {
                if (IsBlockedAddress(address))
                {
                    var blockedAddress = address.ToString();
                    throw new WebhookSsrfBlockedException
                    (
                        $"Webhook host '{host}' resolves to a blocked address ({blockedAddress})."
                    );
                }
            }
        }

        // Every resolved address passed validation above, or allowPrivate is set. Dial the first by
        // its IP — never by hostname — so the name cannot rebind between this check and connect.
        return new IPEndPoint(addresses[0], port);
    }

    // The production ConnectCallback for the dispatcher's SocketsHttpHandler (#326 control 3).
    // Resolves+validates via DNS, then opens the socket to the validated IPEndPoint.
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>
        CreateConnectCallback(bool allowPrivate) =>
        async (context, ct) =>
        {
            var endpoint = await ResolveAndValidateEndpointAsync
            (
                context.DnsEndPoint.Host,
                context.DnsEndPoint.Port,
                allowPrivate,
                DefaultResolveAsync,
                ct
            );

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(endpoint, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };

    private static async ValueTask<IPAddress[]> DefaultResolveAsync(string host, CancellationToken ct) =>
        await Dns.GetHostAddressesAsync(host, ct);

    private static bool IsBlockedV4(IPAddress ip)
    {
        var b = ip.GetAddressBytes();   // 4 bytes, network order

        // Blocked v4 ranges: 0.0.0.0/8 unspecified, 10.0.0.0/8 and 127.0.0.0/8, the 169.254.0.0/16
        // link-local range that carries cloud metadata at 169.254.169.254, 172.16.0.0 through
        // 172.31.255.255, 192.168.0.0/16, and anything at or above 224.0.0.0 for multicast,
        // reserved, and broadcast.
        return b[0] is 0 or 10 or 127
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || b[0] >= 224;
    }

    private static bool IsBlockedV6(IPAddress ip) =>
        ip.IsIPv6LinkLocal               // fe80::/10
        || ip.IsIPv6Multicast            // ff00::/8
        || ip.IsIPv6UniqueLocal          // fc00::/7
        || ip.Equals(IPAddress.IPv6Any); // :: unspecified
}
