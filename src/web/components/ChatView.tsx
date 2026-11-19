"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Virtuoso, type VirtuosoHandle } from "react-virtuoso";
import { api } from "@/lib/api";
import { displayName, formatDayDivider, sameDay } from "@/lib/format";
import { useUi } from "@/lib/ui";
import type { ChatSummary, Message } from "@/lib/types";
import { Avatar } from "./Avatar";
import { MediaGallery } from "./MediaGallery";
import { MessageBubble } from "./MessageBubble";
import { MediaViewer, type ViewerItem } from "./MediaViewer";

const START_INDEX = 1_000_000;
const PAGE = 80;

interface Props {
  chat: ChatSummary;
  /** Open at this message (search hit, reply, deep link); null = newest. */
  targetId: number | null;
  /** Search terms to highlight when opened from a search. */
  terms: string[];
  onTarget: (messageId: number | null) => void;
  onBack: () => void;
}

export function ChatView({ chat, targetId, terms, onTarget, onBack }: Props) {
  const { t } = useUi();
  const virtuoso = useRef<VirtuosoHandle>(null);
  const [items, setItems] = useState<Message[]>([]);
  const [firstIndex, setFirstIndex] = useState(START_INDEX);
  const [initialIndex, setInitialIndex] = useState(0);
  const [moreBefore, setMoreBefore] = useState(false);
  const [moreAfter, setMoreAfter] = useState(false);
  const [loadKey, setLoadKey] = useState(0);
  const [highlight, setHighlight] = useState<number | null>(targetId);
  const loading = useRef(false);

  const [searchOpen, setSearchOpen] = useState(false);
  const [q, setQ] = useState("");
  const [hits, setHits] = useState<number[]>([]);
  const [hitIndex, setHitIndex] = useState(-1);
  const [galleryOpen, setGalleryOpen] = useState(false);
  const [viewer, setViewer] = useState<{ items: ViewerItem[]; index: number } | null>(null);

  const load = useCallback(
    async (cursor: { around?: number; aroundTs?: number }) => {
      loading.current = true;
      try {
        const page = await api.messages(chat.id, cursor, PAGE);
        const target = cursor.around ?? page.items.find((m) => cursor.aroundTs !== undefined && m.ts >= cursor.aroundTs)?.id;
        const index = target === undefined ? page.items.length - 1 : Math.max(0, page.items.findIndex((m) => m.id === target));
        setItems(page.items);
        setFirstIndex(START_INDEX);
        setInitialIndex(Math.max(0, index));
        setMoreBefore(page.hasMoreBefore);
        setMoreAfter(page.hasMoreAfter);
        setHighlight(target ?? null);
        setLoadKey((k) => k + 1);
      } finally {
        loading.current = false;
      }
    },
    [chat.id],
  );

  useEffect(() => {
    void load(targetId === null ? {} : { around: targetId });
  }, [load, targetId]);

  const jumpTo = useCallback(
    (messageId: number) => {
      const index = items.findIndex((m) => m.id === messageId);
      if (index >= 0) {
        setHighlight(messageId);
        virtuoso.current?.scrollToIndex({ index, align: "center", behavior: "auto" });
      } else {
        onTarget(messageId);
      }
    },
    [items, onTarget],
  );

  const older = useCallback(async () => {
    if (!moreBefore || loading.current || items.length === 0) return;
    loading.current = true;
    try {
      const page = await api.messages(chat.id, { before: items[0]!.id }, PAGE);
      setFirstIndex((f) => f - page.items.length);
      setItems((prev) => [...page.items, ...prev]);
      setMoreBefore(page.hasMoreBefore);
    } finally {
      loading.current = false;
    }
  }, [chat.id, items, moreBefore]);

  const newer = useCallback(async () => {
    if (!moreAfter || loading.current || items.length === 0) return;
    loading.current = true;
    try {
      const page = await api.messages(chat.id, { after: items[items.length - 1]!.id }, PAGE);
      setItems((prev) => [...prev, ...page.items]);
      setMoreAfter(page.hasMoreAfter);
    } finally {
      loading.current = false;
    }
  }, [chat.id, items, moreAfter]);

  const runSearch = async () => {
    const ids = q.trim() ? await api.searchInChat(chat.id, q) : [];
    setHits(ids);
    const last = ids.length - 1;
    setHitIndex(last);
    if (last >= 0) jumpTo(ids[last]!);
  };

  const step = (delta: number) => {
    const next = hitIndex + delta;
    if (next < 0 || next >= hits.length) return;
    setHitIndex(next);
    jumpTo(hits[next]!);
  };

  const openMedia = (message: Message) => {
    const visual = items.filter((m) => m.mediaUrl && /^(image|video)\//.test(m.mime ?? ""));
    const list: ViewerItem[] = visual.map((m) => ({ id: m.id, ts: m.ts, url: m.mediaUrl!, mime: m.mime, caption: m.caption }));
    setViewer({ items: list, index: Math.max(0, list.findIndex((i) => i.id === message.id)) });
  };

  const searchTerms = useMemo(() => [...terms, ...(hits.length > 0 ? q.split(/\s+/) : [])], [terms, hits.length, q]);
  const isGroup = chat.kind === "group";

  return (
    <section className="chat">
      <header className="pane-header chat-header">
        <button type="button" className="icon back" onClick={onBack} aria-label={t("backToList")}>
          ‹
        </button>
        <Avatar chat={chat} size={40} />
        <span className="chat-title">
          <strong>{chat.kind === "calls" ? t("calls") : displayName(chat)}</strong>
          <span className="muted">
            {isGroup ? `${t("groupChat")} · ` : ""}
            {chat.kind === "private" && /^\d+$/.test(chat.jid.split("@")[0] ?? "") ? `+${chat.jid.split("@")[0]} · ` : ""}
            {t("messageCount", { count: chat.messageCount })}
          </span>
        </span>
        <span className="chat-actions">
          <label className="date-jump" title={t("jumpToDate")}>
            <span aria-hidden>📅</span>
            <input
              type="date"
              aria-label={t("jumpToDate")}
              onChange={(e) => {
                // the picker's value is a local calendar day; valueAsDate would be UTC midnight
                const [y, m, d] = e.target.value.split("-").map(Number);
                if (y && m && d) void load({ aroundTs: Math.floor(new Date(y, m - 1, d).getTime() / 1000) });
              }}
            />
          </label>
          <button type="button" className="icon" onClick={() => setGalleryOpen((v) => !v)} aria-label={t("media")} title={t("media")}>
            🖼
          </button>
          <button type="button" className="icon" onClick={() => setSearchOpen((v) => !v)} aria-label={t("searchInChat")} title={t("searchInChat")}>
            🔍
          </button>
        </span>
      </header>

      {searchOpen && (
        <form
          className="in-chat-search"
          onSubmit={(e) => {
            e.preventDefault();
            void runSearch();
          }}
        >
          <input autoFocus type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("searchInChat")} aria-label={t("searchInChat")} />
          <span className="muted hit-count">
            {hits.length === 0 ? (q && hitIndex === -1 ? t("noHits") : "") : t("hitCount", { current: hitIndex + 1, total: hits.length })}
          </span>
          <button type="button" className="icon" onClick={() => step(-1)} disabled={hitIndex <= 0} aria-label={t("previous")}>
            ▲
          </button>
          <button type="button" className="icon" onClick={() => step(1)} disabled={hitIndex < 0 || hitIndex >= hits.length - 1} aria-label={t("next")}>
            ▼
          </button>
        </form>
      )}

      <div className="chat-body">
        {items.length === 0 ? (
          <p className="empty">{t("noMessages")}</p>
        ) : (
          <Virtuoso
            key={loadKey}
            ref={virtuoso}
            className="messages"
            data={items}
            firstItemIndex={firstIndex}
            initialTopMostItemIndex={{ index: initialIndex, align: highlight === null ? "end" : "center" }}
            startReached={() => void older()}
            endReached={() => void newer()}
            computeItemKey={(_, m) => m.id}
            increaseViewportBy={{ top: 600, bottom: 600 }}
            itemContent={(index, m) => {
              const prev = items[index - firstIndex - 1];
              return (
                <>
                  {(!prev || !sameDay(prev.ts, m.ts)) && <DayDivider ts={m.ts} />}
                  <MessageBubble
                    message={m}
                    isGroup={isGroup}
                    isCalls={chat.kind === "calls"}
                    highlighted={m.id === highlight}
                    terms={searchTerms}
                    onOpenMedia={openMedia}
                    onJumpTo={jumpTo}
                  />
                </>
              );
            }}
          />
        )}
      </div>

      {galleryOpen && (
        <MediaGallery
          chatId={chat.id}
          onClose={() => setGalleryOpen(false)}
          onShowInChat={(id) => {
            setGalleryOpen(false);
            jumpTo(id);
          }}
        />
      )}
      {viewer && (
        <MediaViewer
          items={viewer.items}
          index={viewer.index}
          onIndex={(index) => setViewer({ ...viewer, index })}
          onClose={() => setViewer(null)}
        />
      )}
    </section>
  );
}

function DayDivider({ ts }: { ts: number }) {
  const { t, intl } = useUi();
  return (
    <div className="day-divider">
      <span>{formatDayDivider(ts, intl, t)}</span>
    </div>
  );
}
