'use client';

import React, { useEffect, useState } from 'react';
import Link from 'next/link';
import { Navbar } from '@/components/Navbar';
import { WeaknessHeatmap } from '@/components/WeaknessHeatmap';
import { ScoreHistoryChart } from '@/components/ScoreHistoryChart';
import { getDashboardData, getDashboardSummary, DashboardSummary } from '@/lib/api';
import { useAuth } from '@/context/AuthContext';
import {
  DashboardStats,
  ScoreHistoryItem,
  SubjectCode,
} from '@/types';
import {
  Target,
  Flame,
  Award,
  BookOpen,
  ArrowRight,
  TrendingUp,
  BrainCircuit,
  Loader2,
  Sparkles,
  CheckCircle2,
  Shuffle,
  ShieldCheck,
} from 'lucide-react';

export default function DashboardPage() {
  const { user: userProfile, role, canAccessAdmin, isLoading: authLoading, signOut } = useAuth();
  const [selectedSubject, setSelectedSubject] = useState<SubjectCode>('GEOGRAPHY');
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [scoreHistory, setScoreHistory] = useState<ScoreHistoryItem[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [loadError, setLoadError] = useState('');

  useEffect(() => {
    if (authLoading || !userProfile.isAuthenticated) return;
    let active = true;
    const frame = requestAnimationFrame(() => {
      if (!active) return;
      void getDashboardSummary().then(sum => {
        if (active) setSummary(sum);
      }).catch(() => {
        if (active) setSummary(null);
      });

      getDashboardData(selectedSubject).then(data => {
        if (!active) return;
        setLoadError('');
        setStats(data.stats);
        setScoreHistory(data.history);
      }).catch(error => {
        if (!active) return;
        setLoadError(error instanceof Error ? error.message : 'Không tải được tiến độ học tập.');
        setStats(null);
        setScoreHistory([]);
      }).finally(() => {
        if (active) setIsLoading(false);
      });
    });

    return () => {
      active = false;
      cancelAnimationFrame(frame);
    };
  }, [authLoading, selectedSubject, userProfile.isAuthenticated]);

  const filteredHistory = scoreHistory.filter(
    h => h.subjectCode === selectedSubject
  );

  if (authLoading) return <div className="min-h-screen grid place-items-center bg-[#07090e] text-slate-300">Đang kiểm tra tài khoản…</div>;
  if (!userProfile.isAuthenticated) {
    return <div className="min-h-screen bg-[#07090e] text-white"><Navbar /><main className="mx-auto mt-16 max-w-xl rounded-3xl border border-white/10 bg-slate-950/80 p-8 text-center"><h1 className="text-xl font-bold">Đăng nhập để xem tiến độ</h1><p className="mt-3 text-sm text-slate-400">Kết quả và biểu đồ được lấy từ các lượt thi đã lưu trong tài khoản của bạn.</p><Link href="/login" className="mt-5 inline-flex rounded-xl bg-indigo-600 px-5 py-2.5 text-sm font-bold">Đăng nhập</Link></main></div>;
  }

  const roleLabel = role === 'ADMIN' ? 'Quản trị viên' : role === 'TEACHER' ? 'Giáo viên' : 'Học sinh';

  return (
    <div className="min-h-screen flex flex-col bg-[#07090e]">
      <Navbar />

      <main className="flex-1 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8 sm:py-10 space-y-10">
        {/* Admin Shortcut Banner (Chỉ xuất hiện khi là Admin hoặc Giáo viên) */}
        {canAccessAdmin && (
          <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 p-4 rounded-2xl bg-gradient-to-r from-purple-950/40 via-indigo-950/30 to-slate-900 border border-purple-500/30 shadow-lg">
            <div className="flex items-center gap-3">
              <div className="w-9 h-9 rounded-xl bg-purple-500/20 border border-purple-500/30 flex items-center justify-center text-purple-300">
                <ShieldCheck className="w-5 h-5" />
              </div>
              <div>
                <p className="font-bold text-white text-sm">
                  Khu vực Quản trị & Hiệu đính ({roleLabel})
                </p>
                <p className="text-xs text-slate-400">
                  Bạn có quyền bóc tách đề thi, quản lý ngân hàng câu hỏi và phê duyệt đề thi.
                </p>
              </div>
            </div>
            <Link
              href="/admin"
              className="flex items-center gap-1.5 px-4 py-2 rounded-xl bg-purple-600 hover:bg-purple-500 text-white text-xs font-bold transition-all shadow-md shadow-purple-600/30 shrink-0"
            >
              <span>Vào trang Quản trị</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </Link>
          </div>
        )}

        {/* ============================================================ */}
        {/* 1. HEADER & XÁC THỰC (SUPABASE AUTH / USER PROFILE BAR) */}
        {/* ============================================================ */}
        <section className="glass-panel rounded-3xl p-6 sm:p-8 border border-white/10 relative overflow-hidden">
          <div className="flex flex-col lg:flex-row items-start lg:items-center justify-between gap-6">
            {/* Left: User Avatar & Identification */}
            <div className="flex items-center gap-4 sm:gap-5">
              <div className="relative">
                <div className="w-16 h-16 sm:w-20 sm:h-20 rounded-2xl border-2 border-indigo-500/40 bg-slate-900 shadow-xl shadow-indigo-600/20 grid place-items-center text-2xl font-black text-indigo-300">
                  {(userProfile.name || userProfile.email || 'U').slice(0, 1).toUpperCase()}
                </div>
                <span className="absolute -bottom-1 -right-1 w-5 h-5 bg-emerald-500 border-2 border-slate-900 rounded-full flex items-center justify-center">
                  <CheckCircle2 className="w-3 h-3 text-white" />
                </span>
              </div>

              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <h1 className="text-xl sm:text-2xl font-black text-white tracking-tight">
                    {userProfile.name}
                  </h1>
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
                <p className="text-xs text-slate-400 font-medium">
                  {userProfile.email}
                </p>
                <div className="text-xs text-slate-400 pt-0.5">Tiến độ đồng bộ từ tài khoản</div>
              </div>
            </div>

            {/* Right: Gamification Badges & Auth Controls */}
            <div className="flex flex-wrap items-center gap-3 w-full lg:w-auto justify-start lg:justify-end">
              {/* Streak Days Badge */}
              <div className="flex items-center gap-2.5 px-4 py-2.5 rounded-2xl bg-amber-500/10 border border-amber-500/30 text-amber-300 shadow-md shadow-amber-500/10">
                <div className="w-8 h-8 rounded-xl bg-amber-500/20 flex items-center justify-center">
                  <Flame className="w-5 h-5 text-amber-400 fill-amber-400 animate-pulse" />
                </div>
                <div>
                  <div className="text-[10px] uppercase tracking-wider font-bold text-amber-400">
                    Chuỗi học tập
                  </div>
                  <div className="text-sm font-extrabold font-mono text-white">
                    {(summary?.streakDays ?? userProfile.streakDays) > 0
                      ? `${summary?.streakDays ?? userProfile.streakDays} ngày liên tiếp`
                      : 'Chưa có dữ liệu'}
                  </div>
                </div>
              </div>

              {/* Target Score Badge */}
              <div className="flex items-center gap-2.5 px-4 py-2.5 rounded-2xl bg-indigo-500/10 border border-indigo-500/30 text-indigo-300 shadow-md shadow-indigo-500/10">
                <div className="w-8 h-8 rounded-xl bg-indigo-500/20 flex items-center justify-center">
                  <Target className="w-5 h-5 text-indigo-400" />
                </div>
                <div>
                  <div className="text-[10px] uppercase tracking-wider font-bold text-indigo-400">
                    Mục tiêu điểm số
                  </div>
                  <div className="text-sm font-extrabold font-mono text-white">
                    {(summary?.targetScore ?? userProfile.targetScore) > 0
                      ? `${(summary?.targetScore ?? userProfile.targetScore).toFixed(1)}+ / 10đ`
                      : 'Chưa đặt'}
                  </div>
                </div>
              </div>

              <button onClick={() => void signOut()} className="rounded-2xl border border-white/10 px-4 py-2.5 text-xs font-semibold text-slate-300 hover:bg-white/5">Đăng xuất</button>
            </div>
          </div>
        </section>

        {loadError && <div role="alert" className="rounded-2xl border border-rose-500/20 bg-rose-950/30 p-4 text-sm text-rose-200">{loadError}</div>}

        {/* ============================================================ */}
        {/* 2. BỘ CHUYỂN ĐỔI MÔN HỌC (SUBJECT SWITCHER - TABS NGANG) */}
        {/* ============================================================ */}
        <section className="space-y-3">
          <div className="flex items-center justify-between">
            <h2 className="text-xs uppercase tracking-wider font-bold text-slate-400 flex items-center gap-1.5">
              <BrainCircuit className="w-4 h-4 text-indigo-400" />
              Chọn môn học ôn luyện trọng tâm:
            </h2>
            <span className="text-xs text-slate-500">
              Thống kê & ma trận tự động đồng bộ
            </span>
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            {[
              { code: 'GEOGRAPHY' as SubjectCode, label: 'Địa lý', icon: '🌍', desc: 'Tự nhiên, Dân cư, Kinh tế & Bảng số liệu' },
              { code: 'LAW_ECONOMICS' as SubjectCode, label: 'Kinh tế & Pháp luật', icon: '⚖️', desc: 'Thị trường, Lạm phát & Hệ thống luật' }
            ].map(sub => {
              const isActive = selectedSubject === sub.code;
              return (
                <button
                  key={sub.code}
                  onClick={() => setSelectedSubject(sub.code)}
                  className={`p-4 rounded-2xl border text-left transition-all cursor-pointer relative overflow-hidden group ${
                    isActive
                      ? 'bg-gradient-to-br from-indigo-950/60 to-slate-900 border-indigo-500 ring-2 ring-indigo-500/30 text-white shadow-xl shadow-indigo-600/20'
                      : 'bg-slate-900/60 border-white/10 text-slate-400 hover:text-white hover:bg-slate-800/60 hover:border-slate-600'
                  }`}
                >
                  <div className="flex items-center justify-between mb-2">
                    <span className="text-2xl">{sub.icon}</span>
                    {isActive && (
                      <span className="w-2 h-2 rounded-full bg-cyan-400 animate-ping" />
                    )}
                  </div>
                  <div className="font-bold text-sm sm:text-base text-white">
                    {sub.label}
                  </div>
                  <p className="text-[11px] text-slate-400 line-clamp-1 mt-0.5">
                    {sub.desc}
                  </p>
                </button>
              );
            })}
          </div>
        </section>

        {/* ============================================================ */}
        {/* 3. KHU VỰC "TẠO ĐỀ THÔNG MINH" (3 CHẾ ĐỘ LUYỆN TẬP) */}
        {/* ============================================================ */}
        <section className="space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-lg sm:text-xl font-bold text-white flex items-center gap-2">
              <Sparkles className="w-5 h-5 text-indigo-400" />
              Tạo Đề Luyện Thi Thông Minh (3 Chế Độ)
            </h2>
            <span className="text-xs text-slate-400 hidden sm:inline">
              Chi phí 0 VNĐ • Cấu trúc chuẩn Bộ GD&ĐT 2025
            </span>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {/* CARD 1: THI THỬ ĐỀ SỞ / TRƯỜNG CHUYÊN GỐC */}
            <div className="glass-panel glass-panel-hover rounded-3xl p-6 border border-white/10 flex flex-col justify-between space-y-5 relative">
              <div>
                <div className="flex items-center justify-between mb-3">
                  <span className="text-[10px] font-bold uppercase tracking-wider px-2.5 py-1 rounded-full bg-cyan-500/20 text-cyan-300 border border-cyan-500/30">
                    Chế độ 1 • Đề nguyên bản
                  </span>
                  <span className="text-xs font-semibold text-emerald-400">
                    Sở & Trường Chuyên
                  </span>
                </div>
                <h3 className="text-base font-bold text-white mb-2">
                  Thi thử Đề Sở / Chuyên gốc
                </h3>
                <p className="text-xs text-slate-400 leading-relaxed mb-4">
                  Cập nhật đề thi mới nhất từ các Sở GD&ĐT Hà Nội, Bắc Ninh, Nghệ An, Chuyên Lam Sơn...
                </p>

                <p className="rounded-xl border border-white/5 bg-slate-950/60 p-3 text-xs text-slate-400">
                  Danh sách đề nguồn sẽ hiển thị sau khi ngân hàng câu hỏi được duyệt và đồng bộ.
                </p>
              </div>

              <button disabled className="w-full rounded-2xl border border-white/10 py-3 text-xs font-bold text-slate-500">Chưa có đề nguồn đã duyệt</button>
            </div>

            {/* CARD 2: TRỘN ĐỀ MA TRẬN CHUẨN (0% CÂU TRÙNG) */}
            <div className="glass-panel glass-panel-hover rounded-3xl p-6 border border-indigo-500/30 bg-gradient-to-br from-indigo-950/40 to-slate-900/60 flex flex-col justify-between space-y-5 relative">
              <div>
                <div className="flex items-center justify-between mb-3">
                  <span className="text-[10px] font-bold uppercase tracking-wider px-2.5 py-1 rounded-full bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
                    Chế độ 2 • Trộn thông minh
                  </span>
                  <span className="text-xs font-semibold text-cyan-400 flex items-center gap-1">
                    <ShieldCheck className="w-3.5 h-3.5" />
                    0% Trùng lặp
                  </span>
                </div>
                <h3 className="text-base font-bold text-white mb-2">
                  Trộn Đề Ma Trận Chuẩn 2025
                </h3>
                <p className="text-xs text-slate-300 leading-relaxed">
                  Rút ngẫu nhiên các câu hỏi theo đúng tỷ lệ 3 phần của Bộ GD&ĐT. Thuật toán tự động <strong className="text-emerald-400">loại bỏ 100% các câu em đã từng làm đúng</strong> trước đó.
                </p>

                <div className="mt-4 p-3 rounded-xl bg-slate-950/60 border border-white/5 space-y-1 text-xs">
                  <div className="flex justify-between text-slate-400">
                    <span>Phần I (Trắc nghiệm):</span>
                    <span className="font-mono text-white">18 câu (0.25đ/câu)</span>
                  </div>
                  <div className="flex justify-between text-slate-400">
                    <span>Phần II (Đúng/Sai):</span>
                    <span className="font-mono text-white">4 câu (Lũy tiến 1.0đ)</span>
                  </div>
                  <div className="flex justify-between text-slate-400">
                    <span>Phần III (Trả lời ngắn):</span>
                    <span className="font-mono text-white">6 câu (Dung sai số)</span>
                  </div>
                </div>
              </div>

              <Link
                href={`/exam/exam-matrix-${selectedSubject.toLowerCase()}?subject=${selectedSubject}`}
                className="flex items-center justify-center gap-2 w-full py-3 rounded-2xl font-bold text-xs sm:text-sm bg-gradient-to-r from-indigo-600 via-indigo-500 to-cyan-500 hover:opacity-95 text-white shadow-xl shadow-indigo-600/30 transition-all hover:scale-[1.02]"
              >
                <Shuffle className="w-4 h-4" />
                <span>Tạo đề ma trận 50p</span>
                <ArrowRight className="w-4 h-4" />
              </Link>
            </div>

            {/* CARD 3: CÀY CHUYÊN ĐỀ YẾU (SMART DRILL) */}
            <div className="glass-panel glass-panel-hover rounded-3xl p-6 border border-rose-500/30 bg-gradient-to-br from-rose-950/20 to-slate-900/60 flex flex-col justify-between space-y-5 relative">
              <div>
                <div className="flex items-center justify-between mb-3">
                  <span className="text-[10px] font-bold uppercase tracking-wider px-2.5 py-1 rounded-full bg-rose-500/20 text-rose-300 border border-rose-500/30">
                    Chế độ 3 • Bắt bệnh lỗ hổng
                  </span>
                  <span className="text-xs font-semibold text-rose-400">
                    Tập trung chuyên sâu
                  </span>
                </div>
                <h3 className="text-base font-bold text-white mb-2">
                  Cày Chuyên Đề Yếu (Smart Drill)
                </h3>
                <p className="text-xs text-slate-400 leading-relaxed">
                  Tự động tập hợp 10 - 15 câu chỉ tập trung vào các chuyên đề em đang bị <strong className="text-rose-400">màu Đỏ (&lt; 50%)</strong> để lấp ngay lỗ hổng kiến thức.
                </p>

                <div className="mt-4 p-3 rounded-xl bg-rose-950/30 border border-rose-500/20 text-xs text-rose-200">
                  <span className="font-bold block mb-1">Chuyên đề cần cải thiện gấp:</span>
                  <span className="font-medium">
                    {stats?.weakestTopics[0] || 'Đang phân tích dữ liệu bài thi...'}
                  </span>
                </div>
              </div>

              <Link
                href="/mistakes"
                className="flex items-center justify-center gap-2 w-full py-3 rounded-2xl font-bold text-xs sm:text-sm bg-gradient-to-r from-rose-600 to-amber-600 hover:opacity-95 text-white shadow-lg shadow-rose-600/20 transition-all hover:scale-[1.02]"
              >
                <Target className="w-4 h-4" />
                <span>Xem sổ tay câu sai</span>
                <ArrowRight className="w-4 h-4" />
              </Link>
            </div>
          </div>
        </section>

        {/* ============================================================ */}
        {/* 4. 4 THẺ THỐNG KÊ NHANH (STAT METRICS) */}
        {/* ============================================================ */}
        {stats && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
            <div className="glass-panel rounded-3xl p-6 border border-white/10">
              <div className="flex items-center justify-between mb-4">
                <span className="w-10 h-10 rounded-2xl bg-indigo-500/20 border border-indigo-500/30 flex items-center justify-center text-indigo-400 font-bold">
                  <BookOpen className="w-5 h-5" />
                </span>
                <span className="text-xs text-slate-400">Đã hoàn thành</span>
              </div>
              <div className="text-3xl font-black text-white font-mono">
                {stats.totalAttempts}
              </div>
              <p className="text-xs text-slate-400 mt-1">Đề thi thử THPT đã làm</p>
            </div>

            <div className="glass-panel rounded-3xl p-6 border border-white/10">
              <div className="flex items-center justify-between mb-4">
                <span className="w-10 h-10 rounded-2xl bg-cyan-500/20 border border-cyan-500/30 flex items-center justify-center text-cyan-400 font-bold">
                  <TrendingUp className="w-5 h-5" />
                </span>
                <span className="text-xs text-slate-400">Trung bình</span>
              </div>
              <div className="text-3xl font-black text-cyan-300 font-mono">
                {stats.averageScore.toFixed(2)}
              </div>
              <p className="text-xs text-slate-400 mt-1">Điểm trung bình các đề</p>
            </div>

            <div className="glass-panel rounded-3xl p-6 border border-white/10">
              <div className="flex items-center justify-between mb-4">
                <span className="w-10 h-10 rounded-2xl bg-emerald-500/20 border border-emerald-500/30 flex items-center justify-center text-emerald-400 font-bold">
                  <Award className="w-5 h-5" />
                </span>
                <span className="text-xs text-slate-400">Kỷ lục điểm</span>
              </div>
              <div className="text-3xl font-black text-emerald-300 font-mono">
                {stats.highestScore.toFixed(2)}
              </div>
              <p className="text-xs text-slate-400 mt-1">Điểm cao nhất đạt được</p>
            </div>

            <div className="glass-panel rounded-3xl p-6 border border-white/10">
              <div className="flex items-center justify-between mb-4">
                <span className="w-10 h-10 rounded-2xl bg-rose-500/20 border border-rose-500/30 flex items-center justify-center text-rose-400 font-bold">
                  <Target className="w-5 h-5" />
                </span>
                <span className="text-xs text-rose-400 font-semibold">Cần chú ý</span>
              </div>
              <div className="text-lg font-bold text-white truncate">
                {stats.weakestTopics[0]}
              </div>
              <p className="text-xs text-slate-400 mt-1">Chủ đề có tỷ lệ sai cao nhất</p>
            </div>
          </div>
        )}

        {/* ============================================================ */}
        {/* 5. BẢN ĐỒ NHIỆT NĂNG LỰC (MASTERY HEATMAP) */}
        {/* ============================================================ */}
        {isLoading ? (
          <div className="p-12 text-center flex flex-col items-center justify-center gap-3">
            <Loader2 className="w-8 h-8 text-indigo-400 animate-spin" />
            <p className="text-xs text-slate-400">Đang đồng bộ dữ liệu ma trận năng lực...</p>
          </div>
        ) : stats ? (
          <WeaknessHeatmap topicMasteries={stats.topicMasteries} />
        ) : null}

        {/* ============================================================ */}
        {/* 6. BIỂU ĐỒ LỊCH SỬ ĐIỂM SỐ (SCORE HISTORY LINE CHART) */}
        {/* ============================================================ */}
        <ScoreHistoryChart
          history={filteredHistory}
          targetScore={userProfile.targetScore}
        />
      </main>

    </div>
  );
}
