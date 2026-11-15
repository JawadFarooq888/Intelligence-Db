import type { AuthType, ConnectionStatus, PlatformType, ServerEnvironment } from "./types";

export const platformLabels: Record<PlatformType, string> = {
  Unknown: "Not detected yet",
  OnPremises: "SQL Server (on-premises / VM)",
  AzureSqlManagedInstance: "Azure SQL Managed Instance",
  AzureSqlDatabase: "Azure SQL Database",
  AwsRds: "Amazon RDS for SQL Server",
  GoogleCloudSql: "Google Cloud SQL for SQL Server",
};

export const platformShort: Record<PlatformType, string> = {
  Unknown: "Unknown",
  OnPremises: "On-prem",
  AzureSqlManagedInstance: "Azure MI",
  AzureSqlDatabase: "Azure SQL DB",
  AwsRds: "AWS RDS",
  GoogleCloudSql: "GCP Cloud SQL",
};

/** Folder names of the login scripts in sql/monitoring-login and sql/admin-login. */
export const platformScriptKey: Record<PlatformType, string> = {
  Unknown: "onprem",
  OnPremises: "onprem",
  AzureSqlManagedInstance: "azure-mi",
  AzureSqlDatabase: "azure-sqldb",
  AwsRds: "aws-rds",
  GoogleCloudSql: "gcp-cloudsql",
};

export const authTypeLabels: Record<AuthType, string> = {
  SqlLogin: "SQL authentication",
  Windows: "Windows authentication (service account)",
  EntraPassword: "Microsoft Entra ID - password",
  EntraServicePrincipal: "Microsoft Entra ID - service principal",
  EntraManagedIdentity: "Microsoft Entra ID - managed identity",
  EntraDefault: "Microsoft Entra ID - default credential chain",
};

export const environmentLabels: Record<ServerEnvironment, string> = {
  Production: "Production",
  Test: "Test",
  Development: "Development",
  Other: "Other",
};

export const statusLabels: Record<ConnectionStatus, string> = {
  Unknown: "Not checked",
  Online: "Online",
  Unreachable: "Unreachable",
  AuthFailed: "Login failed",
};

/** "3 min ago", "2 h ago", "5 days ago". */
export function formatRelative(utc: string | null | undefined): string {
  if (!utc) return "never";
  const then = new Date(utc.endsWith("Z") ? utc : `${utc}Z`).getTime();
  const seconds = Math.round((Date.now() - then) / 1000);
  if (seconds < 0) return "just now";
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 48) return `${hours} h ago`;
  return `${Math.round(hours / 24)} days ago`;
}

export function formatDateTime(utc: string | null | undefined): string {
  if (!utc) return "-";
  const d = new Date(utc.endsWith("Z") ? utc : `${utc}Z`);
  return d.toLocaleString();
}

export function formatMemoryMb(mb: number | null | undefined): string {
  if (mb == null) return "-";
  return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb} MB`;
}

/** Uptime from the SQL Server start time. */
export function formatUptime(startUtc: string | null | undefined): string {
  if (!startUtc) return "-";
  const start = new Date(startUtc.endsWith("Z") ? startUtc : `${startUtc}Z`).getTime();
  const hours = (Date.now() - start) / 3_600_000;
  if (hours < 1) return `${Math.max(1, Math.round(hours * 60))} min`;
  if (hours < 48) return `${Math.round(hours)} h`;
  return `${Math.floor(hours / 24)} days`;
}

/** Downloads rows as a CSV file (Excel opens it directly). */
export function downloadCsv(fileName: string, headers: string[], rows: (string | number | boolean | null | undefined)[][]) {
  const escape = (v: string | number | boolean | null | undefined) => {
    const s = v == null ? "" : String(v);
    // Prevent CSV/formula injection when opened in Excel.
    const safe = /^[=+\-@\t\r]/.test(s) ? `'${s}` : s;
    return /[",\n\r]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
  };
  const csv = [headers, ...rows].map((r) => r.map(escape).join(",")).join("\r\n");
  const blob = new Blob([`﻿${csv}`], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName.endsWith(".csv") ? fileName : `${fileName}.csv`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

/** 512 MB, 1.5 GB, 2.1 TB */
export function formatMb(mb: number | null | undefined): string {
  if (mb == null) return "-";
  if (mb >= 1024 * 1024) return `${(mb / 1024 / 1024).toFixed(1)} TB`;
  if (mb >= 1024) return `${(mb / 1024).toFixed(1)} GB`;
  return `${mb.toFixed(mb < 10 ? 1 : 0)} MB`;
}

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return "-";
  return formatMb(bytes / 1048576);
}

export function formatDuration(seconds: number): string {
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
  return `${Math.floor(seconds / 3600)}h ${Math.floor((seconds % 3600) / 60)}m`;
}

export const backupTypeLabels: Record<string, string> = {
  D: "Full",
  I: "Differential",
  L: "Log",
  F: "File",
  G: "Diff file",
  P: "Partial",
  Q: "Diff partial",
};
