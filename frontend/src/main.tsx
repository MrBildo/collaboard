import React from 'react';
import ReactDOM from 'react-dom/client';
import { QueryClient } from '@tanstack/react-query';
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client';
import { createSyncStoragePersister } from '@tanstack/query-sync-storage-persister';
import { RouterProvider, createBrowserRouter } from 'react-router-dom';
import { ErrorBoundary } from './components/ErrorBoundary';
import { App } from './routes/App';
import { BoardRedirect } from './routes/BoardRedirect';
import { fetchRuntimeConfig } from './lib/runtime-config';
import { Toaster } from './components/ui/sonner';
import { createMutationFloor } from './lib/mutation-floor';
import './styles.css';

const queryClient = new QueryClient({
  // The global mutation-error floor (card #203, spec §5). Wired once here so
  // every mutation surfaces through it — no mutation can fail silently. Call
  // sites declare their surface via `meta` (see lib/mutation-floor.ts).
  mutationCache: createMutationFloor(),
  defaultOptions: {
    queries: {
      staleTime: 10 * 1000,
    },
  },
});
const persister = createSyncStoragePersister({ storage: window.localStorage });

const router = createBrowserRouter([
  {
    path: '/',
    element: <BoardRedirect />,
  },
  {
    path: '/boards/:slug',
    element: <App />,
  },
  {
    path: '/boards/:slug/cards/:cardNumber',
    element: <App />,
  },
]);

async function boot() {
  // The runtime config (API base URL) is a precondition for the entire app tree,
  // not state managed inside it. Resolve it before the first render so both
  // consumers (axios baseURL, EventSource URL) read a settled value.
  await fetchRuntimeConfig();

  ReactDOM.createRoot(document.getElementById('root')!).render(
    <React.StrictMode>
      <ErrorBoundary>
        {/* `buster` is the frontend build identity (vite.config.ts `define`).
            It changes every release, so on restore TanStack discards a cache
            persisted by a prior build rather than serving pre-upgrade data as
            fresh — the version banner / update-available flag can't get stuck
            showing stale info after a server upgrade. */}
        <PersistQueryClientProvider
          client={queryClient}
          persistOptions={{ persister, buster: __BUILD_VERSION__ }}
        >
          <RouterProvider router={router} />
          {/* The toast tier of the mutation-error floor (card #203). Lives once
              at the app root; the floor (lib/mutation-floor.ts) drives it. */}
          <Toaster richColors position="bottom-center" />
        </PersistQueryClientProvider>
      </ErrorBoundary>
    </React.StrictMode>,
  );
}

void boot().catch((err) => {
  // fetchRuntimeConfig() never rejects (every failure mode collapses to the
  // fallback), so reaching here means a hard startup failure elsewhere. Render
  // a minimal inline shell instead of leaving the loading shell up forever.
  console.error('[boot] fatal startup error', err);
  ReactDOM.createRoot(document.getElementById('root')!).render(
    <div
      style={{
        position: 'fixed',
        inset: 0,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: '#666',
        fontFamily: 'system-ui, sans-serif',
        fontSize: '14px',
        textAlign: 'center',
        padding: '1rem',
      }}
    >
      Could not start the application. Reload the page to try again.
    </div>,
  );
});
