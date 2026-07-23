import { describe, test, expect } from 'vitest';
import { compareVersionCores } from './semver';

describe('compareVersionCores', () => {
  test('reports zero for the same release', () => {
    expect(compareVersionCores('1.17.0', '1.17.0')).toBe(0);
  });

  test('reports a positive number when the first argument is the newer release', () => {
    expect(compareVersionCores('2.0.0', '1.17.0')).toBeGreaterThan(0);
    expect(compareVersionCores('1.18.0', '1.17.9')).toBeGreaterThan(0);
    expect(compareVersionCores('1.17.1', '1.17.0')).toBeGreaterThan(0);
  });

  test('reports a negative number when the first argument is the older release', () => {
    expect(compareVersionCores('1.17.0', '2.0.0')).toBeLessThan(0);
    expect(compareVersionCores('1.17.9', '1.18.0')).toBeLessThan(0);
    expect(compareVersionCores('1.17.0', '1.17.1')).toBeLessThan(0);
  });

  test('compares numerically, not lexically', () => {
    // '9' sorts after '10' as text; as a version, 1.9.0 is the older release.
    expect(compareVersionCores('1.9.0', '1.10.0')).toBeLessThan(0);
  });

  test('tolerates the leading v that release tags carry', () => {
    expect(compareVersionCores('v1.17.0', '1.17.0')).toBe(0);
    expect(compareVersionCores('V1.18.0', 'v1.17.0')).toBeGreaterThan(0);
  });

  test('ignores build metadata', () => {
    expect(compareVersionCores('1.17.0+abc1234', '1.17.0')).toBe(0);
  });

  test('treats a pre-release as the release it is a candidate for', () => {
    // This is the case a plain string comparison misses. /version reports the running build
    // as 1.17.0-rc1 while /version/status reports it as 1.17.0; the server considers them the
    // same release and will not offer an upgrade between them, so neither may the client.
    expect(compareVersionCores('1.17.0', '1.17.0-rc1')).toBe(0);
    expect(compareVersionCores('1.17.0-rc1', '1.17.0')).toBe(0);
    expect(compareVersionCores('1.18.0', '1.17.0-rc1')).toBeGreaterThan(0);
    expect(compareVersionCores('1.17.0-rc1', '1.18.0')).toBeLessThan(0);
  });

  test('reads an omitted component as zero', () => {
    expect(compareVersionCores('1.17', '1.17.0')).toBe(0);
    expect(compareVersionCores('2', '2.0.0')).toBe(0);
  });

  test('reports null when either version cannot be parsed', () => {
    expect(compareVersionCores('not-a-version', '1.17.0')).toBeNull();
    expect(compareVersionCores('1.17.0', '')).toBeNull();
    expect(compareVersionCores('1.17.0.1', '1.17.0')).toBeNull();
    expect(compareVersionCores('1.x.0', '1.17.0')).toBeNull();
    expect(compareVersionCores(null, '1.17.0')).toBeNull();
    expect(compareVersionCores('1.17.0', undefined)).toBeNull();
  });
});
