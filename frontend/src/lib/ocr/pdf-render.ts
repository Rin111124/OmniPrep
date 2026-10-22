/**
 * Module kết xuất trang PDF và trích xuất text-native trên trình duyệt bằng PDF.js
 * Hoạt động 100% Client-side.
 */

export interface PdfPageProxyLike {
  getViewport(params: { scale: number }): { width: number; height: number };
  getTextContent(): Promise<{ items: Array<{ str?: string }> }>;
  render(params: { canvasContext: CanvasRenderingContext2D; viewport: unknown }): { promise: Promise<void> };
}

export interface PdfDocumentProxyLike {
  numPages: number;
  getPage(pageNumber: number): Promise<PdfPageProxyLike>;
}

interface PdfJsModuleLike {
  version: string;
  GlobalWorkerOptions: {
    workerSrc?: string;
  };
  getDocument(src: { data: Uint8Array; cMapUrl?: string; cMapPacked?: boolean }): {
    promise: Promise<PdfDocumentProxyLike>;
  };
}

let cachedPdfJs: PdfJsModuleLike | null = null;

export async function loadPdfJs(): Promise<PdfJsModuleLike> {
  if (typeof window === 'undefined') {
    throw new Error('PDF.js chỉ có thể chạy trong môi trường trình duyệt client.');
  }

  if (cachedPdfJs) {
    return cachedPdfJs;
  }

  const pdfjs = (await import('pdfjs-dist')) as unknown as PdfJsModuleLike;
  
  if (pdfjs.GlobalWorkerOptions && !pdfjs.GlobalWorkerOptions.workerSrc) {
    // Pin worker script từ CDN tương ứng chính xác phiên bản pdfjs-dist
    pdfjs.GlobalWorkerOptions.workerSrc = `https://cdnjs.cloudflare.com/ajax/libs/pdf.js/${pdfjs.version}/pdf.worker.min.mjs`;
  }

  cachedPdfJs = pdfjs;
  return pdfjs;
}

export async function loadPdfDocument(file: File): Promise<PdfDocumentProxyLike> {
  const pdfjs = await loadPdfJs();
  const arrayBuffer = await file.arrayBuffer();
  
  const loadingTask = pdfjs.getDocument({
    data: new Uint8Array(arrayBuffer),
    cMapUrl: `https://cdnjs.cloudflare.com/ajax/libs/pdf.js/${pdfjs.version}/cmaps/`,
    cMapPacked: true
  });

  return await loadingTask.promise;
}

/**
 * Lấy số lượng trang của tài liệu PDF
 */
export function getPdfPageCount(pdfDoc: PdfDocumentProxyLike): number {
  return pdfDoc?.numPages || 0;
}

/**
 * Trích xuất text gốc (Native text layer) từ trang PDF nếu có sẵn
 */
export async function extractNativePageText(pdfDoc: PdfDocumentProxyLike, pageNumber: number): Promise<string> {
  try {
    const page = await pdfDoc.getPage(pageNumber);
    const textContent = await page.getTextContent();
    const strings = textContent.items.map(item => item.str || '');
    return strings.join(' ').replace(/\s+/g, ' ').trim();
  } catch {
    return '';
  }
}

/**
 * Render một trang PDF ra HTMLCanvasElement (tuần tự 1 trang một lần để tiết kiệm RAM)
 */
export async function renderPdfPageToCanvas(
  pdfDoc: PdfDocumentProxyLike,
  pageNumber: number,
  scale = 1.6
): Promise<HTMLCanvasElement> {
  const page = await pdfDoc.getPage(pageNumber);
  const viewport = page.getViewport({ scale });

  const canvas = document.createElement('canvas');
  canvas.width = Math.floor(viewport.width);
  canvas.height = Math.floor(viewport.height);

  const context = canvas.getContext('2d');
  if (!context) {
    throw new Error('Không thể khởi tạo Canvas 2D context.');
  }

  const renderContext = {
    canvasContext: context,
    viewport: viewport
  };

  await page.render(renderContext).promise;
  return canvas;
}
