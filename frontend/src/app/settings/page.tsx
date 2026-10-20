'use client';

import React, { useState } from 'react';
import Link from 'next/link';
import { useAuth } from '@/context/AuthContext';
import { exportUserData, deleteUserData } from '@/lib/api';
import {
  Shield,
  Download,
  Trash2,
  AlertTriangle,
  FileText,
  Clock,
  Lock,
  Database,
  CheckCircle2,
  ArrowLeft,
  RefreshCw,
  User
} from 'lucide-react';

export default function SettingsPage() {
  const { user, role, isLoading } = useAuth();
  const [isExporting, setIsExporting] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [confirmDeleteText, setConfirmDeleteText] = useState('');
  const [statusMessage, setStatusMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  const handleExportData = async () => {
    try {
      setIsExporting(true);
      setStatusMessage(null);
      const data = await exportUserData();

      // Trigger file download
      const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `omniprep-data-${user.id || 'export'}-${new Date().toISOString().slice(0, 10)}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);

      setStatusMessage({ type: 'success', text: 'Xuất dữ liệu học tập thành công! File JSON đã được tải về máy.' });
    } catch {
      setStatusMessage({ type: 'error', text: 'Không thể xuất dữ liệu. Vui lòng kiểm tra lại kết nối mạng.' });
    } finally {
      setIsExporting(false);
    }
  };

  const handleDeleteData = async () => {
    if (confirmDeleteText !== 'XÓA DỮ LIỆU') return;

    try {
      setIsDeleting(true);
      setStatusMessage(null);
      const res = await deleteUserData();
      setDeleteModalOpen(false);
      setConfirmDeleteText('');
      setStatusMessage({ type: 'success', text: res.message || 'Toàn bộ dữ liệu học tập đã được xóa thành công.' });
    } catch {
      setStatusMessage({ type: 'error', text: 'Có lỗi xảy ra khi xóa dữ liệu. Vui lòng thử lại.' });
    } finally {
      setIsDeleting(false);
    }
  };

  if (isLoading) {
    return (
      <div className="min-h-[60vh] flex flex-col items-center justify-center gap-3 text-slate-400">
        <div className="w-8 h-8 rounded-full border-2 border-indigo-500 border-t-transparent animate-spin" />
        <span className="text-sm">Đang tải thông tin tài khoản…</span>
      </div>
    );
  }

  const roleLabel = role === 'ADMIN' ? 'Quản trị viên' : role === 'TEACHER' ? 'Giáo viên' : 'Học sinh';

  return (
    <div className="min-h-screen py-10 px-4 sm:px-6 lg:px-8 max-w-4xl mx-auto space-y-8">
      {/* Back button & Page Title */}
      <div>
        <Link
          href="/dashboard"
          className="inline-flex items-center gap-2 text-xs font-semibold text-slate-400 hover:text-white transition-colors mb-4"
        >
          <ArrowLeft className="w-3.5 h-3.5" /> Quay lại Bản đồ Năng lực
        </Link>
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-indigo-500/10 border border-indigo-500/30 flex items-center justify-center text-indigo-400 shadow-lg shadow-indigo-500/10">
            <Shield className="w-5 h-5" />
          </div>
          <div>
            <h1 className="text-2xl font-extrabold text-white tracking-tight">Cài đặt & Quyền riêng tư dữ liệu</h1>
            <p className="text-xs text-slate-400">Quản lý hồ sơ, kiểm soát dữ liệu học tập và quyền riêng tư cá nhân</p>
          </div>
        </div>
      </div>

      {/* Status banner */}
      {statusMessage && (
        <div
          role="alert"
          className={`p-4 rounded-2xl border text-sm flex items-center gap-3 transition-all ${
            statusMessage.type === 'success'
              ? 'bg-emerald-950/40 border-emerald-500/30 text-emerald-200'
              : 'bg-rose-950/40 border-rose-500/30 text-rose-200'
          }`}
        >
          {statusMessage.type === 'success' ? (
            <CheckCircle2 className="w-5 h-5 text-emerald-400 shrink-0" />
          ) : (
            <AlertTriangle className="w-5 h-5 text-rose-400 shrink-0" />
          )}
          <span>{statusMessage.text}</span>
        </div>
      )}

      {/* User Identity Card */}
      <section className="glass-panel p-6 rounded-3xl border border-white/10 space-y-4">
        <h2 className="text-sm font-bold text-slate-200 uppercase tracking-wider flex items-center gap-2">
          <User className="w-4 h-4 text-indigo-400" /> Thông tin tài khoản
        </h2>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 pt-2">
          <div className="p-3.5 rounded-2xl bg-white/[0.03] border border-white/5 space-y-1">
            <span className="text-[11px] font-medium text-slate-400">Tên người dùng</span>
            <p className="text-sm font-semibold text-white truncate">{user.name || 'Học sinh'}</p>
          </div>
          <div className="p-3.5 rounded-2xl bg-white/[0.03] border border-white/5 space-y-1">
            <span className="text-[11px] font-medium text-slate-400">Email</span>
            <p className="text-sm font-semibold text-white truncate">{user.email || 'Chưa đăng nhập'}</p>
          </div>
          <div className="p-3.5 rounded-2xl bg-white/[0.03] border border-white/5 space-y-1">
            <span className="text-[11px] font-medium text-slate-400">Vai trò hệ thống</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-bold text-indigo-300">{roleLabel}</span>
              <span className="text-[9px] px-2 py-0.5 rounded-full font-mono bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
                {role}
              </span>
            </div>
          </div>
        </div>
      </section>

      {/* Data Inventory & Privacy Notice */}
      <section className="glass-panel p-6 rounded-3xl border border-white/10 space-y-5">
        <h2 className="text-sm font-bold text-slate-200 uppercase tracking-wider flex items-center gap-2">
          <Lock className="w-4 h-4 text-cyan-400" /> Chính sách lưu trữ & Phạm vi dữ liệu
        </h2>
        <p className="text-xs text-slate-300 leading-relaxed">
          OmniPrep cam kết minh bạch về dữ liệu học tập thu thập để phục vụ phân tích năng lực và hỗ trợ ôn thi tốt nghiệp THPT:
        </p>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div className="p-4 rounded-2xl bg-white/[0.02] border border-white/5 space-y-2">
            <div className="flex items-center gap-2 text-xs font-bold text-white">
              <Database className="w-4 h-4 text-indigo-400" /> Dữ liệu được lưu trữ
            </div>
            <ul className="text-xs text-slate-400 space-y-1.5 list-disc list-inside">
              <li>Lịch sử thi: mã đề, thời gian làm bài, điểm 3 phần.</li>
              <li>Câu trả lời & autosave tiến độ làm bài thi.</li>
              <li>Sổ tay câu sai (câu hỏi, đáp án chọn, ghi chú cá nhân).</li>
              <li>Bản đồ độ thuần thục theo từng chuyên đề kiến thức.</li>
              <li>Lịch sử trao đổi với Gia sư AI trong quá trình giải đề.</li>
            </ul>
          </div>

          <div className="p-4 rounded-2xl bg-white/[0.02] border border-white/5 space-y-2">
            <div className="flex items-center gap-2 text-xs font-bold text-white">
              <Clock className="w-4 h-4 text-cyan-400" /> Thời hạn & Bảo vệ
            </div>
            <ul className="text-xs text-slate-400 space-y-1.5 list-disc list-inside">
              <li>Dữ liệu phục vụ ôn thi được lưu trữ trong suốt năm học.</li>
              <li>Chính sách phân quyền bảo mật RLS ngăn chặn truy cập chéo giữa các tài khoản học sinh.</li>
              <li>Kết nối truyền dữ liệu mã hóa HTTPS/TLS tiêu chuẩn.</li>
              <li>Học sinh có quyền xuất bản sao dữ liệu hoặc yêu cầu xóa bất kỳ lúc nào.</li>
            </ul>
          </div>
        </div>
      </section>

      {/* Data Management Actions */}
      <section className="glass-panel p-6 rounded-3xl border border-white/10 space-y-6">
        <h2 className="text-sm font-bold text-slate-200 uppercase tracking-wider flex items-center gap-2">
          <FileText className="w-4 h-4 text-emerald-400" /> Quản lý dữ liệu học tập
        </h2>

        {/* Export Data */}
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 rounded-2xl bg-white/[0.02] border border-white/5">
          <div className="space-y-1 max-w-lg">
            <div className="text-sm font-bold text-white flex items-center gap-2">
              <Download className="w-4 h-4 text-emerald-400" /> Xuất dữ liệu học tập cá nhân
            </div>
            <p className="text-xs text-slate-400 leading-relaxed">
              Tải về toàn bộ lịch sử thi, sổ tay câu sai, ma trận chuyên đề và kết quả học tập dưới dạng file JSON chuẩn (GDPR Data Portability).
            </p>
          </div>
          <button
            onClick={() => void handleExportData()}
            disabled={isExporting}
            className="inline-flex items-center justify-center gap-2 px-5 py-2.5 rounded-xl font-bold text-xs bg-emerald-600 hover:bg-emerald-500 text-white shadow-lg shadow-emerald-600/20 transition-all disabled:opacity-50 shrink-0"
          >
            {isExporting ? <RefreshCw className="w-3.5 h-3.5 animate-spin" /> : <Download className="w-3.5 h-3.5" />}
            {isExporting ? 'Đang xuất…' : 'Tải xuống JSON'}
          </button>
        </div>

        {/* Delete Data */}
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 rounded-2xl bg-rose-500/[0.03] border border-rose-500/20">
          <div className="space-y-1 max-w-lg">
            <div className="text-sm font-bold text-rose-300 flex items-center gap-2">
              <Trash2 className="w-4 h-4 text-rose-400" /> Xóa toàn bộ dữ liệu học tập
            </div>
            <p className="text-xs text-slate-400 leading-relaxed">
              Xóa vĩnh viễn toàn bộ lịch sử bài thi, câu trả lời, sổ tay câu sai và tiến độ học tập trên máy chủ.
              <span className="block text-amber-300/80 font-medium mt-1">
                Lưu ý phạm vi: Thao tác này không xóa tài khoản đăng nhập Supabase Auth của bạn.
              </span>
            </p>
          </div>
          <button
            onClick={() => setDeleteModalOpen(true)}
            className="inline-flex items-center justify-center gap-2 px-5 py-2.5 rounded-xl font-bold text-xs bg-rose-600/80 hover:bg-rose-600 text-white shadow-lg shadow-rose-600/20 transition-all shrink-0"
          >
            <Trash2 className="w-3.5 h-3.5" /> Xóa lịch sử học
          </button>
        </div>
      </section>

      {/* Delete Confirmation Modal */}
      {deleteModalOpen && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-black/80 p-4 backdrop-blur-md">
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-modal-title"
            className="w-full max-w-md space-y-5 rounded-3xl border border-rose-500/30 bg-slate-950 p-6 shadow-2xl relative"
          >
            <div className="flex items-center gap-3 text-rose-400">
              <div className="w-10 h-10 rounded-xl bg-rose-500/10 border border-rose-500/30 flex items-center justify-center shrink-0">
                <AlertTriangle className="w-5 h-5 text-rose-400" />
              </div>
              <h3 id="delete-modal-title" className="text-base font-bold text-white">
                Xác nhận xóa dữ liệu học tập
              </h3>
            </div>

            <div className="text-xs text-slate-300 space-y-2 bg-rose-950/20 p-3.5 rounded-2xl border border-rose-500/20">
              <p className="font-semibold text-rose-200">Hành động này sẽ xóa vĩnh viễn:</p>
              <ul className="list-disc list-inside space-y-1 text-slate-300">
                <li>Toàn bộ các lần thi đã thực hiện và điểm số</li>
                <li>Tất cả câu hỏi trong Sổ tay câu sai</li>
                <li>Tiến độ bản đồ năng lực chuyên đề</li>
                <li>Lịch sử các phiên trao đổi với Gia sư AI</li>
              </ul>
              <p className="text-[11px] text-slate-400 pt-1">
                Tài khoản đăng nhập email của bạn vẫn được giữ nguyên để bạn có thể tiếp tục sử dụng hệ thống nếu muốn.
              </p>
            </div>

            <div className="space-y-2">
              <label htmlFor="confirm-delete-input" className="text-xs font-semibold text-slate-300">
                Để xác nhận, vui lòng nhập chính xác chữ <span className="text-rose-400 font-mono font-bold">XÓA DỮ LIỆU</span>:
              </label>
              <input
                id="confirm-delete-input"
                type="text"
                value={confirmDeleteText}
                onChange={(e) => setConfirmDeleteText(e.target.value)}
                placeholder="XÓA DỮ LIỆU"
                className="w-full px-3.5 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white text-xs font-mono focus:outline-none focus:border-rose-500 transition-colors"
              />
            </div>

            <div className="flex justify-end gap-3 pt-2">
              <button
                type="button"
                onClick={() => {
                  setDeleteModalOpen(false);
                  setConfirmDeleteText('');
                }}
                disabled={isDeleting}
                className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-300 hover:bg-white/5 transition-colors"
              >
                Hủy bỏ
              </button>
              <button
                type="button"
                onClick={() => void handleDeleteData()}
                disabled={isDeleting || confirmDeleteText !== 'XÓA DỮ LIỆU'}
                className="inline-flex items-center gap-2 px-5 py-2 rounded-xl text-xs font-bold bg-rose-600 hover:bg-rose-500 text-white shadow-lg shadow-rose-600/30 transition-all disabled:opacity-40 disabled:cursor-not-allowed"
              >
                {isDeleting ? <RefreshCw className="w-3.5 h-3.5 animate-spin" /> : <Trash2 className="w-3.5 h-3.5" />}
                {isDeleting ? 'Đang xóa…' : 'Xóa vĩnh viễn'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
