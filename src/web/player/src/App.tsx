import { useState } from 'react';
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
  const [selectedAlbumId, setSelectedAlbumId] = useState<string | null>(null);

  if (status === 'booting') {
    return <BootScreen />;
  }

  if (status === 'anonymous') {
    return <LoginPage />;
  }

  const handleOpenAlbum = (albumId: string) => {
    setSelectedAlbumId(albumId);
    setActiveTab('library');
  };

  return (
    <Layout activeTab={activeTab} onSelectTab={setActiveTab}>
      {activeTab === 'home' && (
        <HomePage onOpenAlbum={handleOpenAlbum} />
      )}
      {activeTab === 'search' && (
        <SearchPage onOpenAlbum={handleOpenAlbum} />
      )}
      {activeTab === 'library' && (
        <LibraryPage
          openAlbumId={selectedAlbumId}
          onCloseAlbum={() => setSelectedAlbumId(null)}
        />
      )}
      {activeTab === 'nowPlaying' && (
        <NowPlayingPage onBackToHome={() => setActiveTab('home')} />
      )}
      {activeTab === 'settings' && (
        <SettingsPage />
      )}
    </Layout>
  );
}

export default App;
