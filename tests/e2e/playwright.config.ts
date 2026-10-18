import { defineConfig, devices } from "@playwright/test";
import { urls } from "./src/config";

const ci = Boolean(process.env.CI);

// Runs against staging: the deployed frontend and the five deployed services. Nothing is started
// locally. Azure App Services can take a while to wake, hence the generous timeouts.
export default defineConfig({
  testDir: "./tests",
  timeout: 120_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  forbidOnly: ci,
  // Staging is reached over the internet: one retry absorbs a dropped connection. A test that
  // passes on retry is still reported as flaky, not hidden.
  retries: 1,
  // One worker: the workflows share staging tanks (an empty mixing tank, the next free tank code),
  // and two workers would race for the same one.
  workers: 1,

  reporter: [
    ["list"],
    ["html", { outputFolder: "reports/html", open: "never" }],
    ["junit", { outputFile: "reports/junit.xml" }],
  ],

  use: {
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },

  projects: [
    {
      // HTTP only: the services' own APIs, each called with a token from the auth service.
      name: "api",
      testDir: "./tests/api",
    },
    {
      // The processing screens of the deployed frontend, in Chrome.
      name: "ui",
      testDir: "./tests/ui",
      use: { ...devices["Desktop Chrome"], channel: "chrome", baseURL: urls.web },
    },
  ],
});
