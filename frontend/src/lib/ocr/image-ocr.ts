import { createWorker, Worker } from 'tesseract.js';
import { OcrLanguage } from './types';

export interface WorkerInitOptions {
  language: OcrLanguage;
  onProgress?: (progress: number, statusText: string) => void;
}

export interface RecognizeResult {
  text: string;
  confidence: number;
}

/**
 * Khởi tạo một Tesseract Web Worker duy nhất để dùng lại tuần tự cho toàn bộ tài liệu.
 */
export async function initOcrWorker(options: WorkerInitOptions): Promise<Worker> {
  const { language, onProgress } = options;

  const langCode = language === 'vie+eng' ? 'vie+eng' : 'vie';

  let timer: NodeJS.Timeout | null = null;
  const timeoutPromise = new Promise<never>((_, reject) => {
    timer = setTimeout(() => {
      reject(new Error('Tải mô hình Tesseract WebAssembly / traineddata quá thời gian chờ (45 giây). Vui lòng kiểm tra lại kết nối mạng hoặc thử lại.'));
    }, 45000);
  });

  try {
    const workerPromise = createWorker(langCode, 1, {
      logger: (message) => {
        if (onProgress && typeof message.progress === 'number') {
          const pct = Math.round(message.progress * 100);
          const status = message.status || 'Đang xử lý';
          onProgress(pct, status);
        }
      }
    });

    const worker = await Promise.race([workerPromise, timeoutPromise]);
    return worker;
  } finally {
    if (timer) clearTimeout(timer);
  }
}

/**
 * Khử nét bút bi đỏ khoanh tròn đáp án hoặc ghi chú trên đề thi
 */
export function suppressRedAnnotations(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  try {
    const imgData = ctx.getImageData(0, 0, width, height);
    const data = imgData.data;
    for (let i = 0; i < data.length; i += 4) {
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];
      const maxGb = Math.max(g, b);
      // Nét mực bút bi đỏ: R > 115, R vượt trội so với max(G,B) >= 1.25 và độ chênh lệch > 25
      if (r > 115 && r > maxGb * 1.25 && (r - maxGb) > 25) {
        data[i] = 255;
        data[i + 1] = 255;
        data[i + 2] = 255;
      }
    }
    ctx.putImageData(imgData, 0, 0);
  } catch (err) {
    console.warn('Bỏ qua khử mực đỏ do giới hạn ngữ cảnh canvas:', err);
  }
}

/**
 * Quét dải trung tâm (Vertical Gutter) để kiểm tra xem có phải bố cục 2 cột độc lập hay không.
 * Trả về false nếu phát hiện văn bản trải rộng qua dải trung tâm (thân câu hỏi toàn trang).
 */
export function detectTwoColumnLayout(canvas: HTMLCanvasElement): boolean {
  try {
    const ctx = canvas.getContext('2d');
    if (!ctx || canvas.width < 600 || canvas.height < 400) return false;

    const width = canvas.width;
    const height = canvas.height;
    const midX = Math.floor(width / 2);
    const gutterWidth = Math.max(12, Math.floor(width * 0.04));
    const startX = midX - Math.floor(gutterWidth / 2);

    const imgData = ctx.getImageData(startX, 0, gutterWidth, height);
    const data = imgData.data;

    let darkPixelsInCenter = 0;
    const totalSamplePixels = gutterWidth * height;

    for (let i = 0; i < data.length; i += 4) {
      const r = data[i], g = data[i + 1], b = data[i + 2];
      if (r < 110 && g < 110 && b < 110) {
        darkPixelsInCenter++;
      }
    }

    const darkRatio = darkPixelsInCenter / totalSamplePixels;
    // Nếu có > 1.2% điểm ảnh tối ở giữa -> có chữ chạy qua -> là đề 1 cột
    return darkRatio < 0.012;
  } catch {
    return false;
  }
}

/**
 * Xoay hoặc tiền xử lý canvas/ảnh trước khi OCR (nếu cần xoay, khử bóng tối hoặc khử mực đỏ)
 */
