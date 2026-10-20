'use client';

import React, { useState } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useAuth } from '@/context/AuthContext';
import {
  Sparkles,
  BookOpen,
  BarChart3,
  Database,
  GraduationCap,
  Cpu,
  BookMarked,
  Activity,
  Menu,
  X,
  Settings
} from 'lucide-react';
import { SystemStatusModal } from './SystemStatusModal';
import { DevRoleSwitcher } from './DevRoleSwitcher';

export const Navbar: React.FC = () => {
  const pathname = usePathname();
  const { role, user, canAccessAdmin, signOut } = useAuth();
  const [isStatusModalOpen, setIsStatusModalOpen] = useState(false);
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

  // Core navigation for all students and visitors
  const navLinks = [
    { href: '/', label: 'Trang chủ', icon: BookOpen },
    { href: '/exam', label: 'Phòng thi THPT', icon: GraduationCap },
    { href: '/mistakes', label: 'Sổ tay câu sai', icon: BookMarked },
    { href: '/dashboard', label: 'Bản đồ Năng lực', icon: BarChart3 },
    // Admin / Staff route: ONLY shown if user has admin/teacher privileges
    ...(canAccessAdmin ? [{
      href: '/admin',
      label: role === 'ADMIN' ? 'Bóc tách & Quản trị' : 'Kiểm duyệt đề thi',
      icon: Database,
      isAdminBadge: true
    }] : [])
  ];

  const roleLabel = role === 'ADMIN' ? 'Quản trị viên' : role === 'TEACHER' ? 'Giáo viên' : 'Học sinh';

  return (
    <>
      <header className="sticky top-0 z-40 glass-panel border-b border-white/10 backdrop-blur-xl">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between h-16">
            {/* Logo & Tagline */}
            <Link href="/" className="flex items-center gap-2.5 sm:gap-3 group shrink-0">
              <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-xl bg-gradient-to-tr from-indigo-600 via-indigo-500 to-cyan-400 flex items-center justify-center shadow-lg shadow-indigo-500/30 group-hover:scale-105 transition-transform">
                <Sparkles className="w-4 h-4 sm:w-5 sm:h-5 text-white" />
              </div>
              <div>
                <div className="flex items-center gap-1.5 sm:gap-2">
                  <span className="text-lg sm:text-xl font-extrabold tracking-tight bg-gradient-to-r from-white via-slate-100 to-indigo-200 bg-clip-text text-transparent">
                    OmniPrep
                  </span>
                  <span className="text-[9px] sm:text-[10px] font-bold uppercase tracking-wider px-1.5 sm:px-2 py-0.5 rounded-full bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
                    Prep12.vn
                  </span>
                </div>
                <p className="text-[10px] sm:text-[11px] text-slate-400 font-medium hidden sm:block">
                  Cấu trúc đề chuẩn Bộ GD&ĐT 2025
                </p>
              </div>
            </Link>

            {/* Desktop Navigation Links */}
            <nav className="hidden md:flex items-center gap-1">
              {navLinks.map((link) => {
                const Icon = link.icon;
                const isActive = pathname === link.href || (link.href !== '/' && pathname.startsWith(link.href));
                const isAdminItem = 'isAdminBadge' in link && link.isAdminBadge;

                return (
                  <Link
                    key={link.href}
                    href={link.href}
                    className={`flex items-center gap-2 px-3 py-2 rounded-lg text-xs lg:text-sm font-medium transition-all ${
                      isActive
                        ? isAdminItem
                          ? 'bg-purple-600/20 text-purple-300 border border-purple-500/40 shadow-sm'
                          : 'bg-indigo-600/20 text-indigo-300 border border-indigo-500/30 shadow-sm'
                        : isAdminItem
                          ? 'text-purple-300/80 hover:text-purple-200 hover:bg-purple-500/10 border border-purple-500/20'
                          : 'text-slate-300 hover:text-white hover:bg-white/5'
                    }`}
                  >
                    <Icon className={`w-4 h-4 ${isActive ? (isAdminItem ? 'text-purple-400' : 'text-indigo-400') : (isAdminItem ? 'text-purple-400/80' : 'text-slate-400')}`} />
                    <span>{link.label}</span>
                    {isAdminItem && (
                      <span className="text-[9px] uppercase font-mono px-1.5 py-0.2 rounded bg-purple-500/30 text-purple-200 border border-purple-500/40 font-bold">
                        {role === 'ADMIN' ? 'Admin' : 'GV'}
                      </span>
                    )}
                  </Link>
                );
              })}
            </nav>

            {/* Account and system health (Desktop) */}
            <div className="hidden md:flex items-center gap-3">
              {user.isAuthenticated ? (
                <div className="flex items-center gap-2.5 text-xs text-slate-300">
                  <div className="flex items-center gap-1.5">
                    <span className="font-medium text-white max-w-[130px] truncate">{user.name}</span>
                    <span className={`text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-full border ${
                      role === 'ADMIN'
                        ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
                        : role === 'TEACHER'
                        ? 'bg-blue-500/20 text-blue-300 border-blue-500/30'
                        : 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30'
                    }`}>
                      {roleLabel}
                    </span>
                  </div>
                  <Link
                    href="/settings"
                    className={`rounded-lg border p-1.5 transition-colors ${
                      pathname === '/settings'
                        ? 'border-indigo-500 bg-indigo-500/20 text-indigo-300'
                        : 'border-white/10 hover:bg-white/5 text-slate-400 hover:text-white'
                    }`}
                    title="Cài đặt & Quyền riêng tư dữ liệu"
                  >
                    <Settings className="w-3.5 h-3.5" />
                  </Link>
                  <button onClick={() => void signOut()} className="rounded-lg border border-white/10 px-3 py-1.5 hover:bg-white/5 transition-colors">
                    Đăng xuất
                  </button>
                </div>
              ) : (
                <Link href="/login" className="rounded-lg bg-indigo-600 px-3.5 py-1.5 text-xs font-semibold text-white hover:bg-indigo-500 transition-colors shadow-sm shadow-indigo-600/30">
                  Đăng nhập
                </Link>
              )}

              {/* AI & System Health Pill (Chỉ hiển thị cho Ban Quản trị / Giáo viên) */}
              {canAccessAdmin && (
                <button
                  onClick={() => setIsStatusModalOpen(true)}
                  className="flex items-center gap-2 px-3.5 py-1.5 rounded-full bg-slate-900/80 border border-emerald-500/30 hover:border-emerald-400/60 text-xs transition-all cursor-pointer group"
                  title="Bấm để kiểm tra trạng thái Backend & Gemini thời gian thực (Khu vực Quản trị)"
                >
                  <span className="relative flex h-2 w-2">
                    <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"></span>
                    <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-500"></span>
                  </span>
                  <span className="text-slate-300 flex items-center gap-1.5 font-medium group-hover:text-white">
                    <Cpu className="w-3.5 h-3.5 text-emerald-400" />
                    Gemini 2.0 (0đ)
                  </span>
                  <Activity className="w-3 h-3 text-slate-400 group-hover:text-cyan-300 ml-0.5" />
                </button>
              )}
            </div>

            {/* Mobile Header Right Controls */}
            <div className="flex md:hidden items-center gap-2">
              {/* Quick status pill for admin on mobile */}
              {canAccessAdmin && (
                <button
                  onClick={() => setIsStatusModalOpen(true)}
                  className="p-2 rounded-xl bg-slate-900 border border-white/10 text-emerald-400"
                  title="Trạng thái hệ thống"
                  aria-label="Trạng thái hệ thống"
                >
                  <Activity className="w-4 h-4" />
                </button>
              )}

              {/* Mobile Role badge or Login Link */}
              {user.isAuthenticated ? (
                <span className={`text-[10px] font-bold uppercase px-2 py-1 rounded-lg border ${
                  role === 'ADMIN'
                    ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
                    : role === 'TEACHER'
                    ? 'bg-blue-500/20 text-blue-300 border-blue-500/30'
                    : 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30'
                }`}>
                  {role === 'ADMIN' ? 'Admin' : role === 'TEACHER' ? 'GV' : 'HS'}
                </span>
              ) : (
                <Link href="/login" className="rounded-lg bg-indigo-600 px-2.5 py-1 text-xs font-semibold text-white">
                  Đăng nhập
                </Link>
              )}

              {/* Hamburger Button */}
              <button
                onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
                className="p-2 rounded-xl bg-slate-900 border border-white/10 text-slate-300 hover:text-white active:scale-95 transition-all"
                aria-label="Toggle navigation menu"
              >
                {isMobileMenuOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
              </button>
            </div>
          </div>
        </div>

        {/* Mobile Dropdown Menu (Full Native-feel Mobile Drawer) */}
        {isMobileMenuOpen && (
          <div className="md:hidden border-t border-white/10 bg-slate-950/98 backdrop-blur-2xl p-4 space-y-3 animate-in slide-in-from-top-2 duration-200 shadow-2xl">
            {/* User Profile Card in Mobile Menu */}
            {user.isAuthenticated && (
              <div className="p-3.5 rounded-2xl bg-slate-900/80 border border-white/10 flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="w-9 h-9 rounded-xl bg-indigo-600/30 border border-indigo-500/40 flex items-center justify-center font-bold text-indigo-300 text-sm">
                    {(user.name || user.email || 'U').slice(0, 1).toUpperCase()}
                  </div>
                  <div className="overflow-hidden">
                    <p className="text-xs font-bold text-white truncate max-w-[170px]">{user.name}</p>
                    <p className="text-[10px] text-slate-400 truncate max-w-[170px]">{user.email}</p>
                  </div>
                </div>
                <span className={`text-[10px] font-bold uppercase px-2 py-0.5 rounded-full border ${
                  role === 'ADMIN'
                    ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
                    : role === 'TEACHER'
                    ? 'bg-blue-500/20 text-blue-300 border-blue-500/30'
                    : 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30'
                }`}>
                  {roleLabel}
                </span>
              </div>
            )}

            {/* Navigation Links */}
            <div className="space-y-1">
              {navLinks.map((link) => {
                const Icon = link.icon;
                const isActive = pathname === link.href || (link.href !== '/' && pathname.startsWith(link.href));
                const isAdminItem = 'isAdminBadge' in link && link.isAdminBadge;

                return (
                  <Link
                    key={link.href}
                    href={link.href}
                    onClick={() => setIsMobileMenuOpen(false)}
                    className={`flex items-center justify-between px-4 py-3 rounded-xl text-sm font-semibold transition-all ${
                      isActive
                        ? isAdminItem
                          ? 'bg-purple-600/20 text-purple-300 border border-purple-500/40'
                          : 'bg-indigo-600/20 text-indigo-300 border border-indigo-500/30'
                        : isAdminItem
                          ? 'text-purple-300 bg-purple-500/10 border border-purple-500/20'
                          : 'text-slate-300 hover:bg-white/5'
                    }`}
                  >
                    <div className="flex items-center gap-3">
                      <Icon className="w-4 h-4 text-slate-400" />
                      <span>{link.label}</span>
                    </div>
                    {isAdminItem && (
                      <span className="text-[10px] uppercase font-mono px-2 py-0.5 rounded bg-purple-500/30 text-purple-200 border border-purple-500/40 font-bold">
                        {role === 'ADMIN' ? 'Admin' : 'Giáo viên'}
                      </span>
                    )}
                  </Link>
                );
              })}
            </div>

            {/* Mobile Settings and Sign Out */}
            {user.isAuthenticated && (
              <div className="pt-2 border-t border-white/5 space-y-2">
                <Link
                  href="/settings"
                  onClick={() => setIsMobileMenuOpen(false)}
                  className={`flex items-center gap-3 px-4 py-3 rounded-xl text-sm font-semibold transition-colors ${
                    pathname === '/settings' ? 'bg-indigo-600/20 text-indigo-300 border border-indigo-500/30' : 'text-slate-300 hover:bg-white/5'
                  }`}
                >
                  <Settings className="w-4 h-4 text-slate-400" />
                  <span>Cài đặt & Quyền riêng tư</span>
                </Link>
                <button
                  onClick={() => {
                    setIsMobileMenuOpen(false);
                    void signOut();
                  }}
                  className="w-full py-2.5 rounded-xl border border-rose-500/20 text-rose-300 hover:bg-rose-500/10 text-xs font-semibold transition-colors"
                >
                  Đăng xuất tài khoản
                </button>
              </div>
            )}
          </div>
        )}
      </header>

      {/* System Status Transparent Inspector Modal */}
      <SystemStatusModal
        isOpen={isStatusModalOpen}
        onClose={() => setIsStatusModalOpen(false)}
      />

      {/* Local Development Role Switcher for instant UI preview */}
      <DevRoleSwitcher />
    </>
  );
};

