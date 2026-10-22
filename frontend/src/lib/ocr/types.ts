export type OcrLanguage = 'vie' | 'vie+eng';

export type OcrPageStatus = 'idle' | 'rendering' | 'recognizing' | 'done' | 'error' | 'needs_review';

export type OcrOverallStatus = 
  | 'idle' 
  | 'loading_model' 
  | 'rendering' 
  | 'recognizing' 
  | 'completed' 
  | 'cancelled' 
  | 'error';

export interface OcrPageItem {
  id: string;
  pageNumber: number;
  fileName?: string;
  sourceType: 'image' | 'pdf_page';
  file?: File;
  previewUrl: string;
  rotation: number; // 0, 90, 180, 270
  status: OcrPageStatus;
  text: string;
  confidence: number;
  error?: string;
  isNativeText?: boolean;
  columnMode?: 'auto' | 'two_column' | 'single';
}

export interface OcrProgressInfo {
  status: OcrOverallStatus;
  currentPage: number;
  totalPages: number;
  percent: number;
  message: string;
}

export interface OcrSessionConfig {
  language: OcrLanguage;
  twoColumnSplit: boolean;
  skipNativeTextPages: boolean;
  enhanceContrast: boolean;
}

export const OCR_LIMITS = {
  MAX_FILES: 40,
  MAX_FILE_SIZE_BYTES: 25 * 1024 * 1024, // 25 MB
  MAX_TOTAL_BYTES: 100 * 1024 * 1024, // 100 MB
  MAX_PDF_PAGES: 50,
  MIN_PAGE_CHARS_WARNING: 30
} as const;

export interface QuestionContinuityReport {
  detectedQuestions: number[];
  missingQuestions: number[];
  duplicateQuestions: number[];
  hasIssues: boolean;
}

/**
 * Phân tích thứ tự và tính liên tục của các nhãn câu hỏi (Ví dụ: Câu 1, Câu 2...) trong văn bản OCR
 */
export function analyzeQuestionContinuity(text: string): QuestionContinuityReport {
  const matches = Array.from(text.matchAll(/(?:Câu|Cau)\s+(\d+)[:.]/gi));
  const numbers = matches.map(m => parseInt(m[1], 10)).filter(n => !isNaN(n));
  if (numbers.length === 0) {
    return { detectedQuestions: [], missingQuestions: [], duplicateQuestions: [], hasIssues: false };
  }

  const seen = new Set<number>();
  const duplicateSet = new Set<number>();
  for (const num of numbers) {
    if (seen.has(num)) {
      duplicateSet.add(num);
    } else {
      seen.add(num);
    }
  }

  const minNum = Math.min(...numbers);
  const maxNum = Math.max(...numbers);
  const missing: number[] = [];

  // Chỉ kiểm tra số câu thiếu nếu dãy bắt đầu hợp lý và trong phạm vi đề thi thông thường (<= 100)
  if (minNum <= 2 && maxNum <= 100) {
    for (let i = minNum; i <= maxNum; i++) {
      if (!seen.has(i)) {
        missing.push(i);
      }
    }
  }

  const duplicates = Array.from(duplicateSet).sort((a, b) => a - b);
  return {
    detectedQuestions: numbers,
    missingQuestions: missing,
    duplicateQuestions: duplicates,
    hasIssues: missing.length > 0 || duplicates.length > 0
  };
}
