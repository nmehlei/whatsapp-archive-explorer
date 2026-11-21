import { defineConfig, devices } from "@playwright/test";
import os from "node:os";
import path from "node:path";

// README screenshots: the real executable against the scripted showcase archive (fictional people only).
// Run: npm run screenshots   (writes docs/screenshots/*.png)
const port = 18781;
const archive = path.join(os.tmpdir(), "wae-showcase");
const root = path.resolve(import.meta.dirname, "../..");
const explorer = process.env.EXPLORER_BIN
  ? `"${process.env.EXPLORER_BIN}"`
  : `dotnet run --project "${path.join(root, "src/Explorer")}" -c Release --`;

export default defineConfig({
  testDir: "./screenshots",
  fullyParallel: false,
  workers: 1,
  timeout: 240_000,
  reporter: "list",
  use: {
    ...devices["Desktop Chrome"],
    baseURL: `http://127.0.0.1:${port}`,
    locale: "en-GB",
    timezoneId: "Europe/Berlin",
    viewport: { width: 1280, height: 800 },
    deviceScaleFactor: 2,
  },
  webServer: {
    command:
      `rm -rf "${archive}" && dotnet run --project "${path.join(root, "src/DemoArchive")}" -c Release -- "${archive}" --showcase && ` +
      `${explorer} "${archive}" --no-browser --no-cache --port ${port}`,
    url: `http://127.0.0.1:${port}/api/status`,
    timeout: 300_000,
    reuseExistingServer: false,
    env: { NEXT_TELEMETRY_DISABLED: "1" },
  },
});
