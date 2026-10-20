'use client';

import React, { useState } from 'react';
import { ScoreHistoryItem } from '@/types';
import { TrendingUp } from 'lucide-react';

interface ScoreHistoryChartProps {
  history: ScoreHistoryItem[];
  targetScore?: number;
}

export const ScoreHistoryChart: React.FC<ScoreHistoryChartProps> = ({
  history,
  targetScore = 9.0
}) => {
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null);

  if (!history || history.length === 0) {
    return (
      <div className="p-8 text-center text-xs text-slate-400">
        Chưa có dữ liệu bài thi. Hãy làm bài thi đầu tiên để ghi nhận biểu đồ tiến bộ!
      </div>
    );
  }

  // Width and height of SVG viewport
  const width = 640;
  const height = 220;
  const paddingX = 45;
  const paddingTop = 25;
  const paddingBottom = 40;

  const chartW = width - paddingX * 2;
  const chartH = height - paddingTop - paddingBottom;

  const minScore = 4.0;
  const maxScore = 10.0;

  const getX = (idx: number) => paddingX + (idx / Math.max(1, history.length - 1)) * chartW;
  const getY = (val: number) => paddingTop + chartH - ((val - minScore) / (maxScore - minScore)) * chartH;

  // Build SVG path
  const points = history.map((item, idx) => ({
    x: getX(idx),
    y: getY(item.score),
    ...item
  }));

  const pathD = points.reduce((acc, p, i) => {
    return i === 0 ? `M ${p.x} ${p.y}` : `${acc} L ${p.x} ${p.y}`;
  }, '');

  // Gradient area path
  const areaD = `${pathD} L ${points[points.length - 1].x} ${paddingTop + chartH} L ${points[0].x} ${paddingTop + chartH} Z`;

  const targetY = getY(targetScore);

  return (
    <div className="glass-panel rounded-2xl sm:rounded-3xl p-4 sm:p-7 border border-white/10 space-y-4 relative overflow-hidden">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-2xl bg-indigo-500/20 border border-indigo-500/30 flex items-center justify-center text-indigo-400 shrink-0">
            <TrendingUp className="w-5 h-5" />
          </div>
          <div>
            <h3 className="text-sm sm:text-base font-bold text-white flex items-center gap-2">
              Biểu Đồ Tiến Bộ Điểm Số
              <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
                10 bài gần nhất
              </span>
            </h3>
            <p className="text-xs text-slate-400">
              Mục tiêu điểm số: <strong className="text-amber-400">{targetScore.toFixed(1)}+</strong>
            </p>
          </div>
        </div>

        {/* Legend */}
        <div className="flex flex-wrap items-center gap-3 sm:gap-4 text-xs">
          <div className="flex items-center gap-1.5 text-slate-300 font-medium">
            <span className="w-3 h-0.5 bg-indigo-400 inline-block" />
            <span>Điểm đạt</span>
          </div>
          <div className="flex items-center gap-1.5 text-amber-400 font-medium">
            <span className="w-3 h-0.5 border-t border-dashed border-amber-400 inline-block" />
            <span>Mục tiêu ({targetScore.toFixed(1)})</span>
          </div>
        </div>
      </div>

      {/* SVG Chart */}
      <div className="relative w-full overflow-x-auto pb-1">
        <svg
          viewBox={`0 0 ${width} ${height}`}
          className="w-full h-auto min-w-[380px] sm:min-w-full touch-pan-x"
        >
          <defs>
            <linearGradient id="scoreAreaGradient" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#6366f1" stopOpacity="0.35" />
              <stop offset="100%" stopColor="#6366f1" stopOpacity="0.0" />
            </linearGradient>
            <linearGradient id="scoreLineGradient" x1="0" y1="0" x2="1" y2="0">
              <stop offset="0%" stopColor="#818cf8" />
              <stop offset="50%" stopColor="#6366f1" />
              <stop offset="100%" stopColor="#06b6d4" />
            </linearGradient>
          </defs>

          {/* Grid lines (horizontal) */}
          {[5, 6, 7, 8, 9, 10].map((score) => {
            const y = getY(score);
            return (
              <g key={score}>
                <line
                  x1={paddingX}
                  y1={y}
                  x2={width - paddingX}
                  y2={y}
                  stroke="rgba(255,255,255,0.08)"
                  strokeDasharray="3 3"
                />
                <text
                  x={paddingX - 10}
                  y={y + 3}
                  textAnchor="end"
                  fill="#64748b"
                  fontSize="10"
                  fontFamily="monospace"
                >
                  {score.toFixed(0)}đ
                </text>
              </g>
            );
          })}

          {/* Target Score Dash Line */}
          <line
            x1={paddingX}
            y1={targetY}
            x2={width - paddingX}
            y2={targetY}
            stroke="#f59e0b"
            strokeDasharray="5 5"
            strokeWidth="1.5"
            opacity="0.8"
          />

          {/* Area Fill */}
          <path d={areaD} fill="url(#scoreAreaGradient)" />

          {/* Main Line */}
          <path
            d={pathD}
            fill="none"
            stroke="url(#scoreLineGradient)"
            strokeWidth="3"
            strokeLinecap="round"
            strokeLinejoin="round"
          />

          {/* Points & Interactive Tooltips */}
          {points.map((p, i) => {
            const isHovered = hoveredIdx === i;
            const isHighScore = p.score >= 9.0;

            return (
              <g
                key={i}
                onMouseEnter={() => setHoveredIdx(i)}
                onMouseLeave={() => setHoveredIdx(null)}
                onClick={() => setHoveredIdx(prev => prev === i ? null : i)}
                className="cursor-pointer touch-manipulation"
              >
                {/* Invisible large touch target for mobile tapping */}
                <circle cx={p.x} cy={p.y} r={16} fill="transparent" />

                {/* Outer halo */}
                <circle
                  cx={p.x}
                  cy={p.y}
                  r={isHovered ? 8 : 5}
                  fill={isHighScore ? '#10b981' : '#6366f1'}
                  opacity={isHovered ? 0.9 : 0.8}
                  className="transition-all duration-200"
                />
                {/* Center dot */}
                <circle cx={p.x} cy={p.y} r={isHovered ? 4 : 2.5} fill="#ffffff" />

                {/* X Axis Label */}
                <text
                  x={p.x}
                  y={height - 12}
                  textAnchor="middle"
                  fill={isHovered ? '#ffffff' : '#94a3b8'}
                  fontSize="10"
                  fontWeight={isHovered ? 'bold' : 'normal'}
                >
                  {p.date}
                </text>
              </g>
            );
          })}
        </svg>

        {/* Floating Tooltip Box when point hovered or tapped */}
        {hoveredIdx !== null && points[hoveredIdx] && (
          <div
            className="absolute z-20 pointer-events-none -translate-x-1/2 bg-slate-900/95 border border-white/15 p-3 rounded-xl shadow-xl text-xs space-y-1 backdrop-blur-md transition-all"
            style={{
              left: `${Math.min(85, Math.max(15, (points[hoveredIdx].x / width) * 100))}%`,
              top: `${Math.max(10, points[hoveredIdx].y - 75)}px`
            }}
          >
            <div className="font-bold text-white truncate max-w-[200px]">
              {points[hoveredIdx].examTitle}
            </div>
            <div className="flex items-center gap-2">
              <span className="font-mono text-base font-black text-cyan-300">
                {points[hoveredIdx].score.toFixed(2)}đ
              </span>
              <span className="text-[10px] text-slate-400">
                ({points[hoveredIdx].durationMinutes} phút)
              </span>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
