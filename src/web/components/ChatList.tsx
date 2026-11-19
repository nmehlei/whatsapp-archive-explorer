"use client";

import { Virtuoso } from "react-virtuoso";
import { displayName, formatListDate } from "@/lib/format";
import { useUi } from "@/lib/ui";
import type { ChatSummary } from "@/lib/types";
import { Avatar } from "./Avatar";

interface Props {
  chats: ChatSummary[];
  selectedId: number | null;
  onSelect: (chat: ChatSummary) => void;
  /** Render all rows without virtualisation (short lists, tests). */
  plain?: boolean;
}

export function ChatRow({ chat, selected, onSelect }: { chat: ChatSummary; selected: boolean; onSelect: (c: ChatSummary) => void }) {
  const { t, intl } = useUi();
  return (
    <button type="button" className={`chat-row${selected ? " selected" : ""}`} onClick={() => onSelect(chat)} aria-current={selected || undefined}>
      <Avatar chat={chat} />
      <span className="chat-row-body">
        <span className="chat-row-top">
          <span className="chat-row-name">{chat.kind === "calls" ? t("calls") : displayName(chat)}</span>
          {chat.lastTs !== null && <span className="chat-row-date">{formatListDate(chat.lastTs, intl, t)}</span>}
        </span>
        <span className="chat-row-preview">{chat.lastPreview ?? ""}</span>
      </span>
    </button>
  );
}

export function ChatList({ chats, selectedId, onSelect, plain }: Props) {
  const { t } = useUi();
  if (chats.length === 0) {
    return <p className="empty">{t("noChats")}</p>;
  }
  if (plain) {
    return (
      <div className="chat-list">
        {chats.map((c) => (
          <ChatRow key={c.id} chat={c} selected={c.id === selectedId} onSelect={onSelect} />
        ))}
      </div>
    );
  }
  return (
    <Virtuoso
      className="chat-list"
      data={chats}
      computeItemKey={(_, c) => c.id}
      itemContent={(_, c) => <ChatRow chat={c} selected={c.id === selectedId} onSelect={onSelect} />}
    />
  );
}
