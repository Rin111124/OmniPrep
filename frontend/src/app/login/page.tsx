'use client';

import { FormEvent, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { getSupabaseBrowserClient } from '@/lib/supabase/client';

export default function LoginPage() {
  const router = useRouter();
  const [mode, setMode] = useState<'signin' | 'signup'>('signin');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [fullName, setFullName] = useState('');
  const [message, setMessage] = useState('');
  const [isBusy, setIsBusy] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage('');
    setIsBusy(true);
    try {
      const supabase = getSupabaseBrowserClient();
      if (mode === 'signin') {
        const { error } = await supabase.auth.signInWithPassword({ email, password });
        if (error) throw error;
        router.replace('/dashboard');
        router.refresh();
      } else {
        const { data, error } = await supabase.auth.signUp({
          email,
          password,
          options: {
            data: { full_name: fullName.trim() },
            emailRedirectTo: `${window.location.origin}/auth/callback`
          }
        });
        if (error) throw error;
        if (data.session) {
          router.replace('/dashboard');
          router.refresh();
        } else {
          setMessage('Tài khoản đã tạo. Hãy kiểm tra email để xác nhận trước khi đăng nhập.');
        }
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Không thể xác thực tài khoản.');
    } finally {
      setIsBusy(false);
    }
  }

  return (
    <main className="min-h-screen bg-[#07090e] px-4 py-16 text-white">
      <section className="mx-auto max-w-md rounded-3xl border border-white/10 bg-slate-950/80 p-8 shadow-2xl">
        <Link href="/" className="text-sm font-semibold text-indigo-300 hover:text-indigo-200">← OmniPrep</Link>
        <h1 className="mt-8 text-3xl font-black">{mode === 'signin' ? 'Đăng nhập' : 'Tạo tài khoản'}</h1>
        <p className="mt-2 text-sm text-slate-400">Dùng tài khoản OmniPrep để lưu bài thi và tiến độ học tập.</p>

        <form onSubmit={handleSubmit} className="mt-8 space-y-4">
          {mode === 'signup' && (
            <label className="block text-sm text-slate-300">
              Họ và tên
              <input required autoComplete="name" value={fullName} onChange={event => setFullName(event.target.value)} className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2.5 text-white outline-none focus:border-indigo-400" />
            </label>
          )}
          <label className="block text-sm text-slate-300">
            Email
            <input required type="email" autoComplete="email" value={email} onChange={event => setEmail(event.target.value)} className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2.5 text-white outline-none focus:border-indigo-400" />
          </label>
          <label className="block text-sm text-slate-300">
            Mật khẩu
            <input required type="password" minLength={8} autoComplete={mode === 'signin' ? 'current-password' : 'new-password'} value={password} onChange={event => setPassword(event.target.value)} className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2.5 text-white outline-none focus:border-indigo-400" />
          </label>
          <button disabled={isBusy} className="w-full rounded-xl bg-indigo-600 px-4 py-3 font-bold transition hover:bg-indigo-500 disabled:opacity-60">
            {isBusy ? 'Đang xử lý…' : mode === 'signin' ? 'Đăng nhập' : 'Tạo tài khoản'}
          </button>
        </form>

        {message && <p role="status" className="mt-4 rounded-xl border border-amber-400/20 bg-amber-400/10 p-3 text-sm text-amber-100">{message}</p>}
        <button type="button" onClick={() => { setMode(mode === 'signin' ? 'signup' : 'signin'); setMessage(''); }} className="mt-5 text-sm text-indigo-300 hover:text-indigo-200">
          {mode === 'signin' ? 'Chưa có tài khoản? Tạo tài khoản' : 'Đã có tài khoản? Đăng nhập'}
        </button>
      </section>
    </main>
  );
}
