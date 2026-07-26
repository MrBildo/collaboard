import { describe, test, expect } from 'vitest';
import { findInternalPath, isCrossOriginHttpHref } from './internal-href';

const BASE = 'https://board.example/boards/demo/cards/1';

// References that begin with a single slash and still leave the origin. These
// are the reason the check resolves instead of pattern-matching: the first two
// are the shapes a security audit found by hand, and the rest are the ones a
// hand-written pattern would have kept letting through.
const offOriginPathLikeHrefs: Array<[string, string]> = [
  ['//elsewhere.example/x', 'scheme-relative'],
  ['/\\elsewhere.example/x', 'backslash reads as a second slash for web schemes'],
  ['/\\\\elsewhere.example', 'two backslashes'],
  ['/\\/elsewhere.example', 'backslash then slash'],
  ['/\t/elsewhere.example', 'tab is stripped before parsing, leaving //'],
  ['/\n/elsewhere.example', 'newline is stripped before parsing, leaving //'],
  ['/\r/elsewhere.example', 'carriage return is stripped before parsing, leaving //'],
  ['//user:pass@elsewhere.example/x', 'credentials in the authority'],
];

describe('findInternalPath', () => {
  test('returns the path for a link into this app', () => {
    expect(findInternalPath('/boards/demo/cards/28', BASE)).toBe('/boards/demo/cards/28');
    expect(findInternalPath('/', BASE)).toBe('/');
  });

  test('keeps the query and fragment of an in-app link', () => {
    expect(findInternalPath('/search?q=a+b#top', BASE)).toBe('/search?q=a+b#top');
  });

  test.each(offOriginPathLikeHrefs)('rejects %j — %s', (href) => {
    expect(findInternalPath(href, BASE)).toBeNull();
  });

  test('rejects a same-origin reference whose resolved path reads as protocol-relative', () => {
    // Resolves onto our own origin, so an origin check on the href alone
    // accepts it — but its resolved pathname is `//elsewhere.example`, which a
    // router would re-read as an absolute URL. Checking only the input is not
    // enough; the value handed on has to be checked too.
    expect(new URL('/..//elsewhere.example', BASE).origin).toBe('https://board.example');
    expect(findInternalPath('/..//elsewhere.example', BASE)).toBeNull();
    expect(findInternalPath('/a/../..//elsewhere.example', BASE)).toBeNull();
  });

  test('accepts a scheme-relative reference that genuinely points at this app', () => {
    // `//board.example/x` carries its own authority, but that authority is
    // ours, so it is an in-app link — and the path we hand back is the
    // authority-free one that was verified.
    expect(findInternalPath('//board.example/x', BASE)).toBe('/x');
  });

  test('rejects references that were never in-app links to begin with', () => {
    expect(findInternalPath(undefined, BASE)).toBeNull();
    expect(findInternalPath('', BASE)).toBeNull();
    expect(findInternalPath('#section', BASE)).toBeNull();
    expect(findInternalPath('relative/path', BASE)).toBeNull();
    expect(findInternalPath('mailto:someone@example.com', BASE)).toBeNull();
    expect(findInternalPath('javascript:alert(1)', BASE)).toBeNull();
    expect(findInternalPath('https://elsewhere.example/x', BASE)).toBeNull();
  });

  test('leaves an absolute link to this app alone rather than routing it in-app', () => {
    // Same origin, but written as a full URL — it keeps behaving the way full
    // URLs always have here (an ordinary anchor), so this fix changes nothing
    // about links that were never being mistaken for in-app navigation.
    expect(findInternalPath('https://board.example/x', BASE)).toBeNull();
  });

  test('every accepted path resolves back to the origin it was checked against', () => {
    // The property the whole module exists to guarantee, asserted over every
    // shape above rather than case by case: nothing can be returned that
    // resolves somewhere else.
    const candidates = [
      ...offOriginPathLikeHrefs.map(([href]) => href),
      '/boards/demo/cards/28',
      '/',
      '/search?q=a+b#top',
      '/..//elsewhere.example',
      '//board.example/x',
      '/%2F/elsewhere.example',
      '/ /elsewhere.example',
      '/./..//elsewhere.example',
      '//\\elsewhere.example',
    ];

    for (const href of candidates) {
      const path = findInternalPath(href, BASE);
      if (path === null) continue;
      expect(new URL(path, BASE).origin).toBe(new URL(BASE).origin);
    }
  });
});

describe('isCrossOriginHttpHref', () => {
  test('is true for web links to another origin, whatever shape they arrive in', () => {
    expect(isCrossOriginHttpHref('https://elsewhere.example/x', BASE)).toBe(true);
    expect(isCrossOriginHttpHref('http://elsewhere.example/x', BASE)).toBe(true);
    expect(isCrossOriginHttpHref('//elsewhere.example/x', BASE)).toBe(true);
    expect(isCrossOriginHttpHref('/\\elsewhere.example/x', BASE)).toBe(true);
    expect(isCrossOriginHttpHref('/\t/elsewhere.example', BASE)).toBe(true);
  });

  test('is false for links that stay on this origin', () => {
    expect(isCrossOriginHttpHref('/boards/demo', BASE)).toBe(false);
    expect(isCrossOriginHttpHref('#section', BASE)).toBe(false);
    expect(isCrossOriginHttpHref('relative/path', BASE)).toBe(false);
    expect(isCrossOriginHttpHref('https://board.example/x', BASE)).toBe(false);
  });

  test('is false for non-web schemes, which should never open a blank tab', () => {
    expect(isCrossOriginHttpHref('mailto:someone@example.com', BASE)).toBe(false);
    expect(isCrossOriginHttpHref('xmpp:someone@example.com', BASE)).toBe(false);
    expect(isCrossOriginHttpHref(undefined, BASE)).toBe(false);
  });
});
