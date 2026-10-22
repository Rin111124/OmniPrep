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
