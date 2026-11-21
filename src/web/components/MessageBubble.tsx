"use client";

import { useState } from "react";
import { colorFor, formatTime } from "@/lib/format";
import { useUi } from "@/lib/ui";
import type { Message } from "@/lib/types";
import { Highlight } from "./Highlight";

interface Props {
  message: Message;
  isGroup: boolean;
  isCalls?: boolean;
  highlighted?: boolean;
  terms?: string[];
  onOpenMedia?: (message: Message) => void;
  onJumpTo?: (messageId: number) => void;
}

function kindOf(mime: string | null): "image" | "video" | "audio" | "file" {
  const type = (mime ?? "").split("/")[0];
  return type === "image" ? "image" : type === "video" ? "video" : type === "audio" ? "audio" : "file";
}

function fileName(url: string): string {
  return decodeURIComponent(url.split("/").pop() ?? url);
}

function MediaContent({ message, onOpenMedia }: { message: Message; onOpenMedia?: (m: Message) => void }) {
  const { t } = useUi();
  if (!message.mediaUrl) {
    return <p className="media-missing">{t("missingMedia")}</p>;
  }
  switch (kindOf(message.mime)) {
    case "image":
      return (
        <button type="button" className="media-image" onClick={() => onOpenMedia?.(message)} aria-label={t("photo")}>
          <img src={message.mediaUrl} alt={message.caption ?? t("photo")} loading="lazy" />
        </button>
      );
    case "video":
      return <video className="media-video" src={message.mediaUrl} controls preload="metadata" />;
    case "audio":
      return (
        <span className="media-audio">
          <audio src={message.mediaUrl} controls preload="metadata" aria-label={t("voiceMessage")} />
          <a href={message.mediaUrl} download>
            {t("download")}
          </a>
        </span>
      );
    default:
      return (
        <a className="media-file" href={message.mediaUrl} download>
          <span aria-hidden>📄</span> {fileName(message.mediaUrl)}
        </a>
      );
  }
}

function Body({ message, terms, onOpenMedia }: { message: Message; terms: string[]; onOpenMedia?: (m: Message) => void }) {
  return (
    <>
      {message.isMedia && <MediaContent message={message} onOpenMedia={onOpenMedia} />}
      {message.html ? (
        // server-side sanitised to a tiny allow-list (a/br/b/i) with archive-relative links only
        <span className="text html" dangerouslySetInnerHTML={{ __html: message.html }} />
      ) : (
        message.text && (
          <span className="text">
            <Highlight text={message.text} terms={terms} />
          </span>
        )
      )}
      {message.caption && (
        <span className="text caption">
          <Highlight text={message.caption} terms={terms} />
        </span>
      )}
    </>
  );
}

export function MessageBubble({ message, isGroup, isCalls, highlighted, terms = [], onOpenMedia, onJumpTo }: Props) {
  const { t, intl } = useUi();
  const [showRevisions, setShowRevisions] = useState(false);

  if (message.isSystem) {
    return (
      <div className={`system-message${highlighted ? " highlighted" : ""}`} data-message-id={message.id}>
        <Highlight text={message.text ?? ""} terms={terms} />
      </div>
    );
  }

  const revisions = message.revisions ?? [];
  const reactions = Object.entries(message.reactions ?? {});
  return (
    <div className={`bubble-row ${message.fromMe ? "mine" : "theirs"}`} data-message-id={message.id}>
      <div className={`bubble${highlighted ? " highlighted" : ""}${isCalls ? " call" : ""}`}>
        {isGroup && !message.fromMe && message.sender && (
          <span className="sender" style={{ color: colorFor(message.sender) }}>
            {message.sender}
          </span>
        )}
        {(message.replyKeyId || message.quotedPreview) && (
          <button
            type="button"
            className="quote"
            disabled={message.replyToId === null}
            title={message.replyToId === null ? t("replyUnavailable") : undefined}
            onClick={() => message.replyToId !== null && onJumpTo?.(message.replyToId)}
          >
            {message.quotedPreview ?? "…"}
          </button>
        )}
        {isCalls && <span aria-hidden>📞 </span>}
        <Body message={message} terms={terms} onOpenMedia={onOpenMedia} />
        <span className="meta">
          {revisions.length > 0 && <span className="badge">{t("originalKept")}</span>}
          {message.edited && revisions.length === 0 && <span>{t("edited")}</span>}
          <time dateTime={new Date(message.ts * 1000).toISOString()}>{formatTime(message.ts, intl)}</time>
        </span>
        {revisions.length > 0 && (
          <div className="revisions">
            <button type="button" className="link" onClick={() => setShowRevisions((v) => !v)}>
              {showRevisions ? t("hideLaterVersions") : t("showLaterVersions")}
            </button>
            {showRevisions &&
              revisions.map((rev, i) => (
                <div key={i} className="revision">
                  <span className="revision-label">
                    {t("laterVersion")} · {formatTime(rev.ts, intl)}
                  </span>
                  <Body message={rev} terms={terms} />
                </div>
              ))}
          </div>
        )}
        {reactions.length > 0 && (
          <span className="reactions" title={reactions.map(([who, emoji]) => `${who}: ${emoji}`).join("\n")}>
            {reactions.map(([who, emoji]) => (
              <span key={who}>{emoji}</span>
            ))}
          </span>
        )}
      </div>
    </div>
  );
}
