import { defineConfig, devices } from "@playwright/test";
import os from "node:os";
import path from "node:path";

// End-to-end tests run the real executable against a synthetic demo archive (never real chats).
// EXPLORER_BIN: path to a published binary; default: `dotnet run` of the source.
const port = 18780;
const demo = path.join(os.tmpdir(), "wae-e2e-demo");
const root = path.resolve(import.meta.dirname, "../..");
const explorer = process.env.EXPLORER_BIN
  ? `"${process.env.EXPLORER_BIN}"`
  : `dotnet run --project "${path.join(root, "src/Explorer")}" -c Release --`;

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: { baseURL: `http://127.0.0.1:${port}`, trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], locale: "en-US" } }],
  webServer: {
    command:
      `dotnet run --project "${path.join(root, "src/DemoArchive")}" -c Release -- "${demo}" --chats 12 --messages 300 && ` +
      `${explorer} "${demo}" --no-browser --no-cache --port ${port}`,
    url: `http://127.0.0.1:${port}/api/status`,
    timeout: 240_000,
    reuseExistingServer: false,
    env: { NEXT_TELEMETRY_DISABLED: "1" },
  },
});
