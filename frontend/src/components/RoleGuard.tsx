'use client';

import React from 'react';
import Link from 'next/link';
import { useAuth } from '@/context/AuthContext';
import { UserRole } from '@/types';
import { Lock, ShieldAlert } from 'lucide-react';

interface Props {
  allowedRoles: UserRole[];
  children: React.ReactNode;
  fallbackTitle?: string;
  fallbackMessage?: string;
}

export const RoleGuard: React.FC<Props> = ({
  allowedRoles,
  children,
  fallbackTitle = 'Không đủ quyền truy cập',
  fallbackMessage = 'Tài khoản của bạn không có quyền sử dụng khu vực này.'
}) => {
  const { role, user, isLoading } = useAuth();

  if (isLoading) return <div className="min-h-[50vh] grid place-items-center text-sm text-slate-400">Đang kiểm tra tài khoản…</div>;
  if (user.isAuthenticated && allowedRoles.includes(role)) return <>{children}</>;

  const title = user.isAuthenticated ? fallbackTitle : 'Đăng nhập để tiếp tục';
  const message = user.isAuthenticated ? fallbackMessage : 'Bạn cần đăng nhập bằng tài khoản OmniPrep để truy cập trang này.';

  return (
    <div className="min-h-[70vh] flex items-center justify-center p-4">
      <div className="max-w-xl w-full glass-panel rounded-3xl p-8 border border-rose-500/30 text-center space-y-5">
        <div className="w-16 h-16 rounded-2xl bg-rose-500/10 border border-rose-500/30 flex items-center justify-center mx-auto text-rose-400">
          {user.isAuthenticated ? <Lock className="w-8 h-8" /> : <ShieldAlert className="w-8 h-8" />}
        </div>
        <h2 className="text-2xl font-black text-white">{title}</h2>
        <p className="text-sm text-slate-400">{message}</p>
        {!user.isAuthenticated && <Link href="/login" className="inline-flex rounded-xl bg-indigo-600 px-5 py-2.5 text-sm font-bold text-white hover:bg-indigo-500">Đăng nhập</Link>}
      </div>
    </div>
  );
};
