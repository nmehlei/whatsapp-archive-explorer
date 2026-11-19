import type { Translate } from "./i18n";

const DAY = 86_400_000;

function startOfDay(d: Date): number {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
}

export function formatTime(ts: number, locale: string): string {
  return new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit" }).format(new Date(ts * 1000));
}

/** Chat list: time today, "Yesterday", weekday within a week, else the date. */
export function formatListDate(ts: number, locale: string, t: Translate, now = new Date()): string {
  const date = new Date(ts * 1000);
  const days = Math.round((startOfDay(now) - startOfDay(date)) / DAY);
  if (days === 0) return formatTime(ts, locale);
  if (days === 1) return t("yesterday");
  if (days > 1 && days < 7) return new Intl.DateTimeFormat(locale, { weekday: "long" }).format(date);
  return new Intl.DateTimeFormat(locale, { year: "2-digit", month: "2-digit", day: "2-digit" }).format(date);
}

/** Day divider in a chat. */
export function formatDayDivider(ts: number, locale: string, t: Translate, now = new Date()): string {
  const date = new Date(ts * 1000);
  const days = Math.round((startOfDay(now) - startOfDay(date)) / DAY);
  if (days === 0) return t("today");
  if (days === 1) return t("yesterday");
  return new Intl.DateTimeFormat(locale, { weekday: "long", year: "numeric", month: "long", day: "numeric" }).format(date);
}

export function sameDay(a: number, b: number): boolean {
  return startOfDay(new Date(a * 1000)) === startOfDay(new Date(b * 1000));
}

export function initials(name: string | null, fallback: string): string {
  const source = (name ?? fallback).replace(/[^\p{L}\p{N}\s]/gu, " ").trim();
  const parts = source.split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "#";
  const first = parts[0]?.[0] ?? "";
  const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? "") : "";
  return (first + last).toUpperCase();
}

/** A stable colour per name (group sender names, avatars). */
export function colorFor(name: string): string {
  let hash = 0;
  for (const ch of name) hash = (hash * 31 + ch.codePointAt(0)!) | 0;
  return `hsl(${Math.abs(hash) % 360} 55% 42%)`;
}

export function displayName(chat: { name: string | null; jid: string }): string {
  if (chat.name) return chat.name;
  const user = chat.jid.split("@")[0] ?? chat.jid;
  return /^\d+$/.test(user) ? `+${user}` : user;
}

/** Splits a search snippet (matches between \u0002 and \u0003) into plain/highlighted parts. */
export function snippetParts(snippet: string): { text: string; hit: boolean }[] {
  const parts: { text: string; hit: boolean }[] = [];
  const re = /\u0002([^\u0003]*)\u0003/g;
  let last = 0;
  for (const m of snippet.matchAll(re)) {
    if (m.index > last) parts.push({ text: snippet.slice(last, m.index), hit: false });
    parts.push({ text: m[1] ?? "", hit: true });
    last = m.index + m[0].length;
  }
  if (last < snippet.length) parts.push({ text: snippet.slice(last), hit: false });
  return parts;
}
