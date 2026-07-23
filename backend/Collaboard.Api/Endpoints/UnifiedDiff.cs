using System.Globalization;
using System.Text;
using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.Model;

namespace Collaboard.Api.Endpoints;

// Renders a git-style unified diff between two texts.
//
// The line-difference algorithm comes from DiffPlex (Myers); the unified-diff FORMAT is rendered
// here rather than by DiffPlex's own UnidiffRenderer, because the rendered string is a published
// API contract and this renderer needs three properties that renderer does not offer:
//
//   1. "\n" line endings always. DiffPlex's renderer emits Environment.NewLine, so the identical
//      edit would come back with CRLF from a Windows host and LF from a Linux one — a wire format
//      that varies by deployment platform.
//   2. No leading file-name header pair. There is no file, the response envelope already names the
//      revisions being compared, and on the MCP surface (where diff is the default representation,
//      chosen for token economy) two redundant header lines per revision is pure cost.
//   3. git's empty-range hunk convention, so a diff of this renderer's output means the same thing
//      to a reader as a diff from any other tool.
//
// Keeping the format here also stops a DiffPlex upgrade from reshaping a published response.
internal static class UnifiedDiff
{
    // git's default: enough surrounding text to locate a change without quoting the whole card.
    private const int _contextLines = 3;

    public static string Render(string oldText, string newText)
    {
        var oldNormalized = NormalizeLineEndings(oldText);
        var newNormalized = NormalizeLineEndings(newText);

        // A text's final newline terminates its last line rather than starting an empty one, so
        // trimming it avoids a phantom trailing context line on the (very common) description that
        // ends in a newline. Trimmed only when BOTH sides carry one, so an edit that consists ONLY
        // of adding or removing the final newline still renders as a visible change instead of
        // silently vanishing.
        if (oldNormalized.EndsWith('\n') && newNormalized.EndsWith('\n'))
        {
            oldNormalized = oldNormalized[..^1];
            newNormalized = newNormalized[..^1];
        }

        var result = Differ.Instance.CreateDiffs
        (
            oldNormalized,
            newNormalized,
            ignoreWhiteSpace: false,
            ignoreCase: false,
            LineChunker.Instance
        );

        if (result.DiffBlocks.Count == 0)
        {
            return string.Empty;
        }

        var script = BuildEditScript(result);
        var hunks = GroupIntoHunks(script);

        return RenderHunks(script, hunks);
    }

    // Flattens the diff blocks into one ordered line list: every line of the old text plus every
    // inserted line, each tagged with its unified-diff prefix. Rendering and hunk grouping both
    // read this one list, so the two can never disagree about what changed where.
    private static List<DiffScriptLine> BuildEditScript(DiffResult result)
    {
        List<DiffScriptLine> script = [];
        var oldIndex = 0;

        foreach (var block in result.DiffBlocks)
        {
            while (oldIndex < block.DeleteStartA)
            {
                script.Add(new DiffScriptLine(' ', result.PiecesOld[oldIndex]));
                oldIndex++;
            }

            for (var offset = 0; offset < block.DeleteCountA; offset++)
            {
                script.Add(new DiffScriptLine('-', result.PiecesOld[oldIndex]));
                oldIndex++;
            }

            for (var offset = 0; offset < block.InsertCountB; offset++)
            {
                script.Add(new DiffScriptLine('+', result.PiecesNew[block.InsertStartB + offset]));
            }
        }

        while (oldIndex < result.PiecesOld.Count)
        {
            script.Add(new DiffScriptLine(' ', result.PiecesOld[oldIndex]));
            oldIndex++;
        }

        return script;
    }

    // Each changed line claims _contextLines of surrounding text; ranges that touch or overlap merge
    // into one hunk, so a cluster of nearby edits reads as a single block rather than as repeated
    // overlapping quotes of the same lines.
    private static List<HunkRange> GroupIntoHunks(List<DiffScriptLine> script)
    {
        List<HunkRange> hunks = [];
        var start = -1;
        var end = -1;

        for (var index = 0; index < script.Count; index++)
        {
            if (script[index].Prefix == ' ')
            {
                continue;
            }

            var candidateStart = Math.Max(0, index - _contextLines);
            var candidateEnd = Math.Min(script.Count, index + _contextLines + 1);

            if (start < 0)
            {
                start = candidateStart;
                end = candidateEnd;
                continue;
            }

            if (candidateStart <= end)
            {
                end = candidateEnd;
                continue;
            }

            hunks.Add(new HunkRange(start, end));
            start = candidateStart;
            end = candidateEnd;
        }

        if (start >= 0)
        {
            hunks.Add(new HunkRange(start, end));
        }

        return hunks;
    }

    private static string RenderHunks(List<DiffScriptLine> script, List<HunkRange> hunks)
    {
        var builder = new StringBuilder();
        var oldLinesEmitted = 0;
        var newLinesEmitted = 0;
        var cursor = 0;

        foreach (var hunk in hunks)
        {
            // Skipped context still advances the line counters the next hunk header reports.
            for (var index = cursor; index < hunk.Start; index++)
            {
                CountLine(script[index], ref oldLinesEmitted, ref newLinesEmitted);
            }

            var oldCount = 0;
            var newCount = 0;
            for (var index = hunk.Start; index < hunk.End; index++)
            {
                CountLine(script[index], ref oldCount, ref newCount);
            }

            // git points an EMPTY range at the line it follows rather than at a line that does not
            // exist, so a card that gained its first description reads "@@ -0,0 +1,4 @@".
            var oldStart = oldCount == 0 ? oldLinesEmitted : oldLinesEmitted + 1;
            var newStart = newCount == 0 ? newLinesEmitted : newLinesEmitted + 1;

            builder.Append(CultureInfo.InvariantCulture, $"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@\n");

            for (var index = hunk.Start; index < hunk.End; index++)
            {
                var line = script[index];
                builder.Append(line.Prefix).Append(line.Text).Append('\n');
                CountLine(line, ref oldLinesEmitted, ref newLinesEmitted);
            }

            cursor = hunk.End;
        }

        return builder.ToString();
    }

    private static void CountLine(DiffScriptLine line, ref int oldCount, ref int newCount)
    {
        if (line.Prefix != '+')
        {
            oldCount++;
        }

        if (line.Prefix != '-')
        {
            newCount++;
        }
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    // Nested rather than file-scoped: a file-local type cannot appear in any member signature of a
    // non-file-local type, private ones included, and these carry the private helpers' arguments.

    // A single line of the flattened edit script: its unified-diff prefix (' ' context, '-' removed,
    // '+' added) and the line text without its terminator.
    private sealed record DiffScriptLine(char Prefix, string Text);

    // Half-open [Start, End) range of edit-script lines that one hunk covers.
    private sealed record HunkRange(int Start, int End);
}
