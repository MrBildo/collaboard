import { describe, test, expect } from 'vitest';
import { remark } from 'remark';
import remarkGfm from 'remark-gfm';
import { remarkCardLinks } from './remark-card-links';

// Unit-level coverage of the mdast transform in isolation (card #273). The
// component test (MarkdownRenderer.test.tsx) covers the rendered output and the
// router-link wiring; here we assert the tree-level linkify/suppress decisions
// directly, which is the precise place to pin the regex and live-set validation.

const liveCards = new Set([28, 273]);

function transform(markdown: string, cardNumbers: Set<number> = liveCards): string {
  // remark stringifies a `link` mdast node back to `[text](url)`, so the
  // serialized output tells us exactly which `#N` became a link.
  return remark()
    .use(remarkGfm)
    .use(remarkCardLinks, { boardSlug: 'demo', cardNumbers })
    .processSync(markdown)
    .toString()
    .trim();
}

describe('remarkCardLinks', () => {
  test('linkifies a live card reference to a relative card route', () => {
    expect(transform('See #28 here')).toBe('See [#28](/boards/demo/cards/28) here');
  });

  test('leaves an unknown/archived card number as plain text', () => {
    expect(transform('See #9999 here')).toBe('See #9999 here');
  });

  test('linkifies a self-reference (card on its own page)', () => {
    expect(transform('This is #273')).toBe('This is [#273](/boards/demo/cards/273)');
  });

  test('linkifies multiple live references in one paragraph', () => {
    expect(transform('#28 and #273')).toBe(
      '[#28](/boards/demo/cards/28) and [#273](/boards/demo/cards/273)',
    );
  });

  test('does not descend into inline code', () => {
    expect(transform('Use `#28` literally')).toBe('Use `#28` literally');
  });

  test('does not descend into a fenced code block', () => {
    const md = '```\n#28 here\n```';
    expect(transform(md)).toBe(md);
  });

  test('does not re-link a reference already inside a markdown link', () => {
    const md = '[issue](https://example.com/x#28)';
    expect(transform(md)).toBe(md);
  });

  test('rejects a hex color (digits followed by letters)', () => {
    expect(transform('color #28a745 ok')).toBe('color #28a745 ok');
  });

  test('rejects a fragment-style ref with a trailing hyphen', () => {
    expect(transform('anchor #28-foo')).toBe('anchor #28-foo');
  });

  test('does not treat a markdown heading as a card reference', () => {
    // `# Heading` is an mdast `heading` node, not `#` text — never matched.
    expect(transform('# Heading 28')).toBe('# Heading 28');
  });
});
