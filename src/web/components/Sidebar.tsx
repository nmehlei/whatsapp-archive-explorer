"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { formatListDate, snippetParts } from "@/lib/format";
import { useUi } from "@/lib/ui";
import type { ChatSummary, SearchFilters, SearchHit } from "@/lib/types";
import { ChatList, ChatRow } from "./ChatList";

interface Props {
  chats: ChatSummary[];
  hiddenCount: number;
  showHidden: boolean;
  onShowHidden: (show: boolean) => void;
  selectedId: number | null;
  onSelectChat: (chat: ChatSummary) => void;
  onOpenHit: (hit: SearchHit, query: string) => void;
}

function toTs(value: string, endOfDay: boolean): number | undefined {
  if (!value) return undefined;
  const d = new Date(`${value}T${endOfDay ? "23:59:59" : "00:00:00"}`);
  return Number.isNaN(d.getTime()) ? undefined : Math.floor(d.getTime() / 1000);
}

/** WhatsApp-style search: the box filters the chat list and finds messages across all chats. */
export function Sidebar({ chats, hiddenCount, showHidden, onShowHidden, selectedId, onSelectChat, onOpenHit }: Props) {
  const { t, intl } = useUi();
  const [q, setQ] = useState("");
  const [showFilters, setShowFilters] = useState(false);
  const [from, setFrom] = useState<"any" | "me" | "others">("any");
  const [onlyMedia, setOnlyMedia] = useState(false);
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [chatHits, setChatHits] = useState<ChatSummary[]>([]);
  const [hits, setHits] = useState<SearchHit[]>([]);
  const [total, setTotal] = useState(0);

  const query = q.trim();
  const filters: SearchFilters = {
    fromMe: from === "any" ? undefined : from === "me",
    media: onlyMedia || undefined,
    from: toTs(dateFrom, false),
    to: toTs(dateTo, true),
  };
  const filterKey = JSON.stringify(filters);

  useEffect(() => {
    if (!query) return;
    const ctl = new AbortController();
    const timer = setTimeout(() => {
      api.chats(query, showHidden, ctl.signal).then(setChatHits, () => undefined);
      api.search(query, JSON.parse(filterKey) as SearchFilters, 0, ctl.signal).then((r) => {
        setHits(r.hits);
        setTotal(r.total);
      }, () => undefined);
    }, 250);
    return () => {
      clearTimeout(timer);
      ctl.abort();
    };
  }, [query, filterKey, showHidden]);

  const more = () =>
    api.search(query, filters, hits.length).then((r) => setHits((prev) => [...prev, ...r.hits]), () => undefined);

  return (
    <aside className="sidebar">
      <header className="pane-header">
        <strong>{t("appName")}</strong>
      </header>
      <div className="search-box">
        <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("searchPlaceholder")} aria-label={t("searchPlaceholder")} />
        <button type="button" className={`icon${showFilters ? " active" : ""}`} onClick={() => setShowFilters((v) => !v)} aria-label={t("filters")} title={t("filters")}>
          ⚙
        </button>
      </div>
      {showFilters && (
        <div className="filters">
          <select value={from} onChange={(e) => setFrom(e.target.value as typeof from)} aria-label={t("filters")}>
            <option value="any">{t("fromAnyone")}</option>
            <option value="me">{t("fromMe")}</option>
            <option value="others">{t("fromOthers")}</option>
          </select>
          <label>
            <input type="checkbox" checked={onlyMedia} onChange={(e) => setOnlyMedia(e.target.checked)} /> {t("onlyMedia")}
          </label>
          <label>
            {t("dateFrom")} <input type="date" value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} />
          </label>
          <label>
            {t("dateTo")} <input type="date" value={dateTo} onChange={(e) => setDateTo(e.target.value)} />
          </label>
          {hiddenCount > 0 && (
            <label>
              <input type="checkbox" checked={showHidden} onChange={(e) => onShowHidden(e.target.checked)} />{" "}
              {t("showHidden", { count: hiddenCount })}
            </label>
          )}
        </div>
      )}

      {query && !hits.length && !chatHits.length && total === 0 ? (
        <p className="empty">{t("noMessages")}</p>
      ) : query ? (
        <div className="search-results">
          {chatHits.length > 0 && <h2 className="section">{t("chats")}</h2>}
          {chatHits.map((c) => (
            <ChatRow key={c.id} chat={c} selected={c.id === selectedId} onSelect={onSelectChat} />
          ))}
          <h2 className="section">
            {t("messages")} {total > 0 && <span className="muted">({total})</span>}
          </h2>
          {hits.length === 0 && <p className="empty">{t("noMessages")}</p>}
          {hits.map((hit) => (
            <button type="button" key={hit.messageId} className="hit" onClick={() => onOpenHit(hit, query)}>
              <span className="chat-row-top">
                <span className="chat-row-name">{hit.chatName ?? "…"}</span>
                <span className="chat-row-date">{formatListDate(hit.ts, intl, t)}</span>
              </span>
              <span className="hit-snippet">
                {hit.sender && <span className="muted">{hit.sender}: </span>}
                {snippetParts(hit.snippet).map((p, i) => (p.hit ? <mark key={i}>{p.text}</mark> : <span key={i}>{p.text}</span>))}
              </span>
            </button>
          ))}
          {hits.length < total && (
            <button type="button" className="link load-more" onClick={more}>
              {t("loadMore")}
            </button>
          )}
        </div>
      ) : (
        <ChatList chats={chats} selectedId={selectedId} onSelect={onSelectChat} />
      )}
    </aside>
  );
}
