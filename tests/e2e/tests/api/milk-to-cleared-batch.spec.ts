import { expect, json, test, type Dispatch } from "../../src/fixtures";
import { newMixingTank, passQc, roomyStoringTank, SAFE_END_TEMPERATURE, STAGES, startStage, endStage, unload, type Run, type Tank } from "../../src/processing";

// One delivery through all five services, with every hop between services made by the services
// themselves. The test acts only where a person would, and waits for what the system must do:
//
//   auth          issues each role's token; every service validates it on its own
//   intake        the MCC gate: consignment, quality panel, chilling tank, dispatch note
//   processing    looks the dispatch note up in intake over HTTP, unloads it, factory QC,
//                 allocates the milk as a batch, runs the four stages
//                   └─ Kafka ─> traceability   (milk allocated, stage recorded, processing completed)
//                   └─ Kafka ─> quality-lab    (processing completed: the batch joins the work queue)
//   quality-lab   the analyst records the panel and passes the batch
//                   └─ Kafka ─> traceability   (batch cleared)
//
// The steps share state, so they run in order and stop at the first failure. A retry starts the
// whole workflow again with a new dispatch note.

test.describe.configure({ mode: "serial" });

interface Checkpoint {
  checkpoint: string;
  state: string;
  records: { kind: string; data: { dispatchNumber?: string; stageType?: string } }[];
}

interface BatchRecord {
  status: string;
  checkpoints: Checkpoint[];
}

const ALLOCATED_KG = 10;

