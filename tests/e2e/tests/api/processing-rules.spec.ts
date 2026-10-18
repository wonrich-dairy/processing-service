import { expect, json, test, type Dispatch } from "../../src/fixtures";
import { endStage, newMixingTank, passQc, PASSING_QC, roomyStoringTank, startStage, unload, wellStockedStoringTank, type Run, type Stage, type Tank } from "../../src/processing";

// Processing's own rules, exercised on a real bowser load rather than on seeded rows: the run under
// test starts as a dispatch note from intake, so every rule is checked in the state the factory
// actually meets it in.

test.describe.configure({ mode: "serial" });

test.describe("processing rules on a live run", () => {
  let dispatch: Dispatch;
  let run: Run;
  let storingTank: Tank;
  let mixingTank: Tank;

  test.beforeAll(async ({ newDispatch, serviceAs }) => {
    const factory = await serviceAs("processing", "factory");

    dispatch = await newDispatch(40);
    storingTank = await roomyStoringTank(factory, dispatch.litres);
  });

  test.describe("checking a dispatch note against intake", () => {
    test("an unknown dispatch note is reported as not found", async ({ serviceAs }) => {
      const factory = await serviceAs("processing", "factory");
      const found = await json<{ exists: boolean; validFormat: boolean }>(await factory.get("/api/mcc-dispatches/validate/DN-20260101-99"));

      expect(found).toMatchObject({ exists: false, validFormat: true });
    });

    test("a reference that is not a dispatch note is reported as malformed", async ({ serviceAs }) => {
      const factory = await serviceAs("processing", "factory");
      const response = await factory.get("/api/mcc-dispatches/validate/NOT-A-DISPATCH");

      if (response.ok()) {
        expect((await response.json()).validFormat).toBe(false);
      } else {
        expect(response.status()).toBe(400);
      }
    });
  });

  test.describe("unloading", () => {
    test("more milk than the bowser carried is refused", async ({ serviceAs }) => {
      const response = await unload(await serviceAs("processing", "factory"), dispatch.reference, storingTank.id, dispatch.litres + 50);

      expect(response.status()).toBeGreaterThanOrEqual(400);
      expect(response.status()).toBeLessThan(500);
    });

    test("an unknown dispatch note cannot be unloaded", async ({ serviceAs }) => {
      const response = await unload(await serviceAs("processing", "factory"), "DN-20260101-99", storingTank.id, 10);

      expect(response.status()).toBeGreaterThanOrEqual(400);
      expect(response.status()).toBeLessThan(500);
    });

    test("the bowser unloads into a storing tank and waits for QC", async ({ serviceAs }) => {
      run = await json<Run>(await unload(await serviceAs("processing", "factory"), dispatch.reference, storingTank.id, dispatch.litres), 201);

      expect(run).toMatchObject({ state: "AwaitingLabResult", qualityTestStatus: "Pending" });
    });

    test("a fully unloaded bowser cannot be unloaded again", async ({ serviceAs }) => {
      const response = await unload(await serviceAs("processing", "factory"), dispatch.reference, storingTank.id, 1);

      expect(response.status()).toBeGreaterThanOrEqual(400);
      expect(response.status()).toBeLessThan(500);
    });
  });

  test.describe("allocating", () => {
    // Allocation works per storing tank: without a run named, the service takes the tank's latest
    // released run, whoever's it is. On shared staging tanks that is rarely this test's run, so
    // the QC gate is checked through the named-run path below, which is where it fails.

    test("QC passes and releases the run", async ({ serviceAs }) => {
      await passQc(await serviceAs("processing", "analyst"), dispatch.reference);

      const released = await json<Run>(await (await serviceAs("processing", "factory")).get(`/api/processing-runs/${dispatch.reference}`));
      expect(released.state).toBe("ReleasedForAllocation");

      mixingTank = await newMixingTank(await serviceAs("processing", "processing"));
    });

    test("a mixing tank that already holds a batch is refused", async ({ serviceAs }) => {
      const technician = await serviceAs("processing", "processing");
      const tanks = await json<Tank[]>(await technician.get("/api/tanks"));
      const occupied = tanks.find((one) => one.kind === "Mixing" && one.remainingKg > 0 && one.id !== mixingTank.id)!;

      const response = await technician.post("/api/tank-allocations", {
        data: { sourceStoringTankId: storingTank.id, destinationMixingTankId: occupied.id, quantityKg: 10, productType: "FM", processingRunId: run.id },
      });

      expect(response.status()).toBe(400);
      expect(await response.text()).toMatch(/already holds batch/);
    });

    test("the released milk is allocated to the empty mixing tank", async ({ serviceAs }) => {
      const technician = await serviceAs("processing", "processing");
      const allocation = await json<{ batchCode: string }>(
        await technician.post("/api/tank-allocations", {
          data: { sourceStoringTankId: storingTank.id, destinationMixingTankId: mixingTank.id, quantityKg: 10, productType: "FM", processingRunId: run.id },
        }),
        201,
      );

      expect(allocation.batchCode).toMatch(/^\d{1,3}-FM-[A-Z]$/);
    });
  });

  test.describe("stages", () => {
    test("only one stage runs at a time in a mixing tank", async ({ serviceAs }) => {
      const technician = await serviceAs("processing", "processing");
      const heating = await json<Stage>(await startStage(technician, mixingTank.id, run.id, "Heating"));

      const second = await startStage(technician, mixingTank.id, run.id, "Homogeniser");

      expect(second.status()).toBeGreaterThanOrEqual(400);
      expect(second.status()).toBeLessThan(500);
      expect(await second.text()).toMatch(/already in progress/);

      await json(await endStage(technician, heating.id, 55));
    });

    test("a finished stage cannot be run again out of order", async ({ serviceAs }) => {
      const technician = await serviceAs("processing", "processing");
      const homogeniser = await json<Stage>(await startStage(technician, mixingTank.id, run.id, "Homogeniser"));
      await json(await endStage(technician, homogeniser.id, 60));

      const again = await startStage(technician, mixingTank.id, run.id, "Homogeniser");

      expect(again.status()).toBeGreaterThanOrEqual(400);
      expect(again.status()).toBeLessThan(500);
      expect(await again.text()).toMatch(/Cannot start Homogeniser after Homogeniser/);
    });

    test("a stage cannot be ended twice", async ({ serviceAs }) => {
      const technician = await serviceAs("processing", "processing");
      const heating = await json<Stage>(await startStage(technician, mixingTank.id, run.id, "Heating"));
      await json(await endStage(technician, heating.id, 55));

      const again = await endStage(technician, heating.id, 56);

      expect(again.status()).toBeGreaterThanOrEqual(400);
      expect(again.status()).toBeLessThan(500);
    });

    // Boundary values for the heating limits, 40 to 65 °C inclusive. Heating may always be started
    // again, so each case is its own heating stage on the same tank.
    for (const [endTemperatureC, deviation] of [
      [39.9, true],
      [40, false],
      [65, false],
      [65.1, true],
    ] as const) {
      test(`heating ended at ${endTemperatureC} °C is ${deviation ? "a deviation" : "within limits"}`, async ({ serviceAs }) => {
        const technician = await serviceAs("processing", "processing");
        const stage = await json<Stage>(await startStage(technician, mixingTank.id, run.id, "Heating"));
        const ended = await json<Stage>(await endStage(technician, stage.id, endTemperatureC));

        expect(ended.isDeviation).toBe(deviation);
      });
    }

    // Boundary values for the pasteuriser, 80 to 85 °C inclusive. Each case walks heating and the
    // homogeniser first, because the order is enforced.
    for (const [endTemperatureC, deviation] of [
      [79.9, true],
      [80, false],
      [85, false],
      [85.1, true],
    ] as const) {
      test(`pasteurising ended at ${endTemperatureC} °C is ${deviation ? "a deviation" : "within limits"}`, async ({ serviceAs }) => {
        const technician = await serviceAs("processing", "processing");

        for (const [stageType, temperature] of [["Heating", 55], ["Homogeniser", 60]] as const) {
          const stage = await json<Stage>(await startStage(technician, mixingTank.id, run.id, stageType));
          await json(await endStage(technician, stage.id, temperature));
        }

        const pasteuriser = await json<Stage>(await startStage(technician, mixingTank.id, run.id, "Pasteuriser"));
        const ended = await json<Stage>(await endStage(technician, pasteuriser.id, endTemperatureC));

        expect(ended.isDeviation).toBe(deviation);
      });
    }

    // Known defect (docs/e2e-testing.md): the order check refuses going back but not skipping
    // ahead, so a batch can be pasteurised without being homogenised, or cooled and completed
    // without being pasteurised at all.
    test("the homogeniser cannot be skipped", async ({ serviceAs }) => {
      test.fail(true, "Defect: ProcessingStageService refuses earlier stages but lets a later one skip ahead");

      const technician = await serviceAs("processing", "processing");
      const heating = await json<Stage>(await startStage(technician, mixingTank.id, run.id, "Heating"));
      await json(await endStage(technician, heating.id, 55));

      const skipped = await startStage(technician, mixingTank.id, run.id, "Pasteuriser");

      // While the defect stands the stage starts; end it so the tank is not left mid-stage.
      if (skipped.ok()) {
        await endStage(technician, (await skipped.json()).id, 82);
      }

      expect(skipped.status()).toBe(400);
    });
  });
});

