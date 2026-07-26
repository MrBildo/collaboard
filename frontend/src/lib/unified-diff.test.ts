import { describe, test, expect } from 'vitest';
import { parseUnifiedDiff } from './unified-diff';

describe('parseUnifiedDiff', () => {
  test('returns an empty list for an empty diff', () => {
    expect(parseUnifiedDiff('')).toEqual([]);
  });

  test('classifies hunk headers, additions, removals, and context by prefix', () => {
    const diff = '@@ -1,2 +1,3 @@\n alpha\n-beta\n+gamma\n+delta\n';

    expect(parseUnifiedDiff(diff)).toEqual([
      { kind: 'hunk', text: '@@ -1,2 +1,3 @@' },
      { kind: 'context', text: 'alpha' },
      { kind: 'remove', text: 'beta' },
      { kind: 'add', text: 'gamma' },
      { kind: 'add', text: 'delta' },
    ]);
  });

  test('strips exactly one marker character, preserving content that starts with the same character', () => {
    // "+ one" as content of an added line arrives as "++ one" — one strip only.
    const diff = '@@ -1,1 +1,1 @@\n-- minus content\n++ plus content\n';

    expect(parseUnifiedDiff(diff)).toEqual([
      { kind: 'hunk', text: '@@ -1,1 +1,1 @@' },
      { kind: 'remove', text: '- minus content' },
      { kind: 'add', text: '+ plus content' },
    ]);
  });

  test('drops the trailing newline terminator but keeps empty content lines', () => {
    // An empty added line is a bare "+"; an empty context line is a lone space.
    const diff = '@@ -1,2 +1,3 @@\n context\n+\n \n';

    expect(parseUnifiedDiff(diff)).toEqual([
      { kind: 'hunk', text: '@@ -1,2 +1,3 @@' },
      { kind: 'context', text: 'context' },
      { kind: 'add', text: '' },
      { kind: 'context', text: '' },
    ]);
  });

  test('keeps multiple hunks in one diff distinct', () => {
    const diff = '@@ -1,1 +1,1 @@\n-a\n+b\n@@ -9,1 +9,1 @@\n-y\n+z\n';
    const lines = parseUnifiedDiff(diff);

    expect(lines.filter((l) => l.kind === 'hunk')).toEqual([
      { kind: 'hunk', text: '@@ -1,1 +1,1 @@' },
      { kind: 'hunk', text: '@@ -9,1 +9,1 @@' },
    ]);
  });

  test('handles the empty-range convention for a description that started empty', () => {
    const diff = '@@ -0,0 +1,2 @@\n+first line\n+second line\n';

    expect(parseUnifiedDiff(diff)).toEqual([
      { kind: 'hunk', text: '@@ -0,0 +1,2 @@' },
      { kind: 'add', text: 'first line' },
      { kind: 'add', text: 'second line' },
    ]);
  });
});
