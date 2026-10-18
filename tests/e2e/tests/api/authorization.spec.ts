import type { APIRequestContext } from "@playwright/test";
import { expect, test } from "../../src/fixtures";
import { urls, type Account } from "../../src/config";

// Who may do what in processing, checked on staging with real tokens from the auth service. The
// matrix follows the [Authorize(Roles = ...)] attributes on processing's controllers.
//
// No test here changes data. ASP.NET Core authorizes before it reads the body, so the request is
// built to fail just after the policy: an empty body, or a dispatch note that does not exist. A
// refused role gets 403 from the policy; an allowed role gets through it and is stopped by the
// service with a 4xx that is not 401 or 403.

interface Route {
  name: string;
  method: "get" | "post";
  path: string;
  allowed: Account[];
  refused: Account[];
}

const UNKNOWN_DISPATCH = "DN-20260101-99";

const routes: Route[] = [
  {
    name: "check a dispatch note against intake",
    method: "get",
    path: `/api/mcc-dispatches/validate/${UNKNOWN_DISPATCH}`,
    allowed: ["admin", "production", "processing", "factory"],
    refused: ["manager", "officer", "analyst"],
  },
  {
    name: "unload a bowser",
    method: "post",
    path: "/api/processing-runs/unload",
    allowed: ["admin", "production", "processing", "factory"],
    refused: ["manager", "officer", "analyst"],
  },
  {
    name: "start factory QC",
    method: "post",
    path: `/api/quality-tests/${UNKNOWN_DISPATCH}/start`,
    allowed: ["admin", "production", "analyst"],
    refused: ["factory", "processing", "manager", "officer"],
  },
  {
    name: "allocate milk to a mixing tank",
    method: "post",
    path: "/api/tank-allocations",
    allowed: ["admin", "production", "processing"],
    refused: ["factory", "analyst", "manager", "officer"],
  },
  {
    name: "start a processing stage",
    method: "post",
    path: "/api/processing-stages/start",
    allowed: ["admin", "production", "processing"],
    refused: ["factory", "analyst", "manager", "officer"],
  },
  {
    name: "add a factory tank",
    method: "post",
    path: "/api/tanks",
    allowed: ["admin", "production", "processing", "factory"],
    refused: ["manager", "officer", "analyst"],
  },
];

function call(http: APIRequestContext, route: Route, token?: string) {
  const headers: Record<string, string> = token ? { Authorization: `Bearer ${token}` } : {};

  return route.method === "get"
    ? http.get(`${urls.processing}${route.path}`, { headers })
    : http.post(`${urls.processing}${route.path}`, { headers, data: {} });
}

test.describe("role-based access to the processing API", () => {
  for (const route of routes) {
    test.describe(route.name, () => {
      for (const account of route.allowed) {
        test(`${account} is let through the policy`, async ({ request, tokenOf }) => {
          const response = await call(request, route, (await tokenOf(account)).accessToken);

          expect([401, 403]).not.toContain(response.status());
          expect(response.status()).toBeLessThan(500);
        });
      }

      for (const account of route.refused) {
        test(`${account} is refused with 403`, async ({ request, tokenOf }) => {
          const response = await call(request, route, (await tokenOf(account)).accessToken);

          expect(response.status()).toBe(403);
        });
      }

      test("no token is refused with 401", async ({ request }) => {
        const response = await call(request, route);

        expect(response.status()).toBe(401);
      });
    });
  }
});

test.describe("tokens processing will not accept", () => {
  const route = routes.find((one) => one.path === "/api/tank-allocations")!;

  // An intake officer edits their own token to claim the technician role. The payload changes and
  // the signature does not, so processing must reject the token rather than trust the claim.
  test("a token whose role claim was edited is refused with 401", async ({ request, tokenOf }) => {
    const [header, payload, signature] = (await tokenOf("officer")).accessToken.split(".");
    const claims = JSON.parse(Buffer.from(payload, "base64url").toString("utf8"));
    const roleClaim = Object.keys(claims).find((key) => key.endsWith("/role") || key === "role")!;

    claims[roleClaim] = "ProcessingTechnician";
    const forged = [header, Buffer.from(JSON.stringify(claims)).toString("base64url"), signature].join(".");

    expect(roleClaim).toBeTruthy();
    expect((await call(request, route, forged)).status()).toBe(401);
  });

  test("a token with its signature stripped is refused with 401", async ({ request, tokenOf }) => {
    const [header, payload] = (await tokenOf("processing")).accessToken.split(".");

    expect((await call(request, route, `${header}.${payload}.`)).status()).toBe(401);
  });

  test("a malformed bearer value is refused with 401", async ({ request }) => {
    expect((await call(request, route, "not-a-jwt")).status()).toBe(401);
  });
});
