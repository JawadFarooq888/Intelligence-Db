import type { ColumnDef } from "@tanstack/react-table";
import { useMemo, type ReactNode } from "react";
import { Link, useNavigate } from "react-router";
import { DataTable } from "@/components/DataTable";
import { Badge } from "@/components/ui/primitives";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { formatDateTime, formatMb, formatRelative } from "@/lib/format";
import type { DatabaseRowDto } from "@/lib/types";
import { cn } from "@/lib/utils";

function Age({ utc, empty = "never", warn }: { utc: string | null; empty?: string; warn?: boolean }) {
  if (!utc) return <span className={cn(warn ? "text-destructive" : "text-muted-foreground")}>{empty}</span>;
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className={cn(warn && "text-destructive")}>{formatRelative(utc)}</span>
      </TooltipTrigger>
      <TooltipContent>{formatDateTime(utc)}</TooltipContent>
    </Tooltip>
  );
}

function pct(used: number | null, size: number | null) {
  return used != null && size ? Math.round((100 * used) / size) : null;
}

/** Databases with size, backups, CHECKDB and problem counts. Used on the Databases page and the server's Databases tab. */
export function DatabasesTable({ rows, showServer, toolbar, loading }: { rows: DatabaseRowDto[]; showServer: boolean; toolbar?: ReactNode; loading?: boolean }) {
  const navigate = useNavigate();

  const columns = useMemo<ColumnDef<DatabaseRowDto, unknown>[]>(() => {
    const cols: ColumnDef<DatabaseRowDto, unknown>[] = [];
    if (showServer) {
      cols.push({
        accessorKey: "serverName",
        header: "Server",
        cell: ({ row }) => (
          <Link to={`/servers/${row.original.serverId}`} className="hover:underline" onClick={(e) => e.stopPropagation()}>
            {row.original.serverName}
          </Link>
        ),
      });
    }

    cols.push(
      {
        accessorKey: "name",
        header: "Database",
        cell: ({ row }) => (
          <div>
            <span className="font-medium">{row.original.name}</span>
            {row.original.state !== "ONLINE" && (
              <Badge variant="destructive" className="ml-2">
                {row.original.state}
              </Badge>
            )}
            {row.original.isReadOnly && (
              <Badge variant="secondary" className="ml-2">
                read-only
              </Badge>
            )}
            {row.original.isDropped && (
              <Badge variant="secondary" className="ml-2">
                dropped
              </Badge>
            )}
          </div>
        ),
      },
      { accessorKey: "recoveryModel", header: "Recovery" },
      {
        id: "data",
        accessorFn: (d) => d.dataSizeMb ?? 0,
        header: "Data",
        cell: ({ row }) => {
          const p = pct(row.original.dataUsedMb, row.original.dataSizeMb);
          return (
            <span className="tabular-nums">
              {formatMb(row.original.dataSizeMb)}
              {p != null && <span className="text-xs text-muted-foreground"> ({p}% used)</span>}
            </span>
          );
        },
      },
      {
        id: "log",
        accessorFn: (d) => d.logSizeMb ?? 0,
        header: "Log",
        cell: ({ row }) => {
          const p = pct(row.original.logUsedMb, row.original.logSizeMb);
          return (
            <span className="tabular-nums">
              {formatMb(row.original.logSizeMb)}
              {p != null && <span className={cn("text-xs", p > 80 ? "text-warning" : "text-muted-foreground")}> ({p}%)</span>}
            </span>
          );
        },
      },
      {
        id: "full",
        accessorFn: (d) => d.lastFullBackupUtc ?? "",
        header: "Last full",
        cell: ({ row }) =>
          row.original.backupHistoryAvailable ? (
            <Age utc={row.original.lastFullBackupUtc} warn={row.original.name !== "tempdb" && !row.original.lastFullBackupUtc} empty={row.original.name === "tempdb" ? "n/a" : "never"} />
          ) : (
            <span className="text-muted-foreground">n/a</span>
          ),
      },
      {
        id: "log-backup",
        accessorFn: (d) => d.lastLogBackupUtc ?? "",
        header: "Last log",
        cell: ({ row }) =>
          row.original.recoveryModel === "SIMPLE" || !row.original.backupHistoryAvailable ? (
            <span className="text-muted-foreground">n/a</span>
          ) : (
            <Age utc={row.original.lastLogBackupUtc} warn={!row.original.lastLogBackupUtc} />
          ),
      },
      {
        id: "checkdb",
        accessorFn: (d) => d.lastGoodCheckDbUtc ?? "",
        header: "CHECKDB",
        cell: ({ row }) =>
          row.original.checkDbNever ? (
            <span className={cn(row.original.name !== "tempdb" && "text-warning")}>never</span>
          ) : (
            <Age utc={row.original.lastGoodCheckDbUtc} empty="unknown" />
          ),
      },
      { accessorKey: "vlfCount", header: "VLFs", cell: ({ getValue }) => <span className="tabular-nums">{(getValue() as number | null) ?? "-"}</span> },
      {
        id: "problems",
        accessorFn: (d) => d.criticalCount * 100 + d.warningCount * 10 + d.infoCount,
        header: "Findings",
        cell: ({ row }) => (
          <div className="flex gap-1">
            {row.original.criticalCount > 0 && <Badge variant="destructive">{row.original.criticalCount} critical</Badge>}
            {row.original.warningCount > 0 && <Badge variant="warning">{row.original.warningCount} warning</Badge>}
            {row.original.criticalCount + row.original.warningCount === 0 && <span className="text-xs text-success">OK</span>}
          </div>
        ),
      },
    );
    return cols;
  }, [showServer]);

  return (
    <DataTable
      data={rows}
      columns={columns}
      toolbar={toolbar}
      searchPlaceholder="Filter databases..."
      emptyMessage={loading ? "Loading..." : "No databases collected yet."}
      onRowClick={(d) => navigate(`/databases/${d.id}`)}
      csv={{
        fileName: "intelligencedb-databases",
        columns: [
          { header: "Server", value: (d) => d.serverName },
          { header: "Database", value: (d) => d.name },
          { header: "State", value: (d) => d.state },
          { header: "Recovery model", value: (d) => d.recoveryModel },
          { header: "Compatibility", value: (d) => d.compatibilityLevel },
          { header: "Owner", value: (d) => d.owner },
          { header: "Data MB", value: (d) => d.dataSizeMb },
          { header: "Data used MB", value: (d) => d.dataUsedMb },
          { header: "Log MB", value: (d) => d.logSizeMb },
          { header: "Log used MB", value: (d) => d.logUsedMb },
          { header: "VLFs", value: (d) => d.vlfCount },
          { header: "Log reuse wait", value: (d) => d.logReuseWait },
          { header: "Last full (UTC)", value: (d) => d.lastFullBackupUtc },
          { header: "Last diff (UTC)", value: (d) => d.lastDiffBackupUtc },
          { header: "Last log (UTC)", value: (d) => d.lastLogBackupUtc },
          { header: "Last CHECKDB (UTC)", value: (d) => (d.checkDbNever ? "never" : d.lastGoodCheckDbUtc) },
          { header: "Critical", value: (d) => d.criticalCount },
          { header: "Warnings", value: (d) => d.warningCount },
        ],
      }}
    />
  );
}
