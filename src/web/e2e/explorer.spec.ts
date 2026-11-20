import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".chat-row").first()).toBeVisible({ timeout: 60_000 });
});

test("lists chats and opens one at the newest message", async ({ page }) => {
  const chats = (await (await page.request.get("/api/chats")).json()) as unknown[];
  expect(chats).toHaveLength(12);
  await page.getByLabel("Search chats and messages").fill("Jürgen");
  await page.locator(".chat-row", { hasText: "Jürgen Müller" }).click();
  await expect(page).toHaveURL(/\?chat=\d+/);
  await expect(page.locator(".chat-title strong")).toHaveText("Jürgen Müller");
  await expect(page.locator(".bubble").last()).toBeInViewport();
});

test("filters the chat list ignoring accents", async ({ page }) => {
  await page.getByLabel("Search chats and messages").fill("muller");
  await expect(page.locator(".search-results .chat-row")).toHaveCount(1);
});

test("searches across chats and jumps to the hit", async ({ page }) => {
  await page.getByLabel("Search chats and messages").fill("lighthouse");
  const hit = page.locator(".hit");
  await expect(hit).toHaveCount(1);
  await hit.click();
  await expect(page).toHaveURL(/msg=\d+/);
  const highlighted = page.locator(".bubble.highlighted");
  await expect(highlighted).toContainText("lighthouse picnic");
  await expect(highlighted).toBeInViewport();
});

test("searches inside a chat with next and previous", async ({ page }) => {
  await page.locator(".chat-row").nth(1).click();
  await page.getByLabel("Search in chat").first().click();
  const box = page.locator(".in-chat-search input");
  await box.fill("good");
  await box.press("Enter");
  await expect(page.locator(".hit-count")).toHaveText(/(\d+) of \1/);
  await page.getByRole("button", { name: "Previous" }).click();
  await expect(page.locator(".hit-count")).not.toHaveText(/(\d+) of \1/);
  await expect(page.locator(".bubble.highlighted")).toContainText(/good/i);
});

test("travels through the chat with the timeline", async ({ page }) => {
  await page.locator(".chat-row").nth(1).click();
  const timeline = page.getByRole("slider", { name: "Timeline" });
  await expect(timeline).toBeVisible();
  const last = await timeline.getAttribute("aria-valuemax");
  await expect(timeline).toHaveAttribute("aria-valuenow", last!); // opens at the newest messages

  const box = (await timeline.boundingBox())!;
  await page.mouse.move(box.x + 2, box.y + box.height / 2);
  await expect(page.getByRole("tooltip")).toBeVisible();
  await page.mouse.down();
  await page.mouse.up();
  await expect(timeline).toHaveAttribute("aria-valuenow", "0");
  await expect(page.locator(".bubble").first()).toBeInViewport();
  await expect(page.locator(".bubble.highlighted")).toHaveCount(0); // a timeline jump marks nothing

  await timeline.press("End");
  await expect(timeline).toHaveAttribute("aria-valuenow", last!);
});

test("opens an image in the viewer", async ({ page }) => {
  await page.locator(".chat-row").nth(1).click();
  await page.getByRole("button", { name: "Media" }).click();
  await page.locator(".gallery-cell").first().click();
  await expect(page.locator(".viewer img")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.locator(".viewer")).toHaveCount(0);
});

test("loads older messages when scrolling up", async ({ page }) => {
  await page.locator(".chat-row").nth(1).click();
  const first = await page.locator("[data-message-id]").first().getAttribute("data-message-id");
  for (let i = 0; i < 6; i++) {
    await page.locator(".messages").evaluate((el) => {
      const scroller = el.querySelector("[data-virtuoso-scroller]") ?? el;
      scroller.scrollTop = 0;
    });
    await page.waitForTimeout(300);
  }
  const ids = await page.locator("[data-message-id]").evaluateAll((els) => els.map((e) => Number(e.getAttribute("data-message-id"))));
  expect(Math.min(...ids)).toBeLessThan(Number(first));
});

test("never talks to another origin", async ({ page }) => {
  const foreign: string[] = [];
  page.on("request", (r) => {
    if (!r.url().startsWith("http://127.0.0.1:18780/") && !r.url().startsWith("data:")) foreign.push(r.url());
  });
  await page.reload();
  await page.locator(".chat-row").first().click();
  await page.waitForTimeout(1000);
  expect(foreign).toEqual([]);
});
