# End-to-end testing (Playwright)

SE3112 UI / E2E testing for the Processing Service. The suite is in `tests/e2e`; it runs against
staging after every staging deployment, beside the load test, and by hand.

## What it tests

Processing sits in the middle of the system: it takes its milk from intake and hands its batches
to quality-lab and traceability. The suite tests it where it meets the others, with every call
carrying a token from the auth service.

```
 intake ──HTTP── processing ──Kafka──> quality-lab ──Kafka──┐
   │                 │                                      ▼
   └─ dispatch note  └──────────────Kafka───────────────> traceability
```

| Spec | Tests | What it proves |
| --- | ---: | --- |
| `api/milk-to-cleared-batch` | 9 | One delivery through all five services: intake dispatches a bowser; processing finds the note in intake, unloads it, passes factory QC, allocates it as a batch and runs the four stages; traceability builds the batch from processing's events; quality-lab receives the batch over Kafka and the analyst passes it; traceability shows it cleared |
| `api/processing-rules` | 24 | Processing's rules on a live run: dispatch-note validation, unload limits, an occupied mixing tank, one stage at a time, no repeating a finished stage, heating (40–65 °C) and pasteuriser (80–85 °C) limits at their boundaries, a load that fails QC is held. Three known defects (below) |
| `api/authorization` | 51 | Six routes under the seven seeded roles, no token, a forged role claim, a stripped signature |
| `ui/unloading-bay` | 5 | The deployed frontend's Unloading Bay: unloading a bowser intake dispatched, the sensory checks, the quantity limit, an unknown note, a partial unload |
| `ui/role-access` | 9 | The sign-in form, and which roles see the factory screens |

### How a test is built

* **Real data, made the way the system makes it.** A test that needs a bowser gets one from
  intake's API (`newDispatch`): a consignment is registered at the MCC gate, tested, poured and
  dispatched. Processing then validates that note against intake for real.
* **Arrange through the API, act through the screen, assert through the API.** A UI test sets up
  its bowser with HTTP calls, works only the screen under test in Chrome, then reads the result
  back from processing's API.
* **Waiting for Kafka.** The test only acts where a person would; for what the services must do
  themselves (a batch reaching quality-lab, traceability recording it) it polls with
  `expect.poll` until the state appears or a minute passes. No fixed sleeps.
* **Authorization without side effects.** ASP.NET Core checks the role before it reads the body,
  so each request is built to fail just after the policy (an empty body, an unknown dispatch
  note). A refused role gets 403; an allowed one is stopped later with another 4xx. Nothing is
  written.
* **Boundary values.** Each temperature limit is tested on both sides: heating at 39.9, 40, 65 and
  65.1 °C; the pasteuriser at 79.9, 80, 85 and 85.1 °C.
* **Page objects and roles, not CSS.** Screens are classes in `src/pages`; locators are labels and
  roles (`getByLabel("Dispatch Note")`), the same things a screen reader uses.

### Known defects

Found while writing the suite. Each has a test marked `test.fail()`: it is expected to fail while
the defect stands, so the pipeline stays green, and Playwright reports it as an error the moment the
defect is fixed, so the marker comes off. The run's summary lists them.

| # | Defect | Where |
| --- | --- | --- |
| 1 | **Untested milk can be allocated.** When the request names the run (`processingRunId`), allocation never checks the run's QC status, so milk still awaiting its lab result can go into a mixing tank | `TankAllocationService.AllocateAsync` |
| 2 | **A mixing tank can be overfilled.** Allocation compares the quantity with what the storing tank holds but never with the mixing tank's capacity: 301 kg goes into a 300 kg tank | `TankAllocationService.AllocateAsync` |
| 3 | **A stage can be skipped.** The order check refuses going back but not skipping ahead, so a batch can go from heating straight to the pasteuriser, or to cooling, which completes the run | `ProcessingStageService.StartAsync` |
| 4 | **A mixing tank is never emptied.** The code says a mixing tank becomes empty after cooling; it keeps its kilograms and refuses the next batch. Tank codes stop at MT-99, so the suite reuses an empty tank where one exists and otherwise opens the next free code | `ProcessingStageService.EndAsync` |
| 5 | **Deep links on the deployed frontend return 404.** Vercel has no single-page-app fallback, so refreshing `/processing/unloads` or opening a bookmark to it fails. The UI tests load `/` and navigate inside the app | frontend deployment |

## Configuration (staging environment)

Everything has a working default (the staging hosts, in `tests/e2e/src/config.ts`), so nothing is
required. To override:

| Name | Kind | Purpose |
| --- | --- | --- |
| `E2E_PASSWORD` | secret | Password of the seeded staging accounts, if not the documented one |
| `E2E_WEB_URL`, `E2E_AUTH_URL`, `E2E_INTAKE_URL`, `E2E_PROCESSING_URL`, `E2E_QUALITY_LAB_URL`, `E2E_TRACEABILITY_URL` | env | Point the suite at other hosts (`tests/e2e/.env.example`) |

The accounts used are the seeded ones: `factory` unloads, `analyst` runs QC and the lab panel,
`processing` (Processing Technician) allocates and runs stages, `manager` and `officer` work the
MCC gate, `admin` reads traceability.

## Running it

| How | Command |
| --- | --- |
| Automatically | Every push to `develop`: the `e2e` job in `ci-cd.yml`, after `deploy-staging` |
| By hand on GitHub | Actions → **End-to-end tests (Playwright)** → Run workflow → `all`, `api` or `ui` |
| From the CLI | `gh workflow run e2e.yml -R wonrich-dairy/processing-service -f project=all` |

Locally (Node 20+ and Google Chrome):

```bash
cd tests/e2e
npm ci
npx playwright test                   # everything
npx playwright test --project=api     # the services' APIs only
npx playwright test --project=ui --headed   # watch the browser
npm run report                        # the HTML report
```

The suite runs on one worker: its workflows share staging tanks (an empty mixing tank, the next
free tank code), and two workers would race for the same one.

## Reading the report

* **Summary page** (`Summary` step): passed, failed and flaky per spec, and the known defects.
* **`e2e-report` artifact**: the HTML report (`reports/html/index.html`), plus a trace, screenshot
  and video for every failed test. `npx playwright show-trace trace.zip` replays a failure step by
  step, with the DOM, network calls and console at each step.
* **Flaky** means a test failed, then passed on its one retry. Staging is reached over the
  internet, so one retry absorbs a dropped connection; the test is still reported, not hidden.
