// Listado de conversaciones + navegacion al detalle.

import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { mobileApi } from '../api/client';
import type { MobileConversation } from '../api/types';

export default function ConversationsPage() {
  const [rows, setRows] = useState<MobileConversation[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(true);

  async function reload() {
    setBusy(true); setError(null);
    try { setRows(await mobileApi.conversations(50)); }
    catch (e) { setError(e instanceof Error ? e.message : 'Error'); }
    finally { setBusy(false); }
  }

  useEffect(() => { void reload(); }, []);

  return (
    <div className="px-4 py-5 space-y-4">
      <header className="flex items-center justify-between">
        <h1 className="text-xl font-bold text-slate-900">Conversaciones</h1>
        <button className="btn-ghost" onClick={() => void reload()} disabled={busy}>
          {busy ? '…' : '↻'}
        </button>
      </header>
      {error && <div className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{error}</div>}
      {!busy && rows.length === 0 && (
        <div className="text-center text-sm text-slate-500 py-8">Sin conversaciones.</div>
      )}
      <div className="space-y-2">
        {rows.map((c) => (
          <Link
            key={c.id}
            to={`/conversaciones/${c.id}`}
            className="block card active:bg-cubot-soft"
          >
            <div className="flex justify-between items-baseline">
              <div className="font-medium text-slate-900 truncate">{c.contactName}</div>
              {c.lastMessageAt && (
                <div className="text-[11px] text-slate-400 shrink-0 ml-2">{formatTime(c.lastMessageAt)}</div>
              )}
            </div>
            {c.lastMessagePreview && (
              <div className="text-sm text-slate-600 line-clamp-2 mt-1">
                {c.lastMessageDirection === 'outbound' && <span className="text-cubot-primary">Tú: </span>}
                {c.lastMessagePreview}
              </div>
            )}
            {c.lineLabel && (
              <div className="text-[11px] text-slate-400 mt-1">{c.lineLabel} · {c.contactPhone}</div>
            )}
          </Link>
        ))}
      </div>
    </div>
  );
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  const now = new Date();
  const sameDay = d.toDateString() === now.toDateString();
  if (sameDay) { return d.toLocaleTimeString('es-CO', { hour: '2-digit', minute: '2-digit' }); }
  return d.toLocaleDateString('es-CO', { day: '2-digit', month: '2-digit' });
}
