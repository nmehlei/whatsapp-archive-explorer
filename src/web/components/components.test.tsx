import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { translator } from "@/lib/i18n";
import { UiContext } from "@/lib/ui";
import type { ChatSummary, Message } from "@/lib/types";
import { MessageBubble } from "./MessageBubble";
import { ChatList } from "./ChatList";

const base: Message = {
  id: 1, messageId: "1", keyId: "K1", ts: 1700000000, fromMe: false, sender: null, isSystem: false, text: "Hello there",
  caption: null, html: null, isMedia: false, mediaUrl: null, mime: null, replyKeyId: null, quotedPreview: null,
  replyToId: null, reactions: null, edited: false, revisions: null,
};

function withUi(node: React.ReactNode, locale: "en" | "de" = "en") {
  return render(<UiContext.Provider value={{ locale, intl: locale, t: translator(locale) }}>{node}</UiContext.Provider>);
}

describe("MessageBubble", () => {
  it("places own and other messages on different sides", () => {
    const { container } = withUi(
      <>
        <MessageBubble message={{ ...base, id: 1, fromMe: true }} isGroup={false} />
        <MessageBubble message={{ ...base, id: 2 }} isGroup={false} />
      </>,
    );
    expect(container.querySelector('[data-message-id="1"]')!.className).toContain("mine");
    expect(container.querySelector('[data-message-id="2"]')!.className).toContain("theirs");
  });

  it("shows the sender in groups and centres system messages", () => {
    const { container } = withUi(
      <>
        <MessageBubble message={{ ...base, sender: "Anna Becker" }} isGroup />
        <MessageBubble message={{ ...base, id: 3, isSystem: true, text: "Anna joined" }} isGroup />
      </>,
    );
    expect(screen.getByText("Anna Becker")).toBeTruthy();
    expect(container.querySelector(".system-message")!.textContent).toBe("Anna joined");
  });

  it("jumps to the quoted message and shows reactions", () => {
    const onJumpTo = vi.fn();
    withUi(
      <MessageBubble
        message={{ ...base, replyKeyId: "K0", quotedPreview: "Earlier text", replyToId: 42, reactions: { Anna: "❤️" } }}
        isGroup={false}
        onJumpTo={onJumpTo}
      />,
    );
    fireEvent.click(screen.getByText("Earlier text"));
    expect(onJumpTo).toHaveBeenCalledWith(42);
    expect(screen.getByText("❤️")).toBeTruthy();
  });

  it("marks kept originals and reveals later versions", () => {
    withUi(
      <MessageBubble message={{ ...base, edited: true, revisions: [{ ...base, text: "This message was deleted" }] }} isGroup={false} />,
      "de",
    );
    expect(screen.getByText("Original erhalten")).toBeTruthy();
    fireEvent.click(screen.getByText("Spätere Versionen zeigen"));
    expect(screen.getByText("This message was deleted")).toBeTruthy();
  });

  it("highlights search terms", () => {
    const { container } = withUi(<MessageBubble message={base} isGroup={false} terms={["there"]} />);
    expect(container.querySelector("mark")!.textContent).toBe("there");
  });
});

describe("ChatList", () => {
  const chats: ChatSummary[] = [
    { id: 1, jid: "4915550100001@s.whatsapp.net", name: "Jürgen Müller", kind: "private", hasAvatar: false, lastTs: 1700000000, lastPreview: "See you", messageCount: 3 },
    { id: 2, jid: "000000000000000", name: "Calls", kind: "calls", hasAvatar: false, lastTs: null, lastPreview: null, messageCount: 0 },
  ];

  it("renders names, previews and initials, and selects", () => {
    const onSelect = vi.fn();
    withUi(<ChatList chats={chats} selectedId={1} onSelect={onSelect} plain />);
    expect(screen.getByText("Jürgen Müller")).toBeTruthy();
    expect(screen.getByText("See you")).toBeTruthy();
    expect(screen.getByText("JM")).toBeTruthy();
    fireEvent.click(screen.getByText("Calls"));
    expect(onSelect).toHaveBeenCalledWith(chats[1]);
  });

  it("says so when nothing matches", () => {
    withUi(<ChatList chats={[]} selectedId={null} onSelect={() => undefined} />, "de");
    expect(screen.getByText("Keine Chats gefunden")).toBeTruthy();
  });
});
