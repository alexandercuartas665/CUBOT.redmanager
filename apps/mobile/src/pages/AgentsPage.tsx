// Listado de agentes + acciones por agente:
//  1) Renovar token FUXION → abre app-aware.fuxion.com en el browser externo con instrucciones.
//     (Sprint 2 va a reemplazar esto con WebView interno + captura automatica).
//  2) Sincronizar precios FUXION → llama al endpoint.

import { Browser } from '@capacitor/browser';
import { Clipboard } from '@capacitor/clipboard';
import { App } from '@capacitor/app';
import { useCallback, useEffect, useRef, useState } from 'react';
import { mobileApi } from '../api/client';
import type { MobileAgent, MobileSyncPricesResult } from '../api/types';
import { FuxionCapture } from '../plugins/fuxionCapture';

// JWT clasico: header.payload.signature en base64url. Longitud minima ~100 chars.
const JWT_RX = /^ey[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}$/;
function looksLikeJwt(s: string | null | undefined): boolean {
  if (!s) return false;
  const t = s.trim();
  return t.length >= 100 && t.length <= 8000 && JWT_RX.test(t);
}

export default function AgentsPage() {
  const [agents, setAgents] = useState<MobileAgent[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(true);
  const [actionMsg, setActionMsg] = useState<{ agentId: string; text: string; ok: boolean } | null>(null);
  const [renewingAgent, setRenewingAgent] = useState<MobileAgent | null>(null);
  const [jwtInput, setJwtInput] = useState('');
  const [clipJwt, setClipJwt] = useState<string | null>(null);
  const agentsRef = useRef<MobileAgent[]>([]);
  useEffect(() => { agentsRef.current = agents; }, [agents]);

  async function reload() {
    setBusy(true); setError(null);
    try { setAgents(await mobileApi.agents()); }
    catch (e) { setError(e instanceof Error ? e.message : 'Error'); }
    finally { setBusy(false); }
  }
  useEffect(() => { void reload(); }, []);

  // Detecta JWT en el portapapeles y ofrece pegarlo. Se dispara: al montar la pagina,
  // cada vez que la app vuelve al foreground (Capacitor App resume), y al enfocar la
  // pestana (visibilitychange, tambien cubre el navegador web dev).
  const scanClipboard = useCallback(async () => {
    try {
      const { value } = await Clipboard.read();
      if (looksLikeJwt(value) && agentsRef.current.some(a => a.paymentEnabled)) {
        setClipJwt(value.trim());
      }
    } catch { /* clipboard bloqueado o vacio: ignorar */ }
  }, []);
  useEffect(() => {
    void scanClipboard();
    const onVis = () => { if (document.visibilityState === 'visible') void scanClipboard(); };
    document.addEventListener('visibilitychange', onVis);
    const sub = App.addListener('appStateChange', (s) => { if (s.isActive) void scanClipboard(); });
    return () => {
      document.removeEventListener('visibilitychange', onVis);
      void sub.then(h => h.remove());
    };
  }, [scanClipboard]);

  async function saveClipboardTo(agent: MobileAgent) {
    if (!clipJwt) return;
    setBusy(true); setActionMsg(null);
    try {
      const updated = await mobileApi.updateFuxionToken(agent.id, clipJwt);
      setAgents(prev => prev.map(a => a.id === agent.id ? updated : a));
      setActionMsg({ agentId: agent.id, ok: true, text: 'Token pegado y guardado desde portapapeles.' });
      setClipJwt(null);
      try { await Clipboard.write({ string: '' }); } catch { /* si falla, no critico */ }
    } catch (e) {
      setActionMsg({ agentId: agent.id, ok: false, text: e instanceof Error ? e.message : 'Error' });
    } finally { setBusy(false); }
  }

  async function openFuxionPortal() {
    // Fallback para web dev: abre el portal en tab nueva. El user pega el token manual despues.
    await Browser.open({ url: 'https://app-aware.fuxion.com/dashboard', windowName: '_system' });
  }

  // Captura automatica del JWT usando el plugin nativo Android. Abre un WebView modal que
  // sobrevive cookies entre aperturas (el user logea una sola vez), y en cuanto el localStorage
  // tenga el xcorptoken el plugin lo devuelve y aca lo mandamos al backend.
  async function autoCaptureAndSave(agent: MobileAgent) {
    if (!FuxionCapture.isSupported()) {
      setActionMsg({ agentId: agent.id, ok: false, text: 'La captura automatica requiere la app Android. En browser usa el flujo manual.' });
      return;
    }
    setBusy(true); setActionMsg(null);
    try {
      const jwt = await FuxionCapture.openAndCapture('https://app-aware.fuxion.com/dashboard');
      const updated = await mobileApi.updateFuxionToken(agent.id, jwt);
      setAgents((prev) => prev.map((a) => a.id === agent.id ? updated : a));
      setActionMsg({ agentId: agent.id, ok: true, text: 'Token capturado y guardado.' });
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      if (msg === 'cancelled') {
        setActionMsg({ agentId: agent.id, ok: false, text: 'Captura cancelada.' });
      } else {
        setActionMsg({ agentId: agent.id, ok: false, text: msg });
      }
    } finally { setBusy(false); }
  }

  async function submitToken(agentId: string) {
    if (!jwtInput.trim()) { return; }
    setBusy(true);
    try {
      const updated = await mobileApi.updateFuxionToken(agentId, jwtInput.trim());
      setAgents((prev) => prev.map((a) => a.id === agentId ? updated : a));
      setActionMsg({ agentId, text: 'Token actualizado.', ok: true });
      setRenewingAgent(null);
      setJwtInput('');
    } catch (e) {
      setActionMsg({ agentId, text: e instanceof Error ? e.message : 'Error', ok: false });
    } finally { setBusy(false); }
  }

  async function syncPrices(agentId: string) {
    setBusy(true); setActionMsg(null);
    try {
      const r: MobileSyncPricesResult = await mobileApi.syncPrices(agentId);
      if (!r.ok) {
        setActionMsg({ agentId, text: `No se pudo: ${r.errors[0] ?? 'error'}`, ok: false });
      } else {
        setActionMsg({
          agentId,
          text: `OK · ${r.rowsUpdated} actualizados de ${r.rowsChecked}. ${r.rowsAlreadyOk} ya OK.`,
          ok: true,
        });
      }
      await reload();
    } catch (e) {
      setActionMsg({ agentId, text: e instanceof Error ? e.message : 'Error', ok: false });
    } finally { setBusy(false); }
  }

  return (
    <div className="px-4 py-5 space-y-4">
      <header className="flex items-center justify-between">
        <h1 className="text-xl font-bold text-slate-900">Agentes</h1>
        <button className="btn-ghost" onClick={() => void reload()} disabled={busy}>{busy ? '…' : '↻'}</button>
      </header>
      {error && <div className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{error}</div>}
      {clipJwt && agents.some(a => a.paymentEnabled) && (
        <div className="rounded-xl border border-purple-200 bg-purple-50 px-3 py-3 space-y-2">
          <div className="text-sm text-purple-900">
            Detecte un JWT en el portapapeles. Guardarlo como token FUXION de:
          </div>
          <div className="flex flex-wrap gap-2">
            {agents.filter(a => a.paymentEnabled).map(a => (
              <button key={a.id} className="btn-primary text-xs px-3 py-1.5"
                onClick={() => void saveClipboardTo(a)} disabled={busy}>
                {a.name}
              </button>
            ))}
            <button className="btn-ghost text-xs px-3 py-1.5" onClick={() => setClipJwt(null)}>
              Descartar
            </button>
          </div>
        </div>
      )}
      <div className="space-y-3">
        {agents.map((a) => {
          const expiring = a.paymentTokenExpiresAt && new Date(a.paymentTokenExpiresAt).getTime() - Date.now() < 24 * 3600 * 1000;
          const expired = a.paymentTokenExpiresAt && new Date(a.paymentTokenExpiresAt).getTime() < Date.now();
          return (
            <div key={a.id} className="card">
              <div className="flex items-start justify-between">
                <div className="flex-1 min-w-0">
                  <div className="font-medium text-slate-900 truncate">{a.name}</div>
                  {a.role && <div className="text-xs text-slate-500">{a.role}</div>}
                </div>
                <span className={`text-xs px-2 py-0.5 rounded-full ${a.isActive ? 'bg-green-100 text-green-700' : 'bg-slate-100 text-slate-500'}`}>
                  {a.isActive ? 'activo' : 'pausado'}
                </span>
              </div>

              {a.paymentEnabled && (
                <div className="mt-3 pt-3 border-t border-slate-100 text-xs text-slate-600 space-y-1">
                  <div>
                    Token FUXION:{' '}
                    {!a.paymentTokenPresent
                      ? <span className="text-red-600 font-medium">sin token</span>
                      : expired
                        ? <span className="text-red-600 font-medium">expirado</span>
                        : expiring
                          ? <span className="text-amber-600 font-medium">expira en menos de 24h</span>
                          : <span className="text-green-700 font-medium">vigente</span>
                    }
                    {a.paymentTokenExpiresAt && (
                      <span className="text-slate-400"> · {new Date(a.paymentTokenExpiresAt).toLocaleDateString('es-CO')}</span>
                    )}
                  </div>
                  <div>
                    Ultimo sync precios: {a.paymentLastPriceSyncAt
                      ? new Date(a.paymentLastPriceSyncAt).toLocaleDateString('es-CO', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
                      : <span className="italic text-slate-400">nunca</span>}
                  </div>
                </div>
              )}

              {actionMsg?.agentId === a.id && (
                <div className={`mt-3 text-sm rounded-lg px-3 py-2 ${actionMsg.ok ? 'bg-green-50 text-green-700 border border-green-100' : 'bg-red-50 text-red-700 border border-red-100'}`}>
                  {actionMsg.text}
                </div>
              )}

              {a.paymentEnabled && (
                <>
                  {renewingAgent?.id === a.id ? (
                    <div className="mt-3 space-y-2">
                      <label className="form-label">Pegá el JWT del portal FUXION</label>
                      <textarea
                        className="form-input h-24 text-xs font-mono"
                        placeholder="eyJhbGciOi..."
                        value={jwtInput}
                        onChange={(e) => setJwtInput(e.target.value)}
                      />
                      <div className="flex gap-2">
                        <button className="btn-primary flex-1" onClick={() => void submitToken(a.id)} disabled={busy || !jwtInput.trim()}>Guardar token</button>
                        <button className="btn-ghost" onClick={() => { setRenewingAgent(null); setJwtInput(''); }}>Cancelar</button>
                      </div>
                      <button className="btn-secondary text-sm" onClick={() => void openFuxionPortal()}>
                        Abrir portal FUXION →
                      </button>
                      <p className="text-[11px] text-slate-500 mt-1">
                        Modo manual: en el portal, con DevTools ejecuta <code>copy(localStorage.getItem('CapacitorStorage.xcorptoken'))</code> y pegá arriba.
                      </p>
                    </div>
                  ) : (
                    <div className="mt-3 space-y-2">
                      <div className="grid grid-cols-2 gap-2">
                        <button className="btn-secondary text-sm" onClick={() => void autoCaptureAndSave(a)} disabled={busy}>
                          Renovar auto
                        </button>
                        <button className="btn-primary text-sm" onClick={() => void syncPrices(a.id)} disabled={busy || !a.paymentTokenPresent}>
                          Sincronizar precios
                        </button>
                      </div>
                      <button className="btn-ghost text-xs w-full" onClick={async () => {
                        setRenewingAgent(a); setActionMsg(null);
                        try {
                          const { value } = await Clipboard.read();
                          if (looksLikeJwt(value)) setJwtInput(value.trim());
                        } catch { /* ignorar */ }
                      }}>
                        …o pegar el token a mano
                      </button>
                    </div>
                  )}
                </>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
