'use client';

import React from 'react';
import Link from 'next/link';
import { useAuth } from '@/context/AuthContext';
import { UserRole } from '@/types';
import { Lock, ShieldAlert, Home, LayoutDashboard, LogOut } from 'lucide-react';

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
  fallbackMessage = 'Khu vực này được bảo vệ và chỉ dành cho Quản trị viên hoặc Giáo viên.'
}) => {
  const { role, user, isLoading, signOut } = useAuth();

  if (isLoading) {
    return (
      <div className="min-h-[60vh] flex flex-col items-center justify-center gap-3 text-slate-400">
        <div className="w-8 h-8 rounded-full border-2 border-indigo-500 border-t-transparent animate-spin" />
        <span className="text-sm">Đang xác thực thông tin quyền truy cập…</span>
      </div>
    );
  }

  // Access Granted
  if (user.isAuthenticated && allowedRoles.includes(role)) {
    return <>{children}</>;
  }

  // Role translation helper
  const formatRole = (r: UserRole) => {
    switch (r) {
      case 'ADMIN': return 'Quản trị viên (ADMIN)';
      case 'TEACHER': return 'Giáo viên (TEACHER)';
      case 'STUDENT': return 'Học sinh (STUDENT)';
      default: return r;
    }
  };

  const isGuest = !user.isAuthenticated;

  return (
    <div className="min-h-[75vh] flex items-center justify-center p-4">
      <div className="max-w-lg w-full glass-panel rounded-3xl p-8 border border-rose-500/30 text-center space-y-6 shadow-2xl relative overflow-hidden">
        {/* Top Glow Accent */}
        <div className="absolute top-0 left-1/2 -translate-x-1/2 w-48 h-1 bg-gradient-to-r from-transparent via-rose-500 to-transparent" />

        {/* Icon */}
        <div className="w-16 h-16 rounded-2xl bg-rose-500/10 border border-rose-500/30 flex items-center justify-center mx-auto text-rose-400 shadow-lg shadow-rose-500/10">
          {isGuest ? <ShieldAlert className="w-8 h-8" /> : <Lock className="w-8 h-8" />}
        </div>

        {/* Title & Description */}
        <div className="space-y-2">
          <h2 className="text-2xl font-black text-white tracking-tight">
            {isGuest ? 'Yêu cầu đăng nhập Quản trị' : fallbackTitle}
          </h2>
          <p className="text-sm text-slate-400 leading-relaxed">
            {isGuest
              ? 'Trang này thuộc khu vực quản trị và hiệu đính ngân hàng câu hỏi. Bạn cần đăng nhập bằng tài khoản Quản trị viên để tiếp tục.'
              : fallbackMessage}
          </p>
        </div>

        {/* Role Diagnostic Pill (If logged in but wrong role) */}
        {!isGuest && (
          <div className="p-3.5 rounded-2xl bg-slate-900/80 border border-white/10 text-xs text-left space-y-1.5">
            <div className="flex justify-between items-center">
              <span className="text-slate-400">Tài khoản hiện tại:</span>
              <span className="font-semibold text-slate-200">{user.name || user.email}</span>
            </div>
            <div className="flex justify-between items-center">
              <span className="text-slate-400">Vai trò của bạn:</span>
              <span className="font-bold text-amber-400 bg-amber-500/10 border border-amber-500/20 px-2 py-0.5 rounded-md">
                {formatRole(role)}
              </span>
            </div>
            <div className="flex justify-between items-center">
              <span className="text-slate-400">Quyền yêu cầu:</span>
              <span className="font-mono text-indigo-300">
                {allowedRoles.map(formatRole).join(' hoặc ')}
              </span>
            </div>
          </div>
        )}

        {/* Action Navigation Buttons */}
        <div className="flex flex-col sm:flex-row items-center justify-center gap-3 pt-2">
          {isGuest ? (
            <>
              <Link
                href="/login"
                className="w-full sm:w-auto inline-flex items-center justify-center gap-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 px-5 py-2.5 text-sm font-bold text-white transition-all shadow-md shadow-indigo-600/20"
              >
                <span>Đăng nhập</span>
              </Link>
              <Link
                href="/"
                className="w-full sm:w-auto inline-flex items-center justify-center gap-2 rounded-xl border border-white/10 hover:bg-white/5 px-5 py-2.5 text-sm font-semibold text-slate-300 transition-colors"
              >
                <Home className="w-4 h-4" />
                <span>Về Trang chủ</span>
              </Link>
            </>
          ) : (
            <>
              <Link
                href="/"
                className="w-full sm:w-auto inline-flex items-center justify-center gap-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 px-4 py-2.5 text-xs font-bold text-white transition-all shadow-md shadow-indigo-600/20"
              >
                <Home className="w-4 h-4" />
                <span>Về Trang chủ</span>
              </Link>
              <Link
                href="/dashboard"
                className="w-full sm:w-auto inline-flex items-center justify-center gap-2 rounded-xl border border-white/10 hover:bg-white/5 px-4 py-2.5 text-xs font-semibold text-slate-300 transition-colors"
              >
                <LayoutDashboard className="w-4 h-4" />
                <span>Bảng điều khiển học tập</span>
              </Link>
              <button
                onClick={() => void signOut()}
                className="w-full sm:w-auto inline-flex items-center justify-center gap-1.5 rounded-xl border border-rose-500/20 hover:bg-rose-500/10 px-3 py-2.5 text-xs font-semibold text-rose-300 transition-colors"
                title="Đăng xuất để đổi tài khoản khác"
              >
                <LogOut className="w-3.5 h-3.5" />
                <span>Đăng xuất</span>
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
};

