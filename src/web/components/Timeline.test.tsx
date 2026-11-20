import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { translator } from "@/lib/i18n";
import { UiContext } from "@/lib/ui";
import { buildTimeline } from "@/lib/timeline";
import { Timeline } from "./Timeline";

const day = (y: number, m: number, d: number) => Date.UTC(y, m - 1, d) / 86_400_000;
const local = (y: number, m: number, d: number) => new Date(y, m - 1, d, 12).getTime() / 1000;

// Jan 2016 … Dec 2019: 48 month buckets
const tl = buildTimeline([
  { day: day(2016, 1, 5), count: 3 },
  { day: day(2017, 6, 9), count: 40 },
  { day: day(2019, 12, 20), count: 1 },
]);

function renderTimeline(props: Partial<React.ComponentProps<typeof Timeline>> = {}) {
  const onJump = vi.fn();
  const view = render(
    <UiContext.Provider value={{ locale: "en", intl: "en", t: translator("en") }}>
      <Timeline timeline={tl} currentTs={local(2017, 6, 9)} hits={[]} onJump={onJump} {...props} />
    </UiContext.Provider>,
  );
  const slider = screen.getByRole("slider", { name: "Timeline" });
  // jsdom has no layout: give the strip a 480 px wide box, 10 px per month
  slider.getBoundingClientRect = () => ({
    left: 0,
    top: 0,
    width: 480,
    height: 44,
    right: 480,
    bottom: 44,
    x: 0,
    y: 0,
    toJSON: () => ({}),
  });
  return { onJump, slider, ...view };
}

describe("Timeline", () => {
  afterEach(cleanup);

  it("is a labelled slider positioned on the month being read", () => {
    const { slider } = renderTimeline();

    expect(slider.getAttribute("aria-valuemin")).toBe("0");
    expect(slider.getAttribute("aria-valuemax")).toBe("47");
    expect(slider.getAttribute("aria-valuenow")).toBe("17");
    expect(slider.getAttribute("aria-valuetext")).toBe("June 2017, 40 messages");
  });

  it("draws one bar per month and labels the years", () => {
    const { container } = renderTimeline();

    expect(container.querySelectorAll(".timeline-bar")).toHaveLength(48);
    expect([...container.querySelectorAll(".timeline-label")].map((l) => l.textContent)).toEqual(["2016", "2017", "2018", "2019"]);
  });

  it("shows the month and its count while hovering", () => {
    const { slider } = renderTimeline();

    fireEvent.pointerMove(slider, { clientX: 175 });

    expect(screen.getByRole("tooltip").textContent).toBe("June 201740 messages");
  });

  it("jumps to the start of the month that is clicked", () => {
    const { slider, onJump } = renderTimeline();

    fireEvent.pointerDown(slider, { clientX: 5, pointerId: 1 });
    fireEvent.pointerUp(slider, { clientX: 5, pointerId: 1 });

    expect(onJump).toHaveBeenCalledTimes(1);
    expect(onJump).toHaveBeenCalledWith(new Date(2016, 0, 1).getTime() / 1000);
  });

  it("jumps once, where a drag is let go", () => {
    const { slider, onJump } = renderTimeline();

    fireEvent.pointerDown(slider, { clientX: 5, pointerId: 1 });
    fireEvent.pointerMove(slider, { clientX: 100, pointerId: 1 });
    fireEvent.pointerMove(slider, { clientX: 475, pointerId: 1 });
    fireEvent.pointerUp(slider, { clientX: 475, pointerId: 1 });

    expect(onJump).toHaveBeenCalledTimes(1);
    expect(onJump).toHaveBeenCalledWith(new Date(2019, 11, 1).getTime() / 1000);
  });

  it("steps to the previous/next month with messages with the arrow keys, and to the ends with Home and End", () => {
    const { slider, onJump } = renderTimeline();

    fireEvent.keyDown(slider, { key: "ArrowLeft" }); // the months between are empty
    expect(onJump).toHaveBeenLastCalledWith(new Date(2016, 0, 1).getTime() / 1000);
    expect(slider.getAttribute("aria-valuenow")).toBe("0");
    fireEvent.keyDown(slider, { key: "ArrowRight" });
    expect(onJump).toHaveBeenLastCalledWith(new Date(2017, 5, 1).getTime() / 1000);
    fireEvent.keyDown(slider, { key: "End" });
    expect(onJump).toHaveBeenLastCalledWith(new Date(2019, 11, 1).getTime() / 1000);
    fireEvent.keyDown(slider, { key: "Home" });
    expect(onJump).toHaveBeenLastCalledWith(new Date(2016, 0, 1).getTime() / 1000);
  });

  it("returns the marker to where the chat really is once a key jump has settled", async () => {
    // the jump fails (e.g. the index is being rebuilt): the view did not move, so neither may the marker
    const onJump = vi.fn(() => Promise.reject(new Error("503")));
    const { slider } = renderTimeline({ onJump });

    fireEvent.keyDown(slider, { key: "End" });
    expect(slider.getAttribute("aria-valuenow")).toBe("47");
    await vi.waitFor(() => expect(slider.getAttribute("aria-valuenow")).toBe("17"));
  });

  it("only jumps for the main button", () => {
    const { slider, onJump } = renderTimeline();

    fireEvent.pointerDown(slider, { clientX: 5, pointerId: 1, button: 2 });
    fireEvent.pointerUp(slider, { clientX: 5, pointerId: 1, button: 2 });

    expect(onJump).not.toHaveBeenCalled();
  });

  it("marks the months with search hits", () => {
    const { container } = renderTimeline({
      hits: [local(2016, 1, 5), local(2016, 1, 6), local(2019, 12, 20)],
    });

    expect(container.querySelectorAll(".timeline-hit")).toHaveLength(2);
  });

  it("stays out of the way for a chat that fits in one bucket", () => {
    const { container } = render(
      <Timeline timeline={buildTimeline([{ day: day(2020, 1, 1), count: 5 }])} currentTs={null} hits={[]} onJump={() => {}} />,
    );

    expect(container.innerHTML).toBe("");
  });
});
