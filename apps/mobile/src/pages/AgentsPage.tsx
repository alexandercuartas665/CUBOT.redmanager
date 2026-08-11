// Listado de agentes + acciones por agente:
//  1) Renovar token FUXION → abre app-aware.fuxion.com en el browser externo con instrucciones.
//     (Sprint 2 va a reemplazar esto con WebView interno + captura automatica).
//  2) Sincronizar precios FUXION → llama al endpoint.

import { Browser } from '@capacitor/browser';
import { useEffect, useState } from 'react';
import { mobileApi } from '../api/client';
import type { MobileAgent, MobileSyncPricesResult } from '../api/types';

export default function AgentsPage() {
  const [agents, setAgents] = useState<MobileAgent[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(true);
  const [actionMsg, setActionMsg] = useState<{ agentId: string; text: string; ok: boolean } | null>(null);
  const [renewingAgent, setRenewingAgent] = useState<MobileAgent | null>(null);
  const [jwtInput, setJwtInput] = useState('');

  async function reload() {
    setBusy(true); setError(null);
    try { setAgents(await mobileApi.agents()); }
    catch (e) { setError(e instanceof Error ? e.message : 'Error'); }
    finally { setBusy(false); }
  }
  useEffect(() => { void reload(); }, []);

  async function openFuxionPortal() {
    // Abre el portal en el navegador externo del celular. El user hace login ahi (o ya lo tiene),
    // abre DevTools mobile (o instala un bookmarklet), copia el token, vuelve a la app y lo pega.
    await Browser.open({ url: 'https://app-aware.fuxion.com/dashboard', windowName: '_system' });
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
                        En el portal, abri DevTools (Chrome mobile: chrome://inspect desde compu, o usa un bookmarklet)
                        y ejecuta <code>copy(localStorage.getItem('CapacitorStorage.xcorptoken'))</code>. Después pegalo arriba.
                      </p>
                    </div>
                  ) : (
                    <div className="mt-3 grid grid-cols-2 gap-2">
                      <button className="btn-secondary text-sm" onClick={() => { setRenewingAgent(a); setActionMsg(null); }}>
                        Renovar token
                      </button>
                      <button className="btn-primary text-sm" onClick={() => void syncPrices(a.id)} disabled={busy || !a.paymentTokenPresent}>
                        Sincronizar precios
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
