import { api } from "@/lib/api";
import { colorFor, initials } from "@/lib/format";
import type { ChatSummary } from "@/lib/types";

export function Avatar({ chat, size = 48 }: { chat: Pick<ChatSummary, "id" | "name" | "jid" | "hasAvatar" | "kind">; size?: number }) {
  const label = chat.kind === "calls" ? "📞" : initials(chat.name, chat.jid);
  if (chat.hasAvatar) {
    return <img className="avatar" src={api.avatarUrl(chat.id)} alt="" width={size} height={size} loading="lazy" />;
  }
  return (
    <span className="avatar avatar-initials" style={{ width: size, height: size, background: colorFor(chat.name ?? chat.jid) }} aria-hidden>
      {label}
    </span>
  );
}