// Known defects, reported in docs/e2e-testing.md. test.fail() marks a test that is expected to fail
// while its defect stands, so the pipeline stays green; once the defect is fixed the test passes,
// Playwright reports that as unexpected, and the marker comes off. Each test opens its own mixing
// tank, so the defect it provokes cannot leak into the other workflows.
test.describe("known defect: a mixing tank can be filled past its capacity", () => {
  test("more than the mixing tank holds is refused", async ({ newDispatch, serviceAs }) => {
    test.fail(true, "Defect: TankAllocationService does not compare the quantity with the mixing tank's capacity");

    const factory = await serviceAs("processing", "factory");
    const technician = await serviceAs("processing", "processing");
    const dispatch = await newDispatch(30);
    const mixing = await newMixingTank(technician);

    // The source must hold more than the mixing tank does, or the source check refuses the
    // allocation first and this test would pass for the wrong reason.
    const tank = await wellStockedStoringTank(factory, mixing.capacityKg + 1, dispatch.litres);
    const released = await json<Run>(await unload(factory, dispatch.reference, tank.id, dispatch.litres), 201);
    await passQc(await serviceAs("processing", "analyst"), dispatch.reference);

    const response = await technician.post("/api/tank-allocations", {
      data: {
        sourceStoringTankId: tank.id,
        destinationMixingTankId: mixing.id,
        quantityKg: mixing.capacityKg + 1,
        productType: "FM",
        processingRunId: released.id,
      },
    });

    expect(response.status()).toBe(400);
  });
});

