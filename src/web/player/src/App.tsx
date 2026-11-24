import { useState, useEffect } from 'react';
import { useAuth } from './auth/AuthProvider';
import { BootScreen } from './auth/guards';
import { LoginPage } from './pages/LoginPage';
import { Layout, type PlayerTab } from './ui/Layout';
import { HomePage } from './pages/HomePage';
import { SearchPage } from './pages/SearchPage';
import { LibraryPage } from './pages/LibraryPage';
import { NowPlayingPage } from './pages/NowPlayingPage';
import { SettingsPage } from './pages/SettingsPage';

export function App() {
  const { status } = useAuth();
  const [activeTab, setActiveTab] = useState<PlayerTab>('home');

  // Initialize history state and listen for back/forward navigation
  useEffect(() => {
    if (typeof window === 'undefined') return;

    if (!window.history.state || window.history.state.app !== 'irpotify') {
      window.history.replaceState({ app: 'irpotify', tab: 'home' }, '');
    }

    const handlePopState = (e: PopStateEvent) => {
      const state = e.state;
      if (state && state.app === 'irpotify' && state.tab) {
        setActiveTab(state.tab);
      }
    };

    const handleAutoNavigateToNowPlaying = () => {
      setActiveTab((current) => {
        if (current !== 'nowPlaying') {
          if (typeof window !== 'undefined') {
            window.history.pushState({ app: 'irpotify', tab: 'nowPlaying', fromTab: current }, '');
          }
          return 'nowPlaying';
        }
        return current;
      });
    };

    window.addEventListener('popstate', handlePopState);
    window.addEventListener('musichub:navigate-now-playing', handleAutoNavigateToNowPlaying);

    return () => {
      window.removeEventListener('popstate', handlePopState);
      window.removeEventListener('musichub:navigate-now-playing', handleAutoNavigateToNowPlaying);
    };
  }, []);

  if (status === 'booting') {
    return <BootScreen />;
  }

  if (status === 'anonymous') {
    return <LoginPage />;
  }

  const handleSelectTab = (tab: PlayerTab) => {
    if (tab === activeTab) return;
    if (typeof window !== 'undefined') {
      window.history.pushState({ app: 'irpotify', tab }, '');
    }
    setActiveTab(tab);
  };

  const handleBackFromNowPlaying = () => {
    if (typeof window !== 'undefined' && window.history.length > 1) {
      window.history.back();
    } else {
      setActiveTab('home');
    }
  };

  return (
    <Layout activeTab={activeTab} onSelectTab={handleSelectTab}>
      {activeTab === 'home' && (
        <HomePage />
      )}
      {activeTab === 'search' && (
        <SearchPage />
      )}
      {activeTab === 'library' && (
        <LibraryPage />
      )}
      {activeTab === 'nowPlaying' && (
        <NowPlayingPage onBackToHome={handleBackFromNowPlaying} />
      )}
      {activeTab === 'settings' && (
        <SettingsPage />
      )}
    </Layout>
  );
}

export default App;
