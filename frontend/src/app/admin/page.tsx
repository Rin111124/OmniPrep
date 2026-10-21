'use client';

import { FormEvent, useCallback, useEffect, useState } from 'react';
import Image from 'next/image';
import { useRouter } from 'next/navigation';
import { Navbar } from '@/components/Navbar';
import { RoleGuard } from '@/components/RoleGuard';
import { useAuth } from '@/context/AuthContext';
import {
  getModerationStats,
  getPendingQuestions,
  ModerationStats,
  PendingQuestion,
  reviewQuestion,
  submitExamForIngestion,
  updatePendingQuestion,
  verifyAllQuestions,
  getIngestedPapers,
  getIngestedPaperDetails,
  IngestedPaper,
  scanExamSourcesBySubject
} from '@/lib/api';
import { IngestionStatusResponse, SubjectCode, SubjectSourceScanResult } from '@/types';
import {
  Clock,
  CheckCircle2,
  XCircle,
  Edit3,
  Save,
  X,
  FileText,
  RefreshCw,
  CheckCheck,
  Sparkles,
  Eye,
  Search,
  Layers,
  BookOpen,
  AlertTriangle,
  Zap,
  Compass
} from 'lucide-react';

const supportedSubjects: Array<{ code: SubjectCode; label: string }> = [
  { code: 'GEOGRAPHY', label: 'Địa lý' },
  { code: 'LAW_ECONOMICS', label: 'Giáo dục Kinh tế & Pháp luật' },
  { code: 'MATHEMATICS', label: 'Toán học' },
  { code: 'PHYSICS', label: 'Vật lý' },
  { code: 'CHEMISTRY', label: 'Hóa học' },
  { code: 'BIOLOGY', label: 'Sinh học' },
  { code: 'HISTORY', label: 'Lịch sử' },
  { code: 'ENGLISH', label: 'Tiếng Anh' }
];

