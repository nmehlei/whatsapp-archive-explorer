"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useUi } from "@/lib/ui";
import type { MediaItem } from "@/lib/types";
import { MediaViewer } from "./MediaViewer";

export function MediaGallery({ chatId, onClose, onShowInChat }: { chatId: number; onClose: () => void; onShowInChat: (id: number) => void }) {
  const { t } = useUi();
  const [items, setItems] = useState<MediaItem[]>([]);
  const [total, setTotal] = useState(0);
  const [viewer, setViewer] = useState<number | null>(null);

  useEffect(() => {
    const ctl = new AbortController();
    api.media(chatId, 0, 120, ctl.signal).then((r) => {
      setItems(r.items);
      setTotal(r.total);
    }, () => undefined);
    return () => ctl.abort();
  }, [chatId]);

  const more = () => api.media(chatId, items.length, 120).then((r) => setItems((prev) => [...prev, ...r.items]), () => undefined);
  const visual = items.filter((m) => /^(image|video)\//.test(m.mime ?? ""));
  const files = items.filter((m) => !/^(image|video)\//.test(m.mime ?? ""));

  return (
    <aside className="gallery">
      <header className="pane-header">
        <strong>{t("media")}</strong>
        <button type="button" className="icon" onClick={onClose} aria-label={t("close")}>
          ✕
        </button>
      </header>
      <div className="gallery-body">
        {items.length === 0 && <p className="empty">{t("noMedia")}</p>}
        <div className="gallery-grid">
          {visual.map((m, i) => (
            <button key={m.id} type="button" className="gallery-cell" onClick={() => setViewer(i)}>
              {(m.mime ?? "").startsWith("video/") ? <span className="gallery-video">▶</span> : <img src={m.url} alt="" loading="lazy" />}
            </button>
          ))}
        </div>
        {files.length > 0 && (
          <ul className="gallery-files">
            {files.map((m) => (
              <li key={m.id}>
                <a href={m.url} download>
                  {decodeURIComponent(m.url.split("/").pop() ?? "")}
                </a>{" "}
                <button type="button" className="link" onClick={() => onShowInChat(m.id)}>
                  {t("open")}
                </button>
              </li>
            ))}
          </ul>
        )}
        {items.length < total && (
          <button type="button" className="link load-more" onClick={more}>
            {t("loadMore")}
          </button>
        )}
      </div>
      {viewer !== null && (
        <MediaViewer
          items={visual}
          index={viewer}
          onIndex={setViewer}
          onClose={() => setViewer(null)}
          onShowInChat={(item) => {
            setViewer(null);
            onShowInChat(item.id);
          }}
        />
      )}
    </aside>
  );
}
