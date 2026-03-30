import React from 'react';
import ReactDOM from 'react-dom/client';
import { QueryClient } from '@tanstack/react-query';
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client';
import { createSyncStoragePersister } from '@tanstack/query-sync-storage-persister';
import { RouterProvider, createBrowserRouter } from 'react-router-dom';
import { ErrorBoundary } from './components/ErrorBoundary';
import { App } from './routes/App';
import { BoardRedirect } from './routes/BoardRedirect';
import './styles.css';

const queryClient = new QueryClient({
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

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ErrorBoundary>
      <PersistQueryClientProvider client={queryClient} persistOptions={{ persister }}>
        <RouterProvider router={router} />
      </PersistQueryClientProvider>
    </ErrorBoundary>
  </React.StrictMode>,
);
