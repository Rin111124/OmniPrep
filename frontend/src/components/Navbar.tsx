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
  Lock,
} from 'lucide-react';
import { SystemStatusModal } from './SystemStatusModal';

export const Navbar: React.FC = () => {
  const pathname = usePathname();
  const { role, user, canAccessAdmin, signOut } = useAuth();
  const [isStatusModalOpen, setIsStatusModalOpen] = useState(false);
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

  const navLinks = [
    { href: '/', label: 'Trang chủ', icon: BookOpen },
    { href: '/exam', label: 'Phòng thi THPT', icon: GraduationCap },
    { href: '/mistakes', label: 'Sổ tay câu sai', icon: BookMarked },
    { href: '/dashboard', label: 'Bản đồ Năng lực', icon: BarChart3 },
    { 
      href: '/admin', 
      label: 'Bóc tách Đề', 
      icon: Database,
      restricted: !canAccessAdmin
    },
  ];

  const roleLabel = role === 'ADMIN' ? 'Quản trị viên' : role === 'TEACHER' ? 'Giáo viên' : 'Học sinh';

  return (
    <>
      <header className="sticky top-0 z-40 glass-panel border-b border-white/10 backdrop-blur-xl">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between h-16">
            {/* Logo & Tagline */}
            <Link href="/" className="flex items-center gap-3 group">
              <div className="w-10 h-10 rounded-xl bg-gradient-to-tr from-indigo-600 via-indigo-500 to-cyan-400 flex items-center justify-center shadow-lg shadow-indigo-500/30 group-hover:scale-105 transition-transform">
                <Sparkles className="w-5 h-5 text-white" />
              </div>
              <div>
                <div className="flex items-center gap-2">
                  <span className="text-xl font-extrabold tracking-tight bg-gradient-to-r from-white via-slate-100 to-indigo-200 bg-clip-text text-transparent">
                    OmniPrep
                  </span>
                  <span className="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-full bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
                    Prep12.vn
                  </span>
                </div>
                <p className="text-[11px] text-slate-400 font-medium">
                  Cấu trúc đề chuẩn Bộ GD&ĐT 2025
                </p>
              </div>
            </Link>

            {/* Desktop Navigation Links */}
            <nav className="hidden md:flex items-center gap-1">
              {navLinks.map((link) => {
                const Icon = link.icon;
                const isActive = pathname === link.href || (link.href !== '/' && pathname.startsWith(link.href));
                return (
                  <Link
                    key={link.href}
                    href={link.href}
                    className={`flex items-center gap-2 px-3 py-2 rounded-lg text-xs lg:text-sm font-medium transition-all ${
                      isActive
                        ? 'bg-indigo-600/20 text-indigo-300 border border-indigo-500/30 shadow-sm'
                        : 'text-slate-300 hover:text-white hover:bg-white/5'
                    }`}
                  >
                    <Icon className={`w-4 h-4 ${isActive ? 'text-indigo-400' : 'text-slate-400'}`} />
                    <span>{link.label}</span>
                    {link.restricted && (
                      <span title="Khu vực dành cho Giáo viên & Quản trị">
                        <Lock className="w-3 h-3 text-amber-400/80 ml-0.5" />
                      </span>
                    )}
                  </Link>
                );
              })}
            </nav>

            {/* Account and system health */}
            <div className="hidden lg:flex items-center gap-3">
              {user.isAuthenticated ? (
                <div className="flex items-center gap-3 text-xs text-slate-300">
                  <span>{user.name} · {roleLabel}</span>
                  <button onClick={() => void signOut()} className="rounded-lg border border-white/10 px-3 py-1.5 hover:bg-white/5">Đăng xuất</button>
                </div>
              ) : (
                <Link href="/login" className="rounded-lg bg-indigo-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-indigo-500">Đăng nhập</Link>
              )}

              {/* AI & System Health Pill (Clickable) */}
              <button
                onClick={() => setIsStatusModalOpen(true)}
                className="flex items-center gap-2 px-3.5 py-1.5 rounded-full bg-slate-900/80 border border-emerald-500/30 hover:border-emerald-400/60 text-xs transition-all cursor-pointer group"
                title="Bấm để kiểm tra trạng thái Backend & Gemini thời gian thực"
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
            </div>

            {/* Mobile Menu Button */}
            <div className="flex md:hidden items-center gap-2">
              {user.isAuthenticated
                ? <button onClick={() => void signOut()} className="rounded-lg border border-white/10 px-2.5 py-1 text-[11px]">Đăng xuất</button>
                : <Link href="/login" className="rounded-lg border border-white/10 px-2.5 py-1 text-[11px]">Đăng nhập</Link>}

              <button
                onClick={() => setIsStatusModalOpen(true)}
                className="p-2 rounded-xl bg-slate-900 border border-white/10 text-emerald-400"
                title="Trạng thái hệ thống"
              >
                <Activity className="w-4 h-4" />
              </button>
              <button
                onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
                className="p-2 rounded-xl bg-slate-900 border border-white/10 text-slate-300 hover:text-white"
              >
                {isMobileMenuOpen ? <X className="w-5 h-5" /> : <Menu className="w-5 h-5" />}
              </button>
            </div>
          </div>
        </div>

        {/* Mobile Dropdown Menu */}
        {isMobileMenuOpen && (
          <div className="md:hidden border-t border-white/10 bg-slate-950/95 p-4 space-y-2 animate-in slide-in-from-top-2 duration-200">
            {navLinks.map((link) => {
              const Icon = link.icon;
              const isActive = pathname === link.href || (link.href !== '/' && pathname.startsWith(link.href));
              return (
                <Link
                  key={link.href}
                  href={link.href}
                  onClick={() => setIsMobileMenuOpen(false)}
                  className={`flex items-center justify-between px-4 py-2.5 rounded-xl text-sm font-semibold transition-all ${
                    isActive
                      ? 'bg-indigo-600/20 text-indigo-300 border border-indigo-500/30'
                      : 'text-slate-300 hover:bg-white/5'
                  }`}
                >
                  <div className="flex items-center gap-3">
                    <Icon className="w-4 h-4" />
                    <span>{link.label}</span>
                  </div>
                  {link.restricted && <Lock className="w-3.5 h-3.5 text-amber-400" />}
                </Link>
              );
            })}
          </div>
        )}
      </header>

      {/* System Status Transparent Inspector Modal */}
      <SystemStatusModal
        isOpen={isStatusModalOpen}
        onClose={() => setIsStatusModalOpen(false)}
      />
    </>
  );
};
