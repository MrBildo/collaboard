import { afterEach, expect, test } from 'vitest';
import { QueryClient } from '@tanstack/react-query';
import { createSyncStoragePersister } from '@tanstack/query-sync-storage-persister';
import {
  persistQueryClientRestore,
  persistQueryClientSave,
} from '@tanstack/react-query-persist-client';

// Reproduces the stale-cache-across-upgrade condition behind the version-flag bug
// and proves the build-version buster resolves it. The app persists its whole
// TanStack Query cache to localStorage (main.tsx). Before the buster, a cache
// written by the pre-upgrade build rehydrated as fresh after a server upgrade, so
// the gear menu kept showing the old version + "update available" (the update
// status query carries a 30-min staleTime) until localStorage was cleared by hand.
// The buster is the frontend build identity; when it changes on a new build,
// TanStack discards the prior build's persisted cache on restore.

const VERSION_STATUS_KEY = ['version', 'status'];
const STALE_STATUS = { current: '2.0.1', latest: '2.0.2', updateAvailable: true };

function makePersister() {
  // The exact persister main.tsx uses, over the jsdom localStorage. throttleTime
  // 0 collapses the persister's write-throttle to the next macrotask so the test
  // can await the write deterministically (production keeps the 1s default).
  return createSyncStoragePersister({ storage: window.localStorage, throttleTime: 0 });
}

// Let the persister's throttled write settle before reading it back.
const flushWrite = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

// The old build persists the version/update state to localStorage.
async function persistUnderBuild(buster: string) {
  const client = new QueryClient();
  client.setQueryData(VERSION_STATUS_KEY, STALE_STATUS);
  await persistQueryClientSave({ queryClient: client, persister: makePersister(), buster });
  await flushWrite();
}

// A fresh browser boot under a given build restores from localStorage.
async function bootUnderBuild(buster: string): Promise<QueryClient> {
  const client = new QueryClient();
  await persistQueryClientRestore({ queryClient: client, persister: makePersister(), buster });
  return client;
}

afterEach(() => {
  window.localStorage.clear();
});

test('a cache persisted by an older build is discarded when a new build restores it', async () => {
  await persistUnderBuild('build-old');

  // Server upgraded → the new build boots with a different build identity.
  const client = await bootUnderBuild('build-new');

  // The stale pre-upgrade state is gone, so the query refetches fresh instead of
  // rehydrating the old version + update-available flag.
  expect(client.getQueryData(VERSION_STATUS_KEY)).toBeUndefined();
});

test('a cache persisted by the same build is retained on restore', async () => {
  await persistUnderBuild('build-x');

  const client = await bootUnderBuild('build-x');

  // Within one build the persisted cache still hydrates — the buster invalidates
  // across builds, it does not disable persistence.
  expect(client.getQueryData(VERSION_STATUS_KEY)).toEqual(STALE_STATUS);
});
