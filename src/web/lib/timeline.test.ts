import { describe, expect, it } from "vitest";
import { buildTimeline, bucketAt, labelSteps, viewerOffsetMinutes } from "./timeline";

/** The server's day index: the viewer's calendar day, counted like a UTC date. */
const day = (y: number, m: number, d: number) => Date.UTC(y, m - 1, d) / 86_400_000;
const local = (y: number, m: number, d: number, h = 12) => new Date(y, m - 1, d, h).getTime() / 1000;

describe("buildTimeline", () => {
  it("makes one bucket per month from the first to the last message, empty months included", () => {
    const tl = buildTimeline([
      { day: day(2016, 11, 3), count: 4 },
      { day: day(2016, 11, 20), count: 1 },
      { day: day(2017, 3, 1), count: 7 },
    ]);

    expect(tl.granularity).toBe("month");
    expect(tl.buckets.map((b) => [b.year, b.month, b.count])).toEqual([
      [2016, 10, 5],
      [2016, 11, 0],
      [2017, 0, 0],
      [2017, 1, 0],
      [2017, 2, 7],
    ]);
    expect(tl.max).toBe(7);
    expect(tl.total).toBe(12);
  });

  it("starts each month bucket at local midnight on the 1st, so a jump lands on its first message", () => {
    const tl = buildTimeline([{ day: day(2019, 3, 14), count: 1 }, { day: day(2019, 9, 1), count: 1 }]);

    expect(tl.buckets[0]!.start).toBe(new Date(2019, 2, 1).getTime() / 1000);
    expect(tl.buckets[1]!.start).toBe(new Date(2019, 3, 1).getTime() / 1000);
  });

  it("switches to days when the chat spans less than about three months", () => {
    const tl = buildTimeline([{ day: day(2020, 5, 30), count: 2 }, { day: day(2020, 6, 2), count: 3 }]);

    expect(tl.granularity).toBe("day");
    expect(tl.buckets.map((b) => [b.month, b.day, b.count])).toEqual([
      [4, 30, 2],
      [4, 31, 0],
      [5, 1, 0],
      [5, 2, 3],
    ]);
    expect(tl.buckets[3]!.start).toBe(new Date(2020, 5, 2).getTime() / 1000);
  });

  it("has nothing to show for an empty chat or a single day", () => {
    expect(buildTimeline([]).buckets).toEqual([]);
    expect(buildTimeline([{ day: day(2020, 1, 1), count: 9 }]).buckets).toHaveLength(1);
  });
});

describe("bucketAt", () => {
  const tl = buildTimeline([{ day: day(2016, 1, 5), count: 1 }, { day: day(2019, 3, 2), count: 1 }]);

  it("finds the bucket of a message timestamp", () => {
    expect(tl.buckets[bucketAt(tl, local(2016, 1, 31, 23))!]).toMatchObject({ year: 2016, month: 0 });
    expect(tl.buckets[bucketAt(tl, local(2018, 7, 1))!]).toMatchObject({ year: 2018, month: 6 });
  });

  it("clamps timestamps outside the chat to its ends", () => {
    expect(bucketAt(tl, local(2010, 1, 1))).toBe(0);
    expect(bucketAt(tl, local(2030, 1, 1))).toBe(tl.buckets.length - 1);
    expect(bucketAt(buildTimeline([]), local(2020, 1, 1))).toBeNull();
  });
});

describe("labelSteps", () => {
  it("labels every year when there is room and thins labels when there is not", () => {
    const tl = buildTimeline([{ day: day(2010, 1, 1), count: 1 }, { day: day(2025, 12, 31), count: 1 }]);
    const years = (width: number) => labelSteps(tl, width).map((l) => l.text);

    expect(years(1200)).toEqual(["2010", "2011", "2012", "2013", "2014", "2015", "2016", "2017", "2018", "2019", "2020", "2021", "2022", "2023", "2024", "2025"]);
    expect(years(300)).toEqual(["2010", "2015", "2020", "2025"]);
  });

  it("labels the first bucket of every year and places it at the bucket's position", () => {
    const tl = buildTimeline([{ day: day(2016, 7, 1), count: 1 }, { day: day(2017, 6, 1), count: 1 }]);

    expect(labelSteps(tl, 800)).toEqual([
      { index: 0, text: "2016" },
      { index: 6, text: "2017" },
    ]);
  });
});

describe("labelSteps for a chat that starts late in a year", () => {
  it("drops the first label when it would crowd the next one", () => {
    const tl = buildTimeline([{ day: day(2019, 12, 1), count: 1 }, { day: day(2026, 11, 1), count: 1 }]);

    expect(labelSteps(tl, 880).map((l) => l.text)).toEqual(["2020", "2021", "2022", "2023", "2024", "2025", "2026"]);
    expect(labelSteps(tl, 880 * 12).map((l) => l.text)[0]).toBe("2019"); // room enough: kept
  });
});

describe("viewerOffsetMinutes", () => {
  it("is minutes east of UTC, the opposite sign of getTimezoneOffset", () => {
    expect(viewerOffsetMinutes(new Date(2020, 0, 1))).toBe(-new Date(2020, 0, 1).getTimezoneOffset());
  });
});
