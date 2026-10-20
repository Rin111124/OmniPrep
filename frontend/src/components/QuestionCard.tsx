'use client';

import React, { useState, useRef } from 'react';
import { DataTable, Question, StudentAnswer } from '@/types';
import {
  Flag,
  Table as TableIcon,
  Sparkles,
  Flame,
  Highlighter,
  EyeOff,
  Eye,
  X
} from 'lucide-react';
import { MathRenderer } from './MathRenderer';

interface QuestionCardProps {
  question: Question;
  questionNumber: number;
  currentAnswer?: StudentAnswer;
  isFlagged: boolean;
  onAnswerChange: (newAnswer: StudentAnswer) => void;
  onToggleFlag: () => void;
  onAskTutor?: () => void;
}

export const QuestionCard: React.FC<QuestionCardProps> = ({
  question,
  questionNumber,
  currentAnswer,
  isFlagged,
  onAnswerChange,
  onToggleFlag,
  onAskTutor
}) => {
  // Option Elimination (Loại trừ phương án nhiễu)
  const [eliminatedOptions, setEliminatedOptions] = useState<Set<string>>(new Set());

  // Text Highlighter State
  const [highlightedSnippets, setHighlightedSnippets] = useState<string[]>([]);
  const [selectionRange, setSelectionRange] = useState<{ text: string; x: number; y: number } | null>(null);
  const contentRef = useRef<HTMLDivElement>(null);

  // Handle Text Selection for Highlighting
  const handleMouseUp = () => {
    if (typeof window === 'undefined') return;
    const selection = window.getSelection();
    if (!selection || selection.isCollapsed) {
      setSelectionRange(null);
      return;
    }

    const selectedText = selection.toString().trim();
    if (selectedText.length > 2 && contentRef.current && contentRef.current.contains(selection.anchorNode)) {
      const range = selection.getRangeAt(0);
      const rect = range.getBoundingClientRect();
      const containerRect = contentRef.current.getBoundingClientRect();

      setSelectionRange({
        text: selectedText,
        x: rect.left - containerRect.left + rect.width / 2,
        y: rect.top - containerRect.top - 38
      });
    } else {
      setSelectionRange(null);
    }
  };

  const applyHighlight = () => {
    if (selectionRange && selectionRange.text) {
      setHighlightedSnippets(prev => [...prev, selectionRange.text]);
      setSelectionRange(null);
      if (window.getSelection) {
        window.getSelection()?.removeAllRanges();
      }
    }
  };

  const removeHighlight = (snippet: string) => {
    setHighlightedSnippets(prev => prev.filter(s => s !== snippet));
  };

  // Toggle eliminate option
  const toggleEliminate = (e: React.MouseEvent, optKey: string) => {
    e.stopPropagation();
    e.preventDefault();
    setEliminatedOptions(prev => {
      const next = new Set(prev);
      if (next.has(optKey)) {
        next.delete(optKey);
      } else {
        next.add(optKey);
        // If current selected option is eliminated, deselect
        if (currentAnswer?.selectedOption === optKey) {
          onAnswerChange({
            questionId: question.id,
            selectedOption: undefined
          });
        }
      }
      return next;
    });
  };

  // Parse Data Table (either structured dataTable or legacy attachedTable)
  let activeTable: DataTable | null = question.dataTable || null;
  if (!activeTable && question.attachedTable) {
    try {
      activeTable = JSON.parse(question.attachedTable);
    } catch {
      // not json
    }
  }

  const handleSelectOption = (optKey: string) => {
    if (eliminatedOptions.has(optKey)) return;
    onAnswerChange({
      questionId: question.id,
      selectedOption: optKey
    });
  };

  const handleToggleSubAnswer = (subKey: string, value: boolean) => {
    const existingSubs = currentAnswer?.subAnswers || {};
    const updatedSubs = { ...existingSubs, [subKey]: value };
    onAnswerChange({
      questionId: question.id,
      subAnswers: updatedSubs
    });
  };

  const handleTextAnswerChange = (val: string) => {
    // Only allow digits, minus, dot and comma
    const sanitized = val.replace(/[^0-9.,-]/g, '');
    onAnswerChange({
      questionId: question.id,
      textAnswer: sanitized
    });
  };

  const isFocusQuestion = question.sourceTags && question.sourceTags.length >= 2;

  return (
    <div className="glass-panel rounded-3xl p-6 sm:p-8 border border-white/10 relative overflow-hidden space-y-6">
      {/* ============================================================ */}
      {/* 1. TOP TOOLBAR: Question #, Part, Difficulty, Focus Badge, Flag */}
      {/* ============================================================ */}
      <div className="flex flex-wrap items-center justify-between gap-3 pb-4 border-b border-white/10">
        <div className="flex items-center gap-3">
          <span className="flex items-center justify-center w-10 h-10 rounded-xl bg-indigo-600 text-white font-extrabold text-base shadow-lg shadow-indigo-600/30">
            {questionNumber}
          </span>
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-xs font-bold uppercase tracking-wider px-2.5 py-1 rounded-md bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
                {question.format === 'SingleChoice' && 'Phần I: Trắc nghiệm'}
                {question.format === 'TrueFalseGroup' && 'Phần II: Đúng / Sai'}
                {question.format === 'ShortAnswer' && 'Phần III: Trả lời ngắn'}
              </span>

              <span className="text-xs font-medium px-2 py-0.5 rounded bg-slate-800 text-slate-300 border border-white/5">
                {question.difficulty === 'Recognition' && 'Nhận biết'}
                {question.difficulty === 'Comprehension' && 'Thông hiểu'}
                {question.difficulty === 'Application' && 'Vận dụng'}
                {question.difficulty === 'HighApplication' && 'Vận dụng cao'}
              </span>

              <span className="text-xs text-slate-400 font-medium hidden sm:inline">
                {question.topic}
              </span>
            </div>

            {/* Prominent Multi-Exam Focus Badge */}
            {isFocusQuestion ? (
              <div className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-rose-500/20 border border-rose-500/30 text-rose-300 text-[11px] font-bold mt-1 shadow-sm">
                <Flame className="w-3.5 h-3.5 text-rose-400 fill-rose-400 animate-pulse" />
                <span>Câu hỏi trọng tâm: {question.sourceTags.join(' • ')}</span>
              </div>
            ) : question.sourceTags && question.sourceTags.length > 0 ? (
              <p className="text-[11px] text-slate-400 mt-1">
                Nguồn: {question.sourceTags.join(' • ')}
              </p>
            ) : null}
          </div>
        </div>

        {/* Action Controls */}
        <div className="flex items-center gap-2">
          {onAskTutor && (
            <button
              onClick={onAskTutor}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold bg-violet-600/20 text-violet-300 border border-violet-500/30 hover:bg-violet-600/30 transition-all cursor-pointer"
            >
              <Sparkles className="w-3.5 h-3.5 text-violet-400" />
              <span>Gia sư AI gợi ý</span>
            </button>
          )}

          <button
            onClick={onToggleFlag}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold transition-all border cursor-pointer ${
              isFlagged
                ? 'bg-amber-500/20 text-amber-300 border-amber-500/40 shadow-sm'
                : 'bg-slate-900/60 text-slate-400 border-white/10 hover:text-slate-200'
            }`}
          >
            <Flag className={`w-3.5 h-3.5 ${isFlagged ? 'fill-amber-400 text-amber-400' : ''}`} />
            <span>{isFlagged ? 'Đã gắn cờ 🚩' : 'Gắn cờ'}</span>
          </button>
        </div>
      </div>

      {/* ============================================================ */}
      {/* 2. QUESTION CONTENT WITH TEXT HIGHLIGHTER SUPPORT */}
      {/* ============================================================ */}
      <div
        ref={contentRef}
        onMouseUp={handleMouseUp}
        className="relative text-slate-100 text-base sm:text-lg font-medium leading-relaxed select-text"
      >
        {/* Floating Highlight Button */}
        {selectionRange && (
          <div
            className="absolute z-20 -translate-x-1/2 flex items-center gap-1 bg-slate-900 border border-amber-400/40 px-2.5 py-1 rounded-xl shadow-xl text-xs font-bold text-amber-300 animate-in fade-in zoom-in-95 cursor-pointer hover:bg-amber-500/20"
            style={{ left: `${selectionRange.x}px`, top: `${selectionRange.y}px` }}
            onClick={applyHighlight}
          >
            <Highlighter className="w-3.5 h-3.5" />
            <span>Tô vàng dữ kiện</span>
          </div>
        )}

        <div className="whitespace-pre-line">
          <MathRenderer content={question.content} />
        </div>

        {/* Display Active User Highlights */}
        {highlightedSnippets.length > 0 && (
          <div className="mt-3 pt-2 border-t border-white/5 flex flex-wrap items-center gap-2">
            <span className="text-[11px] text-amber-400 font-bold uppercase tracking-wider flex items-center gap-1">
              <Highlighter className="w-3 h-3" />
              Đoạn đã tô sáng:
            </span>
            {highlightedSnippets.map((snip, sIdx) => (
              <span
                key={sIdx}
                className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-lg bg-amber-400/20 border border-amber-400/30 text-amber-200 text-xs font-medium"
              >
                <mark className="bg-transparent text-amber-200 font-semibold truncate max-w-[200px]">
                  &quot;{snip}&quot;
                </mark>
                <button
                  onClick={() => removeHighlight(snip)}
                  className="hover:text-white ml-0.5"
                  title="Xóa highlight"
                >
                  <X className="w-3 h-3" />
                </button>
              </span>
            ))}
          </div>
        )}
      </div>

      {/* ============================================================ */}
      {/* 3. ATTACHED DATA TABLE (FOR GEOGRAPHY / ECONOMICS) */}
      {/* ============================================================ */}
      {activeTable && (
        <div className="overflow-hidden rounded-2xl border border-white/10 bg-slate-950/70 shadow-lg">
          {/* Table Header Bar with Title & Unit */}
          <div className="p-3.5 bg-slate-900/80 border-b border-white/10 flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2 text-xs font-bold text-white uppercase tracking-wider">
              <TableIcon className="w-4 h-4 text-cyan-400" />
              <span>{activeTable.title || 'Bảng số liệu thống kê'}</span>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-[10px] text-slate-400 sm:hidden">↔ Cuộn xem đủ cột</span>
              {activeTable.unit && (
                <span className="text-[11px] font-mono text-cyan-300 bg-cyan-950/40 px-2 py-0.5 rounded border border-cyan-500/20">
                  Đơn vị: {activeTable.unit}
                </span>
              )}
            </div>
          </div>

          <div className="overflow-x-auto -webkit-overflow-scrolling-touch">
            <table className="w-full text-left border-collapse text-xs sm:text-sm">
              <thead>
                <tr className="border-b border-white/10 bg-white/5 text-slate-200">
                  {activeTable.headers.map((h, i) => (
                    <th key={i} className="py-2.5 px-3 sm:px-4 font-bold tracking-wide whitespace-nowrap">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-white/5">
                {activeTable.rows.map((row, rIdx) => (
                  <tr
                    key={rIdx}
                    className="hover:bg-white/5 transition-colors font-mono text-slate-300"
                  >
                    {row.map((cell, cIdx) => (
                      <td key={cIdx} className="py-2.5 px-3 sm:px-4 whitespace-nowrap sm:whitespace-normal">
                        {cell}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ============================================================ */}
      {/* 4. ANSWER INPUTS BY FORMAT */}
      {/* ============================================================ */}

      {/* FORMAT 1: SINGLE CHOICE (A, B, C, D) + OPTION ELIMINATION */}
      {question.format === 'SingleChoice' && question.answerData.options && (
        <div className="space-y-3">
          <div className="flex items-center justify-between text-xs text-slate-400">
            <span>Chọn 1 đáp án đúng:</span>
            <span className="text-[11px] italic text-slate-500 hidden sm:inline">
              Mẹo: Bấm biểu tượng con mắt gạch chéo để loại trừ phương án sai
            </span>
          </div>

          {Object.entries(question.answerData.options).map(([key, text]) => {
            const isSelected = currentAnswer?.selectedOption === key;
            const isEliminated = eliminatedOptions.has(key);

            return (
              <div
                key={key}
                onContextMenu={(e) => toggleEliminate(e, key)}
                onClick={() => handleSelectOption(key)}
                className={`w-full text-left p-3.5 sm:p-4 rounded-2xl border transition-all flex items-start justify-between gap-3 cursor-pointer relative group touch-manipulation ${
                  isEliminated
                    ? 'opacity-35 bg-slate-950/40 border-white/5 text-slate-500 line-through'
                    : isSelected
                    ? 'bg-indigo-600/20 border-indigo-500 ring-2 ring-indigo-500/40 text-white shadow-lg shadow-indigo-600/10'
                    : 'bg-slate-900/50 border-white/10 text-slate-300 hover:bg-slate-800/60 hover:border-slate-600'
                }`}
              >
                <div className="flex items-start gap-3 flex-1 min-w-0">
                  <span
                    className={`shrink-0 w-8 h-8 rounded-xl font-bold flex items-center justify-center text-sm transition-colors ${
                      isEliminated
                        ? 'bg-slate-800 text-slate-600'
                        : isSelected
                        ? 'bg-indigo-600 text-white shadow-md'
                        : 'bg-white/5 text-slate-400 border border-white/10 group-hover:text-white'
                    }`}
                  >
                    {key}
                  </span>
                  <span className={`pt-1 text-sm sm:text-base leading-snug break-words ${isEliminated ? 'line-through' : ''}`}>
                    <MathRenderer content={text} />
                  </span>
                </div>

                {/* Option Elimination Toggle Button */}
                <button
                  type="button"
                  onClick={(e) => toggleEliminate(e, key)}
                  className={`p-2 rounded-lg text-xs transition-colors self-start shrink-0 touch-manipulation ${
                    isEliminated
                      ? 'text-cyan-400 bg-cyan-950/40 hover:bg-cyan-900/40 opacity-100'
                      : 'text-slate-500 hover:text-slate-300 hover:bg-white/5 opacity-70 sm:opacity-0 sm:group-hover:opacity-100'
                  }`}
                  title={isEliminated ? 'Bỏ gạch loại trừ' : 'Gạch loại trừ phương án này'}
                >
                  {isEliminated ? <Eye className="w-4 h-4" /> : <EyeOff className="w-4 h-4" />}
                </button>
              </div>
            );
          })}
        </div>
      )}

      {/* FORMAT 2: TRUE / FALSE GROUP (a, b, c, d) */}
      {question.format === 'TrueFalseGroup' && question.answerData.subQuestions && (
        <div className="space-y-4">
          <div className="text-xs font-semibold text-slate-400 uppercase tracking-wider">
            Chọn Đúng hoặc Sai cho từng mệnh đề sau (Bắt buộc chọn đủ cả 4 ý):
          </div>

          <div className="space-y-3">
            {Object.entries(question.answerData.subQuestions).map(([subKey, subText]) => {
              const currentSubVal = currentAnswer?.subAnswers?.[subKey];

              return (
                <div
                  key={subKey}
                  className="p-3.5 sm:p-5 rounded-2xl bg-slate-900/60 border border-white/10 flex flex-col sm:flex-row sm:items-center justify-between gap-3 sm:gap-4"
                >
                  <div className="flex items-start gap-2.5 sm:gap-3 flex-1 min-w-0">
                    <span className="shrink-0 w-7 h-7 rounded-lg bg-indigo-500/20 border border-indigo-500/30 text-indigo-300 font-bold flex items-center justify-center text-xs">
                      {subKey})
                    </span>
                    <span className="text-sm sm:text-base text-slate-200 font-medium pt-0.5 leading-snug break-words">
                      <MathRenderer content={subText} />
                    </span>
                  </div>

                  {/* Responsive Radio Toggles [ĐÚNG] and [SAI] (Full width grid on mobile, horizontal on desktop) */}
                  <div className="grid grid-cols-2 sm:flex items-center gap-2 w-full sm:w-auto shrink-0 pt-1 sm:pt-0">
                    <button
                      type="button"
                      onClick={() => handleToggleSubAnswer(subKey, true)}
                      className={`py-2.5 sm:px-5 rounded-xl text-xs sm:text-sm font-extrabold tracking-wider transition-all border cursor-pointer text-center touch-manipulation ${
                        currentSubVal === true
                          ? 'bg-emerald-600 text-white border-emerald-500 shadow-lg shadow-emerald-600/30 ring-2 ring-emerald-500/40'
                          : 'bg-slate-950/70 text-slate-400 border-white/10 hover:border-emerald-500/40 hover:text-emerald-300'
                      }`}
                    >
                      ĐÚNG
                    </button>
                    <button
                      type="button"
                      onClick={() => handleToggleSubAnswer(subKey, false)}
                      className={`py-2.5 sm:px-5 rounded-xl text-xs sm:text-sm font-extrabold tracking-wider transition-all border cursor-pointer text-center touch-manipulation ${
                        currentSubVal === false
                          ? 'bg-rose-600 text-white border-rose-500 shadow-lg shadow-rose-600/30 ring-2 ring-rose-500/40'
                          : 'bg-slate-950/70 text-slate-400 border-white/10 hover:border-rose-500/40 hover:text-rose-300'
                      }`}
                    >
                      SAI
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* FORMAT 3: SHORT ANSWER */}
      {question.format === 'ShortAnswer' && (
        <div className="p-6 sm:p-7 rounded-2xl bg-slate-900/60 border border-white/10 space-y-4">
          <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider">
            Nhập kết quả con số của em:
          </label>

          <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-3">
            <input
              type="text"
              value={currentAnswer?.textAnswer || ''}
              onChange={(e) => handleTextAnswerChange(e.target.value)}
              placeholder="Chỉ điền con số (Ví dụ: 1102 hoặc 12,5)..."
              className="flex-1 bg-slate-950 border border-white/15 focus:border-cyan-400 focus:ring-2 focus:ring-cyan-400/30 rounded-2xl px-5 py-3.5 text-slate-100 text-lg font-mono outline-none transition-all shadow-inner"
            />
            {question.answerData.unit && (
              <span className="px-5 py-3.5 rounded-2xl bg-white/5 border border-white/10 text-slate-300 font-semibold text-sm flex items-center justify-center font-mono">
                Đơn vị: {question.answerData.unit}
              </span>
            )}
          </div>

          <div className="flex items-center gap-2 text-xs text-slate-400">
            <span className="w-1.5 h-1.5 rounded-full bg-cyan-400" />
            <span>
              Hệ thống tự động chặn chữ cái, hỗ trợ cả dấu chấm <code className="text-cyan-300 font-mono">.</code> và dấu phẩy <code className="text-cyan-300 font-mono">,</code> theo quy chuẩn Bộ GD&ĐT 2025.
            </span>
          </div>
        </div>
      )}
    </div>
  );
};
