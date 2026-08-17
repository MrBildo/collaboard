using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Collabot.Collattice.Api.Installation;

public static class AppSettingsMergeCli
{
    public const int ExitOk = 0;
    public const int ExitUsage = 2;
    public const int ExitMissingShippedFile = 3;
    public const int ExitMissingCurrentFile = 4;
    public const int ExitParseFailed = 5;
    public const int ExitWriteFailed = 6;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (!TryParseArgs(args, out var shippedPath, out var currentPath, out var baselinePath, out var argError))
        {
            stderr.WriteLine(argError);
            stderr.WriteLine("usage: Collabot.Collattice.Api --merge-appsettings <shipped-path> <ondisk-path> [--baseline <baseline-path>]");
            return ExitUsage;
        }

        if (!File.Exists(shippedPath))
        {
            stderr.WriteLine($"merge-appsettings: shipped file not found: {shippedPath}");
            return ExitMissingShippedFile;
        }

        if (!File.Exists(currentPath))
        {
            stderr.WriteLine($"merge-appsettings: on-disk file not found: {currentPath}");
            return ExitMissingCurrentFile;
        }

        JsonNode? shipped;
        JsonNode? current;
        JsonNode? baseline = null;

        try
        {
            shipped = JsonNode.Parse(File.ReadAllText(shippedPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            stderr.WriteLine($"merge-appsettings: failed to parse shipped file: {ex.Message}");
            return ExitParseFailed;
        }

        try
        {
            current = JsonNode.Parse(File.ReadAllText(currentPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            stderr.WriteLine($"merge-appsettings: failed to parse on-disk file: {ex.Message}");
            stderr.WriteLine("merge-appsettings: leaving on-disk file untouched.");
            return ExitParseFailed;
        }

        if (!string.IsNullOrEmpty(baselinePath) && File.Exists(baselinePath))
        {
            try
            {
                baseline = JsonNode.Parse(File.ReadAllText(baselinePath));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A corrupted baseline is recoverable: fall through to conservative merge mode.
                // Surface the failure so the operator knows the sidecar is broken.
                stderr.WriteLine($"merge-appsettings: ignoring corrupt baseline {baselinePath}: {ex.Message}");
                baseline = null;
            }
        }

        if (shipped is null || current is null)
        {
            stderr.WriteLine("merge-appsettings: shipped or on-disk file parsed as null JSON.");
            return ExitParseFailed;
        }

        MergeResult result;

        try
        {
            result = AppSettingsMerger.Merge(shipped, current, baseline);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"merge-appsettings: cannot merge: {ex.Message}");
            return ExitParseFailed;
        }

        var merged = result.ToJsonString();

        if (!TryWriteAtomic(currentPath, merged, out var writeError))
        {
            stderr.WriteLine($"merge-appsettings: failed to write merged file to {currentPath}: {writeError}");
            return ExitWriteFailed;
        }

        if (!string.IsNullOrEmpty(baselinePath))
        {
            // Refresh the baseline to the freshly-shipped content. Use the raw shipped bytes so
            // the baseline file matches what was just installed byte-for-byte (modulo line
            // endings) -- the baseline is a record of what was shipped, not of what the merger
            // produced.
            var shippedRaw = File.ReadAllText(shippedPath);

            if (!TryWriteAtomic(baselinePath, shippedRaw, out var baselineError))
            {
                // Non-fatal. The merge succeeded; only the sidecar refresh failed. Future merges
                // will fall back to conservative mode for keys that would have been refreshed
                // here, which is the same posture as a first-time merge.
                stderr.WriteLine($"merge-appsettings: warning -- could not refresh baseline at {baselinePath}: {baselineError}");
            }
        }

        WriteSummary(stdout, currentPath, result);

        return ExitOk;
    }

    private static bool TryParseArgs
    (
        string[] args,
        out string shippedPath,
        out string currentPath,
        out string? baselinePath,
        out string? error
    )
    {
        shippedPath = string.Empty;
        currentPath = string.Empty;
        baselinePath = null;
        error = null;

        var positional = new List<string>();
        var index = 0;

        while (index < args.Length)
        {
            if (args[index] == "--baseline")
            {
                if (index + 1 >= args.Length)
                {
                    error = "merge-appsettings: --baseline requires a path argument";
                    return false;
                }

                baselinePath = args[index + 1];
                index += 2;
                continue;
            }

            positional.Add(args[index]);
            index++;
        }

        if (positional.Count != 2)
        {
            error = string.Format
            (
                CultureInfo.InvariantCulture,
                "merge-appsettings: expected 2 positional arguments (shipped, ondisk), got {0}",
                positional.Count
            );

            return false;
        }

        shippedPath = positional[0];
        currentPath = positional[1];

        return true;
    }

    private static bool TryWriteAtomic(string path, string content, out string? error)
    {
        var tempPath = path + ".tmp";

        try
        {
            File.WriteAllText(tempPath, content);

            // File.Move with overwrite=true is atomic on the same volume on both Windows and POSIX
            // (NTFS rename + ext4 rename). The on-disk file is therefore either the prior content
            // or the new content at any observable moment; never a half-written file.
            File.Move(tempPath, path, true);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup. A leftover .tmp file is annoying but harmless.
            }

            return false;
        }
    }

    private static void WriteSummary(TextWriter stdout, string currentPath, MergeResult result)
    {
        var added = result.Changes.Count(c => c.Kind == MergeChangeKind.Added);
        var refreshed = result.Changes.Count(c => c.Kind == MergeChangeKind.RefreshedDefault);
        var preservedEdits = result.Changes.Count(c => c.Kind == MergeChangeKind.PreservedOperatorEdit);
        var preservedExtra = result.Changes.Count(c => c.Kind == MergeChangeKind.PreservedExtraKey);

        if (added == 0 && refreshed == 0 && preservedEdits == 0 && preservedExtra == 0)
        {
            stdout.WriteLine($"merge-appsettings: no changes to {currentPath}");
            return;
        }

        stdout.WriteLine($"merge-appsettings: updated {currentPath}");

        if (added > 0)
        {
            EmitKeyList(stdout, FormatHeader("added", added), result.Changes, MergeChangeKind.Added);
        }

        if (refreshed > 0)
        {
            EmitKeyList(stdout, FormatHeader("refreshed", refreshed), result.Changes, MergeChangeKind.RefreshedDefault);
        }

        if (preservedEdits > 0)
        {
            EmitKeyList(stdout, FormatHeader("preserved operator edits", preservedEdits), result.Changes, MergeChangeKind.PreservedOperatorEdit);
        }

        if (preservedExtra > 0)
        {
            EmitKeyList(stdout, FormatHeader("preserved keys not in shipped", preservedExtra), result.Changes, MergeChangeKind.PreservedExtraKey);
        }

        if (result.Conservative)
        {
            stdout.WriteLine("  note: no baseline file was available, ran in conservative mode (operator values were preserved on every existing key).");
        }
    }

    private static void EmitKeyList(TextWriter stdout, string header, IReadOnlyList<MergeChange> changes, MergeChangeKind kind)
    {
        stdout.WriteLine(header);

        foreach (var change in changes.Where(c => c.Kind == kind))
        {
            stdout.WriteLine($"    {change.Path}");
        }
    }

    private static string FormatHeader(string label, int count) =>
        string.Format(CultureInfo.InvariantCulture, "  {0} ({1})", label, count);
}
