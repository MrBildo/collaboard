// The API reports the running version two different ways. `/version` returns the assembly
// string verbatim, so it keeps a pre-release suffix (`1.17.0-rc1`). `/version/status` reports
// the numeric core only (`1.17.0`) and decides whether an update is available by comparing
// those cores — a pre-release is deliberately treated as the release it is a candidate for.
// Anything on the client that has to reconcile the two forms must use the same yardstick, or
// it will disagree with the server about what counts as an update. Hence: strip a leading
// `v`, drop build metadata and any pre-release suffix, compare major/minor/patch.

type VersionCore = [major: number, minor: number, patch: number];

function parseVersionCore(value: string | null | undefined): VersionCore | null {
  if (value === null || value === undefined) return null;

  let text = value.trim();

  // GitHub release tags carry a leading `v`; the assembly string does not.
  if (text.startsWith('v') || text.startsWith('V')) {
    text = text.slice(1);
  }

  const plus = text.indexOf('+');
  if (plus >= 0) text = text.slice(0, plus);

  const dash = text.indexOf('-');
  if (dash >= 0) text = text.slice(0, dash);

  const parts = text.split('.');
  if (parts.length > 3) return null;

  // An omitted component reads as zero, so `1.17` and `1.17.0` are the same release.
  const core: VersionCore = [0, 0, 0];

  for (let i = 0; i < parts.length; i++) {
    if (!/^\d+$/.test(parts[i])) return null;
    core[i] = Number(parts[i]);
  }

  return core;
}

/**
 * Compares two version strings by their numeric core, the way the server does. Returns a
 * negative number when `a` is the older release, a positive number when it is the newer one,
 * zero when they are the same release, and `null` when either string cannot be parsed —
 * callers decide what an unknown comparison should mean for them.
 */
export function compareVersionCores(
  a: string | null | undefined,
  b: string | null | undefined,
): number | null {
  const left = parseVersionCore(a);
  const right = parseVersionCore(b);

  if (left === null || right === null) return null;

  for (let i = 0; i < left.length; i++) {
    if (left[i] !== right[i]) return left[i] - right[i];
  }

  return 0;
}
