'use client';

import React, { useState, useRef, useEffect, useCallback } from 'react';
import {
  FileText,
  Upload,
  Sparkles,
  RefreshCw,
  X,
  RotateCw,
  RotateCcw,
  Trash2,
  ArrowUp,
  ArrowDown,
  CheckCircle2,
  AlertTriangle,
  Eye,
  Sliders,
  Send,
  StopCircle,
  Copy,
  Columns
} from 'lucide-react';
import { SubjectCode, IngestionStatusResponse } from '@/types';
import { OcrPageItem, OcrProgressInfo, OcrSessionConfig } from '@/lib/ocr/types';
import {
  initOcrWorker,
  loadImageFromFile,
  prepareCanvasForOcr,
  recognizePage,
  releaseCanvas
} from '@/lib/ocr/image-ocr';
import {
  loadPdfDocument,
  getPdfPageCount,
  renderPdfPageToCanvas,
  extractNativePageText,
  PdfDocumentProxyLike
} from '@/lib/ocr/pdf-render';
import { Worker } from 'tesseract.js';
import { submitExamForIngestion } from '@/lib/api';

interface ExamOcrPanelProps {
  subjectCode: SubjectCode;
  sourceName: string;
  examYear?: string;
  onSourceNameChange?: (name: string) => void;
  onExamYearChange?: (year: string) => void;
  onSuccess: (result: IngestionStatusResponse) => void;
  onError: (error: string) => void;
}

