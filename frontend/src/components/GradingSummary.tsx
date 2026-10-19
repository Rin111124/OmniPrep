'use client';

import React, { useState } from 'react';
import { ExamGradingResult, Question, StudentAnswer } from '@/types';
import {
  Trophy,
  HelpCircle,
  Sparkles,
  RotateCcw,
  BarChart2,
  ChevronDown,
  ChevronUp,
  BookMarked,
  AlertTriangle,
  Flag,
  Check,
} from 'lucide-react';
import Link from 'next/link';
import { MathRenderer } from './MathRenderer';

interface GradingSummaryProps {
  result: ExamGradingResult;
  questions: Question[];
  flaggedQuestionIds?: Set<string>;
  onRetry: () => void;
  onOpenAiTutor: (question: Question, studentAns?: StudentAnswer) => void;
}

export const GradingSummary: React.FC<GradingSummaryProps> = ({
  result,
  questions,
  flaggedQuestionIds = new Set(),
  onRetry,
  onOpenAiTutor
}) => {
  const [filter, setFilter] = useState<'all' | 'incorrect' | 'flagged'>('all');
  const [expandedQuestionId, setExpandedQuestionId] = useState<string | null>(null);

  const filteredDetails = result.details.filter(d => {
    if (filter === 'incorrect') return !d.isCorrect;
    if (filter === 'flagged') return flaggedQuestionIds.has(d.questionId);
    return true;
  });

  const toggleExpand = (id: string) => {
    setExpandedQuestionId(prev => (prev === id ? null : id));
  };

  // Extract Part II questions for progressive breakdown
  const partIIDetails = result.details.filter(d => d.format === 'TrueFalseGroup');

  return (
    <div className="space-y-8 animate-in fade-in duration-300">
      {/* ============================================================ */}
      {/* 1. TOP BANNER: SCORECARD & 3-PART PROGRESSIVE BREAKDOWN */}
      {/* ============================================================ */}
      <div className="glass-panel rounded-3xl p-6 sm:p-10 border border-white/10 relative overflow-hidden">
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-center">
          {/* Main Score Badge */}
          <div className="lg:col-span-5 flex flex-col items-center justify-center text-center p-6 sm:p-8 rounded-3xl bg-slate-900/80 border border-white/5 shadow-inner">
            <div className="w-16 h-16 rounded-3xl bg-gradient-to-tr from-indigo-500/20 to-violet-500/30 border border-indigo-500/30 flex items-center justify-center mb-4 shadow-lg shadow-indigo-500/10">
              <Trophy className="w-8 h-8 text-indigo-400" />
            </div>
            <div className="text-xs uppercase tracking-widest text-slate-400 font-bold mb-1">
              Tổng điểm đạt được (Thang 10)
            </div>
            <div className="text-5xl sm:text-6xl font-black tracking-tight bg-gradient-to-r from-white via-indigo-200 to-cyan-300 bg-clip-text text-transparent">
              {result.totalScore.toFixed(2)}
              <span className="text-xl sm:text-2xl text-slate-500 font-medium ml-1">/ 10</span>
            </div>
            <div className="mt-3 text-xs text-slate-400 font-medium">
              Đúng {result.correctCount}/{result.totalQuestions} câu • Thời gian hoàn thành: {Math.floor(result.timeSpentSeconds / 60)} phút {result.timeSpentSeconds % 60} giây
            </div>

            <div className="flex flex-wrap items-center justify-center gap-2.5 mt-6 w-full">
              <button
                onClick={onRetry}
                className="flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-bold bg-white/5 border border-white/10 text-slate-200 hover:bg-white/10 transition-colors"
              >
                <RotateCcw className="w-3.5 h-3.5" />
                Làm lại đề
              </button>
              <Link
                href="/mistakes"
                className="flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-bold bg-rose-600/20 border border-rose-500/30 text-rose-300 hover:bg-rose-600/30 transition-all"
              >
                <BookMarked className="w-3.5 h-3.5 text-rose-400" />
                Sổ tay câu sai ({result.details.filter(d => !d.isCorrect).length})
              </Link>
              <Link
                href="/dashboard"
                className="flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-bold bg-indigo-600 hover:bg-indigo-500 text-white shadow-lg shadow-indigo-600/30 transition-all"
              >
                <BarChart2 className="w-3.5 h-3.5" />
                Về Dashboard
              </Link>
            </div>
          </div>

          {/* 3-Part Official Breakdown */}
          <div className="lg:col-span-7 space-y-3.5">
            <h3 className="text-xs font-bold text-slate-400 uppercase tracking-wider mb-2 flex items-center justify-between">
              <span>Bóc tách điểm theo 3 phần (Chuẩn GDPT 2018 / 2025)</span>
              <span className="text-[11px] text-indigo-400 font-mono">Tổng: 40 câu / 50 phút</span>
            </h3>

            {/* Part I Card */}
            <div className="p-4 rounded-2xl bg-slate-900/60 border border-white/5 flex items-center justify-between">
              <div>
                <div className="flex items-center gap-2">
                  <span className="text-[10px] font-bold px-2 py-0.5 rounded bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
                    Phần I
                  </span>
                  <span className="text-sm font-semibold text-white">Trắc nghiệm nhiều lựa chọn</span>
                </div>
                <p className="text-xs text-slate-400 mt-1">
                  Số câu đúng × 0.25đ ({result.details.filter(d => d.format === 'SingleChoice' && d.isCorrect).length}/{result.details.filter(d => d.format === 'SingleChoice').length} câu đúng)
                </p>
              </div>
              <div className="text-right">
                <span className="text-lg font-mono font-bold text-indigo-300">
                  {result.scorePartI.toFixed(2)}
                </span>
                <span className="text-xs text-slate-500"> / 4.50đ</span>
              </div>
            </div>

            {/* Part II Card (Progressive Scoring) */}
            <div className="p-4 rounded-2xl bg-slate-900/60 border border-white/5 space-y-2">
              <div className="flex items-center justify-between">
                <div>
                  <div className="flex items-center gap-2">
                    <span className="text-[10px] font-bold px-2 py-0.5 rounded bg-cyan-500/20 text-cyan-300 border border-cyan-500/30">
                      Phần II
                    </span>
                    <span className="text-sm font-semibold text-white">Đúng / Sai dạng nhóm ý</span>
                  </div>
                  <p className="text-xs text-slate-400 mt-1">
                    Thang tính lũy tiến: 1 ý = 0.1đ • 2 ý = 0.25đ • 3 ý = 0.5đ • 4 ý = 1.0đ
                  </p>
                </div>
                <div className="text-right">
                  <span className="text-lg font-mono font-bold text-cyan-300">
                    {result.scorePartII.toFixed(2)}
                  </span>
                  <span className="text-xs text-slate-500"> / 4.00đ</span>
                </div>
              </div>

              {/* Mini Breakdown for each Part II Question */}
              {partIIDetails.length > 0 && (
                <div className="pt-2 border-t border-white/5 grid grid-cols-2 sm:grid-cols-4 gap-2 text-[11px]">
                  {partIIDetails.map((p2, idx) => {
                    const earned = p2.scoreEarned;
                    return (
                      <div
                        key={p2.questionId}
                        className="bg-slate-950/60 rounded-xl p-2 border border-white/5 flex flex-col justify-between"
                      >
                        <span className="text-slate-400 font-semibold">Câu {idx + 1}:</span>
                        <span className={`font-mono font-bold ${earned >= 0.5 ? 'text-emerald-400' : earned > 0 ? 'text-amber-400' : 'text-rose-400'}`}>
                          +{earned.toFixed(2)}đ {earned === 1.0 ? '(4/4 ý)' : earned === 0.5 ? '(3/4 ý)' : earned === 0.25 ? '(2/4 ý)' : earned === 0.1 ? '(1/4 ý)' : '(0/4 ý)'}
                        </span>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            {/* Part III Card */}
            <div className="p-4 rounded-2xl bg-slate-900/60 border border-white/5 flex items-center justify-between">
              <div>
                <div className="flex items-center gap-2">
                  <span className="text-[10px] font-bold px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                    Phần III
                  </span>
                  <span className="text-sm font-semibold text-white">Trả lời ngắn</span>
                </div>
                <p className="text-xs text-slate-400 mt-1">
                  0.25đ mỗi kết quả số học chính xác (chấp nhận dung sai và dấu , / .)
                </p>
              </div>
              <div className="text-right">
                <span className="text-lg font-mono font-bold text-emerald-300">
                  {result.scorePartIII.toFixed(2)}
                </span>
                <span className="text-xs text-slate-500"> / 1.50đ</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* ============================================================ */}
      {/* 2. QUESTION DETAILS & MULTI-TIER ERROR DIAGNOSTICS */}
      {/* ============================================================ */}
      <div className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h3 className="text-lg font-bold text-white flex items-center gap-2">
              <span>Bảng Phân Tích Lỗi Sai & Lời Giải Đa Tầng</span>
              <span className="text-xs font-normal text-slate-400">
                ({filteredDetails.length} câu)
              </span>
            </h3>
            <p className="text-xs text-slate-400">
              Bắt đúng bệnh lỗi sai, bẫy đơn vị và tham vấn trực tiếp Gia sư AI theo từng câu
            </p>
          </div>

          {/* 3 User-Requested Filter Tabs: [Tất cả] | [Chỉ xem câu Sai/Mất điểm] | [Câu đã gắn cờ 🚩] */}
          <div className="flex items-center gap-1.5 p-1 rounded-xl bg-slate-900/80 border border-white/10 text-xs">
            <button
              onClick={() => setFilter('all')}
              className={`px-3 py-1.5 rounded-lg font-semibold transition-all ${
                filter === 'all' ? 'bg-indigo-600 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              Tất cả ({result.details.length})
            </button>
            <button
              onClick={() => setFilter('incorrect')}
              className={`px-3 py-1.5 rounded-lg font-semibold transition-all ${
                filter === 'incorrect' ? 'bg-rose-600 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              Chỉ xem câu Sai / Mất điểm ({result.details.filter(d => !d.isCorrect).length})
            </button>
            <button
              onClick={() => setFilter('flagged')}
              className={`px-3 py-1.5 rounded-lg font-semibold transition-all flex items-center gap-1 ${
                filter === 'flagged' ? 'bg-amber-600 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              <Flag className="w-3 h-3 text-amber-300" />
              <span>Câu gắn cờ ({result.details.filter(d => flaggedQuestionIds.has(d.questionId)).length})</span>
            </button>
          </div>
        </div>

        {/* Detailed Question List */}
        <div className="space-y-4">
          {filteredDetails.length === 0 ? (
            <div className="glass-panel rounded-2xl p-8 text-center text-slate-400 text-xs">
              Không có câu hỏi nào khớp với bộ lọc đã chọn.
            </div>
          ) : (
            filteredDetails.map((detail) => {
              const originalQuestion = questions.find(q => q.id === detail.questionId);
              const isExpanded = expandedQuestionId === detail.questionId;
              const isFlagged = flaggedQuestionIds.has(detail.questionId);

              // Detect Specific Trap for Part I, Part II, Part III
              let trapMessage: string | null = null;

              if (detail.format === 'SingleChoice') {
                const opt = detail.studentAnswer.selectedOption;
                if (opt && originalQuestion?.distractorAnalysis?.[opt]) {
                  trapMessage = originalQuestion.distractorAnalysis[opt];
                } else if (!detail.isCorrect && opt) {
                  trapMessage = `Bạn chọn ${opt} sai vì bị lừa bởi phương án nhiễu trực quan. Cần kiểm tra kỹ từ khóa trung tâm!`;
                }
              } else if (detail.format === 'TrueFalseGroup') {
                if (!detail.isCorrect) {
                  // Check which statement student answered wrong
                  const wrongKeys: string[] = [];
                  ['a', 'b', 'c', 'd'].forEach(k => {
                    const stVal = detail.studentAnswer.subAnswers?.[k];
                    const crVal = detail.correctAnswerData.subAnswers?.[k];
                    if (stVal !== undefined && stVal !== crVal) {
                      wrongKeys.push(k);
                    }
                  });
                  if (wrongKeys.length > 0) {
                    const trapItems = wrongKeys.map(k => {
                      const msg = originalQuestion?.distractorAnalysis?.[k] || `Ý ${k}) bạn chọn chưa đúng với dữ liệu đề bài.`;
                      return `Ý ${k}): ${msg}`;
                    });
                    trapMessage = trapItems.join('\n');
                  }
                }
              } else if (detail.format === 'ShortAnswer') {
                const textAns = (detail.studentAnswer.textAnswer || '').trim().replace(',', '.');
                if (originalQuestion?.shortAnswerWrongTraps && originalQuestion.shortAnswerWrongTraps[textAns]) {
                  trapMessage = originalQuestion.shortAnswerWrongTraps[textAns];
                } else if (!detail.isCorrect && textAns) {
                  trapMessage = `⚠️ Bạn tính ra "${textAns}" chưa khớp với đáp án chuẩn "${detail.correctAnswerData.correctAnswer} ${detail.correctAnswerData.unit || ''}". Hãy kiểm tra lại thao tác làm tròn hoặc bẫy đơn vị đo lường!`;
                }
              }

              return (
                <div
                  key={detail.questionId}
                  className={`glass-panel rounded-2xl border transition-all ${
                    detail.isCorrect
                      ? 'border-emerald-500/20 bg-emerald-950/10'
                      : 'border-rose-500/30 bg-rose-950/10'
                  }`}
                >
                  {/* Accordion Header */}
                  <div
                    onClick={() => toggleExpand(detail.questionId)}
                    className="p-5 cursor-pointer flex items-center justify-between gap-4"
                  >
                    <div className="flex items-center gap-3 flex-1 min-w-0">
                      <span
                        className={`flex-shrink-0 w-8 h-8 rounded-xl font-bold flex items-center justify-center text-xs shadow-sm ${
                          detail.isCorrect ? 'bg-emerald-600 text-white' : 'bg-rose-600 text-white'
                        }`}
                      >
                        {detail.questionNumber}
                      </span>
                      <div className="truncate">
                        <div className="flex items-center gap-2 mb-0.5">
                          <span className="text-[10px] font-bold px-2 py-0.5 rounded bg-white/5 text-slate-300">
                            {detail.topic}
                          </span>
                          <span className="text-xs font-semibold text-slate-400">
                            {detail.format === 'SingleChoice' && 'Phần I: Trắc nghiệm'}
                            {detail.format === 'TrueFalseGroup' && 'Phần II: Đúng / Sai'}
                            {detail.format === 'ShortAnswer' && 'Phần III: Trả lời ngắn'}
                          </span>
                          {isFlagged && (
                            <span className="inline-flex items-center gap-1 text-[10px] font-bold px-1.5 py-0.5 rounded bg-amber-500/20 text-amber-300 border border-amber-500/30">
                              <Flag className="w-2.5 h-2.5" />
                              Đã gắn cờ
                            </span>
                          )}
                        </div>
                        <p className="text-sm text-slate-200 truncate">{detail.content}</p>
                      </div>
                    </div>

                    <div className="flex items-center gap-3">
                      <span
                        className={`px-3 py-1 rounded-xl font-mono text-xs font-bold ${
                          detail.isCorrect ? 'text-emerald-300 bg-emerald-500/10' : 'text-rose-300 bg-rose-500/10'
                        }`}
                      >
                        +{detail.scoreEarned.toFixed(2)}đ
                      </span>

                      {isExpanded ? (
                        <ChevronUp className="w-4 h-4 text-slate-400" />
                      ) : (
                        <ChevronDown className="w-4 h-4 text-slate-400" />
                      )}
                    </div>
                  </div>

                  {/* Expanded Content: 2-Box Multi-Tier Explanation */}
                  {isExpanded && (
                    <div className="px-5 pb-5 pt-2 border-t border-white/5 space-y-4 text-sm animate-in fade-in duration-200">
                      {/* Full Question Text */}
                      <div className="text-slate-100 font-medium whitespace-pre-line bg-slate-950/40 p-4 rounded-xl border border-white/5">
                        <MathRenderer content={detail.content} />
                      </div>

                      {/* Attached Data Table if available */}
                      {originalQuestion?.dataTable && (
                        <div className="overflow-x-auto rounded-xl border border-white/10 bg-slate-950/60 p-3">
                          <div className="flex justify-between text-xs font-bold text-slate-300 mb-2">
                            <span>{originalQuestion.dataTable.title}</span>
                            <span className="text-indigo-400 font-mono">{originalQuestion.dataTable.unit}</span>
                          </div>
                          <table className="w-full text-xs text-left border-collapse">
                            <thead>
                              <tr className="border-b border-white/10 text-slate-400">
                                {originalQuestion.dataTable.headers.map((h, i) => (
                                  <th key={i} className="p-2 font-bold">{h}</th>
                                ))}
                              </tr>
                            </thead>
                            <tbody>
                              {originalQuestion.dataTable.rows.map((r, ri) => (
                                <tr key={ri} className="border-b border-white/5 hover:bg-white/5">
                                  {r.map((c, ci) => (
                                    <td key={ci} className="p-2 text-slate-200 font-mono">{c}</td>
                                  ))}
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}

                      {/* Student Choice vs Official Answer Comparison */}
                      <div className="grid grid-cols-1 md:grid-cols-2 gap-3 text-xs">
                        <div className="p-3.5 rounded-xl bg-slate-900/60 border border-white/5">
                          <span className="font-bold text-slate-400 block mb-1.5 flex items-center gap-1.5">
                            Bài làm của em:
                          </span>
                          {detail.format === 'SingleChoice' && (
                            <span className={`font-semibold text-sm ${detail.isCorrect ? 'text-emerald-400' : 'text-rose-400'}`}>
                              {detail.studentAnswer.selectedOption ? `Đáp án ${detail.studentAnswer.selectedOption}` : 'Chưa chọn phương án nào'}
                            </span>
                          )}
                          {detail.format === 'TrueFalseGroup' && (
                            <div className="space-y-1">
                              {['a', 'b', 'c', 'd'].map(k => (
                                <div key={k} className="flex justify-between items-center py-0.5 border-b border-white/5 last:border-0">
                                  <span className="text-slate-300">Ý {k}):</span>
                                  <span className="font-semibold text-slate-200">
                                    {detail.studentAnswer.subAnswers?.[k] !== undefined
                                      ? (detail.studentAnswer.subAnswers[k] ? 'ĐÚNG' : 'SAI')
                                      : 'Chưa chọn'}
                                  </span>
                                </div>
                              ))}
                            </div>
                          )}
                          {detail.format === 'ShortAnswer' && (
                            <span className={`font-mono font-bold text-sm ${detail.isCorrect ? 'text-emerald-400' : 'text-rose-400'}`}>
                              {detail.studentAnswer.textAnswer || 'Chưa nhập con số'}
                            </span>
                          )}
                        </div>

                        <div className="p-3.5 rounded-xl bg-slate-900/60 border border-white/5">
                          <span className="font-bold text-emerald-400 block mb-1.5 flex items-center gap-1.5">
                            <Check className="w-3.5 h-3.5" />
                            Đáp án chuẩn Bộ GD&ĐT:
                          </span>
                          {detail.format === 'SingleChoice' && (
                            <span className="font-semibold text-sm text-emerald-400">
                              Đáp án {detail.correctAnswerData.correctOption}
                            </span>
                          )}
                          {detail.format === 'TrueFalseGroup' && (
                            <div className="space-y-1">
                              {['a', 'b', 'c', 'd'].map(k => (
                                <div key={k} className="flex justify-between items-center py-0.5 border-b border-white/5 last:border-0">
                                  <span className="text-slate-300">Ý {k}):</span>
                                  <span className="font-semibold text-emerald-400">
                                    {detail.correctAnswerData.subAnswers?.[k] ? 'ĐÚNG' : 'SAI'}
                                  </span>
                                </div>
                              ))}
                            </div>
                          )}
                          {detail.format === 'ShortAnswer' && (
                            <span className="font-mono font-bold text-sm text-emerald-400">
                              {detail.correctAnswerData.correctAnswer} {detail.correctAnswerData.unit}
                            </span>
                          )}
                        </div>
                      </div>

                      {/* ============================================================ */}
                      {/* BOX 1: CẢNH BÁO BẪY LỖI SAI (Màu Đỏ nhạt - error_trap) */}
                      {/* ============================================================ */}
                      {trapMessage && (
                        <div className="p-4 rounded-2xl bg-rose-950/30 border border-rose-500/30 text-xs sm:text-sm text-rose-200 space-y-2 animate-in fade-in duration-200">
                          <div className="flex items-center gap-2 font-bold text-rose-400 uppercase tracking-wider text-[11px]">
                            <AlertTriangle className="w-4 h-4 text-rose-400" />
                            <span>Bắt bệnh lỗi sai & Cảnh báo bẫy đề thi</span>
                          </div>
                          <div className="whitespace-pre-line leading-relaxed font-medium pl-6 text-rose-200/90">
                            {trapMessage}
                          </div>
                        </div>
                      )}

                      {/* ============================================================ */}
                      {/* BOX 2: LỜI GIẢI CHUẨN & CĂN CỨ PHÁP LÝ (Màu Xanh lá nhạt) */}
                      {/* ============================================================ */}
                      {detail.explanation && (
                        <div className="p-4 rounded-2xl bg-emerald-950/20 border border-emerald-500/30 text-xs sm:text-sm text-emerald-100 space-y-2">
                          <div className="flex items-center gap-2 font-bold text-emerald-400 uppercase tracking-wider text-[11px]">
                            <HelpCircle className="w-4 h-4 text-emerald-400" />
                            <span>Lời giải chi tiết & Căn cứ chuẩn</span>
                          </div>
                          <div className="leading-relaxed pl-6 text-emerald-100/90">
                            <MathRenderer content={detail.explanation} />
                          </div>
                        </div>
                      )}

                      {/* AI Tutor Action Button (Context-Aware) */}
                      {originalQuestion && (
                        <div className="flex items-center justify-end pt-2">
                          <button
                            onClick={() => onOpenAiTutor(originalQuestion, detail.studentAnswer)}
                            className="flex items-center gap-2 px-5 py-2.5 rounded-xl text-xs font-bold bg-gradient-to-r from-violet-600 via-indigo-600 to-cyan-500 hover:from-violet-500 hover:to-cyan-400 text-white shadow-lg shadow-violet-600/30 transition-all hover:scale-105 cursor-pointer"
                          >
                            <Sparkles className="w-4 h-4" />
                            <span>💬 Hỏi Gia sư AI về câu này</span>
                          </button>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
};
