import { describe, expect, it } from "vitest";
import { pickLocale, translator } from "./i18n";
import { displayName, formatListDate, initials, snippetParts } from "./format";

describe("i18n", () => {
  it("picks German for de-AT and English otherwise", () => {
    expect(pickLocale(["de-AT", "en"])).toBe("de");
    expect(pickLocale(["fr-FR", "en-GB"])).toBe("en");
    expect(pickLocale(["ja"])).toBe("en");
    expect(pickLocale(undefined)).toBe("en");
  });

  it("fills variables", () => {
    expect(translator("de")("hitCount", { current: 2, total: 5 })).toBe("2 von 5");
  });
});

describe("format", () => {
  const t = translator("en");
  const now = new Date(2026, 10, 19, 12, 0, 0);

  it("labels yesterday in the chat list", () => {
    const ts = new Date(2026, 10, 18, 9, 0, 0).getTime() / 1000;
    expect(formatListDate(ts, "en", t, now)).toBe("Yesterday");
  });

  it("never calls a future date today", () => {
    const future = new Date(2027, 0, 5, 9, 0, 0).getTime() / 1000;
    expect(formatListDate(future, "en", t, now)).not.toMatch(/:/);
  });

  it("makes initials and display names", () => {
    expect(initials("Jürgen Müller", "x")).toBe("JM");
    expect(initials("Family 👨‍👩‍👧", "x")).toBe("F");
    expect(displayName({ name: null, jid: "4915550100001@s.whatsapp.net" })).toBe("+4915550100001");
  });

  it("splits search snippets into highlighted parts", () => {
    expect(snippetParts("Shall we do the \u0002lighthouse\u0003 picnic")).toEqual([
      { text: "Shall we do the ", hit: false },
      { text: "lighthouse", hit: true },
      { text: " picnic", hit: false },
    ]);
  });
});
