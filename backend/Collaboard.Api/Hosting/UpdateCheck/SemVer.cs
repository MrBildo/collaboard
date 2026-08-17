using System.Globalization;
using System.Runtime.InteropServices;

namespace Collaboard.Api.Hosting.UpdateCheck;

// Minimal SemVer for the update check: parse "1.16.0" / "v1.16.0" / "1.16.0+build" to
// its numeric core (major.minor.patch) and compare. /releases/latest already excludes
// pre-releases and drafts, so the pre-release suffix is out of scope here — we compare the
// stable numeric core only. System.Version is deliberately not used: it treats a missing
// component as -1 (1.16 < 1.16.0) and chokes on a leading "v", both of which we'd have to
// normalize around anyway.
[StructLayout(LayoutKind.Auto)]
public readonly record struct SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
{
    // The dev/unstamped sentinel — a build with no release tag stamps 0.0.0. An instance at
    // the sentinel must never be nagged: it is not a released version, so "is a
    // newer version available" is not a meaningful question to put in front of the operator.
    public static readonly SemVer DevSentinel = new(0, 0, 0);

    public bool IsDevSentinel => this == DevSentinel;

    public static bool TryParse(string? value, out SemVer result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.AsSpan().Trim();

        // Tolerate a leading "v" (GitHub tags are "vX.Y.Z").
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
        {
            span = span[1..];
        }

        // Drop build metadata ("+...") and any pre-release suffix ("-...") — we compare the
        // stable numeric core only.
        var plus = span.IndexOf('+');
        if (plus >= 0)
        {
            span = span[..plus];
        }

        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            span = span[..dash];
        }

        var parts = span.ToString().Split('.');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                return false;
            }

            numbers[i] = n;
        }

        result = new SemVer(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public int CompareTo(SemVer other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);

        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(SemVer left, SemVer right) => left.CompareTo(right) < 0;

    public static bool operator >(SemVer left, SemVer right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemVer left, SemVer right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemVer left, SemVer right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
