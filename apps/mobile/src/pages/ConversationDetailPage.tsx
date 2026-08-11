// Detalle de una conversacion: historial de mensajes cronologico (inbound izq · outbound der).

import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { mobileApi } from '../api/client';
import type { MobileMessage } from '../api/types';

export default function ConversationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [msgs, setMsgs] = useState<MobileMessage[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(true);

  async function reload() {
    if (!id) { return; }
    setBusy(true); setError(null);
    try { setMsgs(await mobileApi.messages(id, 200)); }
    catch (e) { setError(e instanceof Error ? e.message : 'Error'); }
    finally { setBusy(false); }
  }
  useEffect(() => { void reload(); }, [id]);

  return (
    <div className="flex flex-col h-full">
      <header className="px-4 py-3 border-b border-slate-100 flex items-center gap-3">
        <button className="text-cubot-primary text-lg" onClick={() => navigate(-1)}>← </button>
        <h1 className="text-base font-semibold text-slate-900">Conversacion</h1>
        <button className="ml-auto btn-ghost" onClick={() => void reload()} disabled={busy}>{busy ? '…' : '↻'}</button>
      </header>
      <div className="flex-1 overflow-y-auto px-3 py-4 space-y-2 bg-slate-50">
        {error && <div className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{error}</div>}
        {!busy && msgs.length === 0 && (
          <div className="text-center text-sm text-slate-500 py-8">Sin mensajes.</div>
        )}
        {msgs.map((m) => (
          <div key={m.id} className={`flex ${m.direction === 'outbound' ? 'justify-end' : 'justify-start'}`}>
            <div className={`max-w-[80%] px-3 py-2 rounded-2xl text-sm ${
              m.direction === 'outbound'
                ? 'bg-cubot-primary text-white rounded-br-sm'
                : 'bg-white text-slate-900 rounded-bl-sm border border-slate-200'
            }`}>
              {m.body && <div className="whitespace-pre-wrap">{m.body}</div>}
              {m.mediaType && !m.body && <div className="italic opacity-75">[{m.mediaType}]</div>}
              <div className={`text-[10px] mt-1 ${m.direction === 'outbound' ? 'text-white/60' : 'text-slate-400'}`}>
                {new Date(m.sentAt).toLocaleTimeString('es-CO', { hour: '2-digit', minute: '2-digit' })}
                {m.sentByName && <> · {m.sentByName}</>}
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
