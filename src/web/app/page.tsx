"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { ChatView } from "@/components/ChatView";
import { IndexingScreen } from "@/components/IndexingScreen";
import { Sidebar } from "@/components/Sidebar";
import { api } from "@/lib/api";
import { pickLocale, translator } from "@/lib/i18n";
import { UiContext, type Ui } from "@/lib/ui";
import type { ChatSummary, Status } from "@/lib/types";

interface Route {
  chat: number | null;
  msg: number | null;
}

function readRoute(): Route {
  const p = new URLSearchParams(window.location.search);
  const num = (k: string) => (p.get(k) && /^\d+$/.test(p.get(k)!) ? Number(p.get(k)) : null);
  return { chat: num("chat"), msg: num("msg") };
}

function writeRoute(route: Route, push: boolean) {
  const p = new URLSearchParams();
  if (route.chat !== null) p.set("chat", String(route.chat));
  if (route.msg !== null) p.set("msg", String(route.msg));
  const url = p.toString() ? `?${p}` : window.location.pathname;
  if (push) window.history.pushState(null, "", url);
  else window.history.replaceState(null, "", url);
}

export default function Home() {
  const [ui, setUi] = useState<Ui>({ locale: "en", intl: "en", t: translator("en") });
  const [status, setStatus] = useState<Status | null>(null);
  const [chats, setChats] = useState<ChatSummary[]>([]);
  const [route, setRoute] = useState<Route>({ chat: null, msg: null });
  const [terms, setTerms] = useState<string[]>([]);
  const [showHidden, setShowHidden] = useState(false);

  useEffect(() => {
    // Browser-only values (language, URL) are read after the first render; the static HTML is language-neutral.
    const locale = pickLocale(navigator.languages);
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setUi({ locale, intl: navigator.language || locale, t: translator(locale) });
    document.documentElement.lang = locale;
    setRoute(readRoute());
    const onPop = () => setRoute(readRoute());
    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
  }, []);

  // Poll the status until the index is ready, then load the chat list.
  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      try {
        const s = await api.status();
        if (cancelled) return;
        setStatus(s);
        if (s.state === "ready") return;
        if (s.state === "error") return;
      } catch {
        // server restarting: keep polling
      }
      if (!cancelled) setTimeout(tick, 1000);
    };
    void tick();
    return () => {
      cancelled = true;
    };
  }, []);

  // (Re)load the chat list once ready, and whenever hidden chats are switched on or off.
  useEffect(() => {
    if (status?.state !== "ready") return;
    const ctl = new AbortController();
    api.chats(undefined, showHidden, ctl.signal).then(setChats, () => undefined);
    return () => ctl.abort();
  }, [status?.state, showHidden]);

  const navigate = useCallback((next: Route, push = true) => {
    setRoute(next);
    writeRoute(next, push);
  }, []);

  const selected = useMemo(() => chats.find((c) => c.id === route.chat) ?? null, [chats, route.chat]);

  if (status?.state !== "ready") {
    return (
      <UiContext.Provider value={ui}>
        <IndexingScreen status={status} />
      </UiContext.Provider>
    );
  }

  return (
    <UiContext.Provider value={ui}>
      <main className={`app${selected ? " has-chat" : ""}`}>
        <Sidebar
          chats={chats}
          hiddenCount={status.hiddenChats}
          showHidden={showHidden}
          onShowHidden={setShowHidden}
          selectedId={selected?.id ?? null}
          onSelectChat={(c) => {
            setTerms([]);
            navigate({ chat: c.id, msg: null });
          }}
          onOpenHit={(hit, query) => {
            setTerms(query.split(/\s+/));
            navigate({ chat: hit.chatId, msg: hit.messageId });
          }}
        />
        {selected ? (
          <ChatView
            key={selected.id}
            chat={selected}
            targetId={route.msg}
            terms={terms}
            onTarget={(msg) => navigate({ chat: selected.id, msg }, false)}
            onBack={() => navigate({ chat: null, msg: null })}
          />
        ) : (
          <section className="chat placeholder">
            <p className="muted">{ui.t("selectChat")}</p>
          </section>
        )}
      </main>
    </UiContext.Provider>
  );
}
