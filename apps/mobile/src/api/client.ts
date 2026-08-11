// Cliente HTTP para /api/mobile/*.
//
// En 'npm run dev' vive detras del proxy Vite (relative /api/... → https://red.cubot.com.co/api/...).
// En el APK Capacitor, apunta directo a red.cubot.com.co. Detectamos el entorno con Capacitor.
//
// El apiToken se lee de un lugar unico (auth store), asi cualquier request lo lleva sin que el
// caller tenga que pasarlo.

import { Capacitor } from '@capacitor/core';
import type {
  MobileAgent,
  MobileConversation,
  MobileDashboard,
  MobileLoginRequest,
  MobileLoginResponse,
  MobileMessage,
  MobileSyncPricesResult,
} from './types';

const PROD_BASE = 'https://red.cubot.com.co';
const isNative = Capacitor.isNativePlatform();
const baseUrl = isNative ? PROD_BASE : ''; // browser dev usa proxy relativo

let currentToken: string | null = null;

export function setAuthToken(token: string | null) {
  currentToken = token;
}

async function request<T>(
  method: 'GET' | 'POST' | 'PATCH' | 'DELETE',
  path: string,
  body?: unknown,
  auth: boolean = true,
): Promise<T> {
  const headers: Record<string, string> = { 'Accept': 'application/json' };
  if (body !== undefined) { headers['Content-Type'] = 'application/json'; }
  if (auth && currentToken) { headers['X-Api-Token'] = currentToken; }

  const res = await fetch(`${baseUrl}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (res.status === 401) {
    // Token invalido/expirado → gatilla logout desde el auth context (el listener lo maneja).
    window.dispatchEvent(new CustomEvent('auth:unauthorized'));
    throw new ApiError(401, 'Sesion expirada. Vuelve a iniciar sesion.');
  }
  if (res.status === 429) {
    throw new ApiError(429, 'Demasiados intentos. Esperá un momento e intentá de nuevo.');
  }
  if (!res.ok) {
    let msg = `HTTP ${res.status}`;
    try {
      const errBody = await res.json();
      if (errBody?.error) { msg = errBody.error; }
    } catch { /* body puede no ser JSON */ }
    throw new ApiError(res.status, msg);
  }
  if (res.status === 204) { return undefined as T; }
  return await res.json() as T;
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = 'ApiError';
  }
}

export const mobileApi = {
  login: (req: MobileLoginRequest) =>
    request<MobileLoginResponse>('POST', '/api/mobile/auth/login', req, false),

  dashboard: () =>
    request<MobileDashboard>('GET', '/api/mobile/dashboard'),

  conversations: (take = 30) =>
    request<MobileConversation[]>('GET', `/api/mobile/conversations?take=${take}`),

  messages: (conversationId: string, take = 100) =>
    request<MobileMessage[]>('GET', `/api/mobile/conversations/${conversationId}/messages?take=${take}`),

  agents: () =>
    request<MobileAgent[]>('GET', '/api/mobile/agents'),

  updateFuxionToken: (agentId: string, jwt: string) =>
    request<MobileAgent>('POST', `/api/mobile/agents/${agentId}/fuxion-token`, { jwt }),

  syncPrices: (agentId: string) =>
    request<MobileSyncPricesResult>('POST', `/api/mobile/agents/${agentId}/sync-prices`),
};
