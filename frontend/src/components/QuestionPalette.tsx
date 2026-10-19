'use client';

import React from 'react';
import { Question, StudentAnswer } from '@/types';
import { CheckCircle2 } from 'lucide-react';

interface QuestionPaletteProps {
  questions: Question[];
  currentQuestionIndex: number;
  answers: Map<string, StudentAnswer>;
  flaggedQuestionIds: Set<string>;
  onSelectQuestion: (index: number) => void;
}

export const QuestionPalette: React.FC<QuestionPaletteProps> = ({
  questions,
  currentQuestionIndex,
  answers,
  flaggedQuestionIds,
  onSelectQuestion
}) => {
  // Phân chia câu hỏi theo 3 phần chuẩn Bộ GD&ĐT 2025
  const partI = questions.filter(q => q.format === 'SingleChoice');
  const partII = questions.filter(q => q.format === 'TrueFalseGroup');
  const partIII = questions.filter(q => q.format === 'ShortAnswer');

  const isAnswered = (q: Question) => {
    const ans = answers.get(q.id);
    if (!ans) return false;
    if (q.format === 'SingleChoice') return !!ans.selectedOption;
    if (q.format === 'TrueFalseGroup') return ans.subAnswers && Object.keys(ans.subAnswers).length > 0;
    if (q.format === 'ShortAnswer') return !!ans.textAnswer && ans.textAnswer.trim() !== '';
    return false;
  };

  const answeredCount = questions.filter(isAnswered).length;
  const progressPercent = Math.round((answeredCount / questions.length) * 100);

  const renderQuestionButtons = (groupQuestions: Question[]) => {
    return (
      <div className="grid grid-cols-5 gap-2">
        {groupQuestions.map((q) => {
          const globalIndex = questions.findIndex(item => item.id === q.id);
          const answered = isAnswered(q);
          const isCurrent = globalIndex === currentQuestionIndex;
          const isFlagged = flaggedQuestionIds.has(q.id);

          return (
            <button
              key={q.id}
              onClick={() => onSelectQuestion(globalIndex)}
              className={`relative h-10 rounded-lg text-xs font-bold transition-all flex items-center justify-center border ${
                isCurrent
                  ? 'border-cyan-400 bg-cyan-500/20 text-cyan-200 ring-2 ring-cyan-400/50 shadow-md'
                  : answered
                  ? 'border-indigo-500/40 bg-indigo-600/30 text-indigo-200 hover:bg-indigo-600/40'
                  : 'border-white/10 bg-slate-900/60 text-slate-400 hover:border-slate-600 hover:text-slate-200'
              }`}
            >
              <span>{globalIndex + 1}</span>

              {/* Flag Indicator */}
              {isFlagged && (
                <span className="absolute -top-1.5 -right-1.5 w-3.5 h-3.5 bg-amber-500 rounded-full flex items-center justify-center text-[8px] text-slate-950 font-black shadow-sm">
                  ★
                </span>
              )}
            </button>
          );
        })}
      </div>
    );
  };

  return (
    <div className="glass-panel rounded-2xl p-4 sm:p-5 border border-white/10 sticky top-20">
      {/* Tiến độ bài thi */}
      <div className="mb-5 pb-4 border-b border-white/10">
        <div className="flex items-center justify-between text-xs font-semibold mb-2">
          <span className="text-slate-400 flex items-center gap-1.5">
            <CheckCircle2 className="w-3.5 h-3.5 text-indigo-400" />
            Tiến độ hoàn thành
          </span>
          <span className="text-indigo-300 font-bold">
            {answeredCount}/{questions.length} câu ({progressPercent}%)
          </span>
        </div>
        <div className="w-full bg-slate-800/80 rounded-full h-2 overflow-hidden border border-white/5">
          <div
            className="bg-gradient-to-r from-indigo-500 via-indigo-400 to-cyan-400 h-2 rounded-full transition-all duration-300"
            style={{ width: `${progressPercent}%` }}
          />
        </div>
      </div>

      {/* Legend / Chú thích */}
      <div className="grid grid-cols-3 gap-2 text-[11px] mb-4 text-slate-400 bg-slate-900/40 p-2.5 rounded-xl border border-white/5">
        <div className="flex items-center gap-1.5">
          <div className="w-2.5 h-2.5 rounded bg-indigo-500/80" />
          <span>Đã làm</span>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-2.5 h-2.5 rounded border border-cyan-400 bg-cyan-500/30" />
          <span>Đang làm</span>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-2.5 h-2.5 rounded bg-amber-500" />
          <span>Đánh dấu</span>
        </div>
      </div>

      {/* Danh sách 3 phần */}
      <div className="space-y-4 max-h-[calc(100vh-320px)] overflow-y-auto pr-1">
        {partI.length > 0 && (
          <div>
            <div className="text-[11px] font-bold text-slate-400 uppercase tracking-wider mb-2 flex items-center justify-between">
              <span>Phần I: Trắc nghiệm</span>
              <span className="text-slate-500">({partI.length} câu)</span>
            </div>
            {renderQuestionButtons(partI)}
          </div>
        )}

        {partII.length > 0 && (
          <div>
            <div className="text-[11px] font-bold text-slate-400 uppercase tracking-wider mb-2 flex items-center justify-between">
              <span>Phần II: Đúng / Sai</span>
              <span className="text-slate-500">({partII.length} câu)</span>
            </div>
            {renderQuestionButtons(partII)}
          </div>
        )}

        {partIII.length > 0 && (
          <div>
            <div className="text-[11px] font-bold text-slate-400 uppercase tracking-wider mb-2 flex items-center justify-between">
              <span>Phần III: Trả lời ngắn</span>
              <span className="text-slate-500">({partIII.length} câu)</span>
            </div>
            {renderQuestionButtons(partIII)}
          </div>
        )}
      </div>
    </div>
  );
};
