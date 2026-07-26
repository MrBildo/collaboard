// Deciding whether a link in user-authored markdown points back into this app
// is a URL-resolution question, and the only reliable way to answer it is to
// resolve the link the way a browser would and compare the origin it lands on.
//
// Testing the string's shape instead does not work. `//elsewhere.example`,
// `/\elsewhere.example` and `/<TAB>/elsewhere.example` all begin with a single
// slash and all resolve to somebody else's origin — the last one because the
// URL parser strips tabs and newlines before it parses, turning the reference
// into the scheme-relative form. Any pattern narrow enough to reject the shapes
// we already know about is still blind to the next one; the parser is not.

/** Resolve `href` against `baseHref`, or null when it is not a resolvable URL. */
function resolveHref(href: string, baseHref: string): URL | null {
  try {
    return new URL(href, baseHref);
  } catch {
    return null;
  }
}

/**
 * The origin `href` lands on when resolved against `baseHref` — null when it
 * cannot be resolved, or when it lands on an opaque origin. An opaque origin
 * serialises to the literal string "null", and two opaque origins are never
 * the same origin, so that string must never be allowed to compare equal to
 * itself.
 */
function findResolvedOrigin(href: string, baseHref: string): string | null {
  const url = resolveHref(href, baseHref);
  if (!url || url.origin === 'null') return null;
  return url.origin;
}

/**
 * The in-app path `href` refers to, or null when it is not a link into this
 * app. The returned path — not the original href — is what callers should
 * navigate to: it is the value whose destination was actually verified.
 */
export function findInternalPath(
  href: string | undefined,
  baseHref: string = window.location.href,
): string | null {
  // Only absolute-path references are candidates for in-app routing. Fragment,
  // query-only and path-relative links keep rendering as ordinary anchors, as
  // they always have. This narrows which links are considered; it is not the
  // safety check — the origin comparison below is.
  if (typeof href !== 'string' || !href.startsWith('/')) return null;

  const ourOrigin = findResolvedOrigin(baseHref, baseHref);
  if (ourOrigin === null) return null;

  const target = resolveHref(href, baseHref);
  if (!target || target.origin !== ourOrigin) return null;

  const path = target.pathname + target.search + target.hash;

  // Resolution can turn a genuinely same-origin reference into a path that
  // *reads* as protocol-relative: `/..//elsewhere.example` lands on our own
  // origin, but its pathname is `//elsewhere.example`. A router re-parses
  // whatever string it is handed, and reads a leading `//` as an absolute URL —
  // so the value we are about to hand on has to clear the same origin check as
  // the value we were given.
  if (findResolvedOrigin(path, baseHref) !== ourOrigin) return null;

  return path;
}

/**
 * Whether `href` is a web link to some other origin — the links that must
 * carry `target="_blank"` and `rel="noopener noreferrer"`. Non-web schemes
 * (`mailto:`, `xmpp:`) are excluded: opening a blank tab for them is wrong.
 */
export function isCrossOriginHttpHref(
  href: string | undefined,
  baseHref: string = window.location.href,
): boolean {
  if (typeof href !== 'string') return false;

  const url = resolveHref(href, baseHref);
  if (!url || (url.protocol !== 'http:' && url.protocol !== 'https:')) return false;

  const ourOrigin = findResolvedOrigin(baseHref, baseHref);
  return ourOrigin !== null && url.origin !== ourOrigin;
}
