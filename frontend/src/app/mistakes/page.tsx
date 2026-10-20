'use client';

import React, { useEffect, useState, useMemo } from 'react';
import Link from 'next/link';
import { Navbar } from '@/components/Navbar';
import { useAuth } from '@/context/AuthContext';
import { MathRenderer } from '@/components/MathRenderer';
import { AiTutorDrawer } from '@/components/AiTutorDrawer';
import {
  clearAllMistakes,
  deleteMistake,
  getMistakes,
  syncMistakesWithServer,
  toggleMistakeMastered
} from '@/lib/mistakes';
import { MistakeItem, Question, StudentAnswer } from '@/types';
import {
  BookMarked,
  Sparkles,
  RotateCcw,
  Trash2,
  CheckCircle2,
  XCircle,
  AlertTriangle,
  Search,
  ArrowRight,
  Clock,
  Check
} from 'lucide-react';

export default function MistakeNotebookPage() {
  const { user, isLoading: authLoading } = useAuth();
  const [mistakes, setMistakes] = useState<MistakeItem[]>([]);
  const [selectedSubject, setSelectedSubject] = useState<string>('ALL');
  const [selectedFormat, setSelectedFormat] = useState<string>('ALL');
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [filterStatus, setFilterStatus] = useState<'all' | 'unmastered' | 'mastered'>('all');

  // AI Tutor Drawer State
  const [tutorQuestion, setTutorQuestion] = useState<Question | null>(null);
  const [tutorAnswer, setTutorAnswer] = useState<StudentAnswer | undefined>(undefined);
  const [isTutorOpen, setIsTutorOpen] = useState(false);

  useEffect(() => {
    if (authLoading) return;
    let active = true;
    const frame = requestAnimationFrame(() => {
      if (!active) return;
      if (!user.isAuthenticated) {
        setMistakes([]);
        return;
      }
      setMistakes(getMistakes(user.id));
      void syncMistakesWithServer(user.id)
        .then(synced => {
          if (active) setMistakes(synced);
        })
        .catch(err => console.warn('Background sync failed:', err));
    });

    return () => {
      active = false;
      cancelAnimationFrame(frame);
    };
  }, [authLoading, user.id, user.isAuthenticated]);

  const handleToggleMastered = (id: string) => {
    toggleMistakeMastered(user.id, id);
    setMistakes(getMistakes(user.id));
  };

  const handleDelete = (id: string) => {
    deleteMistake(user.id, id);
    setMistakes(getMistakes(user.id));
  };

  const handleClearAll = () => {
    if (window.confirm('Em có chắc chắn muốn xóa toàn bộ câu trong Sổ tay bẫy đề thi không?')) {
      clearAllMistakes(user.id);
      setMistakes([]);
    }
  };

  const handleOpenAiTutor = (item: MistakeItem) => {
    setTutorQuestion(item.question);
    setTutorAnswer(item.studentAnswer);
    setIsTutorOpen(true);
  };

  // Filtered Mistakes
  const filteredMistakes = useMemo(() => {
    return mistakes.filter(item => {
      if (selectedSubject !== 'ALL' && item.subjectCode !== selectedSubject) return false;
      if (selectedFormat !== 'ALL' && item.question.format !== selectedFormat) return false;
      if (filterStatus === 'unmastered' && item.mastered) return false;
      if (filterStatus === 'mastered' && !item.mastered) return false;
      if (searchQuery.trim()) {
        const query = searchQuery.toLowerCase();
        const inContent = item.question.content.toLowerCase().includes(query);
        const inTopic = item.question.topic.toLowerCase().includes(query);
        return inContent || inTopic;
      }
      return true;
    });
  }, [mistakes, selectedSubject, selectedFormat, filterStatus, searchQuery]);

  const unmasteredCount = mistakes.filter(m => !m.mastered).length;
  const masteredCount = mistakes.filter(m => m.mastered).length;

  return (
    <div className="min-h-screen flex flex-col bg-[#07090e]">
      <Navbar />

      <main className="flex-1 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-10 space-y-8">
        {/* Top Header Banner */}
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-6 pb-6 border-b border-white/10">
          <div>
            <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-rose-500/10 border border-rose-500/30 text-rose-300 text-xs font-semibold mb-2">
              <BookMarked className="w-3.5 h-3.5 text-rose-400" />
              <span>Sổ tay Bẫy Đề Thi Cá nhân hóa (Mistake Notebook)</span>
            </div>
            <h1 className="text-2xl sm:text-4xl font-extrabold text-white tracking-tight">
              Kho Lưu Trữ & Rèn Luyện Câu Sai
            </h1>
            <p className="text-xs sm:text-sm text-slate-400 mt-1 max-w-2xl leading-relaxed">
              Mỗi câu sai đều là một bậc thang tiến tới điểm 10. Hệ thống lưu lại các câu em đã vấp phải, phân tích bẫy đề thi và cung cấp chế độ luyện lại chuyên sâu.
            </p>
          </div>

          {/* Action CTAs */}
          <div className="flex flex-wrap items-center gap-3">
            {unmasteredCount > 0 && (
              <Link
                href="/exam?mode=mistakes"
                className="flex items-center gap-2 px-5 py-3 rounded-2xl font-bold text-white bg-gradient-to-r from-rose-600 to-indigo-600 hover:opacity-95 shadow-lg shadow-rose-600/20 transition-all hover:scale-105 text-xs sm:text-sm"
              >
                <RotateCcw className="w-4 h-4" />
                <span>Luyện lại {unmasteredCount} câu chưa thạo</span>
                <ArrowRight className="w-4 h-4" />
              </Link>
            )}

            {mistakes.length > 0 && (
              <button
                onClick={handleClearAll}
                className="flex items-center gap-1.5 px-3.5 py-3 rounded-2xl font-semibold text-slate-400 hover:text-rose-400 bg-slate-900/60 border border-white/10 hover:border-rose-500/30 transition-all text-xs"
                title="Xóa toàn bộ câu trong sổ"
              >
                <Trash2 className="w-4 h-4" />
                <span>Xóa hết</span>
              </button>
            )}
          </div>
        </div>

        {/* 3 Metric Cards */}
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-6">
          <div className="glass-panel rounded-3xl p-6 border border-white/10">
            <div className="text-xs uppercase tracking-wider text-slate-400 font-bold mb-1">
              Tổng số câu đã vấp bẫy
            </div>
            <div className="text-3xl font-black text-white font-mono">{mistakes.length}</div>
            <p className="text-xs text-slate-400 mt-1">Ghi nhận từ các lần thi thử</p>
          </div>

          <div className="glass-panel rounded-3xl p-6 border border-rose-500/30 bg-rose-950/20">
            <div className="text-xs uppercase tracking-wider text-rose-300 font-bold mb-1">
              Cần rèn luyện lại ngay
            </div>
            <div className="text-3xl font-black text-rose-400 font-mono">{unmasteredCount}</div>
            <p className="text-xs text-slate-400 mt-1">Chưa khắc phục được bẫy</p>
          </div>

          <div className="glass-panel rounded-3xl p-6 border border-emerald-500/30 bg-emerald-950/20">
            <div className="text-xs uppercase tracking-wider text-emerald-300 font-bold mb-1">
              Đã khắc phục thành công
            </div>
            <div className="text-3xl font-black text-emerald-400 font-mono">{masteredCount}</div>
            <p className="text-xs text-slate-400 mt-1">Đã hiểu rõ bản chất & làm đúng</p>
          </div>
        </div>

        {/* Filter and Search Bar */}
        <div className="glass-panel rounded-2xl p-3.5 sm:p-4 border border-white/10 flex flex-col lg:flex-row items-stretch lg:items-center justify-between gap-3 sm:gap-4">
          <div className="flex flex-col sm:flex-row flex-wrap items-stretch sm:items-center gap-2.5 sm:gap-3">
            {/* Subject Filter */}
            <select
              value={selectedSubject}
              onChange={(e) => setSelectedSubject(e.target.value)}
              className="w-full sm:w-auto bg-slate-900 border border-white/10 rounded-xl px-3 py-2 text-xs font-semibold text-slate-200 outline-none focus:border-indigo-500"
            >
              <option value="ALL">Tất cả môn học</option>
              <option value="GEOGRAPHY">Địa lý</option>
              <option value="LAW_ECONOMICS">Giáo dục Kinh tế & Pháp luật</option>
            </select>

            {/* Format Filter */}
            <select
              value={selectedFormat}
              onChange={(e) => setSelectedFormat(e.target.value)}
              className="w-full sm:w-auto bg-slate-900 border border-white/10 rounded-xl px-3 py-2 text-xs font-semibold text-slate-200 outline-none focus:border-indigo-500"
            >
              <option value="ALL">Tất cả định dạng</option>
              <option value="SingleChoice">Phần I: Trắc nghiệm</option>
              <option value="TrueFalseGroup">Phần II: Đúng / Sai</option>
              <option value="ShortAnswer">Phần III: Trả lời ngắn</option>
            </select>

            {/* Status Filter */}
            <div className="grid grid-cols-3 sm:flex items-center gap-1 p-1 rounded-xl bg-slate-900 border border-white/10 text-xs font-semibold">
              <button
                onClick={() => setFilterStatus('all')}
                className={`px-2.5 sm:px-3 py-1.5 rounded-lg transition-all text-center ${
                  filterStatus === 'all' ? 'bg-indigo-600 text-white' : 'text-slate-400 hover:text-white'
                }`}
              >
                Tất cả ({mistakes.length})
              </button>
              <button
                onClick={() => setFilterStatus('unmastered')}
                className={`px-2.5 sm:px-3 py-1.5 rounded-lg transition-all text-center ${
                  filterStatus === 'unmastered' ? 'bg-rose-600 text-white' : 'text-slate-400 hover:text-white'
                }`}
              >
                Chưa thạo ({unmasteredCount})
              </button>
              <button
                onClick={() => setFilterStatus('mastered')}
                className={`px-2.5 sm:px-3 py-1.5 rounded-lg transition-all text-center ${
                  filterStatus === 'mastered' ? 'bg-emerald-600 text-white' : 'text-slate-400 hover:text-white'
                }`}
              >
                Đã thạo ({masteredCount})
              </button>
            </div>
          </div>

          {/* Search Input */}
          <div className="relative w-full lg:max-w-xs">
            <Search className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Tìm theo nội dung, chuyên đề..."
              className="w-full bg-slate-900 border border-white/10 focus:border-indigo-500 rounded-xl pl-9 pr-3 py-2 text-xs text-white outline-none transition-all"
            />
          </div>
        </div>

        {/* Mistakes List */}
        {filteredMistakes.length === 0 ? (
          <div className="glass-panel rounded-3xl p-12 text-center flex flex-col items-center justify-center space-y-4 border border-white/10">
            <div className="w-16 h-16 rounded-full bg-emerald-500/20 border border-emerald-500/30 flex items-center justify-center text-emerald-400">
              <CheckCircle2 className="w-8 h-8" />
            </div>
            <h3 className="text-lg font-bold text-white">Không có câu sai nào trong mục này!</h3>
            <p className="text-xs text-slate-400 max-w-md">
              Tuyệt vời! Em đã khắc phục hết các câu sai hoặc chưa có câu hỏi nào khớp với bộ lọc hiện tại. Hãy tiếp tục làm đề thi thử để củng cố kiến thức.
            </p>
            <Link
              href="/exam"
              className="px-5 py-2.5 rounded-xl font-bold bg-indigo-600 hover:bg-indigo-500 text-white text-xs shadow-md transition-all"
            >
              Vào phòng thi làm đề mới
            </Link>
          </div>
        ) : (
          <div className="space-y-6">
            {filteredMistakes.map((item, idx) => {
              const q = item.question;
              const ans = item.studentAnswer;

              return (
                <div
                  key={item.id}
                  className={`glass-panel rounded-2xl sm:rounded-3xl p-4 sm:p-6 lg:p-8 border transition-all relative overflow-hidden ${
                    item.mastered
                      ? 'border-emerald-500/30 bg-emerald-950/10 opacity-75'
                      : 'border-white/10 hover:border-white/20'
                  }`}
                >
                  {/* Item Header */}
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-4 pb-3 border-b border-white/10">
                    <div className="flex items-center gap-2.5 sm:gap-3 flex-wrap">
                      <span className="flex items-center justify-center w-7 h-7 sm:w-8 sm:h-8 rounded-xl bg-slate-800 text-slate-200 font-bold text-xs border border-white/10 shrink-0">
                        #{idx + 1}
                      </span>
                      <div className="flex items-center gap-1.5 sm:gap-2 flex-wrap">
                        <span className="text-[10px] sm:text-[11px] font-bold px-2 sm:px-2.5 py-0.5 rounded-md bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
                          {q.format === 'SingleChoice' && 'Phần I: Trắc nghiệm'}
                          {q.format === 'TrueFalseGroup' && 'Phần II: Đúng / Sai'}
                          {q.format === 'ShortAnswer' && 'Phần III: Trả lời ngắn'}
                        </span>
                        <span className="text-xs text-slate-300 font-semibold">{q.topic}</span>
                        <span className="text-[10px] text-slate-500">
                          {item.subjectCode === 'GEOGRAPHY' ? 'Địa lý' : 'GDKT&PL'}
                        </span>
                      </div>
                    </div>

                    <div className="flex items-center justify-between sm:justify-end gap-2 w-full sm:w-auto pt-1 sm:pt-0 border-t sm:border-t-0 border-white/5">
                      <button
                        onClick={() => handleToggleMastered(item.id)}
                        className={`flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-bold transition-all border ${
                          item.mastered
                            ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40'
                            : 'bg-white/5 text-slate-400 border-white/10 hover:text-white'
                        }`}
                      >
                        <Check className="w-3.5 h-3.5" />
                        {item.mastered ? 'Đã thành thạo' : 'Đánh dấu đã thạo'}
                      </button>

                      <button
                        onClick={() => handleDelete(item.id)}
                        className="w-8 h-8 rounded-xl bg-white/5 border border-white/10 flex items-center justify-center text-slate-400 hover:text-rose-400 hover:bg-rose-500/10 transition-colors"
                        title="Xóa câu này khỏi sổ"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  </div>

                  {/* Question Content with MathRenderer */}
                  <div className="text-slate-100 text-base font-medium leading-relaxed mb-6">
                    <MathRenderer content={q.content} />
                  </div>

                  {/* Answers Comparison: Student vs Official */}
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-6">
                    {/* Student Selection */}
                    <div className="p-4 rounded-2xl bg-rose-950/30 border border-rose-500/30 space-y-2">
                      <div className="flex items-center gap-1.5 text-xs font-bold text-rose-400 uppercase tracking-wider">
                        <XCircle className="w-4 h-4" />
                        Lựa chọn của em (Chưa đúng)
                      </div>
                      <div className="text-sm font-semibold text-white">
                        {q.format === 'SingleChoice' && (
                          <span>
                            Phương án <strong className="text-rose-400">{ans.selectedOption || 'Bỏ trống'}</strong>
                            {ans.selectedOption && q.answerData.options?.[ans.selectedOption] && (
                              <span className="text-slate-300 block text-xs mt-1">
                                {q.answerData.options[ans.selectedOption]}
                              </span>
                            )}
                          </span>
                        )}
                        {q.format === 'TrueFalseGroup' && (
                          <div className="text-xs space-y-1 font-mono">
                            {Object.entries(ans.subAnswers || {}).map(([subK, subV]) => (
                              <span key={subK} className="mr-3 inline-block">
                                {subK}: <strong className={subV ? 'text-emerald-400' : 'text-rose-400'}>{subV ? 'Đúng' : 'Sai'}</strong>
                              </span>
                            ))}
                          </div>
                        )}
                        {q.format === 'ShortAnswer' && (
                          <span>
                            Kết quả nhập: <strong className="text-rose-400 font-mono">{ans.textAnswer || '(chưa nhập)'}</strong>
                          </span>
                        )}
                      </div>
                    </div>

                    {/* Official Correct Answer */}
                    <div className="p-4 rounded-2xl bg-emerald-950/30 border border-emerald-500/30 space-y-2">
                      <div className="flex items-center gap-1.5 text-xs font-bold text-emerald-400 uppercase tracking-wider">
                        <CheckCircle2 className="w-4 h-4" />
                        Đáp án chuẩn Bộ GD&ĐT 2025
                      </div>
                      <div className="text-sm font-semibold text-white">
                        {q.format === 'SingleChoice' && (
                          <span>
                            Phương án <strong className="text-emerald-400">{q.answerData.correctOption}</strong>
                            {q.answerData.correctOption && q.answerData.options?.[q.answerData.correctOption] && (
                              <span className="text-slate-300 block text-xs mt-1">
                                {q.answerData.options[q.answerData.correctOption]}
                              </span>
                            )}
                          </span>
                        )}
                        {q.format === 'TrueFalseGroup' && (
                          <div className="text-xs space-y-1 font-mono text-slate-300">
                            {Object.entries(q.answerData.subAnswers || {}).map(([subK, subV]) => (
                              <span key={subK} className="mr-3 inline-block">
                                {subK}: <strong className="text-emerald-400">{subV ? 'Đúng' : 'Sai'}</strong>
                              </span>
                            ))}
                          </div>
                        )}
                        {q.format === 'ShortAnswer' && (
                          <span>
                            Đáp án chuẩn: <strong className="text-emerald-400 font-mono">{q.answerData.correctAnswer}</strong>
                            {q.answerData.unit && ` (${q.answerData.unit})`}
                          </span>
                        )}
                      </div>
                    </div>
                  </div>

                  {/* AI Distractor Analysis (Bẫy đề thi) */}
                  <div className="p-4 rounded-2xl bg-amber-500/10 border border-amber-500/20 text-xs sm:text-sm text-slate-300 mb-4">
                    <div className="flex items-center gap-2 text-amber-400 font-bold mb-1.5">
                      <AlertTriangle className="w-4 h-4" />
                      <span>Cái &quot;bẫy&quot; khiến em nhầm lẫn:</span>
                    </div>
                    <p className="leading-relaxed text-slate-300">
                      {item.notes || q.explanation || 'Chú ý các từ khóa loại trừ và bản chất quy luật tự nhiên/pháp luật.'}
                    </p>
                  </div>

                  {/* Card Actions: AI Tutor Button */}
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5 pt-2">
                    <button
                      onClick={() => handleOpenAiTutor(item)}
                      className="w-full sm:w-auto flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl text-xs font-bold bg-cyan-500/20 text-cyan-300 border border-cyan-500/30 hover:bg-cyan-500/30 transition-all shadow-md shadow-cyan-500/10 active:scale-95"
                    >
                      <Sparkles className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
                      <span>Hỏi Gia sư AI phân tích sâu bẫy này</span>
                    </button>

                    <span className="text-[11px] text-slate-500 flex items-center gap-1 self-end sm:self-center">
                      <Clock className="w-3 h-3" />
                      {new Date(item.createdAt).toLocaleDateString('vi-VN')}
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </main>

      {/* AI Tutor Drawer (Context-Aware) */}
      {tutorQuestion && (
        <AiTutorDrawer
          isOpen={isTutorOpen}
          onClose={() => setIsTutorOpen(false)}
          question={tutorQuestion}
          studentAnswer={tutorAnswer}
        />
      )}
    </div>
  );
}
