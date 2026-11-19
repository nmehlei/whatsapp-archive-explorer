# Input format

The explorer reads [WhatsApp-Chat-Exporter](https://github.com/KnugiHK/WhatsApp-Chat-Exporter) JSON (tested
against 0.13). Either one file per chat (`--per-chat`, `json/*.json`, each `{ "<chat id>": chat }`) or one
`result.json` with all chats. Media paths in messages are relative to the export folder.

## Fields used

| Level | Field | Use |
|---|---|---|
| chat | `name` | display name (falls back to the number) |
| chat | `their_avatar` | avatar image path |
| chat | `messages` | object keyed by message id |
| message | `timestamp` | Unix seconds (milliseconds are detected and converted) |
| message | `from_me`, `sender` | side of the bubble, sender name in groups |
| message | `data` | text; for media (`media: true`) the file path; HTML for contact cards |
| message | `mime`, `caption` | media type and caption |
| message | `key_id`, `reply`, `quoted_data` | replies resolve `reply` against `key_id` in the same chat |
| message | `reactions` | `{ "name": "emoji" }` |
| message | `message_type` (7), `meta` | system messages ("joined", "changed the subject") |
| message | `edited` | edited marker |

Chat `000000000000000` is the exporter's calls pseudo-chat. Unknown fields are ignored.

## Never-deleting archives (optional)

Archives that merge several exports over time may extend the format:

- `revisions: [message, …]` on a message — the top-level message is the archived **original**; `revisions` holds
  later, different versions oldest first (edits, "deleted for everyone"). The explorer shows the original with an
  "Original kept" marker and the later versions on demand.
- message keys `<id>~<key_id>` — a different message that reused an exporter id after a restore from an older
  backup; it is shown as its own message.

Plain exporter output has neither; both are optional.