test.describe("known defect: naming the run skips the QC check", () => {
  test("milk still waiting for QC is refused even when the run is named", async ({ newDispatch, serviceAs }) => {
    test.fail(true, "Defect: TankAllocationService does not check QC status when processingRunId is given");

    const factory = await serviceAs("processing", "factory");
    const technician = await serviceAs("processing", "processing");
    const dispatch = await newDispatch(30);
    const tank = await roomyStoringTank(factory, dispatch.litres);
    const pending = await json<Run>(await unload(factory, dispatch.reference, tank.id, dispatch.litres), 201);
    const mixing = await newMixingTank(technician);

    const response = await technician.post("/api/tank-allocations", {
      data: { sourceStoringTankId: tank.id, destinationMixingTankId: mixing.id, quantityKg: 10, productType: "FM", processingRunId: pending.id },
    });

    expect(pending.qualityTestStatus).toBe("Pending");
    expect(response.status()).toBe(400);
  });
});

test.describe("a load that fails factory QC", () => {
  test("is held and never released for allocation", async ({ newDispatch, serviceAs }) => {
    const factory = await serviceAs("processing", "factory");
    const analyst = await serviceAs("processing", "analyst");
    const dispatch = await newDispatch(30);
    const tank = await roomyStoringTank(factory, dispatch.litres);

    await json(await unload(factory, dispatch.reference, tank.id, dispatch.litres), 201);
    await json(await analyst.post(`/api/quality-tests/${dispatch.reference}/start`));
    await json(
      await analyst.post(`/api/quality-tests/${dispatch.reference}/result`, {
        data: { ...PASSING_QC, fatPercent: 2.9, verdict: "Reject", failedParameter: "FatPercent", failedValue: "2.9" },
      }),
    );

    const held = await json<Run>(await factory.get(`/api/processing-runs/${dispatch.reference}`));

    expect(held.qualityVerdict).toBe("Reject");
    expect(held.state).not.toBe("ReleasedForAllocation");
  });
});
