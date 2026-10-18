import type { Locator, Page } from "@playwright/test";
import { openInApp } from "../fixtures";

// The factory's Unloading Bay (/processing/unloads): a bowser's dispatch note is checked against
// intake, the sensory checks are confirmed and the load goes into a storing tank.
export class UnloadingBayPage {
  readonly heading: Locator;
  readonly dispatchNote: Locator;
  readonly storingTank: Locator;
  readonly quantity: Locator;
  readonly temperature: Locator;

  constructor(private readonly page: Page) {
    // The top bar repeats the title; the page heading is the one in the main landmark.
    this.heading = page.getByRole("main").getByRole("heading", { name: "Unloading Bay" });
    this.dispatchNote = page.getByLabel("Dispatch Note");
    this.storingTank = page.getByLabel("Storing Tank");
    this.quantity = page.getByLabel("Quantity KG");
    this.temperature = page.getByLabel("Temperature °C");
  }

  async open() {
    await openInApp(this.page, "/processing/unloads");
    await this.heading.waitFor();
  }

  sense(name: "Smell OK" | "Colour OK" | "Taste OK") {
    return this.page.getByLabel(name);
  }

  async confirmSenses() {
    for (const name of ["Smell OK", "Colour OK", "Taste OK"] as const) {
      await this.sense(name).check();
    }
  }

  // Shows the answer processing got from intake about the dispatch note.
  hint(text: string | RegExp) {
    return this.page.locator(".field__hint").filter({ hasText: text });
  }

  // The button names the quantity, e.g. "Record Unload - 30 KG".
  recordButton() {
    return this.page.getByRole("button", { name: /^Record Unload/ });
  }

  success(text: string | RegExp) {
    return this.page.getByText(text);
  }
}
