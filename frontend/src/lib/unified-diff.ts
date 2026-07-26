// Parses the backend's unified diff string into typed lines for rendering.
//
// The format is a published contract: git-style hunks, `\n` line endings on
// every host, no file headers, ranges always written out in full. Each line's
// first character is the marker — `+` addition, `-` removal, a single space for
// unchanged context, `@@` opening a hunk header — and the marker is NOT part of
// the line's text, so exactly one character is stripped for content lines.
//
// The diff text is user-authored card content. It is parsed into data here and
// rendered as plain text nodes by the history view — never as markdown and
// never as HTML — so nothing an author wrote inside a description can become
// markup by way of appearing in a diff.

export type DiffLineKind = 'hunk' | 'add' | 'remove' | 'context';

export type DiffLine = {
  kind: DiffLineKind;
  text: string;
};

export function parseUnifiedDiff(diff: string): DiffLine[] {
  if (diff === '') return [];

  const rawLines = diff.split('\n');

  // The diff string terminates its last line with `\n`, so the split leaves one
  // trailing empty element. That element is the terminator, not an empty line
  // of content — real empty lines inside the diff still carry their marker
  // (`+`, `-`, or a lone space) and survive the split intact.
  if (rawLines[rawLines.length - 1] === '') {
    rawLines.pop();
  }

  return rawLines.map((line): DiffLine => {
    if (line.startsWith('@@')) {
      // Hunk headers keep their full text — the ranges are the content.
      return { kind: 'hunk', text: line };
    }
    if (line.startsWith('+')) {
      return { kind: 'add', text: line.slice(1) };
    }
    if (line.startsWith('-')) {
      return { kind: 'remove', text: line.slice(1) };
    }
    // Context lines carry a single leading space as their marker.
    return { kind: 'context', text: line.startsWith(' ') ? line.slice(1) : line };
  });
}
