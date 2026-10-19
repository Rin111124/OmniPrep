'use client';

import React, { useEffect, useState } from 'react';
import { Clock, Send, AlertTriangle, ArrowLeft } from 'lucide-react';
import Link from 'next/link';

interface ExamHeaderProps {
  subjectName: string;
  totalQuestions: number;
  answeredCount: number;
  initialTimeSeconds?: number;
  onSubmitExam: () => void;
}

export const ExamHeader: React.FC<ExamHeaderProps> = ({
  subjectName,
  totalQuestions,
  answeredCount,
  initialTimeSeconds = 50 * 60, // 50 phút chuẩn Bộ GD&ĐT
  onSubmitExam
}) => {
  const [timeLeft, setTimeLeft] = useState(initialTimeSeconds);
  const [showSubmitModal, setShowSubmitModal] = useState(false);

  useEffect(() => {
    const timer = setInterval(() => {
      setTimeLeft(prev => {
        if (prev <= 1) {
          clearInterval(timer);
          onSubmitExam();
          return 0;
        }
        return prev - 1;
      });
    }, 1000);

    return () => clearInterval(timer);
  }, [onSubmitExam]);

  const minutes = Math.floor(timeLeft / 60);
  const seconds = timeLeft % 60;
  const isUrgent = timeLeft < 5 * 60; // Dưới 5 phút

  return (
    <>
      <div className="glass-panel border-b border-white/10 px-4 sm:px-8 py-3.5 sticky top-16 z-40 backdrop-blur-xl">
        <div className="max-w-7xl mx-auto flex items-center justify-between">
          {/* Left: Back & Subject */}
          <div className="flex items-center gap-4">
            <Link
              href="/"
              className="w-9 h-9 rounded-xl bg-white/5 border border-white/10 flex items-center justify-center text-slate-400 hover:text-white hover:bg-white/10 transition-colors"
            >
              <ArrowLeft className="w-4 h-4" />
            </Link>
            <div>
              <div className="flex items-center gap-2">
                <h1 className="text-base sm:text-lg font-bold text-white">
                  Đề thi thử THPT: {subjectName}
                </h1>
                <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                  Chuẩn 2025
                </span>
              </div>
              <p className="text-xs text-slate-400">
                Thời gian làm bài: 50 phút • 3 phần thi
              </p>
            </div>
          </div>

          {/* Right: Timer & Submit */}
          <div className="flex items-center gap-4">
            {/* Countdown Clock */}
            <div
              className={`flex items-center gap-2 px-3.5 py-2 rounded-xl border font-mono text-sm sm:text-base font-bold transition-colors ${
                isUrgent
                  ? 'bg-rose-500/20 text-rose-300 border-rose-500/40 animate-pulse'
                  : 'bg-slate-900/80 text-cyan-300 border-cyan-500/30'
              }`}
            >
              <Clock className="w-4 h-4 text-cyan-400" />
              <span>
                {String(minutes).padStart(2, '0')}:{String(seconds).padStart(2, '0')}
              </span>
            </div>

            {/* Submit Button */}
            <button
              onClick={() => setShowSubmitModal(true)}
              className="flex items-center gap-2 px-4 py-2 rounded-xl text-xs sm:text-sm font-bold bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 text-white shadow-lg shadow-indigo-600/30 transition-all hover:scale-105"
            >
              <Send className="w-4 h-4" />
              <span>Nộp bài thi</span>
            </button>
          </div>
        </div>
      </div>

      {/* Confirmation Modal */}
      {showSubmitModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm p-4 animate-in fade-in duration-200">
          <div className="w-full max-w-md glass-panel rounded-3xl p-6 sm:p-7 border border-white/10 shadow-2xl">
            <div className="flex items-center gap-3 text-amber-400 mb-4">
              <div className="w-10 h-10 rounded-2xl bg-amber-500/20 border border-amber-500/30 flex items-center justify-center">
                <AlertTriangle className="w-5 h-5 text-amber-400" />
              </div>
              <div>
                <h3 className="text-base font-bold text-white">Xác nhận nộp bài thi</h3>
                <p className="text-xs text-slate-400">Thời gian làm bài vẫn còn {minutes} phút {seconds} giây</p>
              </div>
            </div>

            <div className="bg-slate-950/60 rounded-2xl p-4 border border-white/5 mb-6 text-xs sm:text-sm text-slate-300 space-y-2">
              <div className="flex justify-between">
                <span>Số câu đã hoàn thành:</span>
                <span className="font-bold text-indigo-300">{answeredCount}/{totalQuestions} câu</span>
              </div>
              <div className="flex justify-between">
                <span>Số câu chưa làm:</span>
                <span className={`font-bold ${totalQuestions - answeredCount > 0 ? 'text-rose-400' : 'text-emerald-400'}`}>
                  {totalQuestions - answeredCount} câu
                </span>
              </div>
            </div>

            <div className="flex items-center justify-end gap-3">
              <button
                onClick={() => setShowSubmitModal(false)}
                className="px-4 py-2.5 rounded-xl text-xs font-semibold bg-white/5 border border-white/10 text-slate-300 hover:bg-white/10 transition-colors"
              >
                Tiếp tục làm bài
              </button>
              <button
                onClick={() => {
                  setShowSubmitModal(false);
                  onSubmitExam();
                }}
                className="px-5 py-2.5 rounded-xl text-xs font-bold bg-indigo-600 hover:bg-indigo-500 text-white shadow-lg shadow-indigo-600/30 transition-all"
              >
                Chấm điểm ngay
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};
