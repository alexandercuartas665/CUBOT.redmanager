// Pantalla de login: email + password + (opcionalmente) selector de tenant si el user tiene mas
// de una agencia. Usa el mismo password que la web de red.cubot.com.co.

import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import type { MobileTenant } from '../api/types';

export default function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [tenants, setTenants] = useState<MobileTenant[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function doLogin(tenantId?: string) {
    setBusy(true); setError(null);
    const r = await login(email.trim(), password, tenantId);
    setBusy(false);
    if (r.kind === 'ok') { navigate('/', { replace: true }); return; }
    if (r.kind === 'select-tenant') { setTenants(r.availableTenants); return; }
    setError(r.message);
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    void doLogin();
  }

  return (
    <div className="min-h-full flex flex-col justify-center px-6 py-8 bg-gradient-to-b from-white to-cubot-soft">
      <div className="mb-8 text-center">
        <div className="mx-auto w-16 h-16 rounded-2xl bg-cubot-primary flex items-center justify-center text-white text-2xl font-bold shadow-lg">
          CR
        </div>
        <h1 className="mt-4 text-2xl font-bold text-slate-900">CUBOT redmanager</h1>
        <p className="text-sm text-slate-500">Inicia sesion con tu email de la plataforma</p>
      </div>

      {tenants ? (
        <div className="space-y-3">
          <p className="text-sm text-slate-700 font-medium">Elegi la agencia con la que queres trabajar:</p>
          {tenants.map((t) => (
            <button
              key={t.id}
              onClick={() => void doLogin(t.id)}
              disabled={busy}
              className="w-full py-3 px-4 rounded-xl border border-slate-200 bg-white text-left active:bg-cubot-soft"
            >
              <div className="font-medium text-slate-900">{t.name}</div>
              <div className="text-xs text-slate-500">{t.id}</div>
            </button>
          ))}
          <button className="btn-ghost mt-2" onClick={() => { setTenants(null); setError(null); }}>Volver</button>
        </div>
      ) : (
        <form onSubmit={onSubmit} className="space-y-4">
          <div>
            <label className="form-label" htmlFor="email">Email</label>
            <input
              id="email" type="email" autoComplete="email" inputMode="email"
              value={email} onChange={(e) => setEmail(e.target.value)}
              className="form-input" placeholder="tu-email@empresa.com" required
            />
          </div>
          <div>
            <label className="form-label" htmlFor="password">Contrasena</label>
            <input
              id="password" type="password" autoComplete="current-password"
              value={password} onChange={(e) => setPassword(e.target.value)}
              className="form-input" placeholder="********" required
            />
          </div>
          {error && <div className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{error}</div>}
          <button type="submit" className="btn-primary" disabled={busy}>
            {busy ? 'Ingresando...' : 'Ingresar'}
          </button>
        </form>
      )}

      <p className="mt-8 text-xs text-slate-400 text-center">
        v0.1 · red.cubot.com.co
      </p>
    </div>
  );
}
