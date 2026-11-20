import type { TimelineDay } from "./timeline";
import type { ChatHit, ChatSummary, MediaItem, Message, Page, SearchFilters, SearchHit, Status } from "./types";

export class IndexingError extends Error {}

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, {
    signal,
    headers: { Accept: "application/json" },
  });
  if (response.status === 503) throw new IndexingError("indexing");
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return (await response.json()) as T;
}

function query(params: Record<string, string | number | boolean | undefined | null>): string {
  const q = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== "") q.set(key, String(value));
  }
  const s = q.toString();
  return s ? `?${s}` : "";
}

export const api = {
  status: (signal?: AbortSignal) => get<Status>("/api/status", signal),
  chats: (q?: string, hidden = false, signal?: AbortSignal) =>
    get<ChatSummary[]>(`/api/chats${query({ q, hidden: hidden || undefined })}`, signal),
  messages: (
    chatId: number,
    cursor: {
      before?: number;
      after?: number;
      around?: number;
      aroundTs?: number;
    },
    limit = 80,
    signal?: AbortSignal,
  ) => get<Page<Message>>(`/api/chats/${chatId}/messages${query({ ...cursor, limit })}`, signal),
  searchInChat: (chatId: number, q: string, signal?: AbortSignal) => get<ChatHit[]>(`/api/chats/${chatId}/search${query({ q })}`, signal),
  timeline: (chatId: number, zone: string | undefined, tz: number, signal?: AbortSignal) =>
    get<TimelineDay[]>(`/api/chats/${chatId}/timeline${query({ zone, tz })}`, signal),
  search: (q: string, filters: SearchFilters, offset = 0, signal?: AbortSignal) =>
    get<{ hits: SearchHit[]; total: number }>(`/api/search${query({ q, ...filters, offset })}`, signal),
  media: (chatId: number, offset = 0, limit = 60, signal?: AbortSignal) =>
    get<{ items: MediaItem[]; total: number }>(`/api/chats/${chatId}/media${query({ offset, limit })}`, signal),
  avatarUrl: (chatId: number) => `/api/chats/${chatId}/avatar`,
};
