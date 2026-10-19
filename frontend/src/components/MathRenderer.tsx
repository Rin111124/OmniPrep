'use client';

import React from 'react';
import katex from 'katex';

interface MathRendererProps {
  content: string;
  className?: string;
}

/**
 * MathRenderer: Tự động phát hiện và render công thức LaTeX ($...$ hoặc $$...$$)
 * bằng KaTeX chuẩn Bộ GD&ĐT 2025. Nếu là văn bản thường thì hiển thị bình thường.
 */
export const MathRenderer: React.FC<MathRendererProps> = ({ content, className = '' }) => {
  if (!content) return null;

  // Split by $$...$$ (display mode) or $...$ (inline mode)
  const regex = /(\$\$[\s\S]+?\$\$|\$[^\$]+?\$)/g;
  const parts = content.split(regex);

  return (
    <span className={className}>
      {parts.map((part, index) => {
        if (!part) return null;

        if (part.startsWith('$$') && part.endsWith('$$')) {
          const math = part.slice(2, -2);
          try {
            const html = katex.renderToString(math, {
              displayMode: true,
              throwOnError: false,
            });
            return (
              <span
                key={index}
                className="my-2 block overflow-x-auto py-1"
                dangerouslySetInnerHTML={{ __html: html }}
              />
            );
          } catch {
            return <code key={index} className="text-amber-300 font-mono">{part}</code>;
          }
        }

        if (part.startsWith('$') && part.endsWith('$')) {
          const math = part.slice(1, -1);
          try {
            const html = katex.renderToString(math, {
              displayMode: false,
              throwOnError: false,
            });
            return (
              <span
                key={index}
                className="inline-block mx-0.5"
                dangerouslySetInnerHTML={{ __html: html }}
              />
            );
          } catch {
            return <code key={index} className="text-amber-300 font-mono">{part}</code>;
          }
        }

        return <span key={index}>{part}</span>;
      })}
    </span>
  );
};
