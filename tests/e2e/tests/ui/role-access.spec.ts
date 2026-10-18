import { expect, openInApp, test } from "../../src/fixtures";
import { accounts, password, type Account } from "../../src/config";

// Signing in to the deployed frontend, and which processing screens each role is shown.

test.describe("signing in to the factory screens", () => {
  test("a processing technician signs in through the form and lands on the factory dashboard", async ({ page }) => {
    await page.goto("/");
    await page.getByLabel("Employee ID").fill(accounts.processing.userName);
    await page.getByLabel("PIN / Password").fill(password);
    await page.getByRole("button", { name: "Log In" }).click();

    await expect(page).toHaveURL(/\/processing$/);
  });

  test("a wrong password is refused with a message", async ({ page }) => {
    await page.goto("/");
    await page.getByLabel("Employee ID").fill(accounts.processing.userName);
    await page.getByLabel("PIN / Password").fill("not-the-password");
    await page.getByRole("button", { name: "Log In" }).click();

    await expect(page.getByRole("alert")).toHaveText("That username and password were not accepted.");
  });
});

test.describe("processing screens by role", () => {
  const refused: { account: Account; path: string }[] = [
    { account: "officer", path: "/processing/unloads" },
    { account: "manager", path: "/processing/unloads" },
    { account: "officer", path: "/processing/tanks/new" },
    { account: "factory", path: "/processing/tanks/new" },
  ];

  for (const { account, path } of refused) {
    test(`${account} is not shown ${path}`, async ({ signInAs }) => {
      const page = await signInAs(account);

      await openInApp(page, path);

      await expect(page.getByRole("heading", { name: "Not available to you" })).toBeVisible();
    });
  }

  const allowed: { account: Account; path: string; heading: string }[] = [
    { account: "factory", path: "/processing/unloads", heading: "Unloading Bay" },
    { account: "processing", path: "/processing/unloads", heading: "Unloading Bay" },
    { account: "production", path: "/processing/unloads", heading: "Unloading Bay" },
  ];

  for (const { account, path, heading } of allowed) {
    test(`${account} can open ${path}`, async ({ signInAs }) => {
      const page = await signInAs(account);

      await openInApp(page, path);

      await expect(page.getByRole("main").getByRole("heading", { name: heading })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Not available to you" })).toHaveCount(0);
    });
  }
});
