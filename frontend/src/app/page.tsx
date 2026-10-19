'use client';

import React, { useEffect, useState } from 'react';
import { Navbar } from '@/components/Navbar';
import { getSubjects } from '@/lib/api';
import { Subject, SubjectCode } from '@/types';
import Link from 'next/link';
import {
  Sparkles,
  GraduationCap,
  Layers,
  Cpu,
  ShieldCheck,
  Flame,
  ArrowRight,
  BookOpen,
  BookMarked
} from 'lucide-react';

function formatNumber(num?: number): string {
  if (!num) return '0';
  return num.toString().replace(/\B(?=(\d{3})+(?!\d))/g, '.');
}

export default function HomePage() {
  const [subjects, setSubjects] = useState<Subject[]>([]);
  const [selectedSubject, setSelectedSubject] = useState<SubjectCode>('GEOGRAPHY');
  const [subjectError, setSubjectError] = useState('');
  const readySubjects = subjects.filter(subject => subject.isExamReady);

  useEffect(() => {
    let active = true;
    getSubjects().then(data => {
      if (active) {
        setSubjects(data);
        const ready = data.filter(subject => subject.isExamReady);
        if (ready.length) setSelectedSubject(current => ready.some(subject => subject.code === current) ? current : ready[0].code);
      }
    }).catch(error => {
      if (active) setSubjectError(error instanceof Error ? error.message : 'Không tải được danh sách môn học.');
    });
    return () => { active = false; };
  }, []);

  return (
    <div className="min-h-screen flex flex-col bg-[#07090e]">
      <Navbar />

      <main className="flex-1 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-10 sm:py-16 space-y-16">
        {/* HERO SECTION */}
        <section className="text-center space-y-6 relative">
          <div className="inline-flex items-center gap-2 px-3.5 py-1.5 rounded-full bg-indigo-500/10 border border-indigo-500/30 text-indigo-300 text-xs font-semibold animate-pulse-glow">
            <Sparkles className="w-4 h-4 text-cyan-400" />
            <span>Nền tảng Ôn thi Tốt nghiệp THPT Thế hệ Mới 2025</span>
          </div>

          <h1 className="text-4xl sm:text-6xl font-black tracking-tight text-white max-w-4xl mx-auto leading-tight">
            Luyện đề với{' '}
            <span className="bg-gradient-to-r from-indigo-400 via-cyan-400 to-indigo-300 bg-clip-text text-transparent">
              câu hỏi đã duyệt
            </span>{' '}
          </h1>

          <p className="text-slate-400 text-base sm:text-lg max-w-2xl mx-auto leading-relaxed">
            Tạo bài thi theo ma trận ở các môn hiện được hỗ trợ, lưu tiến độ làm bài và xem giải thích từ gia sư Gemini khi được cấu hình.
          </p>

          <div className="flex flex-wrap items-center justify-center gap-4 pt-4">
            <Link
              href={`/exam?subject=${selectedSubject}`}
              aria-disabled={readySubjects.length === 0}
              className={`flex items-center gap-2.5 px-7 py-3.5 rounded-2xl font-bold text-white bg-gradient-to-r from-indigo-600 via-indigo-500 to-cyan-500 hover:opacity-95 shadow-xl shadow-indigo-600/30 transition-all hover:scale-105 ${readySubjects.length ? '' : 'pointer-events-none opacity-50'}`}
            >
              <GraduationCap className="w-5 h-5" />
              <span>{readySubjects.length ? 'Bắt đầu luyện tập' : subjects.length ? 'Chưa có môn đủ điều kiện tạo đề' : 'Đang tải môn học'}</span>
              <ArrowRight className="w-4 h-4" />
            </Link>

            <Link
              href="/mistakes"
              className="flex items-center gap-2.5 px-6 py-3.5 rounded-2xl font-bold text-rose-300 bg-rose-950/40 border border-rose-500/30 hover:border-rose-500/50 hover:bg-rose-900/40 transition-all"
            >
              <BookMarked className="w-4 h-4 text-rose-400" />
              <span>Sổ tay Bẫy Đề Thi</span>
            </Link>

            <Link
              href="/dashboard"
              className="flex items-center gap-2.5 px-6 py-3.5 rounded-2xl font-bold text-slate-200 bg-slate-900/80 border border-white/10 hover:border-white/25 hover:bg-slate-800/80 transition-all"
            >
              <Flame className="w-4 h-4 text-amber-400" />
              <span>Bản đồ Nhiệt Lỗ hổng</span>
            </Link>
          </div>
        </section>

        {/* 4 CORE VALUE PILLARS */}
        <section className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
          <div className="glass-panel glass-panel-hover rounded-3xl p-6 border border-white/10">
            <div className="w-12 h-12 rounded-2xl bg-indigo-500/20 border border-indigo-500/30 flex items-center justify-center mb-4">
              <Layers className="w-6 h-6 text-indigo-400" />
            </div>
            <h3 className="text-base font-bold text-white mb-2">Bài thi theo ma trận</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              Bài thi được tạo từ ngân hàng câu hỏi đã duyệt theo cấu trúc được cấu hình cho từng môn.
            </p>
          </div>

          <div className="glass-panel glass-panel-hover rounded-3xl p-6 border border-white/10">
            <div className="w-12 h-12 rounded-2xl bg-cyan-500/20 border border-cyan-500/30 flex items-center justify-center mb-4">
              <Sparkles className="w-6 h-6 text-cyan-400" />
            </div>
            <h3 className="text-base font-bold text-white mb-2">Gia sư AI Ngữ cảnh (SSE)</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              Có thể giải thích câu hỏi và lỗi sai theo ngữ cảnh; cần cấu hình Gemini và có thể phát sinh chi phí theo gói sử dụng.
            </p>
          </div>

          <div className="glass-panel glass-panel-hover rounded-3xl p-6 border border-white/10">
            <div className="w-12 h-12 rounded-2xl bg-emerald-500/20 border border-emerald-500/30 flex items-center justify-center mb-4">
              <ShieldCheck className="w-6 h-6 text-emerald-400" />
            </div>
            <h3 className="text-base font-bold text-white mb-2">Chống trùng lặp 2 Tầng</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              SHA-256 và pgvector hỗ trợ phát hiện câu trùng hoặc tương tự; kết quả trích xuất vẫn cần người phụ trách kiểm duyệt.
            </p>
          </div>

          <div className="glass-panel glass-panel-hover rounded-3xl p-6 border border-white/10">
            <div className="w-12 h-12 rounded-2xl bg-violet-500/20 border border-violet-500/30 flex items-center justify-center mb-4">
              <Cpu className="w-6 h-6 text-violet-400" />
            </div>
            <h3 className="text-base font-bold text-white mb-2">Chi phí theo mức sử dụng</h3>
            <p className="text-xs text-slate-400 leading-relaxed">
              Supabase, Gemini và hosting có thể tính phí. Hãy kiểm tra hạn mức và chi phí theo cấu hình trước khi dùng.
            </p>
          </div>
        </section>

        {/* SUBJECT SELECTION SECTION */}
        <section className="space-y-6">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <h2 className="text-2xl font-bold text-white">Chọn môn học ôn luyện</h2>
              <p className="text-xs text-slate-400 mt-1">
                Chỉ môn có đủ câu hỏi đã duyệt và chiến lược chấm điểm mới cho phép tạo đề.
              </p>
            </div>
            <span className="text-xs font-semibold px-3 py-1.5 rounded-full bg-slate-900 border border-white/10 text-slate-300">
              {readySubjects.length} môn hiện đủ điều kiện tạo đề
            </span>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
            {subjectError && <p role="alert" className="col-span-full rounded-xl border border-rose-500/20 bg-rose-950/30 p-4 text-sm text-rose-200">{subjectError}</p>}
            {!subjectError && subjects.length === 0 && <p className="col-span-full rounded-xl border border-white/10 bg-slate-900/50 p-4 text-sm text-slate-400">Chưa có môn học hoặc ngân hàng câu hỏi khả dụng.</p>}
            {subjects.map((sub) => {
              const isSelected = selectedSubject === sub.code;
              return (
                <div
                  key={sub.code}
                  className={`glass-panel rounded-3xl p-6 border transition-all relative overflow-hidden group ${
                    isSelected
                      ? 'border-indigo-500 bg-indigo-950/20 ring-2 ring-indigo-500/40 shadow-xl shadow-indigo-600/10'
                      : `border-white/10 ${sub.isExamReady ? 'hover:border-slate-600' : 'opacity-60'}`
                  }`}
                >
                  <div className="flex items-center justify-between mb-4">
                    <span className="w-10 h-10 rounded-2xl bg-white/5 border border-white/10 flex items-center justify-center text-indigo-400 font-bold group-hover:scale-110 transition-transform">
                      <BookOpen className="w-5 h-5" />
                    </span>
                    <span suppressHydrationWarning className="text-xs font-mono font-semibold px-2.5 py-1 rounded-full bg-slate-900 text-slate-300 border border-white/10">
                      {formatNumber(sub.activeQuestionsCount)} câu
                    </span>
                  </div>

                  <div className="mb-2 flex items-center justify-between gap-2">
                    <h3 className="text-lg font-bold text-white">{sub.name}</h3>
                    {sub.isExamReady && <button type="button" onClick={() => setSelectedSubject(sub.code)} className="shrink-0 text-xs font-semibold text-indigo-300 hover:text-indigo-200">{isSelected ? 'Đang chọn' : 'Chọn môn'}</button>}
                  </div>
                  <p className="text-xs text-slate-400 mb-6 leading-relaxed line-clamp-2">
                    {!sub.isExamSupported
                      ? 'Chưa có chiến lược chấm điểm cho môn này.'
                      : sub.isExamReady
                        ? 'Đủ câu hỏi đã duyệt trong ngân hàng; số câu khả dụng có thể giảm theo lịch sử làm bài của bạn.'
                        : 'Chưa đủ câu hỏi đã duyệt theo ma trận để tạo đề.'}
                  </p>

                  <div className="space-y-1.5 mb-6 text-[11px] text-slate-400">
                    <div className="flex justify-between">
                      <span>Phần I (Trắc nghiệm):</span>
                      <span className="font-semibold text-slate-300">{sub.questionAvailability?.part1.available ?? 0}/{sub.questionAvailability?.part1.required ?? sub.examMatrix?.partI_SingleChoice_Count ?? 0} câu</span>
                    </div>
                    <div className="flex justify-between">
                      <span>Phần II (Đúng/Sai):</span>
                      <span className="font-semibold text-slate-300">{sub.questionAvailability?.part2.available ?? 0}/{sub.questionAvailability?.part2.required ?? sub.examMatrix?.partII_TrueFalseGroup_Count ?? 0} câu</span>
                    </div>
                    <div className="flex justify-between">
                      <span>Phần III (Trả lời ngắn):</span>
                      <span className="font-semibold text-slate-300">{sub.questionAvailability?.part3.available ?? 0}/{sub.questionAvailability?.part3.required ?? sub.examMatrix?.partIII_ShortAnswer_Count ?? 0} câu</span>
                    </div>
                  </div>

                  <Link
                    aria-disabled={!sub.isExamReady}
                    tabIndex={sub.isExamReady ? 0 : -1}
                    href={sub.isExamReady ? `/exam?subject=${sub.code}` : '#'}
                    className={`w-full py-2.5 rounded-xl text-xs font-bold flex items-center justify-center gap-2 transition-all ${
                      !sub.isExamReady
                        ? 'bg-white/5 text-slate-500 border border-white/10 pointer-events-none'
                        : isSelected
                        ? 'bg-indigo-600 text-white hover:bg-indigo-500 shadow-md shadow-indigo-600/20'
                        : 'bg-white/5 text-slate-300 hover:bg-white/10 border border-white/10'
                    }`}
                  >
                    <span>{sub.isExamReady ? 'Vào làm đề thi ngay' : 'Chưa thể tạo đề'}</span>
                    {sub.isExamReady && <ArrowRight className="w-3.5 h-3.5" />}
                  </Link>
                </div>
              );
            })}
          </div>
        </section>
      </main>
    </div>
  );
}
