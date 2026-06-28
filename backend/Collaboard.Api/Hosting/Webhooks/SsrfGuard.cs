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
//      The denylist splits in two: an UNCONDITIONAL tier (loopback, link-local incl. the
//      169.254.169.254 cloud-metadata endpoint, and the other never-deliverable ranges —
//      unspecified, multicast, reserved/broadcast) that allowPrivate can never re-open, and a
//      flag-gated private tier (RFC1918 v4, unique-local v6) that allowPrivate re-permits.
//   3. Connect-time IP pinning — the ConnectCallback resolves, validates EVERY returned IP, and
//      opens the socket to the validated IP, closing the DNS-rebind/TOCTOU window (a host that
//      resolved public at registration cannot rebind to loopback at delivery).
//   4. AllowAutoRedirect = false on the handler (a 302-to-internal would walk around 1-3) — wired
//      in Program.cs alongside the ConnectCallback, not here.
//
// allowPrivate (Webhooks:AllowPrivateNetworkTargets) is the operator override for a legitimately-
// private target (self-hosted n8n on a LAN/Tailscale address). It re-permits the genuinely-private
// LAN ranges (RFC1918 v4, unique-local v6) ONLY — it deliberately never re-opens loopback or the
// link-local/cloud-metadata range, so turning the flag on to reach a LAN host cannot also expose the
// metadata service (169.254.169.254) or loopback. Startup-bound (IOptions), so the registration
// validator and the connect callback read the SAME value — they must agree (#326 S2).
internal static class SsrfGuard
{
    // The pure decision (#326 control 2): is this resolved IP in a blocked range? No DbContext, no
    // DB — the project's pure-function carve-out, table-testable directly. The single-argument form
    // is the full denylist (the most restrictive posture, allowPrivate off).
    public static bool IsBlockedAddress(IPAddress address) => IsBlockedAddress(address, allowPrivate: false);

    // The flag-aware decision. The always-blocked tier (loopback, link-local incl. the cloud-metadata
    // endpoint, and the other never-deliverable ranges) is rejected regardless of allowPrivate; only
    // the genuinely-private LAN tier (RFC1918, unique-local) is re-permitted when allowPrivate is set.
    public static bool IsBlockedAddress(IPAddress address, bool allowPrivate)
    {
        ArgumentNullException.ThrowIfNull(address);

        // Unwrap an IPv4-mapped IPv6 (::ffff:127.0.0.1) so a mapped loopback/private address cannot
        // slip the IPv4 checks below.
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IsAlwaysBlocked(ip))
        {
            return true;
        }

        // Past the always-blocked tier, the only remaining blocked ranges are the private LAN ranges
        // the operator override re-permits.
        return !allowPrivate && IsPrivateRange(ip);
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

        // Always resolve-and-validate, even when allowPrivate is set: the always-blocked tier
        // (loopback, link-local/metadata, ...) is rejected regardless of the flag, so an early
        // allowPrivate short-circuit would let a loopback or metadata URL register.
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
            if (IsBlockedAddress(address, allowPrivate))
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

        // Validate every resolved address even when allowPrivate is set — the always-blocked tier
        // (loopback, link-local/metadata, ...) stays blocked regardless of the flag.
        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address, allowPrivate))
            {
                var blockedAddress = address.ToString();
                throw new WebhookSsrfBlockedException
                (
                    $"Webhook host '{host}' resolves to a blocked address ({blockedAddress})."
                );
            }
        }

        // Every resolved address passed validation above. Dial the first by its IP — never by
        // hostname — so the name cannot rebind between this check and connect.
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

    // The unconditional tier — blocked regardless of allowPrivate. Loopback, link-local (incl. the
    // 169.254.169.254 cloud-metadata endpoint), and the never-deliverable ranges (unspecified,
    // multicast, reserved/broadcast). The operator override must never re-open any of these.
    private static bool IsAlwaysBlocked(IPAddress ip) =>
        IPAddress.IsLoopback(ip) || ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsAlwaysBlockedV4(ip),
            AddressFamily.InterNetworkV6 => IsAlwaysBlockedV6(ip),
            // Any other family (AppleTalk, IPX, ...) is not a deliverable internet target — block.
            _ => true,
        };

    // The flag-gated tier — the genuinely-private LAN ranges (RFC1918 v4, unique-local v6) that
    // allowPrivate re-permits for a legitimate self-hosted target.
    private static bool IsPrivateRange(IPAddress ip) =>
        ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPrivateV4(ip),
            AddressFamily.InterNetworkV6 => ip.IsIPv6UniqueLocal,   // fc00::/7
            _ => false,
        };

    private static bool IsAlwaysBlockedV4(IPAddress ip)
    {
        var b = ip.GetAddressBytes();   // 4 bytes, network order

        // 0.0.0.0/8 unspecified, the 169.254.0.0/16 link-local range that carries cloud metadata at
        // 169.254.169.254, and anything at or above 224.0.0.0 for multicast, reserved, and
        // broadcast. 127.0.0.0/8 loopback is caught by IPAddress.IsLoopback in IsAlwaysBlocked.
        return b[0] is 0
            || (b[0] == 169 && b[1] == 254)
            || b[0] >= 224;
    }

    private static bool IsPrivateV4(IPAddress ip)
    {
        var b = ip.GetAddressBytes();   // 4 bytes, network order

        // RFC1918: 10.0.0.0/8, 172.16.0.0 through 172.31.255.255, 192.168.0.0/16.
        return b[0] is 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    private static bool IsAlwaysBlockedV6(IPAddress ip) =>
        ip.IsIPv6LinkLocal               // fe80::/10
        || ip.IsIPv6Multicast            // ff00::/8
        || ip.Equals(IPAddress.IPv6Any); // :: unspecified
}
