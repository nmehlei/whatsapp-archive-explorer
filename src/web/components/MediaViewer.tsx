"use client";

import { useEffect } from "react";
import { formatDayDivider, formatTime } from "@/lib/format";
import { useUi } from "@/lib/ui";

export interface ViewerItem {
  id: number;
  ts: number;
  url: string;
  mime: string | null;
  caption: string | null;
}

interface Props {
  items: ViewerItem[];
  index: number;
  onIndex: (index: number) => void;
  onClose: () => void;
  onShowInChat?: (item: ViewerItem) => void;
}

export function MediaViewer({ items, index, onIndex, onClose, onShowInChat }: Props) {
  const { t, intl } = useUi();
  const item = items[index];

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
      if (e.key === "ArrowLeft" && index > 0) onIndex(index - 1);
      if (e.key === "ArrowRight" && index < items.length - 1) onIndex(index + 1);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [index, items.length, onClose, onIndex]);

  if (!item) return null;
  const isVideo = (item.mime ?? "").startsWith("video/");
  return (
    <div className="viewer" role="dialog" aria-modal="true" onClick={onClose}>
      <div className="viewer-bar" onClick={(e) => e.stopPropagation()}>
        <span>
          {formatDayDivider(item.ts, intl, t)} · {formatTime(item.ts, intl)}
        </span>
        <span className="viewer-actions">
          {onShowInChat && (
            <button type="button" className="link" onClick={() => onShowInChat(item)}>
              {t("open")}
            </button>
          )}
          <a href={item.url} download>
            {t("download")}
          </a>
          <button type="button" className="icon" onClick={onClose} aria-label={t("close")}>
            ✕
          </button>
        </span>
      </div>
      <div className="viewer-stage" onClick={(e) => e.stopPropagation()}>
        <button type="button" className="icon nav" disabled={index === 0} onClick={() => onIndex(index - 1)} aria-label={t("previous")}>
          ‹
        </button>
        {isVideo ? (
          <video src={item.url} controls autoPlay />
        ) : (
          <img src={item.url} alt={item.caption ?? t("photo")} />
        )}
        <button
          type="button"
          className="icon nav"
          disabled={index >= items.length - 1}
          onClick={() => onIndex(index + 1)}
          aria-label={t("next")}
        >
          ›
        </button>
      </div>
      {item.caption && <p className="viewer-caption">{item.caption}</p>}
    </div>
  );
}
