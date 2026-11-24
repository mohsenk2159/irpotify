import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { HashRouter } from 'react-router-dom';
import { App } from './App';
import { AuthProvider } from './auth/AuthProvider';
import { PlayerProvider } from './ui/Player';
import { ToastProvider } from './ui/Modal';
import { UiProvider } from './ui/UiProvider';
import './styles.css';

if (typeof window !== 'undefined' && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('./sw.js').catch(() => {});
  });
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: 0,
    },
  },
});

const container = document.getElementById('root');
if (!container) throw new Error('Root container #root was not found.');

createRoot(container).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <UiProvider>
        <ToastProvider>
          <AuthProvider>
            <PlayerProvider>
              <HashRouter>
                <App />
              </HashRouter>
            </PlayerProvider>
          </AuthProvider>
        </ToastProvider>
      </UiProvider>
    </QueryClientProvider>
  </StrictMode>,
);
