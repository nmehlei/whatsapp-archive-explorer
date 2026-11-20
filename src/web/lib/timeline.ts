/** The chat timeline: messages per month (or per day for short chats), from the first message to the last. */

export interface TimelineDay {
  /** The viewer's calendar day, as days since 1970-01-01 (read it like a UTC date). */
  day: number;
  count: number;
}

export interface Bucket {
  year: number;
  /** 0 = January. */
  month: number;
  /** Day of the month for day buckets, 1 for month buckets. */
  day: number;
  /** Local midnight at the bucket's start, in unix seconds: where a jump lands. */
  start: number;
  count: number;
}

export interface Timeline {
  granularity: "month" | "day";
  buckets: Bucket[];
  max: number;
  total: number;
}

const DAY_MS = 86_400_000;
/** Below this span a month bar would be one of only two or three: show days instead. */
const DAY_GRANULARITY_BELOW_DAYS = 92;
/** Room a year or month label needs before labels are thinned. */
const LABEL_MIN_PX = 56;

/** The viewer's IANA time zone ("Europe/Berlin"): the server cuts days there, daylight saving time included. */
export function viewerZone(): string | undefined {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || undefined;
  } catch {
    return undefined;
  }
}

/** Minutes east of UTC: the server's fallback when it does not know the zone. */
export function viewerOffsetMinutes(at = new Date()): number {
  return -at.getTimezoneOffset();
}

function calendar(dayIndex: number) {
  const d = new Date(dayIndex * DAY_MS);
  return {
    year: d.getUTCFullYear(),
    month: d.getUTCMonth(),
    day: d.getUTCDate(),
  };
}

const monthKey = (year: number, month: number) => year * 12 + month;
const dayKey = (year: number, month: number, day: number) => Date.UTC(year, month, day) / DAY_MS;

export function buildTimeline(days: TimelineDay[]): Timeline {
  const sorted = [...days].sort((a, b) => a.day - b.day);
  const first = sorted[0];
  const last = sorted[sorted.length - 1];
  if (!first || !last) return { granularity: "month", buckets: [], max: 0, total: 0 };

  const granularity = last.day - first.day < DAY_GRANULARITY_BELOW_DAYS ? "day" : "month";
  const buckets: Bucket[] = [];
  if (granularity === "day") {
    for (let k = first.day; k <= last.day; k++) {
      const c = calendar(k);
      buckets.push({
        ...c,
        start: new Date(c.year, c.month, c.day).getTime() / 1000,
        count: 0,
      });
    }
    for (const d of sorted) buckets[d.day - first.day]!.count += d.count;
  } else {
    const a = calendar(first.day);
    const b = calendar(last.day);
    for (let k = monthKey(a.year, a.month); k <= monthKey(b.year, b.month); k++) {
      const year = Math.floor(k / 12);
      const month = k % 12;
      buckets.push({
        year,
        month,
        day: 1,
        start: new Date(year, month, 1).getTime() / 1000,
        count: 0,
      });
    }
    const base = monthKey(a.year, a.month);
    for (const d of sorted) {
      const c = calendar(d.day);
      buckets[monthKey(c.year, c.month) - base]!.count += d.count;
    }
  }
  return {
    granularity,
    buckets,
    max: Math.max(...buckets.map((b) => b.count)),
    total: sorted.reduce((sum, d) => sum + d.count, 0),
  };
}

/** Index of the bucket holding a message timestamp, clamped to the chat's span; null without buckets. */
export function bucketAt(tl: Timeline, ts: number): number | null {
  const first = tl.buckets[0];
  if (!first) return null;
  const date = new Date(ts * 1000);
  const index =
    tl.granularity === "month"
      ? monthKey(date.getFullYear(), date.getMonth()) - monthKey(first.year, first.month)
      : dayKey(date.getFullYear(), date.getMonth(), date.getDate()) - dayKey(first.year, first.month, first.day);
  return Math.min(Math.max(index, 0), tl.buckets.length - 1);
}

export interface Label {
  index: number;
  text: string;
}

/**
 * Axis labels: each year for month buckets, each month for day buckets. When they would not fit the width, only
 * every 2nd/5th/10th year (or every 2nd/3rd/6th month) is labelled, on round values.
 */
export function labelSteps(tl: Timeline, width: number, locale = "en"): Label[] {
  const n = tl.buckets.length;
  if (n === 0 || width <= 0) return [];
  const starts = tl.buckets
    .map((b, index) => ({ b, index }))
    .filter(({ b, index }) => index === 0 || (tl.granularity === "month" ? b.month === 0 : b.day === 1));
  const units = tl.granularity === "month" ? n / 12 : n / 30;
  const perUnit = width / Math.max(units, 1);
  const choices = tl.granularity === "month" ? [1, 2, 5, 10, 20, 50] : [1, 2, 3, 6, 12];
  const step = choices.find((s) => s * perUnit >= LABEL_MIN_PX) ?? choices[choices.length - 1]!;
  const monthName = new Intl.DateTimeFormat(locale, { month: "short" });
  const kept = starts.filter(({ b }) => (tl.granularity === "month" ? b.year % step === 0 : b.month % step === 0) || step === 1);
  // a chat starting late in a year: its first label would sit on top of the next one
  if (kept.length > 1 && ((kept[1]!.index - kept[0]!.index) / n) * width < LABEL_MIN_PX) kept.shift();
  return kept.map(({ b, index }) => ({
    index,
    text: tl.granularity === "month" ? String(b.year) : monthName.format(new Date(b.year, b.month, 1)),
  }));
}
