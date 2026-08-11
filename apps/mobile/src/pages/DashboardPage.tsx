// Home de la app. Muestra KPIs del tenant y accesos rapidos.

import { useEffect, useState } from 'react';
import { mobileApi } from '../api/client';
import type { MobileDashboard } from '../api/types';
import { useAuth } from '../auth/AuthContext';

export default function DashboardPage() {
  const { tenant } = useAuth();
  const [data, setData] = useState<MobileDashboard | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(true);

  async function reload() {
    setBusy(true); setError(null);
    try { setData(await mobileApi.dashboard()); }
    catch (e) { setError(e instanceof Error ? e.message : 'Error'); }
    finally { setBusy(false); }
  }

  useEffect(() => { void reload(); }, []);

  return (
    <div className="px-4 py-5 space-y-5">
      <header>
        <div className="text-xs text-slate-500 uppercase tracking-wide">Agencia</div>
        <h1 className="text-xl font-bold text-slate-900 truncate">{tenant?.name ?? '—'}</h1>
      </header>

      {busy && !data && (
        <div className="text-center text-sm text-slate-500 py-8">Cargando…</div>
      )}
      {error && (
        <div className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{error}</div>
      )}
      {data && (
        <>
          <section className="grid grid-cols-2 gap-3">
            <Kpi title="Conversaciones activas" value={data.conversationsActive} subtitle="ultimos 7 dias" />
            <Kpi title="Mensajes 7d" value={data.messagesLast7Days} subtitle={`${data.inboundLast7Days} in · ${data.outboundLast7Days} out`} />
            <Kpi title="Pendientes" value={data.pendingComments} subtitle="esperan respuesta" accent={data.pendingComments > 0} />
            <Kpi title="Agentes con FUXION" value={data.agentsWithFuxion} subtitle={`de ${data.agentsConfigured} configurados`} />
            <Kpi title="Tokens x expirar" value={data.tokensExpiringSoon} subtitle="< 24h" accent={data.tokensExpiringSoon > 0} />
            <Kpi title="Videos TikTok" value={data.videosSynced} subtitle={data.lastTikTokSyncAt ? `sync ${formatRelative(data.lastTikTokSyncAt)}` : 'sin sync'} />
          </section>

          <button className="btn-secondary" onClick={() => void reload()} disabled={busy}>
            {busy ? 'Actualizando…' : 'Refrescar'}
          </button>
        </>
      )}
    </div>
  );
}

function Kpi({ title, value, subtitle, accent = false }: { title: string; value: number; subtitle?: string; accent?: boolean }) {
  return (
    <div className={`card ${accent ? 'ring-2 ring-cubot-primary/30' : ''}`}>
      <div className="text-xs text-slate-500">{title}</div>
      <div className={`text-2xl font-bold ${accent ? 'text-cubot-primary' : 'text-slate-900'}`}>{value.toLocaleString('es-CO')}</div>
      {subtitle && <div className="text-[11px] text-slate-400 mt-1">{subtitle}</div>}
    </div>
  );
}

function formatRelative(iso: string): string {
  const then = new Date(iso).getTime();
  const now = Date.now();
  const secs = Math.max(0, Math.floor((now - then) / 1000));
  if (secs < 60) { return 'hace segundos'; }
  const mins = Math.floor(secs / 60);
  if (mins < 60) { return `hace ${mins}m`; }
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) { return `hace ${hrs}h`; }
  const days = Math.floor(hrs / 24);
  return `hace ${days}d`;
}
