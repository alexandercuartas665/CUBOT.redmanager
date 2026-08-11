// Storage persistente para credenciales de sesion.
// - En Android (Capacitor): @capacitor/preferences → SharedPreferences (respaldado por el keystore
//   del OS; no encriptado por defecto pero acceso solo desde nuestra app).
// - En web (npm run dev): fallback a localStorage.
//
// Guardamos apiToken, userJson, tenantJson.

import { Preferences } from '@capacitor/preferences';
import type { MobileTenant, MobileUser } from '../api/types';

const KEY_TOKEN = 'auth.apiToken';
const KEY_USER = 'auth.user';
const KEY_TENANT = 'auth.tenant';

export interface StoredSession {
  apiToken: string;
  user: MobileUser;
  tenant: MobileTenant;
}

export async function loadSession(): Promise<StoredSession | null> {
  const [tok, u, t] = await Promise.all([
    Preferences.get({ key: KEY_TOKEN }),
    Preferences.get({ key: KEY_USER }),
    Preferences.get({ key: KEY_TENANT }),
  ]);
  if (!tok.value || !u.value || !t.value) { return null; }
  try {
    return {
      apiToken: tok.value,
      user: JSON.parse(u.value),
      tenant: JSON.parse(t.value),
    };
  } catch {
    return null;
  }
}

export async function saveSession(s: StoredSession): Promise<void> {
  await Promise.all([
    Preferences.set({ key: KEY_TOKEN, value: s.apiToken }),
    Preferences.set({ key: KEY_USER, value: JSON.stringify(s.user) }),
    Preferences.set({ key: KEY_TENANT, value: JSON.stringify(s.tenant) }),
  ]);
}

export async function clearSession(): Promise<void> {
  await Promise.all([
    Preferences.remove({ key: KEY_TOKEN }),
    Preferences.remove({ key: KEY_USER }),
    Preferences.remove({ key: KEY_TENANT }),
  ]);
}
