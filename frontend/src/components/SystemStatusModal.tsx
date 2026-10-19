'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { Activity, AlertCircle, CheckCircle2, Cpu, Database, RefreshCw, Server, X } from 'lucide-react';

interface HealthResponse {
  status: string;
  timestamp: string;
  database?: { connected: boolean };
  gemini?: { configured: boolean; model?: string; embeddingModel?: string };
}

interface SystemStatusModalProps {
  isOpen: boolean;
  onClose: () => void;
}

function StatusPill({ ready, readyText, missingText }: { ready: boolean; readyText: string; missingText: string }) {
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold ${ready ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300' : 'border-amber-500/30 bg-amber-500/10 text-amber-200'}`}>
      {ready ? <CheckCircle2 className="h-3.5 w-3.5" /> : <AlertCircle className="h-3.5 w-3.5" />}
      {ready ? readyText : missingText}
    </span>
  );
}

export const SystemStatusModal: React.FC<SystemStatusModalProps> = ({ isOpen, onClose }) => {
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [latencyMs, setLatencyMs] = useState<number | null>(null);
  const [isChecking, setIsChecking] = useState(false);
  const [errorMessage, setErrorMessage] = useState('');

  const checkHealth = useCallback(async () => {
    setIsChecking(true);
    setErrorMessage('');
    const startedAt = performance.now();
    try {
      const response = await fetch('/api/health', { cache: 'no-store' });
      if (!response.ok) throw new Error('Backend chưa phản hồi trạng thái sức khỏe.');
      const data: HealthResponse = await response.json();
      setHealth(data);
      setLatencyMs(Math.round(performance.now() - startedAt));
    } catch {
      setHealth(null);
      setLatencyMs(null);
      setErrorMessage('Không thể kết nối backend.');
    } finally {
      setIsChecking(false);
    }
  }, []);

  useEffect(() => {
    if (!isOpen) return;
    const frame = requestAnimationFrame(() => { void checkHealth(); });
    return () => cancelAnimationFrame(frame);
  }, [isOpen, checkHealth]);

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-black/70 p-4 backdrop-blur-md">
      <section role="dialog" aria-modal="true" aria-labelledby="system-status-title" className="w-full max-w-lg space-y-5 rounded-3xl border border-white/10 bg-slate-950 p-6 shadow-2xl">
        <header className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <Activity className="h-5 w-5 text-indigo-300" />
            <h2 id="system-status-title" className="text-lg font-bold text-white">Trạng thái dịch vụ</h2>
          </div>
          <button onClick={onClose} aria-label="Đóng" className="rounded-lg p-2 text-slate-400 hover:bg-white/5 hover:text-white"><X className="h-4 w-4" /></button>
        </header>

        {errorMessage && <p role="alert" className="rounded-xl border border-rose-500/20 bg-rose-950/30 p-3 text-sm text-rose-200">{errorMessage}</p>}
        <div className="space-y-3">
          <div className="flex items-center justify-between rounded-2xl border border-white/10 bg-slate-900/70 p-4">
            <div className="flex items-center gap-3"><Server className="h-5 w-5 text-indigo-300" /><div><div className="text-sm font-semibold text-white">Backend API</div><div className="text-xs text-slate-400">{latencyMs === null ? 'Chưa có phản hồi' : `${latencyMs} ms`}</div></div></div>
            <StatusPill ready={health !== null} readyText="Kết nối" missingText="Ngoại tuyến" />
          </div>
          <div className="flex items-center justify-between rounded-2xl border border-white/10 bg-slate-900/70 p-4">
            <div className="flex items-center gap-3"><Database className="h-5 w-5 text-cyan-300" /><span className="text-sm font-semibold text-white">PostgreSQL</span></div>
            <StatusPill ready={Boolean(health?.database?.connected)} readyText="Kết nối" missingText="Chưa kết nối" />
          </div>
          <div className="flex items-center justify-between rounded-2xl border border-white/10 bg-slate-900/70 p-4">
            <div className="flex items-center gap-3"><Cpu className="h-5 w-5 text-violet-300" /><div><div className="text-sm font-semibold text-white">Gemini</div><div className="text-xs text-slate-400">{health?.gemini?.model || 'Chưa có thông tin'}</div></div></div>
            <StatusPill ready={Boolean(health?.gemini?.configured)} readyText="Đã cấu hình" missingText="Chưa cấu hình" />
          </div>
        </div>

        <footer className="flex justify-between">
          <button onClick={() => void checkHealth()} disabled={isChecking} className="inline-flex items-center gap-2 rounded-xl border border-white/10 px-4 py-2.5 text-xs font-semibold text-slate-200 hover:bg-white/5 disabled:opacity-50">
            <RefreshCw className={`h-3.5 w-3.5 ${isChecking ? 'animate-spin' : ''}`} /> Kiểm tra lại
          </button>
          <button onClick={onClose} className="rounded-xl bg-indigo-600 px-5 py-2.5 text-xs font-bold text-white hover:bg-indigo-500">Đóng</button>
        </footer>
      </section>
    </div>
  );
};
