import React from 'react';
import ReactDOM from 'react-dom/client';
import { QueryClient } from '@tanstack/react-query';
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client';
import { createSyncStoragePersister } from '@tanstack/query-sync-storage-persister';
import { RouterProvider, createBrowserRouter } from 'react-router-dom';
import { App } from './routes/App';
import { BoardRedirect } from './routes/BoardRedirect';
import './styles.css';

const queryClient = new QueryClient();
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
    <PersistQueryClientProvider client={queryClient} persistOptions={{ persister }}>
      <RouterProvider router={router} />
    </PersistQueryClientProvider>
  </React.StrictMode>,
);
