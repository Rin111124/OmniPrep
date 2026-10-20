'use client';

import React, { useState } from 'react';
import { useAuth } from '@/context/AuthContext';
import { UserRole } from '@/types';
import { Shield, ChevronUp, ChevronDown, Check, RotateCcw } from 'lucide-react';

export const DevRoleSwitcher: React.FC = () => {
  const { role, actualRole, devRole, setDevRole } = useAuth();
  const [isOpen, setIsOpen] = useState(false);

  // Strictly only available in local development mode
  if (process.env.NODE_ENV !== 'development' || !setDevRole) {
    return null;
  }

  const roles: Array<{ key: UserRole; label: string; badge: string; color: string }> = [
    { key: 'STUDENT', label: 'Học sinh (Người dùng thường)', badge: 'STUDENT', color: 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30' },
    { key: 'TEACHER', label: 'Giáo viên (Kiểm duyệt câu hỏi)', badge: 'TEACHER', color: 'bg-blue-500/20 text-blue-300 border-blue-500/30' },
    { key: 'ADMIN', label: 'Quản trị viên (Toàn quyền hệ thống)', badge: 'ADMIN', color: 'bg-purple-500/20 text-purple-300 border-purple-500/30' },
  ];

  return (
    <div className="fixed bottom-3 right-3 sm:bottom-4 sm:right-4 z-50 font-sans text-xs">
      <div className="rounded-2xl border border-indigo-500/30 bg-slate-950/95 shadow-2xl backdrop-blur-xl overflow-hidden max-w-[calc(100vw-24px)]">
        {/* Header Toggle */}
        <button
          onClick={() => setIsOpen(!isOpen)}
          className="flex items-center gap-1.5 sm:gap-2.5 px-2.5 py-1.5 sm:px-3.5 sm:py-2 hover:bg-white/5 transition-colors w-full text-left touch-manipulation"
          title="Bảng chuyển đổi vai trò xem trước (Chỉ xuất hiện khi phát triển local)"
        >
          <div className="flex h-5 w-5 items-center justify-center rounded-lg bg-indigo-500/20 text-indigo-400 shrink-0">
            <Shield className="h-3.5 w-3.5" />
          </div>
          <div className="flex items-center gap-1 font-semibold text-slate-200">
            <span className="hidden sm:inline">Dev Role:</span>
            <span className={`px-1.5 sm:px-2 py-0.5 rounded-md font-mono text-[9px] sm:text-[10px] font-bold uppercase border ${
              role === 'ADMIN'
                ? 'bg-purple-500/20 text-purple-300 border-purple-500/40'
                : role === 'TEACHER'
                ? 'bg-blue-500/20 text-blue-300 border-blue-500/40'
                : 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40'
            }`}>
              {role}
            </span>
            {devRole && (
              <span className="text-[9px] sm:text-[10px] text-amber-400 font-bold hidden xs:inline">(Giả lập)</span>
            )}
          </div>
          <div className="ml-1 sm:ml-2 text-slate-400 shrink-0">
            {isOpen ? <ChevronDown className="h-3.5 w-3.5 sm:h-4 sm:w-4" /> : <ChevronUp className="h-3.5 w-3.5 sm:h-4 sm:w-4" />}
          </div>
        </button>

        {/* Dropdown Options */}
        {isOpen && (
          <div className="p-3 border-t border-white/10 space-y-2 w-72 max-w-[calc(100vw-36px)]">
            <p className="text-[11px] text-slate-400 leading-relaxed">
              Kiểm tra trực quan giao diện theo vai trò (chỉ chạy trên môi trường dev):
            </p>

            <div className="space-y-1">
              {roles.map((r) => {
                const isActive = role === r.key;
                return (
                  <button
                    key={r.key}
                    onClick={() => {
                      setDevRole(r.key);
                    }}
                    className={`flex items-center justify-between w-full p-2 rounded-xl transition-all text-left ${
                      isActive
                        ? 'bg-indigo-600/20 border border-indigo-500/40 text-white'
                        : 'text-slate-300 hover:bg-white/5 border border-transparent'
                    }`}
                  >
                    <div className="space-y-0.5">
                      <div className="font-semibold text-xs text-white">{r.label}</div>
                      <span className={`inline-block px-1.5 py-0.2 rounded text-[9px] font-mono border ${r.color}`}>
                        {r.badge}
                      </span>
                    </div>
                    {isActive && <Check className="h-4 w-4 text-cyan-400 ml-2" />}
                  </button>
                );
              })}
            </div>

            {devRole && (
              <button
                onClick={() => setDevRole(null)}
                className="flex items-center justify-center gap-1.5 w-full mt-2 py-1.5 rounded-lg border border-white/10 text-[11px] text-slate-400 hover:text-white hover:bg-white/5 transition-colors"
              >
                <RotateCcw className="h-3 w-3" />
                <span>Khôi phục quyền gốc ({actualRole})</span>
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
};
