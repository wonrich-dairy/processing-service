import { expect, request, test as base, type APIRequestContext, type APIResponse, type Page } from "@playwright/test";
import { accounts, password, urls, type Account, type Service } from "./config";

interface TokenResponse {
  accessToken: string;
  expiresAtUtc: string;
  userName: string;
  displayName: string;
  role: string;
  facility: string | null;
}

export interface Dispatch {
  reference: string;
  litres: number;
  consignment: string;
}

type ServiceAs = (service: Service, account: Account) => Promise<APIRequestContext>;

interface WorkerFixtures {
  serviceAs: ServiceAs;
  tokenOf: (account: Account) => Promise<TokenResponse>;
  newDispatch: (kilograms?: number) => Promise<Dispatch>;
}

interface TestFixtures {
  signInAs: (account: Account) => Promise<Page>;
}

// The frontend keeps its session in sessionStorage under this key (src/auth/session.ts).
const SESSION_KEY = "wonrich.session";

export const test = base.extend<TestFixtures, WorkerFixtures>({
  // Each role logs in once per worker, through the auth service, like any client would.
  tokenOf: [
    async ({}, use) => {
      const tokens = new Map<Account, Promise<TokenResponse>>();

      await use((account) => {
        if (!tokens.has(account)) {
          tokens.set(account, login(accounts[account].userName));
        }

        return tokens.get(account)!;
      });
    },
    { scope: "worker" },
  ],

  // An HTTP client for one service, carrying one role's token. Every service checks that token
  // itself, so a 401 from any of them means they disagree with auth about keys or claims.
  serviceAs: [
    async ({ tokenOf }, use) => {
      const contexts = new Map<string, APIRequestContext>();

      await use(async (service, account) => {
        const key = `${service}:${account}`;

        if (!contexts.has(key)) {
          const { accessToken } = await tokenOf(account);

          contexts.set(
            key,
            await request.newContext({
              baseURL: urls[service],
              extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` },
              timeout: 60_000,
            }),
          );
        }

        return contexts.get(key)!;
      });

      await Promise.all([...contexts.values()].map((context) => context.dispose()));
    },
    { scope: "worker" },
  ],

  // A bowser load as processing receives it: a consignment taken in at the MCC gate, tested,
  // poured and dispatched by the intake service. Each call returns a new dispatch note, so no two
  // tests unload the same bowser. One test society per worker, deactivated at the end.
  newDispatch: [
    async ({ serviceAs }, use) => {
      const manager = await serviceAs("intake", "manager");
      const officer = await serviceAs("intake", "officer");
      const code = `Z${letters(5)}`;

      const society = await json<{ id: string }>(
        await manager.post("/api/societies", {
          data: { code, name: `E2E Society ${code}`, canLabelPrefix: code, contactPerson: "Processing E2E" },
        }),
        201,
      );

      await use(async (kilograms = 30) => {
        const tank = await json<{ code: string }>(
          await manager.post("/api/tanks", { data: { code: `P${letters(5)}`, name: "E2E intake tank", capacityLitres: 1000 } }),
          201,
        );
        const consignment = await json<{ reference: string }>(
          await officer.post("/api/consignments", {
            data: { societyId: society.id, cans: [{ canNumber: 1, quantityKg: kilograms }], arrivalAtLocal: morningArrival() },
          }),
          201,
        );

        await json(
          await officer.post(`/api/consignments/${consignment.reference}/quality-test`, {
            data: {
              fatPercent: 4.2,
              rawLactometerReading: 29,
              temperatureCelsius: 27,
              waterPercent: 0,
              kqColour: "Blue",
              alcoholOutcomes: { Alcohol80: "Negative" },
              verdict: "Accept",
            },
          }),
          201,
        );

        const poured = await json<{ tank: { availableQuantityLitres: number } }>(
          await officer.post(`/api/tanks/${tank.code}/pours`, { data: { consignmentReference: consignment.reference } }),
          201,
        );

        const note = await json<{ reference: string; totalQuantityLitres: number }>(
          await manager.post("/api/dispatch-notes", {
            data: {
              bowserRegistration: "WP LK-4521",
              driverName: "E2E Driver",
              draws: [{ tankCode: tank.code, quantityLitres: poured.tank.availableQuantityLitres }],
              fatPercent: 4.2,
              snf: 8.89,
              kqColour: "Blue",
              stabilityGrade: "Stable",
              temperatureCelsius: 4,
            },
          }),
          201,
        );

        return { reference: note.reference, litres: note.totalQuantityLitres, consignment: consignment.reference };
      });

      await manager.post(`/api/societies/${society.id}/deactivate`);
    },
    { scope: "worker" },
  ],

  // A page already signed in as the given role. storageState does not capture sessionStorage, so
  // the session is written there before the app's first script runs. The sign-in form itself is
  // tested in tests/ui/role-access.spec.ts.
  signInAs: async ({ page, tokenOf }, use) => {
    await use(async (account) => {
      const tokens = await tokenOf(account);
      const session = {
        accessToken: tokens.accessToken,
        expiresAtUtc: tokens.expiresAtUtc,
        userName: tokens.userName,
        displayName: tokens.displayName ?? tokens.userName,
        role: tokens.role ?? "",
        facility: tokens.facility ?? null,
      };

      await page.addInitScript(([key, value]) => window.sessionStorage.setItem(key, value), [SESSION_KEY, JSON.stringify(session)] as const);

      return page;
    });
  },
});

export { expect };

export async function login(userName: string, secret = password): Promise<TokenResponse> {
  const http = await request.newContext({ baseURL: urls.auth, timeout: 60_000 });

  try {
    const response = await http.post("/api/auth/login", { data: { userName, password: secret } });

    if (!response.ok()) {
      throw new Error(`Login as ${userName} failed with ${response.status()}: ${await response.text()}`);
    }

    return (await response.json()) as TokenResponse;
  } finally {
    await http.dispose();
  }
}

// Checks the status first, so a failure shows the service's own error body rather than a
// confusing "cannot read property" further down the test.
export async function json<T = unknown>(response: APIResponse, status: number | number[] = 200): Promise<T> {
  const body = await response.text();
  const expected = Array.isArray(status) ? status : [status];

  expect(expected, `${response.url()} answered ${response.status()}: ${body.slice(0, 500)}`).toContain(response.status());

  return (body ? JSON.parse(body) : undefined) as T;
}

export function letters(length: number): string {
  return Array.from({ length }, () => "ABCDEFGHIJKLMNOPQRSTUVWXYZ"[Math.floor(Math.random() * 26)]).join("");
}

// Staging intake keeps the real 16:00 cutoff. An officer may correct the captured arrival time
// backwards, so arrivals are recorded at 08:00 at the centre (Colombo, UTC+5:30), or a few minutes
// ago when the suite runs before eight.
function morningArrival(): string {
  const now = new Date(Date.now() + 5.5 * 60 * 60 * 1000);
  const eight = new Date(`${now.toISOString().slice(0, 10)}T08:00:00Z`);
  const arrival = now < eight ? new Date(now.getTime() - 5 * 60 * 1000) : eight;

  return arrival.toISOString().slice(0, 19);
}
