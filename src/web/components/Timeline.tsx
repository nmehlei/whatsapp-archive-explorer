"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { bucketAt, labelSteps, type Timeline as TimelineData } from "@/lib/timeline";
import { useUi } from "@/lib/ui";

interface Props {
  timeline: TimelineData;
  /** Timestamp of the message at the top of the view; the marker follows it. */
  currentTs: number | null;
  /** Timestamps of the in-chat search hits, shown as dots. */
  hits: number[];
  /** Jump to this moment (the start of a month or day); settles once the chat has moved (or failed to). */
  onJump: (ts: number) => Promise<void> | void;
}

/** Bars are drawn on a fixed 0…100 height; a square-root scale keeps quiet months visible next to busy ones. */
const BAR_AREA = 100;
const MIN_BAR = 6;

export function Timeline({ timeline, currentTs, hits, onJump }: Props) {
  const { t, intl } = useUi();
  const strip = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(600);
  const [hover, setHover] = useState<number | null>(null);
  const [drag, setDrag] = useState<number | null>(null);
  /** Where a key press jumped to, until that jump settles (repeated presses keep moving from there). */
  const [pending, setPending] = useState<number | null>(null);
  const { buckets, granularity, max } = timeline;
  const n = buckets.length;

  useEffect(() => {
    const el = strip.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(([entry]) => entry && setWidth(entry.contentRect.width));
    observer.observe(el);
    return () => observer.disconnect();
  }, [n]);

  const dateFormat = useMemo(
    () =>
      new Intl.DateTimeFormat(
        intl,
        granularity === "month" ? { month: "long", year: "numeric" } : { day: "numeric", month: "long", year: "numeric" },
      ),
    [intl, granularity],
  );
  const labels = useMemo(() => labelSteps(timeline, width, intl), [timeline, width, intl]);
  const hitBuckets = useMemo(
    () => [...new Set(hits.map((ts) => bucketAt(timeline, ts)).filter((i): i is number => i !== null))],
    [hits, timeline],
  );

  if (n < 2) return null;

  const current = drag ?? pending ?? (currentTs === null ? n - 1 : bucketAt(timeline, currentTs)!);
  const shown = hover ?? drag;
  const describe = (i: number) => {
    const b = buckets[i]!;
    const count =
      b.count === 0 ? t("timelineNone") : b.count === 1 ? t("timelineOne") : t("timelineCount", { count: b.count.toLocaleString(intl) });
    return { date: dateFormat.format(new Date(b.year, b.month, b.day)), count };
  };
  const indexAt = (clientX: number) => {
    const rect = strip.current!.getBoundingClientRect();
    if (rect.width <= 0) return 0;
    return Math.min(n - 1, Math.max(0, Math.floor(((clientX - rect.left) / rect.width) * n)));
  };
  const jump = (i: number) => onJump(buckets[i]!.start);
  const pct = (i: number) => ((i + 0.5) / n) * 100;
  const now = describe(current);
  const tip = shown === null ? null : describe(shown);

  return (
    <div className="timeline-wrap">
      <div
        ref={strip}
        className={`timeline${drag !== null ? " dragging" : ""}`}
        role="slider"
        tabIndex={0}
        aria-label={t("timeline")}
        aria-valuemin={0}
        aria-valuemax={n - 1}
        aria-valuenow={current}
        aria-valuetext={`${now.date}, ${now.count}`}
        onPointerMove={(e) => {
          const i = indexAt(e.clientX);
          setHover(i);
          if (drag !== null) setDrag(i);
        }}
        onPointerLeave={() => setHover(null)}
        onPointerDown={(e) => {
          if (e.button !== 0) return; // the context menu or a middle click is not a jump
          e.currentTarget.setPointerCapture?.(e.pointerId);
          setDrag(indexAt(e.clientX));
        }}
        onPointerUp={(e) => {
          if (drag === null) return;
          const i = indexAt(e.clientX);
          setDrag(null);
          jump(i);
        }}
        onPointerCancel={() => setDrag(null)}
        onLostPointerCapture={() => setDrag(null)}
        onKeyDown={(e) => {
          // arrows step to the previous/next bucket that has messages: an empty one would land on the next busy one
          const busy = (from: number, step: number) => {
            for (let i = from + step; i >= 0 && i < n; i += step) if (buckets[i]!.count > 0) return i;
            return from;
          };
          const target =
            e.key === "ArrowLeft"
              ? busy(current, -1)
              : e.key === "ArrowRight"
                ? busy(current, 1)
                : e.key === "Home"
                  ? 0
                  : e.key === "End"
                    ? n - 1
                    : null;
          if (target === null) return;
          e.preventDefault();
          setPending(target);
          const settle = () => setPending((p) => (p === target ? null : p));
          Promise.resolve(jump(target)).then(settle, settle);
        }}
      >
        <svg className="timeline-bars" viewBox={`0 0 ${n} ${BAR_AREA}`} preserveAspectRatio="none" aria-hidden>
          {labels.map((l) => (
            <line key={`y${l.index}`} className="timeline-tick" x1={l.index} x2={l.index} y1={0} y2={BAR_AREA} />
          ))}
          {buckets.map((b, i) => {
            const h = b.count === 0 ? 0 : MIN_BAR + (BAR_AREA - MIN_BAR) * Math.sqrt(b.count / max);
            return (
              <rect
                key={i}
                className={`timeline-bar${i === current ? " current" : ""}${i === shown ? " hover" : ""}${b.count === 0 ? " empty" : ""}`}
                x={i + 0.12}
                width={0.76}
                y={BAR_AREA - Math.max(h, 2)}
                height={Math.max(h, 2)}
              />
            );
          })}
        </svg>
        {hitBuckets.map((i) => (
          <span key={`h${i}`} className="timeline-hit" style={{ left: `${pct(i)}%` }} aria-hidden />
        ))}
        <span className="timeline-marker" style={{ left: `${pct(current)}%` }} aria-hidden />
        <div className="timeline-labels" aria-hidden>
          {labels.map((l) => (
            <span key={l.index} className="timeline-label" style={{ left: `${(l.index / n) * 100}%` }}>
              {l.text}
            </span>
          ))}
        </div>
        {tip && shown !== null && (
          <div className="timeline-tip" role="tooltip" style={{ left: `clamp(64px, ${pct(shown)}%, calc(100% - 64px))` }}>
            <strong>{tip.date}</strong>
            <span>{tip.count}</span>
          </div>
        )}
      </div>
    </div>
  );
}