export function ExamOcrPanel({
  subjectCode,
  sourceName,
  examYear,
  onSourceNameChange,
  onExamYearChange,
  onSuccess,
  onError
}: ExamOcrPanelProps) {
  const [pages, setPages] = useState<OcrPageItem[]>([]);
  const [localSourceName, setLocalSourceName] = useState<string>(sourceName);
  const [localExamYear, setLocalExamYear] = useState<string>(examYear || '');
  const [config, setConfig] = useState<OcrSessionConfig>({
    language: 'vie',
    twoColumnSplit: false,
    skipNativeTextPages: true,
    enhanceContrast: true
  });
  const [progress, setProgress] = useState<OcrProgressInfo>({
    status: 'idle',
    currentPage: 0,
    totalPages: 0,
    percent: 0,
    message: ''
  });
  const [autoSolve, setAutoSolve] = useState<boolean>(false);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [activePreviewIndex, setActivePreviewIndex] = useState<number | null>(null);
  const [copiedNotice, setCopiedNotice] = useState<boolean>(false);

  const pdfDocRef = useRef<PdfDocumentProxyLike | null>(null);
  const activeWorkerRef = useRef<Worker | null>(null);
  const isCancelledRef = useRef<boolean>(false);

  useEffect(() => {
    setLocalSourceName(sourceName);
  }, [sourceName]);

  useEffect(() => {
    if (examYear !== undefined) setLocalExamYear(examYear);
  }, [examYear]);

  // Dọn dẹp object URLs khi component unmount
  useEffect(() => {
    return () => {
      isCancelledRef.current = true;
      if (activeWorkerRef.current) {
        void activeWorkerRef.current.terminate();
      }
      pages.forEach(p => {
        if (p.previewUrl && p.previewUrl.startsWith('blob:')) {
          URL.revokeObjectURL(p.previewUrl);
        }
      });
    };
  }, [pages]);

  // Xử lý nạp file PDF hoặc danh sách ảnh
  const handleFilesSelected = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const files = event.target.files;
    if (!files || files.length === 0) return;

    const fileList = Array.from(files);
    const pdfFile = fileList.find(f => f.name.toLowerCase().endsWith('.pdf'));

    if (pdfFile) {
      setProgress({
        status: 'rendering',
        currentPage: 0,
        totalPages: 0,
        percent: 10,
        message: `Đang nạp file PDF: ${pdfFile.name}...`
      });

      try {
        const doc = await loadPdfDocument(pdfFile);
        pdfDocRef.current = doc;
        const count = getPdfPageCount(doc);

        const newPages: OcrPageItem[] = [];
        for (let i = 1; i <= count; i++) {
          newPages.push({
            id: `pdf-page-${i}-${Date.now()}`,
            pageNumber: i,
            fileName: `${pdfFile.name} (Trang ${i})`,
            sourceType: 'pdf_page',
            previewUrl: '', // Sẽ được kết xuất lazy khi render
            rotation: 0,
            status: 'idle',
            text: '',
            confidence: 0,
            columnMode: 'auto'
          });
        }

        // Tạo preview thumbnail nhanh cho các trang đầu
        setPages(newPages);
        setProgress({
          status: 'idle',
          currentPage: 0,
          totalPages: count,
          percent: 0,
          message: `Đã nạp ${count} trang từ PDF. Sẵn sàng nhận dạng OCR.`
        });

        // Kết xuất thumbnail ảnh cho các trang
        void generatePdfThumbnails(doc, newPages);

        // Tự động gợi ý tên nguồn từ tên file PDF nếu chưa có
        if (!localSourceName.trim()) {
          const baseName = pdfFile.name.replace(/\.[^/.]+$/, '').replace(/[-_]/g, ' ');
          const yearMatch = baseName.match(/(202\d)/);
          setLocalSourceName(baseName);
          onSourceNameChange?.(baseName);
          if (yearMatch) {
            setLocalExamYear(yearMatch[1]);
            onExamYearChange?.(yearMatch[1]);
          }
        }
      } catch (err) {
        onError(err instanceof Error ? err.message : 'Không thể đọc file PDF.');
        setProgress({ status: 'idle', currentPage: 0, totalPages: 0, percent: 0, message: '' });
      }
    } else {
      // Danh sách file ảnh
      const imageFiles = fileList.filter(f =>
        f.type.startsWith('image/') || /\.(png|jpe?g|webp|bmp)$/i.test(f.name)
      );

      if (imageFiles.length === 0) {
        onError('Vui lòng chọn file PDF hoặc các định dạng ảnh hợp lệ (.jpg, .png, .webp, .bmp).');
        return;
      }

      const newPages: OcrPageItem[] = imageFiles.map((img, idx) => ({
        id: `img-${idx}-${Date.now()}`,
        pageNumber: pages.length + idx + 1,
        fileName: img.name,
        sourceType: 'image',
        file: img,
        previewUrl: URL.createObjectURL(img),
        rotation: 0,
        status: 'idle',
        text: '',
        confidence: 0,
        columnMode: 'auto'
      }));

      setPages(prev => [...prev, ...newPages]);

      // Tự động gợi ý tên nguồn từ tên ảnh đầu tiên nếu chưa có
      if (!localSourceName.trim() && imageFiles.length > 0) {
        const baseName = imageFiles[0].name.replace(/\.[^/.]+$/, '').replace(/[-_]/g, ' ');
        const yearMatch = baseName.match(/(202\d)/);
        const derived = `Đề in ${baseName}`;
        setLocalSourceName(derived);
        onSourceNameChange?.(derived);
        if (yearMatch) {
          setLocalExamYear(yearMatch[1]);
          onExamYearChange?.(yearMatch[1]);
        }
      }
    }

    event.target.value = '';
  };

  const generatePdfThumbnails = async (doc: PdfDocumentProxyLike, pageItems: OcrPageItem[]) => {
    for (let i = 0; i < pageItems.length; i++) {
      if (isCancelledRef.current) break;
      try {
        const canvas = await renderPdfPageToCanvas(doc, i + 1, 0.4);
        const dataUrl = canvas.toDataURL('image/jpeg', 0.6);
        releaseCanvas(canvas);

        setPages(curr =>
          curr.map((p, idx) => (idx === i ? { ...p, previewUrl: dataUrl } : p))
        );
      } catch {
        // Ignored
      }
    }
  };

  const handleRotatePage = (index: number, delta: number) => {
    setPages(prev =>
      prev.map((p, idx) => {
        if (idx !== index) return p;
        const newRot = (p.rotation + delta + 360) % 360;
        return { ...p, rotation: newRot };
      })
    );
  };

  const handleDeletePage = (index: number) => {
    setPages(prev => {
      const removed = prev[index];
      if (removed?.previewUrl && removed.previewUrl.startsWith('blob:')) {
        URL.revokeObjectURL(removed.previewUrl);
      }
      return prev.filter((_, idx) => idx !== index).map((p, idx) => ({ ...p, pageNumber: idx + 1 }));
    });
  };

  const handleMovePage = (index: number, direction: 'up' | 'down') => {
    const targetIndex = direction === 'up' ? index - 1 : index + 1;
    if (targetIndex < 0 || targetIndex >= pages.length) return;

    setPages(prev => {
      const copy = [...prev];
      const temp = copy[index];
      copy[index] = copy[targetIndex];
      copy[targetIndex] = temp;
      return copy.map((p, idx) => ({ ...p, pageNumber: idx + 1 }));
    });
  };

  const handleCancelOcr = () => {
    isCancelledRef.current = true;
    if (activeWorkerRef.current) {
      void activeWorkerRef.current.terminate();
      activeWorkerRef.current = null;
    }
    setProgress(prev => ({
      ...prev,
      status: 'cancelled',
      message: 'Đã hủy quá trình nhận dạng chữ trên trình duyệt.'
    }));
  };

  // Vòng lặp OCR tuần tự từng trang
  const handleStartOcr = async () => {
    if (pages.length === 0) {
      onError('Chưa có trang nào để nhận dạng. Vui lòng chọn ảnh hoặc file PDF.');
      return;
    }

    isCancelledRef.current = false;
    setProgress({
      status: 'loading_model',
      currentPage: 0,
      totalPages: pages.length,
      percent: 5,
      message: 'Đang tải Web Worker và mô hình Tesseract WebAssembly...'
    });

    let worker = null;

    try {
      worker = await initOcrWorker({
        language: config.language,
        onProgress: (pct, statusText) => {
          setProgress(curr => ({
            ...curr,
            percent: Math.min(95, Math.max(curr.percent, Math.round(pct * 0.2))),
            message: `Mô hình AI: ${statusText} (${pct}%)`
          }));
        }
      });
      activeWorkerRef.current = worker;

      const total = pages.length;

      for (let i = 0; i < total; i++) {
        if (isCancelledRef.current) break;

        const page = pages[i];
        const pageNum = i + 1;

        // Cập nhật trạng thái trang đang chạy
        setPages(curr =>
          curr.map((p, idx) => (idx === i ? { ...p, status: 'recognizing' } : p))
        );

        setProgress({
          status: 'recognizing',
          currentPage: pageNum,
          totalPages: total,
          percent: Math.round(((i) / total) * 100),
          message: `Đang nhận dạng chữ trang ${pageNum}/${total}...`
        });

        // Kiểm tra xem trang PDF có text-native không nếu cấu hình bật
        if (page.sourceType === 'pdf_page' && config.skipNativeTextPages && pdfDocRef.current) {
          const nativeText = await extractNativePageText(pdfDocRef.current, page.pageNumber);
          if (nativeText.length > 80) {
            setPages(curr =>
              curr.map((p, idx) =>
                idx === i
                  ? {
                      ...p,
                      status: 'done',
                      text: nativeText,
                      confidence: 100,
                      isNativeText: true
                    }
                  : p
              )
            );
            continue;
          }
        }

        // Render trang ra canvas với tỉ lệ chất lượng cao
        let canvas: HTMLCanvasElement | null = null;
        try {
          if (page.sourceType === 'pdf_page' && pdfDocRef.current) {
            setProgress(curr => ({
              ...curr,
              message: `Đang kết xuất PDF trang ${pageNum}/${total}...`
            }));
            canvas = await renderPdfPageToCanvas(pdfDocRef.current, page.pageNumber, 1.8);
          } else if (page.file) {
            const img = await loadImageFromFile(page.file);
            canvas = prepareCanvasForOcr(img, page.rotation, config.enhanceContrast);
          }

          if (!canvas) {
            throw new Error('Không thể kết xuất trang.');
          }

          // Áp dụng xoay nếu có
          if (page.rotation !== 0 && page.sourceType === 'pdf_page') {
            const rotatedCanvas = prepareCanvasForOcr(canvas, page.rotation, config.enhanceContrast);
            releaseCanvas(canvas);
            canvas = rotatedCanvas;
          }

          const split2Col = page.columnMode === 'two_column' || (page.columnMode === 'auto' && config.twoColumnSplit);
          const result = await recognizePage(worker, canvas, split2Col);

          const isTooShort = result.text.length < 15;
          setPages(curr =>
            curr.map((p, idx) =>
              idx === i
                ? {
                    ...p,
                    status: isTooShort ? 'needs_review' : 'done',
                    text: result.text,
                    confidence: result.confidence,
                    isNativeText: false
                  }
                : p
            )
          );
        } catch (pageErr) {
          setPages(curr =>
            curr.map((p, idx) =>
              idx === i
                ? {
                    ...p,
                    status: 'error',
                    error: pageErr instanceof Error ? pageErr.message : 'Lỗi OCR trang'
                  }
                : p
            )
          );
        } finally {
          releaseCanvas(canvas);
        }
      }

      if (!isCancelledRef.current) {
        setProgress({
          status: 'completed',
          currentPage: total,
          totalPages: total,
          percent: 100,
          message: `✅ Nhận dạng OCR hoàn tất cho ${total} trang!`
        });
      }
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Lỗi trong quá trình OCR trên trình duyệt.');
      setProgress(curr => ({ ...curr, status: 'error', message: 'Lỗi nhận dạng' }));
    } finally {
      if (worker) {
        await worker.terminate();
        activeWorkerRef.current = null;
      }
    }
  };

  // Tổng hợp văn bản có cấu trúc theo chuẩn đề thi
  const getCompiledRawContent = useCallback((): string => {
    const lines: string[] = [];
    const displayName = localSourceName.trim() || sourceName.trim() || 'Đề thi';
    lines.push(`[Tài liệu: ${displayName}]`);
    lines.push(`[Trình duyệt OCR: Tesseract.js (ngôn ngữ: ${config.language})]`);
    lines.push('');

    pages.forEach(p => {
      lines.push(`--- TRANG ${p.pageNumber} ---`);
      lines.push(p.text.trim());
      lines.push('');
    });

    return lines.join('\n').trim();
  }, [pages, localSourceName, sourceName, config.language]);

  const handleCopyCompiledText = async () => {
    const text = getCompiledRawContent();
    await navigator.clipboard.writeText(text);
    setCopiedNotice(true);
    setTimeout(() => setCopiedNotice(false), 2500);
  };

  const handleTextChange = (index: number, newText: string) => {
    setPages(prev =>
      prev.map((p, idx) => (idx === index ? { ...p, text: newText } : p))
    );
  };

  // Gửi nội dung rawContent đã OCR lên endpoint nạp đề
  const handleSubmitToIngest = async () => {
    const content = getCompiledRawContent();
    if (!content || pages.every(p => !p.text.trim())) {
      onError('Vui lòng kiểm tra lại nội dung OCR trước khi gửi.');
      return;
    }

    const finalSource = (localSourceName || sourceName).trim();
    if (!finalSource) {
      onError('Vui lòng nhập tên nguồn đề thi (Sở GD / Trường THPT).');
      return;
    }

    const finalYear = (localExamYear || examYear) ? Number(localExamYear || examYear) : undefined;

    setIsSubmitting(true);
    try {
      const resp = await submitExamForIngestion({
        subjectCode,
        sourceName: finalSource,
        examYear: finalYear,
        rawContent: content,
        autoSolve: autoSolve
      });
      onSuccess(resp);
    } catch (cause) {
      onError(cause instanceof Error ? cause.message : 'Không thể gửi đề thi vào hệ thống.');
    } finally {
      setIsSubmitting(false);
    }
  };

  // Đếm sơ bộ số câu hỏi được phát hiện từ văn bản
  const totalCharacters = pages.reduce((sum, p) => sum + p.text.length, 0);
  const detectedQuestions = pages.reduce((sum, p) => {
    const matches = p.text.match(/(?:Câu|Cau)\s+\d+[:.]/gi);
    return sum + (matches ? matches.length : 0);
  }, 0);

  return (
    <div className="space-y-6 rounded-3xl border border-white/10 bg-slate-950/80 p-6 shadow-2xl">
      {/* HEADER SECTION */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-white/10 pb-4">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-gradient-to-br from-indigo-500 to-cyan-500 text-white shadow-lg shadow-cyan-500/20">
            <Sparkles className="h-5 w-5" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h2 className="text-lg font-black text-white">OCR Đề Thi Trực Tiếp Trên Trình Duyệt</h2>
              <span className="rounded-full bg-emerald-500/20 border border-emerald-500/30 px-2.5 py-0.5 text-[10px] font-bold text-emerald-300 uppercase tracking-wider">
                Khuyên dùng • Tiết kiệm RAM
              </span>
            </div>
            <p className="text-xs text-slate-400 mt-0.5">
              Toàn bộ ảnh/PDF được đọc và nhận dạng chữ tiếng Việt ngay trên máy bạn. Không chiếm CPU máy chủ Render Free.
            </p>
          </div>
        </div>

        {/* STATUS BADGE */}
        <div className="flex items-center gap-2 self-start sm:self-center">
          <span className="text-xs text-slate-400">Trạng thái:</span>
          <span className="rounded-xl border border-white/10 bg-slate-900 px-3 py-1 font-mono text-xs text-cyan-300">
            {pages.length} trang • {totalCharacters} ký tự • ~{detectedQuestions} câu
          </span>
        </div>
      </div>

      {/* DROPZONE CHO FILE ẢNH & PDF */}
      <div className="relative rounded-2xl border-2 border-dashed border-white/15 hover:border-cyan-500/50 bg-slate-900/40 p-6 text-center transition-all group">
        <input
          type="file"
          multiple
          accept=".pdf,image/png,image/jpeg,image/webp,image/bmp"
          onChange={handleFilesSelected}
          className="absolute inset-0 h-full w-full cursor-pointer opacity-0 z-10"
          id="ocr-file-upload-input"
        />
        <div className="flex flex-col items-center justify-center gap-2 pointer-events-none">
          <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-indigo-600/20 border border-indigo-500/30 text-cyan-400 group-hover:scale-110 transition-transform">
            <Upload className="h-6 w-6" />
          </div>
          <div>
            <p className="text-sm font-bold text-white">
              Chọn tập tin PDF hoặc kéo thả ảnh chụp đề in ấn vào đây
            </p>
            <p className="text-xs text-slate-400 mt-1">
              Hỗ trợ: PDF (tự tách trang), JPG, PNG, WEBP • Bạn có thể nạp thêm nhiều trang ảnh tiếp theo
            </p>
          </div>
        </div>
      </div>

      {/* OCR CONFIGURATION TOOLBAR */}
      <div className="grid gap-3 sm:grid-cols-2 md:grid-cols-4 rounded-2xl border border-white/10 bg-slate-900/60 p-4 text-xs">
        <div>
          <label className="font-semibold text-slate-300 block mb-1.5">Ngôn ngữ nhận dạng</label>
          <select
            value={config.language}
            onChange={e => setConfig(prev => ({ ...prev, language: e.target.value as 'vie' | 'vie+eng' }))}
            className="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-white font-medium focus:outline-none focus:border-cyan-500"
          >
            <option value="vie">🇻🇳 Tiếng Việt (Khuyên dùng)</option>
            <option value="vie+eng">🇻🇳+🇬🇧 Song ngữ Việt - Anh</option>
          </select>
        </div>

        <div>
          <label className="font-semibold text-slate-300 block mb-1.5">Bố cục chia 2 cột</label>
          <button
            type="button"
            onClick={() => setConfig(prev => ({ ...prev, twoColumnSplit: !prev.twoColumnSplit }))}
            className={`w-full rounded-xl border px-3 py-2 font-medium flex items-center justify-center gap-2 transition-all ${
              config.twoColumnSplit
                ? 'bg-cyan-500/20 border-cyan-400 text-cyan-200'
                : 'bg-slate-950 border-white/10 text-slate-400 hover:text-white'
            }`}
          >
            <Columns className="w-3.5 h-3.5" />
            <span>{config.twoColumnSplit ? 'Bật chia trái/phải' : 'Tắt (OCR toàn trang)'}</span>
          </button>
        </div>

        <div>
          <label className="font-semibold text-slate-300 block mb-1.5">Tối ưu tương phản</label>
          <button
            type="button"
            onClick={() => setConfig(prev => ({ ...prev, enhanceContrast: !prev.enhanceContrast }))}
            className={`w-full rounded-xl border px-3 py-2 font-medium flex items-center justify-center gap-2 transition-all ${
              config.enhanceContrast
                ? 'bg-indigo-500/20 border-indigo-400 text-indigo-200'
                : 'bg-slate-950 border-white/10 text-slate-400 hover:text-white'
            }`}
          >
            <Sliders className="w-3.5 h-3.5" />
            <span>{config.enhanceContrast ? 'Khử bóng & đậm mực' : 'Màu gốc'}</span>
          </button>
        </div>

        <div>
          <label className="font-semibold text-slate-300 block mb-1.5">Trích text PDF có sẵn</label>
          <button
            type="button"
            onClick={() => setConfig(prev => ({ ...prev, skipNativeTextPages: !prev.skipNativeTextPages }))}
            className={`w-full rounded-xl border px-3 py-2 font-medium flex items-center justify-center gap-2 transition-all ${
              config.skipNativeTextPages
                ? 'bg-emerald-500/20 border-emerald-400 text-emerald-200'
                : 'bg-slate-950 border-white/10 text-slate-400 hover:text-white'
            }`}
          >
            <CheckCircle2 className="w-3.5 h-3.5" />
            <span>{config.skipNativeTextPages ? 'Ưu tiên text gốc PDF' : 'Ép OCR ảnh'}</span>
          </button>
        </div>
      </div>

      {/* PROGRESS BAR & CONTROLS */}
      {pages.length > 0 && (
        <div className="space-y-3 rounded-2xl border border-white/10 bg-slate-900/80 p-4">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div className="space-y-1">
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold uppercase tracking-wider text-slate-300">Tiến trình nhận dạng</span>
                {progress.status === 'recognizing' && (
                  <span className="inline-flex items-center gap-1 rounded-full bg-cyan-500/20 px-2 py-0.5 text-[10px] font-bold text-cyan-300 animate-pulse">
                    Đang xử lý Web Worker
                  </span>
                )}
              </div>
              <p className="text-xs text-slate-400">{progress.message || 'Sẵn sàng khởi động OCR'}</p>
            </div>

            <div className="flex items-center gap-2">
              {progress.status === 'recognizing' || progress.status === 'loading_model' ? (
                <button
                  type="button"
                  onClick={handleCancelOcr}
                  className="flex items-center gap-1.5 rounded-xl bg-rose-600/90 hover:bg-rose-500 px-4 py-2 text-xs font-bold text-white shadow-md transition-all"
                >
                  <StopCircle className="w-4 h-4" />
                  <span>Hủy OCR</span>
                </button>
              ) : (
                <button
                  type="button"
                  id="btn-start-browser-ocr"
                  onClick={() => void handleStartOcr()}
                  className="flex items-center gap-2 rounded-xl bg-gradient-to-r from-cyan-500 via-indigo-600 to-purple-600 hover:from-cyan-400 hover:to-purple-500 px-5 py-2.5 text-xs font-black text-white shadow-lg shadow-cyan-950/50 transition-all hover:scale-[1.02] active:scale-[0.98]"
                >
                  <Sparkles className="w-4 h-4 text-yellow-300" />
                  <span>Bắt đầu nhận dạng OCR ({pages.length} trang)</span>
                </button>
              )}
            </div>
          </div>

          {/* Progress bar visual */}
          {(progress.status === 'recognizing' || progress.status === 'loading_model') && (
            <div className="relative h-2 w-full overflow-hidden rounded-full bg-slate-800">
              <div
                className="h-full bg-gradient-to-r from-cyan-400 to-indigo-500 transition-all duration-300"
                style={{ width: `${progress.percent}%` }}
              />
            </div>
          )}
        </div>
      )}

      {/* PAGE CARDS & TEXT EDITING SECTION */}
      {pages.length > 0 && (
        <div className="space-y-4">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-bold text-slate-200 uppercase tracking-wider flex items-center gap-2">
              <FileText className="w-4 h-4 text-cyan-400" />
              <span>Danh sách các trang & Văn bản bóc tách ({pages.length} trang)</span>
            </h3>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => void handleCopyCompiledText()}
                className="flex items-center gap-1 rounded-lg bg-slate-900 border border-white/10 px-3 py-1.5 text-xs text-slate-300 hover:text-white transition-all"
                title="Sao chép toàn bộ nội dung đã OCR"
              >
                <Copy className="w-3.5 h-3.5 text-cyan-400" />
                <span>{copiedNotice ? 'Đã sao chép!' : 'Sao chép văn bản'}</span>
              </button>
            </div>
          </div>

          <div className="space-y-4">
            {pages.map((page, index) => {
              const isNeedsReview = page.status === 'needs_review' || (page.status === 'done' && page.text.length < 30);
              return (
                <div
                  key={page.id}
                  className={`rounded-2xl border transition-all p-4 ${
                    isNeedsReview
                      ? 'border-amber-500/40 bg-amber-950/20'
                      : page.status === 'done'
                        ? 'border-emerald-500/30 bg-slate-900/60'
                        : 'border-white/10 bg-slate-900/40'
                  }`}
                >
                  <div className="flex flex-col md:flex-row gap-4">
                    {/* THUMBNAIL PREVIEW & PAGE CONTROLS */}
                    <div className="w-full md:w-48 shrink-0 flex flex-col gap-2">
                      <div
                        className="relative aspect-[3/4] w-full rounded-xl bg-slate-950 border border-white/10 overflow-hidden cursor-pointer group"
                        onClick={() => setActivePreviewIndex(activePreviewIndex === index ? null : index)}
                        title="Nhấp để phóng to đối chiếu"
                      >
                        {page.previewUrl ? (
                          // eslint-disable-next-line @next/next/no-img-element
                          <img
                            src={page.previewUrl}
                            alt={`Trang ${page.pageNumber}`}
                            style={{ transform: `rotate(${page.rotation}deg)` }}
                            className="h-full w-full object-contain transition-transform"
                          />
                        ) : (
                          <div className="flex h-full w-full items-center justify-center text-xs text-slate-500">
                            Chưa có preview
                          </div>
                        )}
                        <span className="absolute top-1.5 left-1.5 rounded-md bg-black/80 px-2 py-0.5 text-[10px] font-bold text-cyan-300 border border-white/10">
                          Trang {page.pageNumber}
                        </span>

                        <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 flex items-center justify-center gap-1 transition-opacity">
                          <Eye className="w-4 h-4 text-white" />
                          <span className="text-[10px] font-bold text-white">So sánh</span>
                        </div>
                      </div>

                      {/* CONTROLS PER PAGE */}
                      <div className="flex items-center justify-between gap-1 text-[11px] text-slate-400">
                        <div className="flex items-center gap-1">
                          <button
                            type="button"
                            onClick={() => handleRotatePage(index, -90)}
                            className="p-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-300"
                            title="Xoay ngược chiều 90°"
                          >
                            <RotateCcw className="w-3.5 h-3.5" />
                          </button>
                          <button
                            type="button"
                            onClick={() => handleRotatePage(index, 90)}
                            className="p-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-300"
                            title="Xoay thuận chiều 90°"
                          >
                            <RotateCw className="w-3.5 h-3.5" />
                          </button>
                          <button
                            type="button"
                            onClick={() =>
                              setPages(curr =>
                                curr.map((p, idx) =>
                                  idx === index
                                    ? {
                                        ...p,
                                        columnMode: p.columnMode === 'two_column' ? 'single' : 'two_column'
                                      }
                                    : p
                                )
                              )
                            }
                            className={`px-1.5 py-1 rounded text-[10px] font-bold border ${
                              page.columnMode === 'two_column'
                                ? 'bg-cyan-500/20 border-cyan-400 text-cyan-200'
                                : 'bg-slate-800 border-white/10 text-slate-400'
                            }`}
                            title="Chuyển chế độ 2 cột cho riêng trang này"
                          >
                            2 Cột
                          </button>
                        </div>

                        <div className="flex items-center gap-1">
                          <button
                            type="button"
                            disabled={index === 0}
                            onClick={() => handleMovePage(index, 'up')}
                            className="p-1 rounded bg-slate-800 hover:bg-slate-700 disabled:opacity-30"
                            title="Di chuyển lên trước"
                          >
                            <ArrowUp className="w-3.5 h-3.5" />
                          </button>
                          <button
                            type="button"
                            disabled={index === pages.length - 1}
                            onClick={() => handleMovePage(index, 'down')}
                            className="p-1 rounded bg-slate-800 hover:bg-slate-700 disabled:opacity-30"
                            title="Di chuyển xuống sau"
                          >
                            <ArrowDown className="w-3.5 h-3.5" />
                          </button>
                          <button
                            type="button"
                            onClick={() => handleDeletePage(index)}
                            className="p-1 rounded bg-rose-950/60 hover:bg-rose-900 text-rose-300"
                            title="Xóa trang này"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      </div>
                    </div>

                    {/* EDITABLE TEXT AREA & METADATA */}
                    <div className="flex-1 flex flex-col gap-2">
                      <div className="flex items-center justify-between text-xs flex-wrap gap-2">
                        <div className="flex items-center gap-2">
                          <span className="font-bold text-slate-200">
                            Văn bản nhận dạng [Trang {page.pageNumber}]
                          </span>
                          {page.isNativeText && (
                            <span className="rounded-md bg-emerald-500/20 border border-emerald-500/30 px-2 py-0.5 text-[10px] font-bold text-emerald-300">
                              Text PDF chuẩn gốc
                            </span>
                          )}
                          {page.status === 'done' && !page.isNativeText && (
                            <span className="rounded-md bg-cyan-500/20 border border-cyan-500/30 px-2 py-0.5 text-[10px] font-bold text-cyan-300">
                              OCR Tesseract ({page.confidence}%)
                            </span>
                          )}
                          {isNeedsReview && (
                            <span className="rounded-md bg-amber-500/20 border border-amber-500/30 px-2 py-0.5 text-[10px] font-bold text-amber-300 flex items-center gap-1">
                              <AlertTriangle className="w-3 h-3" /> Cần đối chiếu lại
                            </span>
                          )}
                          {page.status === 'recognizing' && (
                            <span className="rounded-md bg-indigo-500/20 border border-indigo-500/30 px-2 py-0.5 text-[10px] font-bold text-indigo-300 flex items-center gap-1 animate-pulse">
                              <RefreshCw className="w-3 h-3 animate-spin" /> Đang nhận dạng...
                            </span>
                          )}
                        </div>

                        <span className="text-[11px] font-mono text-slate-400">
                          {page.text.length} ký tự
                        </span>
                      </div>

                      <textarea
                        value={page.text}
                        onChange={e => handleTextChange(index, e.target.value)}
                        placeholder="Nội dung chữ của trang này sẽ xuất hiện tại đây sau khi OCR. Bạn có thể sửa trực tiếp..."
                        rows={page.text ? 7 : 4}
                        className="w-full rounded-xl border border-white/10 bg-slate-950 p-3 font-mono text-xs text-slate-100 placeholder:text-slate-600 focus:outline-none focus:border-cyan-500 transition-colors"
                      />

                      {isNeedsReview && (
                        <p className="text-[11px] text-amber-300 flex items-center gap-1.5">
                          <AlertTriangle className="w-3.5 h-3.5 shrink-0" />
                          <span>
                            Trang này có ít hơn 30 ký tự. Vui lòng đối chiếu với bản gốc bên trái để kiểm tra nếu trang bị mờ hoặc xoay ngang.
                          </span>
                        </p>
                      )}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* AUTO SOLVE TOGGLE & SUBMIT BANNER */}
      {pages.length > 0 && (
        <div className="space-y-4 pt-2">
          {/* AI Auto-solve disclaimer banner (Default OFF as per image-exam-extraction.md) */}
          <div className="p-4 rounded-2xl bg-gradient-to-r from-purple-950/60 via-indigo-950/50 to-slate-900 border border-purple-500/30 flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-4">
            <div className="space-y-1">
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold uppercase tracking-wider text-white">
                  Tùy chọn tự giải đề qua AI (Mặc định TẮT)
                </span>
                <span className={`text-[10px] px-2 py-0.5 rounded-full font-bold uppercase tracking-wider border ${
                  autoSolve 
                    ? 'bg-purple-500/20 text-purple-300 border-purple-500/30' 
                    : 'bg-slate-800 text-slate-400 border-white/10'
                }`}>
                  {autoSolve ? 'BẬT' : 'TẮT (Khuyên dùng)'}
                </span>
              </div>
              <p className="text-xs text-slate-300 max-w-xl">
                Bước OCR chỉ chép lại trung thực văn bản đề thi. Nếu bật tự giải đề, AI sẽ tự suy luận đáp án và được gắn nhãn riêng để kiểm duyệt viên đối chiếu kỹ lưỡng.
              </p>
            </div>

            <label className="relative inline-flex items-center cursor-pointer shrink-0 self-end sm:self-center">
              <input
                type="checkbox"
                checked={autoSolve}
                onChange={e => setAutoSolve(e.target.checked)}
                className="sr-only peer"
              />
              <div className="w-11 h-6 bg-slate-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-gradient-to-r peer-checked:from-purple-600 peer-checked:to-cyan-500"></div>
            </label>
          </div>

          {/* Inline Source & Year metadata fields */}
          <div className="grid gap-3 sm:grid-cols-2 rounded-2xl border border-white/10 bg-slate-900/60 p-3.5 text-xs">
            <div>
              <label className="text-slate-300 font-semibold block mb-1">Nguồn đề thi (Sở GD / THPT):</label>
              <input
                type="text"
                value={localSourceName}
                onChange={e => {
                  setLocalSourceName(e.target.value);
                  onSourceNameChange?.(e.target.value);
                }}
                placeholder="VD: Sở GD&ĐT Hà Nội 2025"
                className="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-white placeholder:text-slate-500 focus:outline-none focus:border-cyan-500"
              />
            </div>
            <div>
              <label className="text-slate-300 font-semibold block mb-1">Năm thi (không bắt buộc):</label>
              <input
                type="number"
                value={localExamYear}
                onChange={e => {
                  setLocalExamYear(e.target.value);
                  onExamYearChange?.(e.target.value);
                }}
                placeholder="2025"
                className="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-white placeholder:text-slate-500 focus:outline-none focus:border-cyan-500"
              />
            </div>
          </div>

          {/* SUBMIT BUTTON */}
          <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3 border-t border-white/10 pt-4">
            <p className="text-xs text-slate-400">
              Chỉ gửi chuỗi văn bản đã xác nhận lên máy chủ. Byte hình ảnh gốc không cần nạp lên API.
            </p>

            <button
              type="button"
              id="btn-submit-ocr-to-ingestion"
              onClick={() => void handleSubmitToIngest()}
              disabled={isSubmitting || pages.every(p => !p.text.trim())}
              className="flex items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-emerald-600 via-teal-600 to-cyan-600 hover:from-emerald-500 hover:to-cyan-500 px-6 py-3 font-black text-xs sm:text-sm text-white shadow-xl shadow-emerald-950/60 transition-all hover:scale-[1.02] active:scale-[0.98] disabled:opacity-50"
            >
              <Send className={`w-4 h-4 ${isSubmitting ? 'animate-spin' : ''}`} />
              <span>
                {isSubmitting
                  ? 'Đang chuyển văn bản vào hàng đợi bóc tách...'
                  : 'Gửi văn bản vào hàng bóc tách & duyệt đề'}
              </span>
            </button>
          </div>
        </div>
      )}

      {/* POPUP MODAL ĐỐI CHIẾU ẢNH PHÓNG TO */}
      {activePreviewIndex !== null && pages[activePreviewIndex] && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/85 backdrop-blur-md animate-fadeIn">
          <div className="bg-slate-900 border border-white/10 rounded-3xl max-w-4xl w-full p-6 space-y-4 max-h-[90vh] flex flex-col shadow-2xl">
            <div className="flex items-center justify-between border-b border-white/10 pb-3">
              <div className="flex items-center gap-2">
                <Eye className="w-5 h-5 text-cyan-400" />
                <h4 className="text-base font-bold text-white">
                  Đối chiếu ảnh gốc [Trang {pages[activePreviewIndex].pageNumber}]
                </h4>
              </div>
              <button
                type="button"
                onClick={() => setActivePreviewIndex(null)}
                className="p-1 rounded-lg text-slate-400 hover:text-white hover:bg-white/10"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4 flex-1 overflow-y-auto">
              <div className="aspect-[3/4] bg-slate-950 rounded-2xl border border-white/10 flex items-center justify-center p-2 overflow-hidden">
                {pages[activePreviewIndex].previewUrl ? (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img
                    src={pages[activePreviewIndex].previewUrl}
                    alt={`Đối chiếu trang ${pages[activePreviewIndex].pageNumber}`}
                    style={{ transform: `rotate(${pages[activePreviewIndex].rotation}deg)` }}
                    className="max-h-full max-w-full object-contain"
                  />
                ) : (
                  <p className="text-xs text-slate-500">Không có ảnh</p>
                )}
              </div>

              <div className="flex flex-col gap-2">
                <span className="text-xs font-bold text-slate-300">Nội dung đã nhận dạng (chỉnh sửa được):</span>
                <textarea
                  value={pages[activePreviewIndex].text}
                  onChange={e => handleTextChange(activePreviewIndex, e.target.value)}
                  className="flex-1 rounded-2xl border border-white/10 bg-slate-950 p-3 font-mono text-xs text-slate-100 focus:outline-none focus:border-cyan-500"
                />
              </div>
            </div>

            <div className="flex justify-end pt-2 border-t border-white/10">
              <button
                type="button"
                onClick={() => setActivePreviewIndex(null)}
                className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-bold text-slate-200"
              >
                Đóng đối chiếu
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
