const en = {
  appName: "Archive Explorer",
  searchPlaceholder: "Search chats and messages",
  chats: "Chats",
  messages: "Messages",
  noChats: "No chats found",
  noMessages: "No messages found",
  filters: "Filters",
  fromAnyone: "Anyone",
  fromMe: "Me",
  fromOthers: "Others",
  onlyMedia: "Only media",
  dateFrom: "From",
  dateTo: "To",
  selectChat: "Select a chat to start reading",
  searchInChat: "Search in chat",
  hitCount: "{current} of {total}",
  noHits: "No results",
  previous: "Previous",
  next: "Next",
  close: "Close",
  jumpToDate: "Jump to date",
  timeline: "Timeline",
  timelineCount: "{count} messages",
  timelineOne: "1 message",
  timelineNone: "No messages",
  media: "Media",
  noMedia: "No media in this chat",
  you: "You",
  edited: "edited",
  originalKept: "Original kept",
  showLaterVersions: "Show later versions",
  hideLaterVersions: "Hide later versions",
  laterVersion: "Later version",
  download: "Download",
  open: "Open",
  photo: "Photo",
  video: "Video",
  voiceMessage: "Voice message",
  document: "Document",
  contactCard: "Contact card",
  missingMedia: "Media not in the archive",
  indexingTitle: "Building the search index…",
  indexingDetail: "{done} of {total} chat files",
  indexingNote: "This happens once; later starts are fast.",
  indexingError: "Indexing failed. Check the terminal for details.",
  today: "Today",
  yesterday: "Yesterday",
  groupChat: "Group",
  calls: "Calls",
  messageCount: "{count} messages",
  loadMore: "Load more",
  backToList: "Back",
  replyUnavailable: "Original message not in the archive",
  showHidden: "Show hidden chats ({count})",
  hiddenBadge: "hidden",
};

export type MessageKey = keyof typeof en;

const de: Record<MessageKey, string> = {
  appName: "Archive Explorer",
  searchPlaceholder: "Chats und Nachrichten durchsuchen",
  chats: "Chats",
  messages: "Nachrichten",
  noChats: "Keine Chats gefunden",
  noMessages: "Keine Nachrichten gefunden",
  filters: "Filter",
  fromAnyone: "Alle",
  fromMe: "Ich",
  fromOthers: "Andere",
  onlyMedia: "Nur Medien",
  dateFrom: "Von",
  dateTo: "Bis",
  selectChat: "Wähle einen Chat aus",
  searchInChat: "Im Chat suchen",
  hitCount: "{current} von {total}",
  noHits: "Keine Treffer",
  previous: "Vorheriger",
  next: "Nächster",
  close: "Schließen",
  jumpToDate: "Zu Datum springen",
  timeline: "Zeitleiste",
  timelineCount: "{count} Nachrichten",
  timelineOne: "1 Nachricht",
  timelineNone: "Keine Nachrichten",
  media: "Medien",
  noMedia: "Keine Medien in diesem Chat",
  you: "Du",
  edited: "bearbeitet",
  originalKept: "Original erhalten",
  showLaterVersions: "Spätere Versionen zeigen",
  hideLaterVersions: "Spätere Versionen ausblenden",
  laterVersion: "Spätere Version",
  download: "Herunterladen",
  open: "Öffnen",
  photo: "Foto",
  video: "Video",
  voiceMessage: "Sprachnachricht",
  document: "Dokument",
  contactCard: "Kontaktkarte",
  missingMedia: "Medium nicht im Archiv",
  indexingTitle: "Suchindex wird aufgebaut…",
  indexingDetail: "{done} von {total} Chat-Dateien",
  indexingNote: "Das passiert einmal; spätere Starts sind schnell.",
  indexingError: "Indexierung fehlgeschlagen. Details stehen im Terminal.",
  today: "Heute",
  yesterday: "Gestern",
  groupChat: "Gruppe",
  calls: "Anrufe",
  messageCount: "{count} Nachrichten",
  loadMore: "Mehr laden",
  backToList: "Zurück",
  replyUnavailable: "Originalnachricht nicht im Archiv",
  showHidden: "Ausgeblendete Chats zeigen ({count})",
  hiddenBadge: "ausgeblendet",
};

const catalogs = { en, de } as const;
export type Locale = keyof typeof catalogs;

export function pickLocale(languages: readonly string[] | undefined): Locale {
  for (const lang of languages ?? []) {
    const base = lang.toLowerCase().split("-")[0];
    if (base === "de") return "de";
    if (base === "en") return "en";
  }
  return "en";
}

export function translator(locale: Locale) {
  const catalog = catalogs[locale];
  return (key: MessageKey, vars?: Record<string, string | number>): string =>
    catalog[key].replace(/\{(\w+)\}/g, (_, name: string) => String(vars?.[name] ?? `{${name}}`));
}

export type Translate = ReturnType<typeof translator>;