test.describe("a delivery from the MCC gate to a cleared factory batch", () => {
  let dispatch: Dispatch;
  let run: Run;
  let storingTank: Tank;
  let mixingTank: Tank;
  let batchCode = "";

  test("intake dispatches a bowser from the MCC", async ({ newDispatch }) => {
    dispatch = await newDispatch(30);

    expect(dispatch.reference).toMatch(/^DN-\d{8}-\d{2,}$/);
    expect(dispatch.litres).toBeGreaterThan(0);
  });

  test("processing finds the dispatch note in intake before unloading", async ({ serviceAs }) => {
    const factory = await serviceAs("processing", "factory");
    const found = await json<{ exists: boolean; validFormat: boolean; remainingKg: number; isFullyUnloaded: boolean }>(
      await factory.get(`/api/mcc-dispatches/validate/${dispatch.reference}`),
    );

    expect(found).toMatchObject({ exists: true, validFormat: true, isFullyUnloaded: false });
    expect(found.remainingKg).toBeCloseTo(dispatch.litres, 2);
  });

  test("the factory unloads the bowser into a storing tank", async ({ serviceAs }) => {
    const factory = await serviceAs("processing", "factory");

    storingTank = await roomyStoringTank(factory, dispatch.litres);
    run = await json<Run>(await unload(factory, dispatch.reference, storingTank.id, dispatch.litres), 201);

    expect(run).toMatchObject({
      dispatchNumber: dispatch.reference,
      storingTankCode: storingTank.code,
      state: "AwaitingLabResult",
      qualityTestStatus: "Pending",
    });

    // The bowser is now fully unloaded, as far as intake's dispatch note is concerned.
    const after = await json<{ isFullyUnloaded: boolean }>(await factory.get(`/api/mcc-dispatches/validate/${dispatch.reference}`));
    expect(after.isFullyUnloaded).toBe(true);
  });

  test("factory QC passes and releases the run for allocation", async ({ serviceAs }) => {
    await passQc(await serviceAs("processing", "analyst"), dispatch.reference);

    const released = await json<Run>(await (await serviceAs("processing", "factory")).get(`/api/processing-runs/${dispatch.reference}`));
    expect(released).toMatchObject({ state: "ReleasedForAllocation", qualityTestStatus: "Passed", qualityVerdict: "Accept" });
  });

  test("the technician allocates the milk as a batch and runs the four stages", async ({ serviceAs }) => {
    const technician = await serviceAs("processing", "processing");

    mixingTank = await newMixingTank(technician);

    const allocation = await json<{ batchCode: string; dispatchNumber: string; destinationMixingTankCode: string }>(
      await technician.post("/api/tank-allocations", {
        data: {
          sourceStoringTankId: storingTank.id,
          destinationMixingTankId: mixingTank.id,
          quantityKg: ALLOCATED_KG,
          productType: "FM",
          processingRunId: run.id,
        },
      }),
      201,
    );

    // <day of year>-<product line>-<letter>, e.g. 291-FM-A.
    expect(allocation.batchCode).toMatch(/^\d{1,3}-FM-[A-Z]$/);
    expect(allocation).toMatchObject({ dispatchNumber: dispatch.reference, destinationMixingTankCode: mixingTank.code });
    batchCode = allocation.batchCode;

    for (const stageType of STAGES) {
      const stage = await json<{ id: string }>(await startStage(technician, mixingTank.id, run.id, stageType));
      const ended = await json<{ isDeviation: boolean }>(await endStage(technician, stage.id, SAFE_END_TEMPERATURE[stageType]));

      expect(ended.isDeviation, `${stageType} ended at ${SAFE_END_TEMPERATURE[stageType]} °C`).toBe(false);
    }
  });

  test("traceability builds the batch record from the processing events", async ({ serviceAs }) => {
    const traceability = await serviceAs("traceability", "admin");

    await expect
      .poll(
        async () => {
          const response = await traceability.get(`/api/batches/${batchCode}`);
          return response.ok() ? stateOf(await response.json(), "Processing") : `HTTP ${response.status()}`;
        },
        { message: "the batch's Processing checkpoint in traceability", timeout: 60_000, intervals: [2_000] },
      )
      .toBe("Recorded");

    const record = await json<BatchRecord>(await traceability.get(`/api/batches/${batchCode}`));
    const processing = record.checkpoints.find((one) => one.checkpoint === "Processing")!;

    // Every event names the dispatch note intake issued: the batch traces back to the MCC.
    expect(new Set(processing.records.map((one) => one.data.dispatchNumber))).toEqual(new Set([dispatch.reference]));
    expect(processing.records.map((one) => one.kind)).toContain("MilkAllocatedToMixingTank");
    expect(new Set(processing.records.map((one) => one.data.stageType).filter(Boolean))).toEqual(new Set(STAGES));
  });

  test("quality-lab receives the finished batch in its work queue", async ({ serviceAs }) => {
    const lab = await serviceAs("qualityLab", "analyst");

    await expect
      .poll(
        async () => {
          const response = await lab.get(`/api/panels/batch/${batchCode}`);
          return response.ok() ? (await response.json()).status : `HTTP ${response.status()}`;
        },
        { message: "the batch in the quality-lab work queue", timeout: 60_000, intervals: [2_000] },
      )
      .toBe("AwaitingPanel");

    const item = await json<{ dispatchNumber: string; productLine: string; storingTankCode: string }>(await lab.get(`/api/panels/batch/${batchCode}`));
    expect(item).toMatchObject({ dispatchNumber: dispatch.reference, productLine: "FM", storingTankCode: mixingTank.code });
  });

  test("the analyst records the lab panel and passes the batch", async ({ serviceAs }) => {
    const lab = await serviceAs("qualityLab", "analyst");

    const panel = await json<{ hasOutOfSpecFlags: boolean }>(
      await lab.post(`/api/panels/${batchCode}`, { data: { fatPercent: 3.8, lactometerReading: 29, temperatureCelsius: 27, ph: 6.7 } }),
    );
    expect(panel.hasOutOfSpecFlags).toBe(false);

    await json(
      await lab.post(`/api/panels/${batchCode}/sensory`, {
        data: { taste: "Acceptable", smell: "Acceptable", colour: "Acceptable", appearance: "Acceptable" },
      }),
      201,
    );

    const determination = await json<{ result: string; isActive: boolean }>(
      await lab.post(`/api/panels/${batchCode}/determination`, { data: { result: "Pass" } }),
    );
    expect(determination).toMatchObject({ result: "Pass", isActive: true });
  });

  test("traceability shows the batch cleared", async ({ serviceAs }) => {
    const traceability = await serviceAs("traceability", "admin");

    await expect
      .poll(async () => (await json<BatchRecord>(await traceability.get(`/api/batches/${batchCode}`))).status, {
        message: "the batch's status in traceability",
        timeout: 60_000,
        intervals: [2_000],
      })
      .toBe("Cleared");

    const record = await json<BatchRecord>(await traceability.get(`/api/batches/${batchCode}`));
    expect(stateOf(record, "Processing")).toBe("Recorded");
    expect(stateOf(record, "Lab")).toBe("Recorded");

    test.info().annotations.push({ type: "batch", description: `${batchCode} from ${dispatch.reference} via ${mixingTank.code}` });
  });
});

function stateOf(record: BatchRecord, checkpoint: string): string | undefined {
  return record.checkpoints.find((one) => one.checkpoint === checkpoint)?.state;
}
