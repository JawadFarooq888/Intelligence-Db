import { useQuery } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { useMemo, type ReactNode } from "react";
import { Link, useParams } from "react-router";
import { TimeChart } from "@/components/charts/TimeChart";
import { AdviceList } from "@/components/monitoring";
import { Badge, Card, CardContent, CardDescription, CardHeader, CardTitle, Skeleton } from "@/components/ui/primitives";
import { InfoTip } from "@/components/ui/tooltip";
import { api, errorMessage } from "@/lib/api";
import { backupTypeLabels, formatBytes, formatDateTime, formatDuration, formatMb, formatRelative } from "@/lib/format";
import type { DatabaseDetailDto } from "@/lib/types";

export function DatabaseDetailPage() {
  const id = Number(useParams().id);
  const { data, isLoading, error } = useQuery({
    queryKey: ["database", id],
    queryFn: () => api<DatabaseDetailDto>(`/databases/${id}`),
    enabled: Number.isFinite(id),
    refetchInterval: 60_000,
  });

  const sizeSeries = useMemo(
    () =>
      data
        ? [
            { name: "Data size", data: data.sizeHistory.map((p) => [p.day, p.dataSizeMb] as [string, number | null]) },
            { name: "Data used", data: data.sizeHistory.map((p) => [p.day, p.dataUsedMb] as [string, number | null]), area: true },
            { name: "Log size", data: data.sizeHistory.map((p) => [p.day, p.logSizeMb] as [string, number | null]) },
          ]
        : [],
    [data],
  );

  if (isLoading) return <Skeleton className="h-64" />;
  if (error || !data) {
    return (
      <div className="flex flex-col gap-3">
        <Link to="/databases" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" /> Databases
        </Link>
        <p className="text-sm text-destructive">{errorMessage(error)}</p>
      </div>
    );
  }

  const d = data.database;

  return (
    <div className="flex flex-col gap-5">
      <Link to="/databases" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="size-4" /> Databases
      </Link>

      <div className="flex flex-wrap items-center gap-2">
        <h1 className="text-2xl font-semibold">{d.name}</h1>
        <Badge variant={d.state === "ONLINE" ? "success" : "destructive"}>{d.state}</Badge>
        <span className="text-sm text-muted-foreground">
          on{" "}
          <Link to={`/servers/${d.serverId}`} className="text-primary hover:underline">
            {d.serverName}
          </Link>
        </span>
        {d.isDropped && <Badge variant="secondary">dropped (last seen {formatRelative(d.lastSeenUtc)})</Badge>}
      </div>

      <Card>
        <CardContent className="grid grid-cols-2 gap-4 p-5 sm:grid-cols-4">
          <Fact label="Data" tip="Size of the data files and how much of it is used.">
            {formatMb(d.dataSizeMb)} {d.dataUsedMb != null && <span className="text-xs text-muted-foreground">({formatMb(d.dataUsedMb)} used)</span>}
          </Fact>
          <Fact label="Log" tip="Size of the transaction log and how much is in use. A full log stops writes.">
            {formatMb(d.logSizeMb)} {d.logUsedMb != null && <span className="text-xs text-muted-foreground">({formatMb(d.logUsedMb)} used)</span>}
          </Fact>
          <Fact label="Recovery model" tip="FULL: point-in-time restore, needs log backups. SIMPLE: log is reused automatically, restore only to the last backup.">
            {d.recoveryModel}
          </Fact>
          <Fact label="Compatibility level" tip="Which SQL Server version's query optimizer behaviour this database uses.">
            {d.compatibilityLevel}
          </Fact>
          <Fact label="Last full backup">{d.backupHistoryAvailable ? (d.lastFullBackupUtc ? formatDateTime(d.lastFullBackupUtc) : "never") : "n/a on this platform"}</Fact>
          <Fact label="Last differential">{d.lastDiffBackupUtc ? formatDateTime(d.lastDiffBackupUtc) : "-"}</Fact>
          <Fact label="Last log backup">{d.lastLogBackupUtc ? formatDateTime(d.lastLogBackupUtc) : d.recoveryModel === "SIMPLE" ? "n/a (SIMPLE)" : "never"}</Fact>
          <Fact label="Last good CHECKDB" tip="Last DBCC CHECKDB that finished without errors. 'unknown' = the server version does not report it.">
            {d.checkDbNever ? "never" : d.lastGoodCheckDbUtc ? formatDateTime(d.lastGoodCheckDbUtc) : "unknown"}
          </Fact>
          <Fact label="Virtual log files" tip="Number of VLFs in the log. Thousands slow down startup, restores and log backups.">{d.vlfCount ?? "-"}</Fact>
          <Fact label="Log reuse wait" tip="Why log space cannot be reused right now. NOTHING is normal.">{d.logReuseWait ?? "-"}</Fact>
          <Fact label="Owner">{d.owner ?? "-"}</Fact>
          <Fact label="Last collected">{formatRelative(d.lastSeenUtc)}</Fact>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Findings</CardTitle>
        </CardHeader>
        <CardContent>
          <AdviceList items={data.advice} empty="This database follows best practices." />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-1.5">
            Size history <InfoTip>One point per day (or per collection while there is less than two days of history).</InfoTip>
          </CardTitle>
        </CardHeader>
        <CardContent>
          {data.sizeHistory.length === 0 ? <p className="text-sm text-muted-foreground">No history yet.</p> : <TimeChart series={sizeSeries} unit=" MB" height={240} />}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Files</CardTitle>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-muted-foreground">
              <tr>
                <th className="py-1.5 font-medium">File</th>
                <th className="py-1.5 font-medium">Type</th>
                <th className="py-1.5 text-right font-medium">Size</th>
                <th className="py-1.5 text-right font-medium">Used</th>
                <th className="py-1.5 font-medium pl-4">Growth</th>
                <th className="py-1.5 font-medium">Max size</th>
                <th className="py-1.5 font-medium">Volume</th>
              </tr>
            </thead>
            <tbody>
              {data.files.map((f) => (
                <tr key={f.fileId} className="border-t align-top">
                  <td className="py-1.5">
                    <div>{f.logicalName}</div>
                    <div className="break-all font-mono text-[11px] text-muted-foreground">{f.physicalName}</div>
                  </td>
                  <td className="py-1.5">{f.typeDesc}</td>
                  <td className="py-1.5 text-right tabular-nums">{formatMb(f.sizeMb)}</td>
                  <td className="py-1.5 text-right tabular-nums">{formatMb(f.usedMb)}</td>
                  <td className="py-1.5 pl-4">{f.isPercentGrowth ? `${f.growthValue}%` : f.growthValue === 0 ? "none" : formatMb(f.growthValue)}</td>
                  <td className="py-1.5">{f.maxSizeMb == null ? "unlimited" : formatMb(f.maxSizeMb)}</td>
                  <td className="py-1.5 font-mono text-xs">{f.volumeMountPoint ?? "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Backup history</CardTitle>
          <CardDescription>From msdb on the server (last 200).</CardDescription>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          {!d.backupHistoryAvailable ? (
            <p className="text-sm text-muted-foreground">Backup history is not available on this platform or for this login (see the server's Setup tab).</p>
          ) : data.backups.length === 0 ? (
            <p className="text-sm text-muted-foreground">No backups recorded in the collected period.</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="text-left text-muted-foreground">
                <tr>
                  <th className="py-1.5 font-medium">Type</th>
                  <th className="py-1.5 font-medium">Finished</th>
                  <th className="py-1.5 font-medium">Duration</th>
                  <th className="py-1.5 text-right font-medium">Size</th>
                  <th className="py-1.5 text-right font-medium">Compressed</th>
                  <th className="py-1.5 font-medium pl-4">Destination</th>
                </tr>
              </thead>
              <tbody>
                {data.backups.map((b) => (
                  <tr key={b.id} className="border-t">
                    <td className="py-1.5">
                      {backupTypeLabels[b.backupType] ?? b.backupType}
                      {b.isCopyOnly && (
                        <Badge variant="secondary" className="ml-2">
                          copy-only
                        </Badge>
                      )}
                    </td>
                    <td className="py-1.5">{formatDateTime(b.finishUtc)}</td>
                    <td className="py-1.5">{formatDuration(b.durationSeconds)}</td>
                    <td className="py-1.5 text-right tabular-nums">{formatBytes(b.sizeBytes)}</td>
                    <td className="py-1.5 text-right tabular-nums">{formatBytes(b.compressedBytes)}</td>
                    <td className="max-w-xs truncate py-1.5 pl-4 font-mono text-xs" title={b.physicalDevice ?? ""}>
                      {b.physicalDevice ?? "-"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function Fact({ label, tip, children }: { label: string; tip?: string; children: ReactNode }) {
  return (
    <div>
      <div className="flex items-center gap-1 text-xs text-muted-foreground">
        {label} {tip && <InfoTip>{tip}</InfoTip>}
      </div>
      <div className="text-sm">{children}</div>
    </div>
  );
}
