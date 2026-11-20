export type ChatKind = "private" | "group" | "calls" | "other";

export interface ChatSummary {
  id: number;
  jid: string;
  name: string | null;
  kind: ChatKind;
  hasAvatar: boolean;
  lastTs: number | null;
  lastPreview: string | null;
  messageCount: number;
  hidden: boolean;
}

export interface Message {
  id: number;
  messageId: string;
  keyId: string | null;
  ts: number;
  fromMe: boolean;
  sender: string | null;
  isSystem: boolean;
  text: string | null;
  caption: string | null;
  html: string | null;
  isMedia: boolean;
  mediaUrl: string | null;
  mime: string | null;
  replyKeyId: string | null;
  quotedPreview: string | null;
  replyToId: number | null;
  reactions: Record<string, string> | null;
  edited: boolean;
  revisions: Message[] | null;
}

export interface Page<T> {
  items: T[];
  hasMoreBefore: boolean;
  hasMoreAfter: boolean;
}

/** A search hit inside one chat. */
export interface ChatHit {
  id: number;
  ts: number;
}

export interface SearchHit {
  chatId: number;
  chatName: string | null;
  messageId: number;
  ts: number;
  sender: string | null;
  snippet: string;
}

export interface MediaItem {
  id: number;
  ts: number;
  url: string;
  mime: string | null;
  caption: string | null;
}

export interface Status {
  state: "indexing" | "ready" | "error";
  filesDone: number;
  filesTotal: number;
  chats: number;
  messages: number;
  hiddenChats: number;
}

export interface SearchFilters {
  fromMe?: boolean;
  from?: number;
  to?: number;
  media?: boolean;
}
