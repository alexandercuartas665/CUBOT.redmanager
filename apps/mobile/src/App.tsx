// Router principal. AuthProvider hidrata la sesion; hasta que ready=true muestra un placeholder
// (evita el flash de LoginPage sobre una sesion valida). RequireAuth redirige a /login si no hay sesion.

import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from './auth/AuthContext';
import AgentsPage from './pages/AgentsPage';
import ConversationDetailPage from './pages/ConversationDetailPage';
import ConversationsPage from './pages/ConversationsPage';
import DashboardPage from './pages/DashboardPage';
import LoginPage from './pages/LoginPage';
import SettingsPage from './pages/SettingsPage';
import AppShell from './shell/AppShell';
import type { ReactNode } from 'react';

function RequireAuth({ children }: { children: ReactNode }) {
  const { ready, apiToken } = useAuth();
  const location = useLocation();
  if (!ready) {
    return (
      <div className="flex-1 flex items-center justify-center text-slate-400">
        <div className="animate-pulse">Cargando…</div>
      </div>
    );
  }
  if (!apiToken) {
    return <Navigate to="/login" replace state={{ from: location }} />;
  }
  return <>{children}</>;
}

function AlreadyAuthed({ children }: { children: ReactNode }) {
  const { ready, apiToken } = useAuth();
  if (!ready) { return null; }
  if (apiToken) { return <Navigate to="/" replace />; }
  return <>{children}</>;
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<AlreadyAuthed><LoginPage /></AlreadyAuthed>} />
          <Route element={<RequireAuth><AppShell /></RequireAuth>}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/conversaciones" element={<ConversationsPage />} />
            <Route path="/conversaciones/:id" element={<ConversationDetailPage />} />
            <Route path="/agentes" element={<AgentsPage />} />
            <Route path="/config" element={<SettingsPage />} />
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
