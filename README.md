# whatsapp-archive-explorer

Browse and search your exported WhatsApp history like in WhatsApp itself — chat list, chats, media and
full-text search across every chat — from **one self-contained executable** that runs entirely on your own
computer.

It reads the output of [WhatsApp-Chat-Exporter](https://github.com/KnugiHK/WhatsApp-Chat-Exporter)
(`--per-chat` JSON plus the media folder) **read-only**.

> **Not affiliated with WhatsApp or Meta.** "WhatsApp" is a trademark of WhatsApp LLC. This project uses
> no WhatsApp logo, fonts or design assets.

## Privacy

- Runs only on `127.0.0.1`; nothing is sent anywhere — no telemetry, no update checks, no external fonts.
- Never writes into your archive. The search index lives in your user cache folder.
- Never prints chat names or message text to the terminal.

*Work in progress — see `docs/` for the design.*
