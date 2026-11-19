/** Marks case-insensitive occurrences of the search terms in plain text. */
export function Highlight({ text, terms }: { text: string; terms: string[] }) {
  const clean = terms.map((t) => t.trim()).filter((t) => t.length > 1);
  if (clean.length === 0) return <>{text}</>;
  const escaped = clean.map((t) => t.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));
  const parts = text.split(new RegExp(`(${escaped.join("|")})`, "giu"));
  return (
    <>
      {parts.map((part, i) =>
        i % 2 === 1 ? <mark key={i}>{part}</mark> : <span key={i}>{part}</span>,
      )}
    </>
  );
}
