import "dotenv/config";

// Empty CI variables arrive as empty strings, hence || rather than ??.
const url = (name: string, fallback: string) => (process.env[name] || fallback).replace(/\/$/, "");

// Staging, where the five services are deployed together and wired to the Kafka broker.
export const urls = {
  web: url("E2E_WEB_URL", "https://frontend-phi-sage-81.vercel.app"),
  auth: url("E2E_AUTH_URL", "https://wonrich-auth-app-f4dndrgcgzgjb5h4.malaysiawest-01.azurewebsites.net"),
  intake: url("E2E_INTAKE_URL", "https://app-mcc-intake-staging.azurewebsites.net"),
  processing: url("E2E_PROCESSING_URL", "https://app-wonrich-processing-staging.azurewebsites.net"),
  qualityLab: url("E2E_QUALITY_LAB_URL", "https://wonrich-quality-lab-bvega5acfkgpb5gx.southeastasia-01.azurewebsites.net"),
  traceability: url("E2E_TRACEABILITY_URL", "https://wonrich-traceability-hhfsh9d4cff6gsd3.southeastasia-01.azurewebsites.net"),
};

export type Service = Exclude<keyof typeof urls, "web">;

export const password = process.env.E2E_PASSWORD || "Wonrich@2026";

// The seeded accounts, one per role, and what each does in this suite.
export const accounts = {
  admin: { userName: "admin", role: "SystemAdministrator" },
  manager: { userName: "manager", role: "MccManager" }, //            intake: societies, tanks, dispatch notes
  officer: { userName: "officer", role: "IntakeOfficer" }, //         intake: gate, panel, pour
  factory: { userName: "factory", role: "FactoryIntakeOfficer" }, //  processing: unloads the bowser
  analyst: { userName: "analyst", role: "QualityAnalyst" }, //        processing QC, quality-lab
  production: { userName: "production", role: "ProductionManager" },
  processing: { userName: "processing", role: "ProcessingTechnician" }, // processing: allocation, stages
} as const;

export type Account = keyof typeof accounts;
