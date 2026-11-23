import { Navigate, Route, Routes } from 'react-router-dom';
import { LoginRoute, NotFoundPage, RequireAdmin, RequireAuth, RequireContributor, SetupRoute } from './auth/guards';
import { Layout } from './ui/Layout';
import { LoginPage } from './pages/LoginPage';
import { SetupPage } from './pages/SetupPage';
import { DashboardPage } from './pages/DashboardPage';
import { TracksPage } from './pages/TracksPage';
import { AlbumsPage } from './pages/AlbumsPage';
import { ArtistsPage } from './pages/ArtistsPage';
import { FoldersPage } from './pages/FoldersPage';
import { GenresPage } from './pages/GenresPage';
import { PlaylistsPage } from './pages/PlaylistsPage';
import { UploadPage } from './pages/UploadPage';
import { UsersPage } from './pages/UsersPage';
import { SettingsPage } from './pages/SettingsPage';
import { TelegramPage } from './pages/TelegramPage';
import { MonitoringPage } from './pages/MonitoringPage';
import { AuditPage } from './pages/AuditPage';
import { SessionsPage } from './pages/SessionsPage';

function HomeRedirect() {
  return <Navigate to="/dashboard" replace />;
}

export function App() {
  return (
    <Routes>
      <Route path="/setup" element={<SetupRoute><SetupPage /></SetupRoute>} />
      <Route path="/login" element={<LoginRoute><LoginPage /></LoginRoute>} />

      <Route element={<RequireAuth />}>
        <Route element={<Layout />}>
          <Route index element={<HomeRedirect />} />
          <Route path="/dashboard" element={<RequireAdmin><DashboardPage /></RequireAdmin>} />

          <Route path="/library/tracks" element={<TracksPage />} />
          <Route path="/library/albums" element={<AlbumsPage />} />
          <Route path="/library/artists" element={<ArtistsPage />} />
          <Route path="/library/folders" element={<FoldersPage />} />
          <Route path="/library/genres" element={<GenresPage />} />
          <Route path="/library/playlists" element={<PlaylistsPage />} />
          <Route path="/library/upload" element={<RequireContributor><UploadPage /></RequireContributor>} />

          <Route path="/admin/users" element={<RequireAdmin><UsersPage /></RequireAdmin>} />
          <Route path="/admin/settings" element={<RequireAdmin><SettingsPage /></RequireAdmin>} />
          <Route path="/admin/telegram" element={<RequireAdmin><TelegramPage /></RequireAdmin>} />
          <Route path="/admin/monitoring" element={<RequireAdmin><MonitoringPage /></RequireAdmin>} />
          <Route path="/admin/audit" element={<RequireAdmin><AuditPage /></RequireAdmin>} />

          <Route path="/account/sessions" element={<SessionsPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