export function prepareCanvasForOcr(
  source: HTMLCanvasElement | HTMLImageElement,
  rotation = 0,
  enhanceContrast = false,
  suppressRedInk = false
): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  const isRotated90or270 = rotation % 180 !== 0;

  const sourceWidth = 'videoWidth' in source ? 0 : source.width;
  const sourceHeight = 'videoHeight' in source ? 0 : source.height;

  canvas.width = isRotated90or270 ? sourceHeight : sourceWidth;
  canvas.height = isRotated90or270 ? sourceWidth : sourceHeight;

  const ctx = canvas.getContext('2d');
  if (!ctx) return canvas;

  if (enhanceContrast) {
    ctx.filter = 'contrast(1.25) brightness(1.05)';
  }

  ctx.save();
  ctx.translate(canvas.width / 2, canvas.height / 2);
  ctx.rotate((rotation * Math.PI) / 180);
  ctx.drawImage(source, -sourceWidth / 2, -sourceHeight / 2);
  ctx.restore();

  if (enhanceContrast) {
    ctx.filter = 'none';
  }

  // Khử nét bút bi đỏ nếu được bật
  if (suppressRedInk && canvas.width > 0 && canvas.height > 0) {
    suppressRedAnnotations(ctx, canvas.width, canvas.height);
  }

  return canvas;
}

/**
 * Cắt canvas thành 2 cột (trái và phải) để OCR theo đúng thứ tự đọc đề thi 2 cột
 */
function splitCanvasTwoColumns(sourceCanvas: HTMLCanvasElement): [HTMLCanvasElement, HTMLCanvasElement] {
  const width = sourceCanvas.width;
  const height = sourceCanvas.height;
  const midX = Math.floor(width / 2);

  // Cột trái
  const leftCanvas = document.createElement('canvas');
  leftCanvas.width = midX;
  leftCanvas.height = height;
  const leftCtx = leftCanvas.getContext('2d');
  if (leftCtx) {
    leftCtx.drawImage(sourceCanvas, 0, 0, midX, height, 0, 0, midX, height);
  }

  // Cột phải
  const rightCanvas = document.createElement('canvas');
  rightCanvas.width = width - midX;
  rightCanvas.height = height;
  const rightCtx = rightCanvas.getContext('2d');
  if (rightCtx) {
    rightCtx.drawImage(sourceCanvas, midX, 0, width - midX, height, 0, 0, width - midX, height);
  }

  return [leftCanvas, rightCanvas];
}

/**
 * Dọn dẹp bộ nhớ canvas sau khi kết thúc OCR (giải phóng GPU / RAM)
 */
export function releaseCanvas(canvas?: HTMLCanvasElement | null) {
  if (!canvas) return;
  canvas.width = 0;
  canvas.height = 0;
}

/**
 * Thực hiện nhận dạng văn bản trên một canvas hoặc hình ảnh
 */
export async function recognizePage(
  worker: Worker,
  canvas: HTMLCanvasElement,
  twoColumnSplit = false
): Promise<RecognizeResult> {
  if (twoColumnSplit && canvas.width > 600) {
    // Kiểm tra an toàn: nếu phát hiện chữ chạy ngang giữa trang, ưu tiên OCR toàn trang để tránh xé nát câu hỏi
    const hasGutter = detectTwoColumnLayout(canvas);
    if (hasGutter) {
      const [leftCanvas, rightCanvas] = splitCanvasTwoColumns(canvas);
      try {
        const leftRes = await worker.recognize(leftCanvas);
        const rightRes = await worker.recognize(rightCanvas);

        const combinedText = `${leftRes.data.text.trim()}\n\n${rightRes.data.text.trim()}`.trim();
        const avgConfidence = Math.round((leftRes.data.confidence + rightRes.data.confidence) / 2);

        return {
          text: combinedText,
          confidence: avgConfidence
        };
      } finally {
        releaseCanvas(leftCanvas);
        releaseCanvas(rightCanvas);
      }
    }
  }

  const res = await worker.recognize(canvas);
  return {
    text: res.data.text.trim(),
    confidence: Math.round(res.data.confidence)
  };
}

/**
 * Tải file ảnh thành HTMLImageElement để xử lý trên canvas
 */
export function loadImageFromFile(file: File): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file);
    const img = new Image();
    img.onload = () => {
      URL.revokeObjectURL(url);
      resolve(img);
    };
    img.onerror = (err) => {
      URL.revokeObjectURL(url);
      reject(new Error(`Không thể nạp file ảnh ${file.name}: ${err}`));
    };
    img.src = url;
  });
}
