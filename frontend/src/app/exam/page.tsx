'use client';

import React, { Suspense, useEffect, useState, useMemo } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Navbar } from '@/components/Navbar';
import { getSubjects, getExamSources } from '@/lib/api';
import { ExamSource, Subject, SubjectCode } from '@/types';
import {
  Sparkles,
  GraduationCap,
  Layers,
  Clock,
  Shuffle,
  CheckCircle2,
  Search,
  ArrowRight,
  Brain,
  Loader2
} from 'lucide-react';

const SUBJECT_ICONS: Record<string, string> = {
  GEOGRAPHY: '🌍',
  LAW_ECONOMICS: '⚖️',
  MATHEMATICS: '📐',
  PHYSICS: '⚛️',
  CHEMISTRY: '🧪',
  BIOLOGY: '🧬',
  HISTORY: '📜',
  ENGLISH: '🇬🇧'
};

function ExamSetupContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const initialSubject = (searchParams.get('subject') as SubjectCode) || 'GEOGRAPHY';

  const [subjects, setSubjects] = useState<Subject[]>([]);
  const [selectedSubject, setSelectedSubject] = useState<SubjectCode>(initialSubject);
  const [sources, setSources] = useState<ExamSource[]>([]);
  const [selectedSource, setSelectedSource] = useState<string>('RANDOM');
  const [sourceSearch, setSourceSearch] = useState<string>('');
  const [sourceTypeFilter, setSourceTypeFilter] = useState<'all' | 'so_gddt' | 'truong_chuyen'>('all');
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isStarting, setIsStarting] = useState<boolean>(false);

  useEffect(() => {
    let active = true;
    Promise.all([
      getSubjects().catch(() => [] as Subject[]),
      getExamSources().catch(() => [] as ExamSource[])
    ]).then(([subList, srcList]) => {
      if (!active) return;
      setSubjects(subList);
      setSources(srcList);
      const ready = subList.filter(s => s.isExamReady);
      if (ready.length && !ready.some(s => s.code === selectedSubject)) {
        setSelectedSubject(ready[0].code);
      }
      setIsLoading(false);
    });
    return () => { active = false; };
  }, []);

  const filteredSources = useMemo(() => {
    return sources.filter(src => {
      const matchType = sourceTypeFilter === 'all' || src.sourceType === sourceTypeFilter;
      const term = sourceSearch.trim().toLowerCase();
      const matchSearch = !term || src.name.toLowerCase().includes(term) || (src.province && src.province.toLowerCase().includes(term));
      return matchType && matchSearch;
    });
  }, [sources, sourceTypeFilter, sourceSearch]);

  const activeSubjectData = subjects.find(s => s.code === selectedSubject);

  const handleStartExam = () => {
    setIsStarting(true);
    const params = new URLSearchParams();
    params.set('subject', selectedSubject);
    if (selectedSource && selectedSource !== 'RANDOM') {
      params.set('source', selectedSource);
    }
    router.push(`/exam/exam-bgd-2025?${params.toString()}`);
  };

  return (
    <div className="min-h-screen flex flex-col bg-[#07090e] text-white">
      <Navbar />

      <main className="flex-1 max-w-6xl mx-auto px-4 sm:px-6 lg:px-8 py-8 sm:py-12 space-y-10">
        {/* HEADER HERO */}
        <div className="text-center space-y-3 relative">
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-indigo-500/10 border border-indigo-500/30 text-indigo-300 text-xs font-semibold animate-pulse-glow">
            <Sparkles className="w-3.5 h-3.5 text-cyan-400" />
            <span>Phòng Luyện Thi Chuẩn Cấu Trúc Bộ GD&ĐT 2025</span>
          </div>
          <h1 className="text-3xl sm:text-5xl font-black tracking-tight text-white">
            Cấu Hình{' '}
            <span className="bg-gradient-to-r from-indigo-400 via-cyan-400 to-indigo-300 bg-clip-text text-transparent">
              Phòng Thi Ảo
            </span>
          </h1>
          <p className="text-slate-400 text-sm sm:text-base max-w-2xl mx-auto">
            Tự do chọn môn học và nguồn đề từ các Sở GD&ĐT, Trường chuyên hàng đầu hoặc để hệ thống trộn đề ngẫu nhiên toàn quốc.
          </p>
        </div>

        {isLoading ? (
          <div className="flex flex-col items-center justify-center py-20 space-y-4">
            <Loader2 className="w-9 h-9 text-indigo-400 animate-spin" />
            <p className="text-sm text-slate-400">Đang tải danh mục môn học và nguồn đề thi uy tín...</p>
          </div>
        ) : (
          <div className="space-y-10">
            {/* STEP 1: CHỌN MÔN HỌC */}
            <section className="glass-panel rounded-3xl p-6 sm:p-8 border border-white/10 space-y-5">
              <div className="flex items-center justify-between border-b border-white/10 pb-4">
                <div className="flex items-center gap-3">
                  <span className="flex items-center justify-center w-8 h-8 rounded-xl bg-indigo-600/30 text-indigo-400 border border-indigo-500/30 text-sm font-black">
                    1
                  </span>
                  <div>
                    <h2 className="text-lg font-bold text-white">Chọn Môn Thi</h2>
                    <p className="text-xs text-slate-400">Các môn đã nạp đủ ngân hàng câu hỏi chuẩn 3 phần</p>
                  </div>
                </div>
                {activeSubjectData && (
                  <span className="text-xs font-semibold px-3 py-1 rounded-full bg-emerald-500/10 border border-emerald-500/30 text-emerald-300">
                    {activeSubjectData.name} ({activeSubjectData.activeQuestionsCount || 0} câu)
                  </span>
                )}
              </div>

              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 sm:gap-4">
                {subjects.map(sub => {
                  const isSelected = selectedSubject === sub.code;
                  const isReady = sub.isExamReady;
                  const icon = SUBJECT_ICONS[sub.code] || '📚';
                  return (
                    <button
                      key={sub.code}
                      onClick={() => isReady && setSelectedSubject(sub.code)}
                      disabled={!isReady}
                      className={`relative text-left p-4 rounded-2xl border transition-all flex flex-col justify-between h-28 ${
                        isSelected
                          ? 'bg-indigo-600/20 border-cyan-400 shadow-lg shadow-indigo-600/20 scale-[1.02]'
                          : isReady
                          ? 'bg-slate-900/60 border-white/10 hover:border-white/20 hover:bg-slate-800/60'
                          : 'bg-slate-950/40 border-white/5 opacity-40 cursor-not-allowed'
                      }`}
                    >
                      <div className="flex items-start justify-between">
                        <span className="text-2xl">{icon}</span>
                        {isSelected && (
                          <CheckCircle2 className="w-5 h-5 text-cyan-400 fill-cyan-400/20" />
                        )}
                        {!isReady && (
                          <span className="text-[10px] px-1.5 py-0.5 rounded bg-slate-800 text-slate-400 font-mono">
                            Đang cập nhật
                          </span>
                        )}
                      </div>
                      <div>
                        <p className="font-bold text-sm text-white truncate">{sub.name}</p>
                        <p className="text-[11px] text-slate-400">
                          {isReady ? `${sub.activeQuestionsCount || 0} câu đã duyệt` : 'Chưa mở'}
                        </p>
                      </div>
                    </button>
                  );
                })}
              </div>
            </section>

            {/* STEP 2: CHỌN NGUỒN ĐỀ THI */}
            <section className="glass-panel rounded-3xl p-6 sm:p-8 border border-white/10 space-y-6">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border-b border-white/10 pb-4">
                <div className="flex items-center gap-3">
                  <span className="flex items-center justify-center w-8 h-8 rounded-xl bg-cyan-600/30 text-cyan-400 border border-cyan-500/30 text-sm font-black">
                    2
                  </span>
                  <div>
                    <h2 className="text-lg font-bold text-white">Chọn Nguồn Đề Thi</h2>
                    <p className="text-xs text-slate-400">Tùy chọn đề tổng hợp toàn quốc hoặc luyện sâu theo từng Sở/Trường</p>
                  </div>
                </div>

                {/* Filter chips */}
                <div className="flex items-center gap-1.5 bg-slate-900/80 p-1 rounded-xl border border-white/10 self-start sm:self-auto text-xs">
                  <button
                    onClick={() => setSourceTypeFilter('all')}
                    className={`px-2.5 py-1 rounded-lg font-semibold transition-all ${
                      sourceTypeFilter === 'all'
                        ? 'bg-indigo-600 text-white'
                        : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    Tất cả
                  </button>
                  <button
                    onClick={() => setSourceTypeFilter('so_gddt')}
                    className={`px-2.5 py-1 rounded-lg font-semibold transition-all ${
                      sourceTypeFilter === 'so_gddt'
                        ? 'bg-indigo-600 text-white'
                        : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    🏛️ Sở GD&ĐT
                  </button>
                  <button
                    onClick={() => setSourceTypeFilter('truong_chuyen')}
                    className={`px-2.5 py-1 rounded-lg font-semibold transition-all ${
                      sourceTypeFilter === 'truong_chuyen'
                        ? 'bg-indigo-600 text-white'
                        : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    🎓 Trường Chuyên
                  </button>
                </div>
              </div>

              {/* DEFAULT RANDOM OPTION */}
              <div
                onClick={() => setSelectedSource('RANDOM')}
                className={`cursor-pointer rounded-2xl p-4 sm:p-5 border transition-all flex items-center justify-between gap-4 ${
                  selectedSource === 'RANDOM'
                    ? 'bg-gradient-to-r from-indigo-900/40 via-cyan-900/30 to-indigo-950/50 border-cyan-400 shadow-xl shadow-cyan-500/10 scale-[1.01]'
                    : 'bg-slate-900/60 border-white/10 hover:border-white/25 hover:bg-slate-800/60'
                }`}
              >
                <div className="flex items-center gap-4">
                  <div className="w-12 h-12 rounded-2xl bg-cyan-500/20 border border-cyan-500/40 flex items-center justify-center shrink-0">
                    <Shuffle className="w-6 h-6 text-cyan-300" />
                  </div>
                  <div>
                    <div className="flex items-center gap-2">
                      <h3 className="font-bold text-base text-white">🎲 Ngẫu Nhiên Toàn Quốc</h3>
                      <span className="text-[10px] font-extrabold px-2 py-0.5 rounded-full bg-cyan-400/20 text-cyan-300 border border-cyan-400/30">
                        Khuyên Dùng
                      </span>
                    </div>
                    <p className="text-xs text-slate-300 mt-1 max-w-2xl">
                      Hệ thống tự động trích lọc và phối trộn ngẫu nhiên từ 180+ Sở GD&ĐT và Trường chuyên danh tiếng, ưu tiên rút câu hỏi trọng tâm xuất hiện ở nhiều đề thi.
                    </p>
                  </div>
                </div>
                <div className="shrink-0">
                  <div
                    className={`w-6 h-6 rounded-full border-2 flex items-center justify-center transition-all ${
                      selectedSource === 'RANDOM'
                        ? 'border-cyan-400 bg-cyan-400'
                        : 'border-slate-600'
                    }`}
                  >
                    {selectedSource === 'RANDOM' && <div className="w-2 h-2 rounded-full bg-[#07090e]" />}
                  </div>
                </div>
              </div>

              {/* SEARCH SOURCES */}
              <div className="space-y-3">
                <div className="relative">
                  <Search className="w-4 h-4 text-slate-400 absolute left-3.5 top-1/2 -translate-y-1/2" />
                  <input
                    type="text"
                    value={sourceSearch}
                    onChange={e => setSourceSearch(e.target.value)}
                    placeholder="Tìm theo tên Sở (Hà Nội, Nam Định, Nghệ An...) hoặc Trường chuyên..."
                    className="w-full pl-10 pr-4 py-2.5 rounded-xl bg-slate-900/90 border border-white/10 text-white placeholder-slate-500 text-xs sm:text-sm focus:outline-none focus:border-cyan-400 transition-all"
                  />
                  {sourceSearch && (
                    <button
                      onClick={() => setSourceSearch('')}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-xs text-slate-400 hover:text-white"
                    >
                      Xóa
                    </button>
                  )}
                </div>

                {/* SCROLLABLE SOURCES LIST */}
                <div className="max-h-60 overflow-y-auto pr-1 grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-2.5 custom-scrollbar">
                  {filteredSources.map(src => {
                    const isSelected = selectedSource === src.name;
                    return (
                      <div
                        key={src.id}
                        onClick={() => setSelectedSource(src.name)}
                        className={`cursor-pointer p-3 rounded-xl border text-xs transition-all flex items-center justify-between gap-2 ${
                          isSelected
                            ? 'bg-indigo-600/30 border-cyan-400 text-white shadow-md shadow-indigo-600/20'
                            : 'bg-slate-950/60 border-white/5 hover:border-white/15 hover:bg-slate-900/60 text-slate-300'
                        }`}
                      >
                        <div className="min-w-0">
                          <p className="font-semibold truncate">{src.name}</p>
                          <p className="text-[10px] text-slate-400 truncate">
                            {src.sourceType === 'truong_chuyen' ? '🎓 Trường Chuyên' : '🏛️ Sở GD&ĐT'}{' '}
                            {src.province ? `• ${src.province}` : ''}
                          </p>
                        </div>
                        <div
                          className={`w-4 h-4 rounded-full border shrink-0 flex items-center justify-center ${
                            isSelected ? 'border-cyan-400 bg-cyan-400' : 'border-slate-600'
                          }`}
                        >
                          {isSelected && <div className="w-1.5 h-1.5 rounded-full bg-[#07090e]" />}
                        </div>
                      </div>
                    );
                  })}
                  {filteredSources.length === 0 && (
                    <div className="col-span-full py-6 text-center text-xs text-slate-400">
                      Không tìm thấy nguồn đề phù hợp với từ khóa &ldquo;{sourceSearch}&rdquo;.
                    </div>
                  )}
                </div>
              </div>
            </section>

            {/* STEP 3: TỔNG QUAN BÀI THI & NÚT BẮT ĐẦU */}
            <section className="glass-panel rounded-3xl p-6 sm:p-8 border border-white/10 space-y-6">
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-center">
                <div className="p-4 rounded-2xl bg-slate-900/60 border border-white/5 space-y-1">
                  <Clock className="w-5 h-5 text-indigo-400 mx-auto mb-1" />
                  <p className="text-xl font-black text-white">50 phút</p>
                  <p className="text-[11px] text-slate-400">Thời gian làm bài</p>
                </div>
                <div className="p-4 rounded-2xl bg-slate-900/60 border border-white/5 space-y-1">
                  <Layers className="w-5 h-5 text-cyan-400 mx-auto mb-1" />
                  <p className="text-xl font-black text-white">28 câu</p>
                  <p className="text-[11px] text-slate-400">Chuẩn ma trận Bộ</p>
                </div>
                <div className="p-4 rounded-2xl bg-slate-900/60 border border-white/5 space-y-1">
                  <Brain className="w-5 h-5 text-emerald-400 mx-auto mb-1" />
                  <p className="text-xl font-black text-white">Loại trừ</p>
                  <p className="text-[11px] text-slate-400">Bỏ câu đã làm đúng</p>
                </div>
                <div className="p-4 rounded-2xl bg-slate-900/60 border border-white/5 space-y-1">
                  <Sparkles className="w-5 h-5 text-amber-400 mx-auto mb-1" />
                  <p className="text-xl font-black text-white">AI Tutor</p>
                  <p className="text-[11px] text-slate-400">Giải thích lỗi sai</p>
                </div>
              </div>

              {/* ACTION BUTTON */}
              <div className="flex flex-col sm:flex-row items-center justify-between gap-4 pt-4 border-t border-white/10">
                <div className="text-center sm:text-left">
                  <p className="text-sm font-bold text-white">
                    Sẵn sàng thi môn {activeSubjectData?.name || selectedSubject}
                  </p>
                  <p className="text-xs text-slate-400">
                    Nguồn đề: {selectedSource === 'RANDOM' ? '🎲 Ngẫu nhiên toàn quốc' : `🏛️ ${selectedSource}`}
                  </p>
                </div>

                <button
                  onClick={handleStartExam}
                  disabled={isStarting}
                  className="w-full sm:w-auto flex items-center justify-center gap-3 px-8 py-4 rounded-2xl font-bold text-white bg-gradient-to-r from-indigo-600 via-indigo-500 to-cyan-500 hover:opacity-95 shadow-xl shadow-indigo-600/30 transition-all hover:scale-105 active:scale-95 text-sm"
                >
                  {isStarting ? (
                    <>
                      <Loader2 className="w-5 h-5 animate-spin" />
                      <span>Đang vào phòng thi...</span>
                    </>
                  ) : (
                    <>
                      <GraduationCap className="w-5 h-5" />
                      <span>BẮT ĐẦU LÀM BÀI THI</span>
                      <ArrowRight className="w-4 h-4" />
                    </>
                  )}
                </button>
              </div>
            </section>
          </div>
        )}
      </main>
    </div>
  );
}

export default function ExamSetupPage() {
  return (
    <Suspense
      fallback={
        <div className="min-h-screen flex items-center justify-center bg-[#07090e] text-white">
          <Loader2 className="w-8 h-8 text-indigo-400 animate-spin" />
        </div>
      }
    >
      <ExamSetupContent />
    </Suspense>
  );
}
