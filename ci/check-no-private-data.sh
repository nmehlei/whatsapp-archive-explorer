#!/usr/bin/env bash
# Fails when files that look like real chat data are tracked: exports, databases, backups, media.
# Media is only allowed as generated demo output (never committed) or under docs/screenshots/.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

bad=$(git ls-files | grep -E -i \
  -e '\.(db|sqlite|sqlite3|crypt1[245]|crypt|key|vcf|opus|ogg|mp4|m4a|aac|amr|3gp|jpe?g|webp|heic|gif|zip|7z)$' \
  -e '(^|/)WhatsApp Chat with .*\.txt$' \
  -e '(^|/)_chat\.txt$' \
  -e '(^|/)(msgstore|wa)\.db' \
  -e '(^|/)(result\.json|json/[^/]+\.json)$' \
  | grep -v -E '^(docs/screenshots/|src/web/public/)' || true)

if [ -n "$bad" ]; then
  echo "These tracked files look like private chat data — never commit real exports:" >&2
  echo "$bad" >&2
  exit 1
fi
echo "no private-looking data tracked"
