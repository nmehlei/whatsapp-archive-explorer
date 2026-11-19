import { test } from "@playwright/test";
import path from "node:path";

// Regenerates the README screenshots from the synthetic demo archive: SCREENSHOTS=1 npm run e2e -- screenshots
const out = path.resolve(import.meta.dirname, "../../../docs/screenshots");

test.skip(!process.env.SCREENSHOTS, "only when regenerating screenshots");
test.use({ viewport: { width: 1280, height: 760 } });

for (const scheme of ["light", "dark"] as const) {
  test(`screenshots ${scheme}`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: scheme });
    await page.goto("/");
    await page.locator(".chat-row").first().waitFor();
    await page.getByLabel("Search chats and messages").fill("lighthouse");
    await page.locator(".hit").click();
    await page.locator(".bubble.highlighted").waitFor();
    await page.screenshot({ path: path.join(out, `search-${scheme}.png`) });
    if (scheme === "light") {
      await page.getByLabel("Search chats and messages").fill("Climbing crew");
      await page.locator(".chat-row", { hasText: "Climbing crew" }).click();
      await page.getByLabel("Search chats and messages").fill("");
      await page.waitForTimeout(800);
      await page.screenshot({ path: path.join(out, "group-chat.png") });
    }
  });
}
