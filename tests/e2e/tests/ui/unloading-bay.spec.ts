import { expect, json, test } from "../../src/fixtures";
import { roomyStoringTank, type Run } from "../../src/processing";
import { UnloadingBayPage } from "../../src/pages/UnloadingBayPage";

// The deployed frontend's Unloading Bay against staging. Each test takes a fresh bowser from the
// MCC (dispatched through intake's API, as the MCC would have done), works the screen as the
// factory intake officer, and reads the result back from processing's API.

test.describe("the Unloading Bay", () => {
  test("a factory officer unloads a bowser that intake dispatched", async ({ signInAs, newDispatch, serviceAs }) => {
    const factory = await serviceAs("processing", "factory");
    const dispatch = await newDispatch(30);
    const tank = await roomyStoringTank(factory, dispatch.litres);
    const quantity = Math.floor(dispatch.litres);

    const page = await signInAs("factory");
    const bay = new UnloadingBayPage(page);

    await bay.open();
    await bay.dispatchNote.fill(dispatch.reference);

    // Processing asks intake about the note while the officer types.
    await expect(bay.hint(`Dispatch ${dispatch.reference} found`)).toBeVisible();
    await expect(bay.hint("not yet unloaded")).toBeVisible();

    await bay.confirmSenses();
    await bay.storingTank.selectOption(tank.id);
    await bay.quantity.fill(String(quantity));
    await bay.temperature.fill("4");

    await expect(bay.recordButton()).toHaveText(`Record Unload - ${quantity} KG`);
    await bay.recordButton().click();

    await expect(bay.success(`Unloaded ${dispatch.reference} to ${tank.code} - ${quantity} KG - Quality status: Pending`)).toBeVisible();

    const run = await json<Run>(await factory.get(`/api/processing-runs/${dispatch.reference}`));
    expect(run).toMatchObject({ storingTankCode: tank.code, quantityKg: quantity, state: "AwaitingLabResult" });
  });

  test("the load cannot be recorded until every sensory check is confirmed", async ({ signInAs, newDispatch, serviceAs }) => {
    const dispatch = await newDispatch(30);
    const tank = await roomyStoringTank(await serviceAs("processing", "factory"), dispatch.litres);

    const page = await signInAs("factory");
    const bay = new UnloadingBayPage(page);

    await bay.open();
    await bay.dispatchNote.fill(dispatch.reference);
    await expect(bay.hint(`Dispatch ${dispatch.reference} found`)).toBeVisible();
    await bay.storingTank.selectOption(tank.id);
    await bay.quantity.fill("10");
    await bay.temperature.fill("4");

    await bay.sense("Smell OK").check();
    await bay.sense("Colour OK").check();
    await expect(bay.recordButton()).toBeDisabled();

    await bay.sense("Taste OK").check();
    await expect(bay.recordButton()).toBeEnabled();
  });

  test("more than the bowser carried cannot be recorded", async ({ signInAs, newDispatch, serviceAs }) => {
    const dispatch = await newDispatch(30);
    const tank = await roomyStoringTank(await serviceAs("processing", "factory"), dispatch.litres + 100);

    const page = await signInAs("factory");
    const bay = new UnloadingBayPage(page);

    await bay.open();
    await bay.dispatchNote.fill(dispatch.reference);
    await expect(bay.hint(`Dispatch ${dispatch.reference} found`)).toBeVisible();
    await bay.confirmSenses();
    await bay.storingTank.selectOption(tank.id);
    await bay.temperature.fill("4");

    await bay.quantity.fill(String(Math.ceil(dispatch.litres) + 50));
    await expect(bay.recordButton()).toBeDisabled();

    await bay.quantity.fill(String(Math.floor(dispatch.litres)));
    await expect(bay.recordButton()).toBeEnabled();
  });

  test("a dispatch note intake never issued is flagged and cannot be recorded", async ({ signInAs }) => {
    const page = await signInAs("factory");
    const bay = new UnloadingBayPage(page);

    await bay.open();
    await bay.dispatchNote.fill("DN-20260101-99");
    await bay.confirmSenses();
    await bay.temperature.fill("4");
    await bay.quantity.fill("10");

    await expect(page.locator(".field__hint--warning")).toBeVisible();
    await expect(bay.recordButton()).toBeDisabled();
  });

  test("a part-unloaded bowser shows what is still on it", async ({ signInAs, newDispatch, serviceAs }) => {
    const dispatch = await newDispatch(40);
    const tank = await roomyStoringTank(await serviceAs("processing", "factory"), dispatch.litres);
    const first = 15;
    const remaining = dispatch.litres - first;

    const page = await signInAs("factory");
    const bay = new UnloadingBayPage(page);

    await bay.open();
    await bay.dispatchNote.fill(dispatch.reference);
    await expect(bay.hint(`Dispatch ${dispatch.reference} found`)).toBeVisible();
    await bay.confirmSenses();
    await bay.storingTank.selectOption(tank.id);
    await bay.quantity.fill(String(first));
    await bay.temperature.fill("4");
    await bay.recordButton().click();

    await expect(bay.success(`Remaining for ${dispatch.reference}: ${remaining.toFixed(0)} KG can still be unloaded to another tank`)).toBeVisible();

    // The same note again: the bay now shows a partial unload rather than a fresh bowser.
    await bay.open();
    await bay.dispatchNote.fill(dispatch.reference);

    await expect(page.getByText("Partial Unload")).toBeVisible();
    await expect(page.getByText(`Already: ${first} KG, Remaining: ${remaining.toFixed(0)} KG`)).toBeVisible();
  });
});
