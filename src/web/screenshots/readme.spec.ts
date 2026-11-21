import { expect, test, type Page } from "@playwright/test";
import path from "node:path";

// Every README screenshot, from the showcase archive (src/DemoArchive --showcase): fictional people, generated photos.
const out = path.resolve(import.meta.dirname, "../../../docs/screenshots");
const shot = (name: string) => path.join(out, `${name}.png`);

async function open(page: Page, scheme: "light" | "dark") {
  await page.clock.setFixedTime(new Date("2026-11-20T19:30:00+01:00"));
  await page.emulateMedia({ colorScheme: scheme, reducedMotion: "reduce" });
  await page.goto("/");
  await expect(page.locator(".chat-row").first()).toBeVisible({ timeout: 240_000 });
}

async function openChat(page: Page, name: string) {
  await page.locator(".chat-row", { hasText: name }).first().click();
  await expect(page.locator(".chat-title strong")).toContainText(name);
  await expect(page.locator(".bubble").last()).toBeVisible();
}

/** Hover the timeline over a month, counted from the chat's first month. */
async function hoverMonth(page: Page, index: number) {
  const slider = page.getByRole("slider", { name: "Timeline" });
  const max = Number(await slider.getAttribute("aria-valuemax"));
  const box = (await slider.boundingBox())!;
  await page.mouse.move(box.x + ((index + 0.5) / (max + 1)) * box.width, box.y + box.height / 2);
  await expect(page.getByRole("tooltip")).toBeVisible();
}

/** A date jump marks the message it lands on; the pictures look calmer without that frame. */
async function unmark(page: Page) {
  await page.evaluate(() => document.querySelectorAll(".highlighted").forEach((e) => e.classList.remove("highlighted")));
}

const months = (y: number, m: number) => (y - 2015) * 12 + (m - 3); // Lena's chat starts March 2015

test("hero: a chat with its timeline", async ({ page }) => {
  await open(page, "light");
  await openChat(page, "Lena Hoffmann");
  // an in-chat search brings that evening into view (photos above it load and settle first), then closes again
  await page.getByLabel("Search in chat").first().click();
  await page.locator(".in-chat-search input").fill("ate them all");
  await page.locator(".in-chat-search input").press("Enter");
  await expect(page.locator(".bubble.highlighted")).toBeVisible();
  await page.waitForTimeout(1200);
  await page.locator(".chat-actions").getByLabel("Search in chat").click();
  await expect(page.locator(".in-chat-search")).toHaveCount(0);
  await page.addStyleTag({ content: "mark { background: none; color: inherit; }" }); // the search was only a way there
  // a little further up, so the photo sits below the timeline's tooltip
  const list = (await page.locator(".chat-body").boundingBox())!;
  await page.mouse.move(list.x + list.width / 2, list.y + list.height / 2);
  await page.mouse.wheel(0, -170);
  await page.waitForTimeout(800);
  await unmark(page);
  await hoverMonth(page, months(2019, 6));
  await page.waitForTimeout(300);
  await page.screenshot({ path: shot("hero") });
});

test("timeline close-up with search hits", async ({ page }) => {
  await open(page, "light");
  await openChat(page, "Lena Hoffmann");
  await page.getByLabel("Search in chat").first().click();
  await page.locator(".in-chat-search input").fill("lighthouse");
  await page.locator(".in-chat-search input").press("Enter");
  await expect(page.locator(".hit-count")).toHaveText(/of 2/);
  await expect(page.locator(".timeline-hit")).toHaveCount(2);
  await unmark(page);
  await page.waitForTimeout(300);
  const header = (await page.locator(".chat-header").boundingBox())!;
  const strip = (await page.locator(".timeline-wrap").boundingBox())!;
  await page.screenshot({
    path: shot("timeline"),
    clip: { x: header.x, y: header.y, width: header.width, height: strip.y + strip.height - header.y },
  });
});

test("search across every chat", async ({ page }) => {
  await open(page, "light");
  await page.getByLabel("Search chats and messages").fill("lighthouse");
  await expect(page.locator(".hit")).toHaveCount(6);
  await page.locator(".hit", { hasText: "Five years later" }).click();
  await expect(page.locator(".bubble.highlighted")).toBeVisible();
  await page.waitForTimeout(800);
  await page.screenshot({ path: shot("search") });
});

test("group chat in dark mode", async ({ page }) => {
  await open(page, "dark");
  await openChat(page, "Climbing crew");
  await page.locator(".bubble", { hasText: "Sam sending his project" }).evaluate((el) => el.scrollIntoView({ block: "start" }));
  await page.waitForTimeout(800);
  await page.screenshot({ path: shot("group-dark") });
});

test("media gallery", async ({ page }) => {
  await open(page, "light");
  await openChat(page, "Lena Hoffmann");
  await page.getByRole("button", { name: "Media" }).click();
  await expect(page.locator(".gallery-cell img").nth(11)).toBeVisible();
  await page.waitForTimeout(800);
  await page.screenshot({ path: shot("gallery") });
});

test("on a phone", async ({ browser }) => {
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 },
    deviceScaleFactor: 3,
    isMobile: true,
    hasTouch: true,
    locale: "en-GB",
    timezoneId: "Europe/Berlin",
    baseURL: test.info().project.use.baseURL,
  });
  const page = await context.newPage();
  await open(page, "light");
  await page.screenshot({ path: shot("phone-list") });
  await openChat(page, "Lena Hoffmann");
  await page.getByLabel("Jump to date").fill("2019-06-22");
  await expect(page.locator(".bubble", { hasText: "Westernmost point" })).toBeVisible();
  await page.locator(".bubble", { hasText: "Westernmost point" }).evaluate((el) => el.scrollIntoView({ block: "center" }));
  await page.waitForTimeout(600);
  await unmark(page);
  await page.screenshot({ path: shot("phone-chat") });
  await context.close();
});

test("social preview card", async ({ page }) => {
  // GitHub → Settings → Social preview (1280×640 layout, saved at 2x); built from the logo and the hero screenshot taken above
  const fs = await import("node:fs/promises");
  const logo = await fs.readFile(path.resolve(out, "../brand/logo.svg"), "utf8");
  const hero = (await fs.readFile(shot("hero"))).toString("base64");
  await page.setViewportSize({ width: 1280, height: 640 });
  await page.setContent(`<!doctype html><html><body style="margin:0;width:1280px;height:640px;overflow:hidden;
    font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;background:#0B3F39;color:#fff;position:relative">
    <div style="position:absolute;left:72px;top:96px;width:470px">
      <div style="width:112px;height:112px">${logo}</div>
      <h1 style="font-size:52px;line-height:1.05;margin:28px 0 18px;letter-spacing:-0.5px">Archive Explorer</h1>
      <p style="font-size:25px;line-height:1.4;margin:0;color:#CDEDE6">Browse and search your whole WhatsApp history. Jump years back in one click. Runs only on your computer.</p>
    </div>
    <img src="data:image/png;base64,${hero}" style="position:absolute;left:590px;top:96px;width:760px;border-radius:14px;box-shadow:0 20px 60px rgba(0,0,0,.45)">
  </body></html>`);
  await page.screenshot({ path: path.resolve(out, "../brand/social-preview.png") });
});