export default function AdminPage() {
  const router = useRouter();
  const { role, isLoading: authLoading } = useAuth();
  const [activeTab, setActiveTab] = useState<'questions' | 'provenance'>('questions');
  const [subjectCode, setSubjectCode] = useState<SubjectCode>('GEOGRAPHY');
  const [sourceName, setSourceName] = useState('');
  const [examYear, setExamYear] = useState('');
  const [rawContent, setRawContent] = useState('');
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [result, setResult] = useState<IngestionStatusResponse | null>(null);
  const [pendingQuestions, setPendingQuestions] = useState<PendingQuestion[]>([]);
  const [hasMoreQuestions, setHasMoreQuestions] = useState(false);
  const [reviewNotes, setReviewNotes] = useState<Record<string, string>>({});
  const [reviewError, setReviewError] = useState('');
  const [isLoadingQuestions, setIsLoadingQuestions] = useState(true);
  const [reviewingId, setReviewingId] = useState<string | null>(null);
  const [isVerifyingAll, setIsVerifyingAll] = useState(false);
  const [verifyAllSuccess, setVerifyAllSuccess] = useState('');

  // Source Provenance / Ingested papers state
  const [ingestedPapers, setIngestedPapers] = useState<IngestedPaper[]>([]);
  const [selectedPaperDetails, setSelectedPaperDetails] = useState<IngestedPaper | null>(null);
  const [isLoadingPapers, setIsLoadingPapers] = useState(false);
  const [paperSearch, setPaperSearch] = useState('');
  const [inspectingPaperId, setInspectingPaperId] = useState<string | null>(null);

  // Moderation stats
  const [stats, setStats] = useState<ModerationStats | null>(null);

  // Question editing modal state
  const [editingQuestion, setEditingQuestion] = useState<PendingQuestion | null>(null);
  const [editForm, setEditForm] = useState<{
    content: string;
    attachedTable: string;
    topic: string;
    difficulty: string;
    explanation: string;
  }>({ content: '', attachedTable: '', topic: '', difficulty: '', explanation: '' });
  const [isSavingEdit, setIsSavingEdit] = useState(false);
  const [editError, setEditError] = useState('');

  // Source auto-scan & detection state
  const [isScanningSource, setIsScanningSource] = useState(false);
  const [scannedSourceResult, setScannedSourceResult] = useState<SubjectSourceScanResult | null>(null);
  const [isSourceModalOpen, setIsSourceModalOpen] = useState(false);
  const [sourceNotice, setSourceNotice] = useState<string | null>(null);

  const handleScanSubjectSources = async (autoSelect = false) => {
    setIsScanningSource(true);
    setSourceNotice(null);
    try {
      const res = await scanExamSourcesBySubject(subjectCode);
      setScannedSourceResult(res);
      if (autoSelect || !isSourceModalOpen) {
        if (res.recommendedSource) {
          setSourceName(res.recommendedSource);
          const yearMatch = res.recommendedSource.match(/(202\d)/);
          if (yearMatch && !examYear) setExamYear(yearMatch[1]);
          const top = res.sources.find(s => s.name === res.recommendedSource);
          setSourceNotice(`Đã tự động chọn nguồn tối ưu: "${res.recommendedSource}" (${top?.totalQuestions ?? 0} câu)`);
        } else if (res.sources.length > 0) {
          setSourceName(res.sources[0].name);
          const yearMatch = res.sources[0].name.match(/(202\d)/);
          if (yearMatch && !examYear) setExamYear(yearMatch[1]);
          setSourceNotice(`Đã chọn nguồn: "${res.sources[0].name}" (${res.sources[0].totalQuestions} câu)`);
        } else {
          setSourceNotice(`Môn ${supportedSubjects.find(s => s.code === subjectCode)?.label} chưa có nguồn đề thi trên hệ thống.`);
          setIsSourceModalOpen(true);
        }
      }
    } catch (cause) {
      setSourceNotice(cause instanceof Error ? cause.message : 'Không thể quét nguồn đề theo môn');
    } finally {
      setIsScanningSource(false);
    }
  };

  const handleAutoDetectFromContent = () => {
    const textToScan = `${selectedFile?.name || ''}\n${rawContent || ''}`;
    if (!textToScan.trim()) {
      setSourceNotice('Vui lòng chọn file đề thi hoặc dán nội dung văn bản đề trước khi quét.');
      return;
    }

    let detectedSource = '';
    let detectedYear = '';
    let detectedSubject: SubjectCode | null = null;

    // Detect Year
    const yearMatch = textToScan.match(/(?:năm\s+học|năm|khóa)\s*(?:20\d\d[-–])?\s*(202\d)/i) || textToScan.match(/\b(202\d)\b/);
    if (yearMatch) {
      detectedYear = yearMatch[1];
    }

    // Detect Department of Education (Sở GD&ĐT)
    const soMatch = textToScan.match(/SỞ\s+(?:GIÁO\s+DỤC\s+(?:VÀ|&)\s+ĐÀO\s+TẠO|GD\s*&?\s*ĐT)\s+([A-ZÀ-Ỹ\s]+?)(?:\s+TRƯỜNG|\s+ĐỀ|\s+KỲ|\s*\n|$)/i);
    if (soMatch) {
      const prov = soMatch[1].trim().replace(/[-–_]/g, ' ');
      detectedSource = `Sở GD&ĐT ${prov.charAt(0).toUpperCase() + prov.slice(1).toLowerCase()}`;
    }

    // Detect High School (Trường THPT)
    const thptMatch = textToScan.match(/TRƯỜNG\s+THPT\s+(?:CHUYÊN\s+)?([A-ZÀ-Ỹ\s]+?)(?:\s+ĐỀ|\s+KỲ|\s*\n|$)/i);
    if (thptMatch && !detectedSource) {
      const school = thptMatch[1].trim();
      detectedSource = `THPT ${school.charAt(0).toUpperCase() + school.slice(1).toLowerCase()}`;
    }

    // Detect Ministry of Education (Bộ GD&ĐT)
    if (/BỘ\s+GIÁO\s+DỤC\s+(?:VÀ|&)\s+ĐÀO\s+TẠO/i.test(textToScan)) {
      detectedSource = 'Bộ Giáo Dục & Đào Tạo';
    }

    // Detect Subject
    if (/ĐỊA\s+L[ÍÝ]/i.test(textToScan)) detectedSubject = 'GEOGRAPHY';
    else if (/LỊCH\s+SỬ/i.test(textToScan)) detectedSubject = 'HISTORY';
    else if (/TOÁN/i.test(textToScan)) detectedSubject = 'MATHEMATICS';
    else if (/VẬT\s+L[ÍÝ]/i.test(textToScan)) detectedSubject = 'PHYSICS';
    else if (/HÓA\s+HỌC/i.test(textToScan)) detectedSubject = 'CHEMISTRY';
    else if (/SINH\s+HỌC/i.test(textToScan)) detectedSubject = 'BIOLOGY';
    else if (/TIẾNG\s+ANH/i.test(textToScan)) detectedSubject = 'ENGLISH';
    else if (/KINH\s+TẾ|PHÁP\s+LUẬT/i.test(textToScan)) detectedSubject = 'LAW_ECONOMICS';

    const msg: string[] = [];
    if (detectedSource) {
      const fullSourceName = detectedYear ? `${detectedSource} ${detectedYear}` : detectedSource;
      setSourceName(fullSourceName);
      msg.push(`Nguồn: "${fullSourceName}"`);
    }
    if (detectedYear) {
      setExamYear(detectedYear);
      msg.push(`Năm: ${detectedYear}`);
    }
    if (detectedSubject && detectedSubject !== subjectCode) {
      setSubjectCode(detectedSubject);
      const subLabel = supportedSubjects.find(s => s.code === detectedSubject)?.label;
      msg.push(`Môn: ${subLabel}`);
    }

    if (msg.length > 0) {
      setSourceNotice(`🔍 Đã tự động nhận diện từ văn bản: ${msg.join(' • ')}`);
    } else {
      setSourceNotice('Không tìm thấy thông tin Sở/Trường trong văn bản. Bạn có thể nhấn "⚡ Quét nguồn theo môn" để chọn từ danh mục có sẵn.');
    }
  };

  const handleScanExtractAndSelectExam = async () => {
    // 1. Check if user has uploaded a file or pasted text
    const hasContent = Boolean(selectedFile) || rawContent.trim().length > 0;
    if (!hasContent) {
      setSourceNotice('⚠️ Vui lòng chọn file đề thi (.pdf/.txt) hoặc dán nội dung văn bản đề trước khi tự động bóc tách.');
      return;
    }

    // 2. Auto-detect source & year & subject from content if not already filled
    let currentSource = sourceName.trim();
    let currentYear = examYear ? Number(examYear) : undefined;
    let currentSubject = subjectCode;

    const textToScan = `${selectedFile?.name || ''}\n${rawContent || ''}`;
    // If sourceName is empty, run detection
    if (!currentSource) {
      const yearMatch = textToScan.match(/(?:năm\s+học|năm|khóa)\s*(?:20\d\d[-–])?\s*(202\d)/i) || textToScan.match(/\b(202\d)\b/);
      if (yearMatch) {
        currentYear = Number(yearMatch[1]);
        setExamYear(yearMatch[1]);
      }
      const soMatch = textToScan.match(/SỞ\s+(?:GIÁO\s+DỤC\s+(?:VÀ|&)\s+ĐÀO\s+TẠO|GD\s*&?\s*ĐT)\s+([A-ZÀ-Ỹ\s]+?)(?:\s+TRƯỜNG|\s+ĐỀ|\s+KỲ|\s*\n|$)/i);
      if (soMatch) {
        const prov = soMatch[1].trim().replace(/[-–_]/g, ' ');
        currentSource = `Sở GD&ĐT ${prov.charAt(0).toUpperCase() + prov.slice(1).toLowerCase()}`;
      } else if (/BỘ\s+GIÁO\s+DỤC\s+(?:VÀ|&)\s+ĐÀO\s+TẠO/i.test(textToScan)) {
        currentSource = 'Bộ Giáo Dục & Đào Tạo';
      } else {
        const thptMatch = textToScan.match(/TRƯỜNG\s+THPT\s+(?:CHUYÊN\s+)?([A-ZÀ-Ỹ\s]+?)(?:\s+ĐỀ|\s+KỲ|\s*\n|$)/i);
        if (thptMatch) {
          const school = thptMatch[1].trim();
          currentSource = `THPT ${school.charAt(0).toUpperCase() + school.slice(1).toLowerCase()}`;
        } else {
          currentSource = selectedFile ? selectedFile.name.replace(/\.[^/.]+$/, '') : `Đề thi môn ${supportedSubjects.find(s => s.code === subjectCode)?.label}`;
        }
      }
      if (currentYear) currentSource = `${currentSource} ${currentYear}`;
      setSourceName(currentSource);
    }

    // Detect subject if not set
    if (/ĐỊA\s+L[ÍÝ]/i.test(textToScan)) currentSubject = 'GEOGRAPHY';
    else if (/LỊCH\s+SỬ/i.test(textToScan)) currentSubject = 'HISTORY';
    else if (/TOÁN/i.test(textToScan)) currentSubject = 'MATHEMATICS';
    else if (/VẬT\s+L[ÍÝ]/i.test(textToScan)) currentSubject = 'PHYSICS';
    else if (/HÓA\s+HỌC/i.test(textToScan)) currentSubject = 'CHEMISTRY';
    else if (/SINH\s+HỌC/i.test(textToScan)) currentSubject = 'BIOLOGY';
    else if (/TIẾNG\s+ANH/i.test(textToScan)) currentSubject = 'ENGLISH';
    else if (/KINH\s+TẾ|PHÁP\s+LUẬT/i.test(textToScan)) currentSubject = 'LAW_ECONOMICS';
    setSubjectCode(currentSubject);

    // 3. Trigger Ingestion extraction directly
    setIsSubmitting(true);
    setError('');
    setResult(null);
    setSourceNotice(`⚡ Đang quét nguồn "${currentSource}" và tự động bóc tách câu hỏi qua AI...`);

    try {
      const response = await submitExamForIngestion({
        subjectCode: currentSubject,
        sourceName: currentSource,
        examYear: currentYear,
        rawContent: rawContent.trim(),
        file: selectedFile || undefined
      });
      setResult(response);
      setSourceNotice(`✅ Bóc tách hoàn tất: ${response.totalExtracted || 0} câu hỏi từ nguồn "${currentSource}". Đã sẵn sàng để chọn đề thi làm bài ngay!`);
      void refreshPending();
      void loadIngestedPapers();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Không thể bóc tách đề thi.');
      setSourceNotice(null);
    } finally {
      setIsSubmitting(false);
    }
  };

  const refreshStats = useCallback(async () => {
    try {
      const data = await getModerationStats();
      setStats(data);
    } catch {
      // Ignored
    }
  }, []);

  const refreshPending = useCallback(async (offset = 0) => {
    setIsLoadingQuestions(true);
    setReviewError('');
    try {
      const questions = await getPendingQuestions(offset);
      setPendingQuestions(current => offset === 0 ? questions : [...current, ...questions]);
      setHasMoreQuestions(questions.length === 100);
      void refreshStats();
    } catch (cause) {
      setReviewError(cause instanceof Error ? cause.message : 'Không thể tải câu hỏi chờ duyệt.');
    } finally {
      setIsLoadingQuestions(false);
    }
  }, [refreshStats]);

  useEffect(() => {
    if (authLoading || (role !== 'ADMIN' && role !== 'TEACHER')) return;
    let active = true;
    const frame = requestAnimationFrame(() => {
      if (!active) return;
      void getPendingQuestions(0).then(questions => {
        if (active) {
          setPendingQuestions(questions);
          setHasMoreQuestions(questions.length === 100);
        }
      }).catch(cause => {
        if (active) setReviewError(cause instanceof Error ? cause.message : 'Không thể tải câu hỏi chờ duyệt.');
      }).finally(() => {
        if (active) setIsLoadingQuestions(false);
      });
      void refreshStats();
    });

    return () => {
      active = false;
      cancelAnimationFrame(frame);
    };
  }, [authLoading, refreshStats, role]);

  async function handleReview(questionId: string, action: 'verify' | 'reject') {
    const note = reviewNotes[questionId]?.trim() || '';
    if (action === 'reject' && !note) {
      setReviewError('Nhập lý do loại câu hỏi trước khi từ chối.');
      return;
    }
    setReviewingId(questionId);
    setReviewError('');
    try {
      await reviewQuestion(questionId, action, note);
      setPendingQuestions(current => current.filter(question => question.id !== questionId));
      setReviewNotes(current => {
        const next = { ...current };
        delete next[questionId];
        return next;
      });
      void refreshStats();
    } catch (cause) {
      setReviewError(cause instanceof Error ? cause.message : 'Không thể lưu quyết định duyệt.');
    } finally {
      setReviewingId(null);
    }
  }

  function handleOpenEdit(question: PendingQuestion) {
    setEditingQuestion(question);
    setEditForm({
      content: question.content,
      attachedTable: question.attachedTable || '',
      topic: question.topic,
      difficulty: question.difficulty,
      explanation: question.explanation || ''
    });
    setEditError('');
  }

  async function handleSaveEdit() {
    if (!editingQuestion) return;
    setIsSavingEdit(true);
    setEditError('');
    try {
      const updated = await updatePendingQuestion(editingQuestion.id, {
        content: editForm.content,
        attachedTable: editForm.attachedTable,
        topic: editForm.topic,
        difficulty: editForm.difficulty,
        explanation: editForm.explanation
      });

      setPendingQuestions(current =>
        current.map(q => q.id === updated.id ? { ...q, ...updated } : q)
      );
      setEditingQuestion(null);
    } catch (cause) {
      setEditError(cause instanceof Error ? cause.message : 'Không thể lưu câu hỏi đã sửa.');
    } finally {
      setIsSavingEdit(false);
    }
  }

  async function handleVerifyAll() {
    if (pendingQuestions.length === 0) return;
    const confirmMessage = `Bạn có chắc chắn muốn duyệt và xuất bản TOÀN BỘ ${pendingQuestions.length} câu hỏi đang chờ vào ngân hàng đề thi không?`;
    if (!window.confirm(confirmMessage)) return;

    setIsVerifyingAll(true);
    setReviewError('');
    setVerifyAllSuccess('');
    try {
      const res = await verifyAllQuestions();
      setVerifyAllSuccess(res.message || `Đã duyệt thành công ${res.updatedCount} câu hỏi!`);
      setPendingQuestions([]);
      void refreshStats();
    } catch (cause) {
      setReviewError(cause instanceof Error ? cause.message : 'Không thể duyệt toàn bộ câu hỏi.');
    } finally {
      setIsVerifyingAll(false);
    }
  }

  const loadIngestedPapers = useCallback(async () => {
    setIsLoadingPapers(true);
    try {
      const papers = await getIngestedPapers();
      setIngestedPapers(papers);
    } catch {
      // Ignored
    } finally {
      setIsLoadingPapers(false);
    }
  }, []);

  async function handleInspectPaper(paperId: string) {
    setInspectingPaperId(paperId);
    try {
      const details = await getIngestedPaperDetails(paperId);
      setSelectedPaperDetails(details);
    } catch (cause) {
      alert(cause instanceof Error ? cause.message : 'Không thể tải chi tiết văn bản đề gốc.');
    } finally {
      setInspectingPaperId(null);
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isSubmitting) return;
    setError('');
    setResult(null);
    setIsSubmitting(true);
    try {
      const response = await submitExamForIngestion({
        subjectCode,
        sourceName: sourceName.trim(),
        examYear: examYear ? Number(examYear) : undefined,
        rawContent: rawContent.trim(),
        file: selectedFile || undefined
      });
      setResult(response);
      setRawContent('');
      setSelectedFile(null);
      void refreshPending();
      void loadIngestedPapers();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Không thể gửi tài liệu vào hàng đợi.');
    } finally {
      setIsSubmitting(false);
    }
  }

  async function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) {
      setSelectedFile(null);
      return;
    }
    const isTxt = file.name.toLowerCase().endsWith('.txt');
    const isPdf = file.name.toLowerCase().endsWith('.pdf');
    if (!isTxt && !isPdf) {
      setError('Hệ thống hỗ trợ file .txt hoặc .pdf (tối đa 10 MB).');
      event.target.value = '';
      setSelectedFile(null);
      return;
    }
    if (file.size > 10 * 1024 * 1024) {
      setError('File vượt quá giới hạn 10 MB.');
      event.target.value = '';
      setSelectedFile(null);
      return;
    }
    setError('');
    setSelectedFile(file);
    if (!sourceName.trim()) {
      setSourceName(file.name.replace(/\.[^/.]+$/, ''));
    }
    if (isTxt) {
      const text = await file.text();
      setRawContent(text);
    } else {
      setRawContent(`[Tập tin PDF đính kèm: ${file.name} - ${(file.size / 1024).toFixed(1)} KB. Máy chủ sẽ bóc tách văn bản qua PdfPig Engine]`);
    }
  }

  return (
    <div className="min-h-screen bg-[#07090e] text-white">
      <Navbar />
      <RoleGuard allowedRoles={['ADMIN', 'TEACHER']}>
        <main className="mx-auto max-w-5xl space-y-8 px-4 py-10 sm:px-6 lg:px-8">
          <header className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-white/10 pb-6">
            <div className="space-y-1">
              <h1 className="text-3xl font-black">Quản trị & Kiểm duyệt Ngân hàng đề</h1>
              <p className="text-sm text-slate-400">
                {role === 'ADMIN'
                  ? 'Nạp đề thi dạng văn bản vào hàng đợi và kiểm duyệt nội dung câu hỏi.'
                  : 'Xem xét và phê duyệt hoặc từ chối câu hỏi trước khi đưa vào ngân hàng đề.'}
              </p>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-xs text-slate-400">Quyền hạn:</span>
              <span className={`text-xs font-bold uppercase tracking-wider px-3 py-1 rounded-full border ${
                role === 'ADMIN'
                  ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
                  : 'bg-blue-500/20 text-blue-300 border-blue-500/30'
              }`}>
                {role === 'ADMIN' ? '🛡️ Toàn quyền Admin' : '👨‍🏫 Giáo viên Kiểm duyệt'}
              </span>
            </div>
          </header>

          {/* Moderation Metrics Bar */}
          {stats && (
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div className="rounded-2xl border border-amber-500/30 bg-amber-950/20 p-4">
                <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-amber-400">
                  <Clock className="w-4 h-4" />
                  <span>Đang chờ duyệt</span>
                </div>
                <div className="mt-2 text-2xl font-black font-mono text-white">{stats.totalPending}</div>
                <p className="text-xs text-slate-400 mt-0.5">Câu hỏi cần giáo viên hiệu đính</p>
              </div>

              <div className="rounded-2xl border border-emerald-500/30 bg-emerald-950/20 p-4">
                <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-emerald-400">
                  <CheckCircle2 className="w-4 h-4" />
                  <span>Đã duyệt & xuất bản</span>
                </div>
                <div className="mt-2 text-2xl font-black font-mono text-white">{stats.totalVerified}</div>
                <p className="text-xs text-slate-400 mt-0.5">Sẵn sàng tạo đề thi thử</p>
              </div>

              <div className="rounded-2xl border border-rose-500/30 bg-rose-950/20 p-4">
                <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-rose-400">
                  <XCircle className="w-4 h-4" />
                  <span>Đã từ chối / loại</span>
                </div>
                <div className="mt-2 text-2xl font-black font-mono text-white">{stats.totalRejected}</div>
                <p className="text-xs text-slate-400 mt-0.5">Lưu lịch sử kèm lý do loại</p>
              </div>
            </div>
          )}

          {role === 'ADMIN' && (
            <form onSubmit={handleSubmit} className="space-y-4 rounded-3xl border border-white/10 bg-slate-950/80 p-6 shadow-xl">
              <div className="flex items-center gap-2 border-b border-white/10 pb-3">
                <FileText className="w-5 h-5 text-indigo-400" />
                <h2 className="text-lg font-bold">Nạp đề thi mới vào hàng đợi bóc tách</h2>
              </div>
              {/* AUTO-SCAN & SELECT BANNER */}
              <div className="space-y-3">
                <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3 p-4 rounded-2xl bg-gradient-to-r from-indigo-950/70 via-purple-950/50 to-cyan-950/60 border border-indigo-500/40 shadow-lg">
                  <div className="flex items-center gap-3">
                    <div className="flex items-center justify-center w-10 h-10 rounded-xl bg-indigo-600/30 border border-indigo-500/40 text-yellow-300 shrink-0">
                      <Zap className="w-5 h-5 animate-pulse" />
                    </div>
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-bold text-white uppercase tracking-wider">
                          Tự động quét & chọn nguồn đề
                        </span>
                        <span className="text-[10px] px-2 py-0.5 rounded-full bg-cyan-500/20 text-cyan-300 border border-cyan-500/30 font-semibold">
                          Tính năng mới
                        </span>
                      </div>
                      <p className="text-xs text-slate-300">
                        Quét CSDL ngân hàng câu hỏi theo môn học hoặc quét tiêu đề văn bản đề thi để tự điền nguồn.
                      </p>
                    </div>
                  </div>

                  <div className="flex items-center gap-2 flex-wrap sm:flex-nowrap shrink-0">
                    {/* BUTTON 1: QUÉT NGUỒN THEO MÔN */}
                    <button
                      type="button"
                      id="btn-admin-scan-source-by-subject"
                      onClick={() => void handleScanSubjectSources(true)}
                      disabled={isScanningSource}
                      className="flex-1 sm:flex-initial flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl bg-gradient-to-r from-indigo-600 via-indigo-500 to-cyan-500 hover:from-indigo-500 hover:to-cyan-400 text-white font-black text-xs shadow-lg shadow-indigo-950/50 transition-all hover:scale-[1.02] active:scale-[0.98] disabled:opacity-50"
                      title="Quét ngân hàng đề thi theo môn học và chọn nguồn tối ưu"
                    >
                      <Zap className={`w-3.5 h-3.5 text-yellow-300 ${isScanningSource ? 'animate-spin' : ''}`} />
                      <span>{isScanningSource ? 'Đang quét...' : '⚡ Quét nguồn theo môn'}</span>
                    </button>

                    {/* BUTTON 2: BROWSE SCANNED SOURCES */}
                    <button
                      type="button"
                      id="btn-admin-browse-sources"
                      onClick={async () => {
                        if (!scannedSourceResult) {
                          await handleScanSubjectSources(false);
                        }
                        setIsSourceModalOpen(true);
                      }}
                      disabled={isScanningSource}
                      className="flex items-center justify-center gap-1.5 px-3 py-2.5 rounded-xl bg-slate-900/90 hover:bg-slate-800 border border-white/10 text-slate-200 hover:text-white text-xs font-semibold transition-all"
                      title="Xem toàn bộ danh sách nguồn đề theo môn"
                    >
                      <Layers className="w-3.5 h-3.5 text-cyan-400" />
                      <span>DS Nguồn ({scannedSourceResult?.sources.length ?? '...'})</span>
                    </button>

                    {/* BUTTON 3: TỰ ĐỘNG NHẬN DIỆN TỪ VĂN BẢN / FILE */}
                    <button
                      type="button"
                      id="btn-admin-detect-source"
                      onClick={handleAutoDetectFromContent}
                      className="flex items-center justify-center gap-1.5 px-3 py-2.5 rounded-xl bg-slate-900/90 hover:bg-slate-800 border border-white/10 text-slate-200 hover:text-white text-xs font-semibold transition-all"
                      title="Quét tiêu đề văn bản hoặc tên file để tự trích xuất Sở GD, Trường, Năm"
                    >
                      <Search className="w-3.5 h-3.5 text-emerald-400" />
                      <span>🔍 Quét từ văn bản đề</span>
                    </button>

                    {/* BUTTON 4: QUÉT NGUỒN VÀ TỰ BÓC TÁCH LUÔN */}
                    <button
                      type="button"
                      id="btn-admin-scan-and-extract"
                      onClick={() => void handleScanExtractAndSelectExam()}
                      disabled={isSubmitting || isScanningSource}
                      className="flex items-center justify-center gap-1.5 px-4 py-2.5 rounded-xl bg-gradient-to-r from-emerald-600 via-teal-600 to-cyan-600 hover:from-emerald-500 hover:to-cyan-500 text-white font-black text-xs shadow-lg shadow-emerald-950/60 transition-all hover:scale-[1.02] active:scale-[0.98] disabled:opacity-50"
                      title="Tự động quét nguồn và kích hoạt bóc tách AI câu hỏi ngay lập tức"
                    >
                      <Sparkles className={`w-3.5 h-3.5 text-yellow-300 ${isSubmitting ? 'animate-spin' : ''}`} />
                      <span>{isSubmitting ? 'Đang bóc tách đề...' : '⚡ Quét nguồn & Bóc tách luôn'}</span>
                    </button>
                  </div>
                </div>

                {sourceNotice && (
                  <div className="flex items-center justify-between p-3 rounded-xl bg-cyan-950/50 border border-cyan-500/30 text-xs text-cyan-200 shadow-md">
                    <span className="flex items-center gap-2">
                      <CheckCircle2 className="w-4 h-4 text-cyan-400 shrink-0" />
                      <span>{sourceNotice}</span>
                    </span>
                    <button
                      type="button"
                      onClick={() => setSourceNotice(null)}
                      className="text-cyan-400 hover:text-white ml-2 p-1 rounded hover:bg-cyan-900/40"
                    >
                      <X className="w-3.5 h-3.5" />
                    </button>
                  </div>
                )}
              </div>

              <div className="grid gap-4 sm:grid-cols-3">
                <label className="text-sm text-slate-300">
                  Môn học
                  <select
                    value={subjectCode}
                    onChange={event => {
                      setSubjectCode(event.target.value as SubjectCode);
                      setScannedSourceResult(null);
                    }}
                    className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2.5 text-white focus:outline-none focus:border-indigo-500"
                  >
                    {supportedSubjects.map(subject => <option key={subject.code} value={subject.code}>{subject.label}</option>)}
                  </select>
                </label>
                <div className="space-y-1">
                  <label className="flex items-center justify-between text-sm text-slate-300">
                    <span>Nguồn đề</span>
                    <span className="text-[11px] text-cyan-400 font-normal">Sở GD&ĐT / THPT Chuyên</span>
                  </label>
                  <input
                    required
                    maxLength={200}
                    placeholder="VD: Sở GD&ĐT Hà Nội 2025"
                    value={sourceName}
                    onChange={event => setSourceName(event.target.value)}
                    className="w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2.5 text-white placeholder:text-slate-500 focus:outline-none focus:border-indigo-500"
                  />
                </div>
                <label className="text-sm text-slate-300">
                  Năm (không bắt buộc)
                  <input
                    type="number"
                    min="2000"
                    max="2100"
                    placeholder="2025"
                    value={examYear}
                    onChange={event => setExamYear(event.target.value)}
                    className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2.5 text-white placeholder:text-slate-500 focus:outline-none focus:border-indigo-500"
                  />
                </label>
              </div>

              {/* QUICK SUGGESTION PILLS */}
              <div className="flex items-center gap-1.5 flex-wrap text-xs text-slate-400 pt-1">
                <span className="text-[11px] text-slate-400 font-semibold mr-1">Gợi ý nhanh:</span>
                {[
                  'Bộ GD&ĐT 2025',
                  'Sở GD&ĐT Hà Nội 2025',
                  'Sở GD&ĐT TP.HCM 2025',
                  'Sở GD&ĐT Bắc Ninh 2025',
                  'Sở GD&ĐT Nghệ An 2025',
                  'THPT Chuyên Hà Nội - Amsterdam 2025',
                  'THPT Chuyên Lam Sơn 2025'
                ].map(quick => (
                  <button
                    key={quick}
                    type="button"
                    onClick={() => {
                      setSourceName(quick);
                      const m = quick.match(/(202\d)/);
                      if (m) setExamYear(m[1]);
                      setSourceNotice(`Đã chọn gợi ý nhanh: "${quick}"`);
                    }}
                    className={`px-2.5 py-1 rounded-lg border transition-all ${
                      sourceName === quick
                        ? 'bg-indigo-600/30 border-cyan-400 text-cyan-300 font-bold shadow-sm'
                        : 'bg-slate-900/60 border-white/5 hover:border-white/20 hover:text-white'
                    }`}
                  >
                    + {quick}
                  </button>
                ))}
              </div>

              {/* MODAL DANH SÁCH NGUỒN THEO MÔN */}
              {isSourceModalOpen && (
                <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm animate-fadeIn">
                  <div className="bg-slate-900 border border-white/10 rounded-3xl max-w-2xl w-full p-6 space-y-4 max-h-[85vh] flex flex-col shadow-2xl">
                    <div className="flex items-center justify-between border-b border-white/10 pb-3">
                      <div className="flex items-center gap-2.5">
                        <div className="w-8 h-8 rounded-xl bg-indigo-600/30 flex items-center justify-center text-cyan-400 border border-indigo-500/30">
                          <Compass className="w-4 h-4" />
                        </div>
                        <div>
                          <h3 className="text-base font-bold text-white">
                            Danh Sách Nguồn Đề Môn {supportedSubjects.find(s => s.code === subjectCode)?.label}
                          </h3>
                          <p className="text-xs text-slate-400">
                            Nhấn vào 1 nguồn để tự động điền tên đề và năm thi
                          </p>
                        </div>
                      </div>
                      <button
                        type="button"
                        onClick={() => setIsSourceModalOpen(false)}
                        className="p-1 rounded-lg hover:bg-white/10 text-slate-400 hover:text-white"
                      >
                        <X className="w-5 h-5" />
                      </button>
                    </div>

                    <div className="overflow-y-auto space-y-2 pr-1 flex-1">
                      {isScanningSource ? (
                        <div className="py-8 text-center text-slate-400 text-sm">
                          <RefreshCw className="w-6 h-6 animate-spin mx-auto mb-2 text-indigo-400" />
                          Đang quét ngân hàng câu hỏi...
                        </div>
                      ) : scannedSourceResult && scannedSourceResult.sources.length > 0 ? (
                        scannedSourceResult.sources.map(src => {
                          const isSelected = sourceName === src.name;
                          return (
                            <div
                              key={src.name}
                              className={`p-3 rounded-2xl border transition-all flex items-center justify-between gap-3 ${
                                isSelected
                                  ? 'bg-indigo-600/20 border-cyan-400 shadow-md'
                                  : 'bg-slate-950/60 border-white/10 hover:border-white/20'
                              }`}
                            >
                              <div className="space-y-1">
                                <div className="flex items-center gap-2">
                                  <span className="font-bold text-sm text-white">{src.name}</span>
                                  {src.isFullMatrix && (
                                    <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 font-semibold">
                                      Đủ ma trận
                                    </span>
                                  )}
                                  {scannedSourceResult.recommendedSource === src.name && (
                                    <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-indigo-500/20 text-indigo-300 border border-indigo-500/30 font-semibold">
                                      ⭐ Tối ưu
                                    </span>
                                  )}
                                </div>
                                <div className="flex items-center gap-2 text-xs text-slate-400">
                                  <span>{src.totalQuestions} câu hỏi</span>
                                  <span>•</span>
                                  <span>P.I: {src.part1Count}</span>
                                  <span>•</span>
                                  <span>P.II: {src.part2Count}</span>
                                  <span>•</span>
                                  <span>P.III: {src.part3Count}</span>
                                </div>
                              </div>
                              <button
                                type="button"
                                onClick={() => {
                                  setSourceName(src.name);
                                  const y = src.name.match(/(202\d)/);
                                  if (y) setExamYear(y[1]);
                                  setSourceNotice(`Đã chọn: "${src.name}" (${src.totalQuestions} câu)`);
                                  setIsSourceModalOpen(false);
                                }}
                                className={`px-3 py-1.5 rounded-xl text-xs font-bold transition-all ${
                                  isSelected
                                    ? 'bg-cyan-500 text-slate-950 font-black'
                                    : 'bg-indigo-600 hover:bg-indigo-500 text-white'
                                }`}
                              >
                                {isSelected ? 'Đang chọn' : 'Chọn nguồn này'}
                              </button>
                            </div>
                          );
                        })
                      ) : (
                        <div className="py-8 text-center text-slate-400 text-sm space-y-3">
                          <p>Chưa có câu hỏi nào trong ngân hàng gắn với môn này.</p>
                          <p className="text-xs text-slate-500">
                            Bạn có thể chọn nhanh các nguồn chuẩn quốc gia bên dưới:
                          </p>
                          <div className="flex flex-wrap gap-2 justify-center pt-2">
                            {[
                              'Bộ Giáo Dục & Đào Tạo 2025',
                              'Sở GD&ĐT Hà Nội 2025',
                              'Sở GD&ĐT TP.HCM 2025',
                              'Sở GD&ĐT Nghệ An 2025',
                              'THPT Chuyên Hà Nội - Amsterdam 2025'
                            ].map(preset => (
                              <button
                                key={preset}
                                type="button"
                                onClick={() => {
                                  setSourceName(preset);
                                  const y = preset.match(/(202\d)/);
                                  if (y) setExamYear(y[1]);
                                  setSourceNotice(`Đã chọn: "${preset}"`);
                                  setIsSourceModalOpen(false);
                                }}
                                className="px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-semibold text-white border border-white/10"
                              >
                                + {preset}
                              </button>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>

                    <div className="flex justify-end pt-2 border-t border-white/10">
                      <button
                        type="button"
                        onClick={() => setIsSourceModalOpen(false)}
                        className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-bold text-slate-300"
                      >
                        Đóng
                      </button>
                    </div>
                  </div>
                </div>
              )}

              <label className="block text-sm text-slate-300">
                Chọn file đề thi (.txt hoặc .pdf, tối đa 10 MB)
                <input
                  type="file"
                  accept=".txt,.pdf,text/plain,application/pdf"
                  onChange={handleFileChange}
                  className="mt-2 block w-full text-sm text-slate-400 file:mr-3 file:rounded-lg file:border-0 file:bg-indigo-600 file:px-3 file:py-2 file:font-semibold file:text-white hover:file:bg-indigo-500"
                />
                {selectedFile && (
                  <p className="mt-1.5 text-xs text-indigo-300 flex items-center gap-1.5">
                    <FileText className="w-3.5 h-3.5" />
                    <span>Đã chọn: <strong className="text-white">{selectedFile.name}</strong> ({(selectedFile.size / 1024).toFixed(1)} KB)</span>
                  </p>
                )}
              </label>

              <label className="block text-sm text-slate-300">
                Hoặc dán nội dung đề văn bản
                <textarea
                  required={!selectedFile}
                  value={rawContent}
                  onChange={event => setRawContent(event.target.value)}
                  rows={8}
                  maxLength={2 * 1024 * 1024}
                  placeholder="Dán nội dung đề thi ở đây (hoặc chọn file PDF / TXT ở trên)..."
                  className="mt-2 w-full rounded-xl border border-white/10 bg-slate-900 p-3 font-mono text-xs text-slate-100 placeholder:text-slate-500 focus:outline-none focus:border-indigo-500/50"
                />
              </label>

              {error && <p role="alert" className="rounded-xl border border-rose-500/30 bg-rose-950/30 p-3 text-sm text-rose-200">{error}</p>}
              {result && (
                <div role="status" className="rounded-2xl border border-emerald-500/30 bg-emerald-950/40 p-4 text-sm text-emerald-200 space-y-3 shadow-lg">
                  <div className="flex items-center gap-2 font-bold text-emerald-400">
                    <Sparkles className="w-5 h-5" />
                    <span>{result.message || 'Bóc tách đề thi thành công!'}</span>
                  </div>

                  <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-xs">
                    <div className="bg-black/30 rounded-xl p-2.5 border border-white/5">
                      <span className="text-slate-400 block text-[11px]">Tổng trích xuất</span>
                      <span className="text-base font-black text-white font-mono">{result.totalExtracted || 0}</span>
                    </div>
                    <div className="bg-black/30 rounded-xl p-2.5 border border-emerald-500/20">
                      <span className="text-emerald-400 block text-[11px]">Câu mới hợp lệ</span>
                      <span className="text-base font-black text-emerald-300 font-mono">{result.totalNew || 0}</span>
                    </div>
                    <div className="bg-black/30 rounded-xl p-2.5 border border-amber-500/20">
                      <span className="text-amber-400 block text-[11px]">Câu trùng lặp (Gộp)</span>
                      <span className="text-base font-black text-amber-300 font-mono">{result.totalMerged || 0}</span>
                    </div>
                    <div className="bg-black/30 rounded-xl p-2.5 border border-rose-500/20">
                      <span className="text-rose-400 block text-[11px]">Lỗi kiểm định</span>
                      <span className="text-base font-black text-rose-300 font-mono">{result.totalFailed || 0}</span>
                    </div>
                  </div>

                  {/* Format Breakdown (P0 Roadmap) */}
                  <div className="flex flex-wrap items-center gap-2 pt-1 border-t border-white/10 text-xs">
                    <span className="text-slate-400 font-semibold">Cấu trúc đề 2025:</span>
                    <span className="rounded-lg bg-indigo-500/20 px-2.5 py-1 text-indigo-300 font-medium border border-indigo-500/30">
                      Phần I (MCQ): <strong>{result.part1Count ?? 0}</strong>
                    </span>
                    <span className="rounded-lg bg-purple-500/20 px-2.5 py-1 text-purple-300 font-medium border border-purple-500/30">
                      Phần II (Đúng/Sai): <strong>{result.part2Count ?? 0}</strong>
                    </span>
                    <span className="rounded-lg bg-cyan-500/20 px-2.5 py-1 text-cyan-300 font-medium border border-cyan-500/30">
                      Phần III (Điền số): <strong>{result.part3Count ?? 0}</strong>
                    </span>
                  </div>

                  {/* Validation Errors Breakdown */}
                  {result.errors && result.errors.length > 0 && (
                    <details className="mt-2 text-xs bg-rose-950/40 border border-rose-500/30 rounded-xl p-3 text-rose-200">
                      <summary className="cursor-pointer font-bold flex items-center gap-1.5 text-rose-300">
                        <AlertTriangle className="w-3.5 h-3.5" />
                        <span>Chi tiết {result.errors.length} cảnh báo / lỗi định dạng câu hỏi</span>
                      </summary>
                      <ul className="mt-2 space-y-1 list-disc list-inside font-mono text-[11px] text-rose-300/90 max-h-40 overflow-y-auto">
                        {result.errors.map((err, idx) => (
                          <li key={idx}>{err}</li>
                        ))}
                      </ul>
                    </details>
                  )}
                  {/* DIRECT EXAM ACTION (CHỌN ĐỀ THI LUÔN) */}
                  <div className="pt-3 border-t border-emerald-500/30 flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3 bg-black/40 p-3.5 rounded-2xl border border-white/10 shadow-inner">
                    <div className="text-xs">
                      <p className="font-bold text-white flex items-center gap-1.5">
                        <CheckCircle2 className="w-4 h-4 text-emerald-400" />
                        <span>Đã bóc tách đề xong! Bạn có thể chọn đề thi này để làm bài ngay:</span>
                      </p>
                      <p className="text-slate-300 text-[11px] mt-0.5">
                        Môn: <strong className="text-cyan-300">{supportedSubjects.find(s => s.code === subjectCode)?.label}</strong> • Nguồn: <strong className="text-white">{sourceName || 'Đề vừa bóc tách'}</strong>
                      </p>
                    </div>

                    <div className="flex items-center gap-2 flex-wrap">
                      <button
                        type="button"
                        onClick={async () => {
                          if (pendingQuestions.length > 0) {
                            try {
                              await verifyAllQuestions();
                            } catch {
                              // Continue anyway
                            }
                          }
                          router.push(`/exam/exam-bgd-2025?subject=${subjectCode}&source=${encodeURIComponent(sourceName)}`);
                        }}
                        className="px-4 py-2.5 rounded-xl bg-gradient-to-r from-emerald-600 via-teal-600 to-cyan-600 hover:from-emerald-500 hover:to-cyan-500 text-white font-black text-xs shadow-lg shadow-emerald-950/60 flex items-center gap-2 transition-all hover:scale-[1.03] active:scale-[0.98]"
                      >
                        <Sparkles className="w-4 h-4 text-yellow-300 animate-pulse" />
                        <span>🎯 Chọn đề này & Vào phòng thi ngay</span>
                      </button>
                    </div>
                  </div>
                </div>
              )}

              <button
                disabled={isSubmitting || (!rawContent.trim() && !selectedFile) || !sourceName.trim()}
                className="flex items-center gap-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 px-5 py-3 text-sm font-bold text-white shadow-lg shadow-indigo-950/50 disabled:cursor-not-allowed disabled:opacity-50 transition-all hover:scale-[1.01] active:scale-[0.99]"
              >
                <Sparkles className="w-4 h-4 text-indigo-300" />
                <span>{isSubmitting ? 'Hệ thống đang bóc tách & nạp đề thi...' : 'Bóc tách & Nạp đề thi ngay'}</span>
              </button>
            </form>
          )}

          {/* Tab Navigation */}
          <div className="flex items-center gap-2 border-b border-white/10 pb-1">
            <button
              onClick={() => setActiveTab('questions')}
              className={`flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-bold transition-all ${
                activeTab === 'questions'
                  ? 'border-indigo-500 text-indigo-400 bg-indigo-500/10 rounded-t-xl'
                  : 'border-transparent text-slate-400 hover:text-slate-200'
              }`}
            >
              <Layers className="w-4 h-4" />
              <span>Hàng chờ duyệt câu hỏi</span>
              <span className="ml-1 rounded-full bg-slate-800 px-2 py-0.5 text-xs font-mono text-slate-300">
                {pendingQuestions.length}
              </span>
            </button>

            <button
              onClick={() => {
                setActiveTab('provenance');
                if (ingestedPapers.length === 0) {
                  void loadIngestedPapers();
                }
              }}
              className={`flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-bold transition-all ${
                activeTab === 'provenance'
                  ? 'border-indigo-500 text-indigo-400 bg-indigo-500/10 rounded-t-xl'
                  : 'border-transparent text-slate-400 hover:text-slate-200'
              }`}
            >
              <BookOpen className="w-4 h-4" />
              <span>Đối chiếu đề gốc & Nguồn tài liệu</span>
              {ingestedPapers.length > 0 && (
                <span className="ml-1 rounded-full bg-slate-800 px-2 py-0.5 text-xs font-mono text-slate-300">
                  {ingestedPapers.length}
                </span>
              )}
            </button>
          </div>

          {activeTab === 'questions' && (
            <section className="space-y-4">
              <header className="flex flex-col sm:flex-row sm:items-end justify-between gap-3">
                <div>
                  <h2 className="text-xl font-black">Hàng chờ duyệt câu hỏi ({pendingQuestions.length})</h2>
                  <p className="mt-1 text-sm text-slate-400">Hiệu đính, kiểm tra đáp án và xuất bản vào ngân hàng đề thi.</p>
                </div>
                <div className="flex items-center gap-2 flex-wrap">
                  {pendingQuestions.length > 0 && (
                    <button
                      onClick={handleVerifyAll}
                      disabled={isVerifyingAll || isLoadingQuestions}
                      className="flex items-center gap-1.5 rounded-xl bg-emerald-600 hover:bg-emerald-500 px-4 py-2 text-sm font-bold text-white shadow-lg shadow-emerald-950/40 disabled:opacity-50 transition-all hover:scale-[1.02] active:scale-[0.98]"
                    >
                      <CheckCheck className="w-4 h-4" />
                      <span>{isVerifyingAll ? 'Đang duyệt tất cả…' : `Chấp nhận toàn bộ (${pendingQuestions.length} câu)`}</span>
                    </button>
                  )}
                  <button onClick={() => void refreshPending()} disabled={isLoadingQuestions} className="flex items-center gap-1.5 rounded-xl border border-white/10 px-4 py-2 text-sm disabled:opacity-50 hover:bg-white/5 transition-colors">
                    <RefreshCw className={`w-3.5 h-3.5 ${isLoadingQuestions ? 'animate-spin' : ''}`} />
                    <span>{isLoadingQuestions ? 'Đang tải…' : 'Tải lại'}</span>
                  </button>
                </div>
              </header>

              {verifyAllSuccess && (
                <div role="status" className="rounded-xl border border-emerald-500/30 bg-emerald-950/40 p-4 text-sm text-emerald-200 flex items-center justify-between gap-2 shadow-lg">
                  <div className="flex items-center gap-2">
                    <CheckCircle2 className="w-5 h-5 text-emerald-400 shrink-0" />
                    <span>{verifyAllSuccess}</span>
                  </div>
                  <button onClick={() => setVerifyAllSuccess('')} className="text-xs text-slate-400 hover:text-white px-2 py-1">Đóng</button>
                </div>
              )}

              {reviewError && <p role="alert" className="rounded-xl border border-rose-500/30 bg-rose-950/30 p-3 text-sm text-rose-200">{reviewError}</p>}
              {isLoadingQuestions && pendingQuestions.length === 0 && <p className="text-sm text-slate-400">Đang tải hàng chờ duyệt…</p>}
              {!isLoadingQuestions && pendingQuestions.length === 0 && <p className="rounded-2xl border border-white/10 p-5 text-sm text-slate-400">Không có câu hỏi đang chờ duyệt.</p>}

              {pendingQuestions.map(question => (
                <article key={question.id} className="space-y-4 rounded-2xl border border-white/10 bg-slate-950/70 p-5 shadow-lg">
                  <div className="flex flex-wrap items-center justify-between gap-2 border-b border-white/5 pb-2">
                    <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-slate-400">
                      <span className="font-bold text-indigo-300">{question.subjectName}</span>
                      <span>·</span>
                      <span>{question.topic}</span>
                      <span>·</span>
                      <span className="font-mono text-slate-300">{question.format}</span>
                      <span>·</span>
                      <span className="rounded bg-white/5 px-1.5 py-0.5 text-[10px] text-amber-300">{question.difficulty}</span>
                    </div>

                    <button
                      onClick={() => handleOpenEdit(question)}
                      className="flex items-center gap-1 rounded-lg border border-indigo-500/40 bg-indigo-500/10 px-2.5 py-1 text-xs font-semibold text-indigo-300 hover:bg-indigo-500/20"
                    >
                      <Edit3 className="w-3.5 h-3.5" />
                      <span>Sửa câu hỏi</span>
                    </button>
                  </div>

                  <div className="whitespace-pre-wrap text-sm text-white leading-relaxed">{question.content}</div>

                  {question.attachedTable && (
                    <pre className="overflow-auto rounded-xl bg-black/40 p-3 font-mono text-xs text-slate-300 border border-white/5">{question.attachedTable}</pre>
                  )}

                  <details className="text-sm text-slate-300">
                    <summary className="cursor-pointer font-medium text-slate-400 hover:text-white">Xem chi tiết đáp án & lời giải</summary>
                    <pre className="mt-2 overflow-auto rounded-xl bg-black/40 p-3 text-xs border border-white/5">{JSON.stringify(question.answerData, null, 2)}</pre>
                    {question.explanation && <p className="mt-2 whitespace-pre-wrap text-xs text-slate-300 bg-slate-900/40 p-2.5 rounded-lg border border-white/5">{question.explanation}</p>}
                  </details>

                  {question.imageUrls?.map((url, index) => (
                    <Image key={`${question.id}-${index}`} src={url} alt={`Hình minh họa ${index + 1}`} width={1024} height={768} unoptimized className="max-h-80 w-auto rounded-xl object-contain" />
                  ))}

                  <p className="text-xs text-slate-500">Nguồn: {question.sourceTags?.join(', ') || 'Chưa có thông tin nguồn'}</p>

                  <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2 pt-2 border-t border-white/5">
                    <button
                      disabled={reviewingId !== null}
                      onClick={() => void handleReview(question.id, 'verify')}
                      className="flex items-center justify-center gap-1.5 rounded-xl bg-emerald-600 hover:bg-emerald-500 px-4 py-2.5 text-xs font-bold text-white disabled:opacity-50 transition-colors shrink-0"
                    >
                      <CheckCircle2 className="w-3.5 h-3.5" />
                      <span>{reviewingId === question.id ? 'Đang lưu…' : 'Duyệt & Xuất bản'}</span>
                    </button>

                    <div className="flex items-center gap-2 flex-1">
                      <input
                        value={reviewNotes[question.id] || ''}
                        onChange={event => setReviewNotes(current => ({ ...current, [question.id]: event.target.value }))}
                        maxLength={1000}
                        placeholder="Lý do từ chối (bắt buộc khi từ chối)"
                        className="w-full flex-1 rounded-xl border border-white/10 bg-slate-900 px-3 py-2 text-xs"
                      />

                      <button
                        disabled={reviewingId !== null || !reviewNotes[question.id]?.trim()}
                        onClick={() => void handleReview(question.id, 'reject')}
                        className="flex items-center justify-center gap-1.5 rounded-xl border border-rose-500/40 px-3.5 py-2 text-xs font-bold text-rose-300 hover:bg-rose-500/10 disabled:opacity-50 transition-colors shrink-0"
                      >
                        <XCircle className="w-3.5 h-3.5" />
                        <span>Từ chối</span>
                      </button>
                    </div>
                  </div>
                </article>
              ))}

              {hasMoreQuestions && (
                <button
                  onClick={() => void refreshPending(pendingQuestions.length)}
                  disabled={isLoadingQuestions}
                  className="w-full rounded-2xl border border-white/10 bg-slate-950/40 px-4 py-3 text-sm font-semibold text-slate-300 hover:bg-white/5 disabled:opacity-50"
                >
                  {isLoadingQuestions ? 'Đang tải…' : 'Tải thêm câu hỏi'}
                </button>
              )}
            </section>
          )}

          {/* Tab 2: Provenance & Raw Ingested Papers */}
          {activeTab === 'provenance' && (
            <section className="space-y-4">
              <header className="flex flex-col sm:flex-row sm:items-end justify-between gap-3">
                <div>
                  <h2 className="text-xl font-black">Đối chiếu nguồn tài liệu & Văn bản gốc</h2>
                  <p className="mt-1 text-sm text-slate-400">
                    Truy vết nguồn gốc từng đề thi, kiểm tra lại nội dung thô trước khi bóc tách.
                  </p>
                </div>
                <div className="flex items-center gap-2">
                  <div className="relative">
                    <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
                    <input
                      type="text"
                      placeholder="Tìm theo nguồn đề / môn..."
                      value={paperSearch}
                      onChange={e => setPaperSearch(e.target.value)}
                      className="pl-9 pr-3 py-1.5 rounded-xl border border-white/10 bg-slate-900 text-xs text-white placeholder:text-slate-500 w-64"
                    />
                  </div>
                  <button
                    onClick={() => void loadIngestedPapers()}
                    disabled={isLoadingPapers}
                    className="flex items-center gap-1.5 rounded-xl border border-white/10 px-3 py-1.5 text-xs disabled:opacity-50 hover:bg-white/5 transition-colors"
                  >
                    <RefreshCw className={`w-3.5 h-3.5 ${isLoadingPapers ? 'animate-spin' : ''}`} />
                    <span>Tải lại</span>
                  </button>
                </div>
              </header>

              {isLoadingPapers && ingestedPapers.length === 0 && (
                <p className="text-sm text-slate-400">Đang tải danh sách tài liệu đề thi…</p>
              )}

              {!isLoadingPapers && ingestedPapers.length === 0 && (
                <div className="rounded-2xl border border-white/10 bg-slate-950/40 p-8 text-center text-slate-400 space-y-2">
                  <BookOpen className="w-8 h-8 mx-auto text-slate-500" />
                  <p className="font-semibold">Chưa có đề thi nào trong danh mục đối chiếu.</p>
                  <p className="text-xs">Hãy nạp một file .pdf hoặc .txt ở trên để bắt đầu bóc tách và lưu vết nguồn gốc.</p>
                </div>
              )}

              {ingestedPapers.length > 0 && (
                <div className="overflow-x-auto rounded-2xl border border-white/10 bg-slate-950/60 shadow-xl">
                  <table className="w-full text-left text-xs text-slate-300">
                    <thead className="border-b border-white/10 bg-white/5 text-[11px] font-bold uppercase tracking-wider text-slate-400">
                      <tr>
                        <th className="px-4 py-3">Tên đề / Nguồn</th>
                        <th className="px-4 py-3">Môn học</th>
                        <th className="px-4 py-3">Năm</th>
                        <th className="px-4 py-3">Trạng thái</th>
                        <th className="px-4 py-3">Dung lượng gốc</th>
                        <th className="px-4 py-3">Thời gian nạp</th>
                        <th className="px-4 py-3 text-right">Đối chiếu</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-white/5">
                      {ingestedPapers
                        .filter(p =>
                          paperSearch
                            ? p.sourceName.toLowerCase().includes(paperSearch.toLowerCase()) ||
                              p.subjectCode.toLowerCase().includes(paperSearch.toLowerCase())
                            : true
                        )
                        .map(paper => (
                          <tr key={paper.id} className="hover:bg-white/[0.02] transition-colors">
                            <td className="px-4 py-3.5 font-semibold text-white">
                              {paper.sourceName}
                            </td>
                            <td className="px-4 py-3.5">
                              <span className="font-mono text-indigo-300 bg-indigo-500/10 px-2 py-0.5 rounded border border-indigo-500/20">
                                {paper.subjectCode}
                              </span>
                            </td>
                            <td className="px-4 py-3.5 font-mono text-slate-400">
                              {paper.examYear || '—'}
                            </td>
                            <td className="px-4 py-3.5">
                              <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider ${
                                paper.status === 'COMPLETED'
                                  ? 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/30'
                                  : paper.status === 'FAILED'
                                  ? 'bg-rose-500/20 text-rose-300 border border-rose-500/30'
                                  : 'bg-amber-500/20 text-amber-300 border border-amber-500/30'
                              }`}>
                                {paper.status}
                              </span>
                            </td>
                            <td className="px-4 py-3.5 font-mono text-slate-300">
                              {paper.rawLength ? `${(paper.rawLength / 1024).toFixed(1)} KB` : '—'}
                            </td>
                            <td className="px-4 py-3.5 text-slate-400 text-[11px]">
                              {new Date(paper.submittedAt).toLocaleString('vi-VN')}
                            </td>
                            <td className="px-4 py-3.5 text-right space-x-1.5 whitespace-nowrap">
                              <button
                                onClick={() => void handleInspectPaper(paper.id)}
                                disabled={inspectingPaperId === paper.id}
                                className="inline-flex items-center gap-1 rounded-lg border border-indigo-500/30 bg-indigo-500/10 hover:bg-indigo-500/20 px-2.5 py-1 text-xs font-semibold text-indigo-300 transition-colors disabled:opacity-50"
                              >
                                <Eye className="w-3.5 h-3.5" />
                                <span>{inspectingPaperId === paper.id ? 'Đang đọc…' : 'Xem đề gốc'}</span>
                              </button>

                              <button
                                onClick={() => {
                                  router.push(`/exam/exam-bgd-2025?subject=${paper.subjectCode}&source=${encodeURIComponent(paper.sourceName)}`);
                                }}
                                className="inline-flex items-center gap-1 rounded-lg border border-emerald-500/30 bg-emerald-500/10 hover:bg-emerald-500/20 px-2.5 py-1 text-xs font-semibold text-emerald-300 transition-colors"
                                title="Chọn đề thi này để vào phòng thi làm bài ngay"
                              >
                                <Sparkles className="w-3.5 h-3.5 text-yellow-300" />
                                <span>🎯 Thi đề này</span>
                              </button>
                            </td>
                          </tr>
                        ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          )}

          {/* Paper Raw Content Inspection Modal */}
          {selectedPaperDetails && (
            <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/85 backdrop-blur-md p-4 overflow-y-auto">
              <div className="relative w-full max-w-4xl rounded-3xl border border-white/20 bg-slate-950 p-6 shadow-2xl space-y-4 max-h-[90vh] flex flex-col">
                <div className="flex items-center justify-between border-b border-white/10 pb-3 shrink-0">
                  <div className="flex items-center gap-2.5">
                    <BookOpen className="w-5 h-5 text-indigo-400" />
                    <div>
                      <h3 className="text-base font-bold text-white">{selectedPaperDetails.sourceName}</h3>
                      <div className="flex items-center gap-2 text-xs text-slate-400 mt-0.5">
                        <span className="text-indigo-300 font-mono">{selectedPaperDetails.subjectCode}</span>
                        <span>·</span>
                        <span>Năm: {selectedPaperDetails.examYear || 'Không ghi'}</span>
                        <span>·</span>
                        <span>{new Date(selectedPaperDetails.submittedAt).toLocaleString('vi-VN')}</span>
                      </div>
                    </div>
                  </div>
                  <button
                    onClick={() => setSelectedPaperDetails(null)}
                    className="rounded-lg p-1.5 text-slate-400 hover:text-white hover:bg-white/10 transition-colors"
                  >
                    <X className="w-5 h-5" />
                  </button>
                </div>

                <div className="flex-1 overflow-hidden flex flex-col space-y-2">
                  <div className="flex items-center justify-between text-xs text-slate-400 px-1">
                    <span>Nội dung văn bản đề thi thô trích xuất từ nguồn:</span>
                    <button
                      onClick={() => {
                        if (selectedPaperDetails.rawContent) {
                          navigator.clipboard.writeText(selectedPaperDetails.rawContent);
                          alert('Đã sao chép nội dung văn bản đề gốc vào bộ nhớ tạm.');
                        }
                      }}
                      className="text-indigo-400 hover:text-indigo-300 underline"
                    >
                      Sao chép toàn bộ
                    </button>
                  </div>
                  <pre className="flex-1 overflow-auto rounded-2xl bg-black/70 p-4 font-mono text-xs text-slate-200 border border-white/10 whitespace-pre-wrap leading-relaxed select-text">
                    {selectedPaperDetails.rawContent || 'Không có văn bản gốc.'}
                  </pre>
                </div>

                <div className="flex items-center justify-between pt-2 border-t border-white/10 shrink-0">
                  <button
                    onClick={() => {
                      router.push(`/exam/exam-bgd-2025?subject=${selectedPaperDetails.subjectCode}&source=${encodeURIComponent(selectedPaperDetails.sourceName)}`);
                    }}
                    className="flex items-center gap-1.5 px-4 py-2 rounded-xl bg-gradient-to-r from-emerald-600 via-teal-600 to-cyan-600 hover:from-emerald-500 hover:to-cyan-500 text-white font-bold text-xs shadow-md shadow-emerald-950/50 transition-all hover:scale-[1.02] active:scale-[0.98]"
                  >
                    <Sparkles className="w-3.5 h-3.5 text-yellow-300" />
                    <span>🎯 Chọn đề này để thi thử ngay</span>
                  </button>

                  <button
                    onClick={() => setSelectedPaperDetails(null)}
                    className="rounded-xl border border-white/10 bg-slate-900 px-4 py-2 text-xs font-semibold text-slate-300 hover:bg-white/5"
                  >
                    Đóng đối chiếu
                  </button>
                </div>
              </div>
            </div>
          )}

          {/* Edit Question Modal */}
          {editingQuestion && (
            <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 backdrop-blur-sm p-4 overflow-y-auto">
              <div className="relative w-full max-w-3xl rounded-3xl border border-white/20 bg-slate-950 p-6 shadow-2xl space-y-4 max-h-[90vh] overflow-y-auto">
                <div className="flex items-center justify-between border-b border-white/10 pb-3">
                  <div className="flex items-center gap-2">
                    <Edit3 className="w-5 h-5 text-indigo-400" />
                    <h3 className="text-lg font-bold">Hiệu đính câu hỏi</h3>
                  </div>
                  <button onClick={() => setEditingQuestion(null)} className="rounded-lg p-1 text-slate-400 hover:text-white">
                    <X className="w-5 h-5" />
                  </button>
                </div>

                {editError && <p role="alert" className="rounded-xl border border-rose-500/30 bg-rose-950/30 p-3 text-xs text-rose-200">{editError}</p>}

                <div className="space-y-3 text-xs">
                  <label className="block text-slate-300">
                    Nội dung câu hỏi (Markdown / LaTeX)
                    <textarea
                      rows={5}
                      value={editForm.content}
                      onChange={e => setEditForm(prev => ({ ...prev, content: e.target.value }))}
                      className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 p-3 text-slate-100 font-mono text-xs"
                    />
                  </label>

                  <div className="grid grid-cols-2 gap-3">
                    <label className="block text-slate-300">
                      Chuyên đề (Topic)
                      <input
                        value={editForm.topic}
                        onChange={e => setEditForm(prev => ({ ...prev, topic: e.target.value }))}
                        className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2 text-slate-100"
                      />
                    </label>

                    <label className="block text-slate-300">
                      Mức độ khó
                      <select
                        value={editForm.difficulty}
                        onChange={e => setEditForm(prev => ({ ...prev, difficulty: e.target.value }))}
                        className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2 text-slate-100"
                      >
                        <option value="Recognition">Nhận biết (Recognition)</option>
                        <option value="Comprehension">Thông hiểu (Comprehension)</option>
                        <option value="Application">Vận dụng (Application)</option>
                        <option value="HighApplication">Vận dụng cao (HighApplication)</option>
                      </select>
                    </label>
                  </div>

                  <label className="block text-slate-300">
                    Bảng số liệu đính kèm (Markdown Table)
                    <textarea
                      rows={3}
                      value={editForm.attachedTable}
                      onChange={e => setEditForm(prev => ({ ...prev, attachedTable: e.target.value }))}
                      placeholder="| Cột 1 | Cột 2 | ..."
                      className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 p-3 text-slate-100 font-mono text-xs"
                    />
                  </label>

                  <label className="block text-slate-300">
                    Lời giải chi tiết (Explanation)
                    <textarea
                      rows={4}
                      value={editForm.explanation}
                      onChange={e => setEditForm(prev => ({ ...prev, explanation: e.target.value }))}
                      className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 p-3 text-slate-100 text-xs"
                    />
                  </label>
                </div>

                <div className="flex justify-end gap-3 pt-3 border-t border-white/10">
                  <button
                    onClick={() => setEditingQuestion(null)}
                    className="rounded-xl border border-white/10 px-4 py-2 text-xs font-semibold text-slate-300 hover:bg-white/5"
                  >
                    Hủy
                  </button>
                  <button
                    onClick={() => void handleSaveEdit()}
                    disabled={isSavingEdit || !editForm.content.trim()}
                    className="flex items-center gap-1.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 px-4 py-2 text-xs font-bold text-white disabled:opacity-50"
                  >
                    <Save className="w-3.5 h-3.5" />
                    <span>{isSavingEdit ? 'Đang lưu…' : 'Lưu hiệu đính'}</span>
                  </button>
                </div>
              </div>
            </div>
          )}
        </main>
      </RoleGuard>
    </div>
  );
}
