// Turns Playwright's JSON report into a Markdown table: results per spec file, then the known
// defects (tests marked test.fail). On GitHub Actions it is appended to the run's summary page,
// beside the unit-test coverage and the load-test results.
//
//   node scripts/summary.mjs [reports/results.json]

import { appendFileSync, existsSync, readFileSync } from "node:fs";

const file = process.argv[2] ?? "reports/results.json";

if (!existsSync(file)) {
  console.error(`No Playwright JSON report at ${file}.`);
  process.exit(1);
}

const report = JSON.parse(readFileSync(file, "utf8"));
const rows = new Map();
const defects = [];

function walk(suite, path) {
  for (const spec of suite.specs ?? []) {
    for (const test of spec.tests) {
      const row = rows.get(spec.file) ?? { passed: 0, failed: 0, flaky: 0, skipped: 0, defects: 0 };
      const knownDefect = test.annotations.some((annotation) => annotation.type === "fail");

      if (test.status === "skipped") row.skipped++;
      else if (test.status === "flaky") row.flaky++;
      else if (test.status === "unexpected") row.failed++;
      else if (knownDefect) row.defects++;
      else row.passed++;

      if (knownDefect) {
        const note = test.annotations.find((annotation) => annotation.type === "fail")?.description ?? "";
        defects.push(`- ${[...path, spec.title].join(" › ")}: ${note}${test.status === "unexpected" ? " **(now passing: remove test.fail)**" : ""}`);
      }

      rows.set(spec.file, row);
    }
  }

  for (const child of suite.suites ?? []) {
    walk(child, child.title && !child.title.endsWith(".ts") ? [...path, child.title] : path);
  }
}

for (const suite of report.suites) walk(suite, []);

const total = { passed: 0, failed: 0, flaky: 0, skipped: 0, defects: 0 };
const lines = ["| Spec | Passed | Failed | Flaky | Known defects |", "| --- | ---: | ---: | ---: | ---: |"];

for (const [spec, row] of rows) {
  for (const key of Object.keys(total)) total[key] += row[key];
  lines.push(`| ${spec} | ${row.passed} | ${row.failed} | ${row.flaky} | ${row.defects} |`);
}

lines.push(`| **Total** | **${total.passed}** | **${total.failed}** | **${total.flaky}** | **${total.defects}** |`);

const markdown = [
  `## End-to-end tests (Playwright) ${total.failed ? "❌" : "✅"}`,
  "",
  ...lines,
  "",
  ...(defects.length ? ["**Known defects** (expected failures until fixed):", "", ...defects, ""] : []),
].join("\n");

console.log(markdown);

if (process.env.GITHUB_STEP_SUMMARY) {
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${markdown}\n`);
}
