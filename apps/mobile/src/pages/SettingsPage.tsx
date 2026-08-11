// Pantalla config: info del usuario/agencia + boton logout. Version de la app abajo.

import { useAuth } from '../auth/AuthContext';

export default function SettingsPage() {
  const { user, tenant, logout } = useAuth();
  return (
    <div className="px-4 py-5 space-y-6">
      <h1 className="text-xl font-bold text-slate-900">Config</h1>
      <section className="card space-y-2">
        <div>
          <div className="text-xs uppercase tracking-wide text-slate-500">Usuario</div>
          <div className="font-medium text-slate-900">{user?.displayName ?? '—'}</div>
          <div className="text-sm text-slate-500">{user?.email ?? '—'}</div>
        </div>
        <div className="pt-3 border-t border-slate-100">
          <div className="text-xs uppercase tracking-wide text-slate-500">Agencia</div>
          <div className="font-medium text-slate-900">{tenant?.name ?? '—'}</div>
        </div>
      </section>
      <button className="btn-secondary" onClick={() => void logout()}>Cerrar sesion</button>
      <p className="text-xs text-slate-400 text-center">v0.1 · red.cubot.com.co</p>
    </div>
  );
}
