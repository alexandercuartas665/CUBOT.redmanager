// Auth context: mantiene el usuario actual + apiToken en memoria y persistidos en el device.
// Provee login/logout y un flag ready para saber si ya se hidrato el estado inicial (evita
// pantallazo de login mientras se lee Preferences al arrancar).

import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { mobileApi, setAuthToken } from '../api/client';
import type { MobileTenant, MobileUser } from '../api/types';
import { clearSession, loadSession, saveSession, type StoredSession } from './storage';

interface AuthState {
  ready: boolean;
  user: MobileUser | null;
  tenant: MobileTenant | null;
  apiToken: string | null;
}

interface AuthContextValue extends AuthState {
  /** Intenta login. Puede requerir seleccion de tenant → devuelve la lista disponible. */
  login: (email: string, password: string, tenantId?: string) => Promise<LoginOutcome>;
  logout: () => Promise<void>;
}

export type LoginOutcome =
  | { kind: 'ok' }
  | { kind: 'select-tenant'; availableTenants: MobileTenant[] }
  | { kind: 'error'; message: string };

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({
    ready: false, user: null, tenant: null, apiToken: null,
  });

  // Hidrata sesion al montar (async).
  useEffect(() => {
    let cancelled = false;
    loadSession().then((s) => {
      if (cancelled) { return; }
      if (s) { setAuthToken(s.apiToken); }
      setState({
        ready: true,
        user: s?.user ?? null,
        tenant: s?.tenant ?? null,
        apiToken: s?.apiToken ?? null,
      });
    });
    return () => { cancelled = true; };
  }, []);

  // Cuando el api client detecta 401, dispara este event para forzar logout limpio.
  useEffect(() => {
    const handler = () => { void logout(); };
    window.addEventListener('auth:unauthorized', handler);
    return () => window.removeEventListener('auth:unauthorized', handler);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const login = useCallback(async (email: string, password: string, tenantId?: string): Promise<LoginOutcome> => {
    try {
      const res = await mobileApi.login({ email, password, tenantId, deviceLabel: 'android' });
      if (res.tenantSelectionRequired) {
        return { kind: 'select-tenant', availableTenants: res.availableTenants };
      }
      if (!res.apiToken || !res.user || !res.tenant) {
        return { kind: 'error', message: 'Respuesta invalida del servidor.' };
      }
      const stored: StoredSession = { apiToken: res.apiToken, user: res.user, tenant: res.tenant };
      await saveSession(stored);
      setAuthToken(stored.apiToken);
      setState({ ready: true, user: stored.user, tenant: stored.tenant, apiToken: stored.apiToken });
      return { kind: 'ok' };
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Error de conexion.';
      return { kind: 'error', message: msg };
    }
  }, []);

  const logout = useCallback(async () => {
    await clearSession();
    setAuthToken(null);
    setState({ ready: true, user: null, tenant: null, apiToken: null });
  }, []);

  return (
    <AuthContext.Provider value={{ ...state, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) { throw new Error('useAuth: falta AuthProvider en el arbol'); }
  return ctx;
}
