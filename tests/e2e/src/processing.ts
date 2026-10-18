import type { APIRequestContext } from "@playwright/test";
import { expect, json } from "./fixtures";

// The slices of the processing contracts the suite reads, and the steps it repeats.

export interface Tank {
  id: string;
  code: string;
  kind: "Storing" | "Mixing";
  status: string;
  capacityKg: number;
  remainingKg: number;
  availableKg: number;
}

export interface Run {
  id: string;
  dispatchNumber: string;
  storingTankCode: string;
  quantityKg: number;
  state: string;
  qualityTestStatus: string;
  qualityVerdict: string | null;
}

export interface Stage {
  id: string;
  stageType: string;
  isDeviation: boolean;
}

export const STAGES = ["Heating", "Homogeniser", "Pasteuriser", "Cooling"] as const;

// End temperatures inside every stage's limits (ProcessingStageService.IsDeviation).
export const SAFE_END_TEMPERATURE: Record<(typeof STAGES)[number], number> = {
  Heating: 55,
  Homogeniser: 60,
  Pasteuriser: 82,
  Cooling: 4,
};

// A factory QC panel that passes, as the analyst records it in processing.
export const PASSING_QC = {
  fatPercent: 4.2,
  rawLactometerReading: 29,
  temperatureCelsius: 27,
  waterPercent: 0,
  kqColour: "Blue",
  alcoholOutcomesJson: JSON.stringify({ Alcohol80: "Negative" }),
  alcoholResult: "Stable",
  smellOk: true,
  colourOk: true,
  tasteOk: true,
  verdict: "Accept",
  snf: 8.89,
  ts: 13.09,
  ph: 6.7,
};

// Staging is shared: the milk goes into whichever active storing tank has room for it.
export async function roomyStoringTank(http: APIRequestContext, kilograms: number): Promise<Tank> {
  const tanks = await json<Tank[]>(await http.get("/api/tanks"));
  const tank = tanks
    .filter((one) => one.kind === "Storing" && one.status === "Active" && one.availableKg >= kilograms)
    .sort((a, b) => b.availableKg - a.availableKg)[0];

  expect(tank, `no active storing tank on staging has ${kilograms} kg free`).toBeTruthy();

  return tank;
}

// An empty mixing tank. A mixing tank only takes a batch when it is empty, and processing never
// empties one after a run (see docs/e2e-testing.md, findings), so a workflow reuses an empty one
// if there is any and otherwise opens the next free code. Codes run MT-01 to MT-99.
export async function newMixingTank(http: APIRequestContext): Promise<Tank> {
  const tanks = await json<Tank[]>(await http.get("/api/tanks"));
  const empty = tanks.find((one) => one.kind === "Mixing" && one.status === "Active" && one.remainingKg === 0);

  if (empty) {
    return empty;
  }

  const taken = new Set(tanks.filter((one) => one.kind === "Mixing").map((one) => Number(one.code.replace(/\D/g, ""))));
  const free = Array.from({ length: 99 }, (_, index) => index + 1).find((number) => !taken.has(number));

  expect(free, "every mixing tank code MT-01 to MT-99 is taken on staging").toBeTruthy();

  return json<Tank>(await http.post("/api/tanks", { data: { code: `MT${free}`, kind: "Mixing", capacityKg: 300 } }), 201);
}

export async function unload(http: APIRequestContext, dispatchNumber: string, storingTankId: string, quantityKg: number) {
  return http.post("/api/processing-runs/unload", {
    data: { dispatchNumber, storingTankId, quantityKg, temperatureC: 4, smellOk: true, colourOk: true, tasteOk: true },
  });
}

export async function passQc(http: APIRequestContext, dispatchNumber: string) {
  await json(await http.post(`/api/quality-tests/${dispatchNumber}/start`));
  await json(await http.post(`/api/quality-tests/${dispatchNumber}/result`, { data: PASSING_QC }));
}

export async function startStage(http: APIRequestContext, mixingTankId: string, processingRunId: string, stageType: string) {
  return http.post("/api/processing-stages/start", { data: { mixingTankId, processingRunId, stageType } });
}

export async function endStage(http: APIRequestContext, stageId: string, endTemperatureC: number) {
  return http.post(`/api/processing-stages/${stageId}/end`, { data: { endTemperatureC } });
}

// A storing tank already holding at least this much milk, for a test that must draw more than one
// bowser's worth. Allocation draws on the tank, not on one run, so the milk can be anyone's.
export async function wellStockedStoringTank(http: APIRequestContext, kilograms: number, room: number): Promise<Tank> {
  const tanks = await json<Tank[]>(await http.get("/api/tanks"));
  const tank = tanks
    .filter((one) => one.kind === "Storing" && one.status === "Active" && one.remainingKg >= kilograms && one.availableKg >= room)
    .sort((a, b) => b.remainingKg - a.remainingKg)[0];

  expect(tank, `no active storing tank on staging holds ${kilograms} kg with ${room} kg free`).toBeTruthy();

  return tank;
}
