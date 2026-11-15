import { useQuery } from "@tanstack/react-query";
import { CheckCircle2, XCircle } from "lucide-react";
import type { ReactNode } from "react";
import { AdviceList, HealthBadge } from "@/components/monitoring";
import { Card, CardContent, CardDescription, CardHeader, CardTitle, Skeleton } from "@/components/ui/primitives";
import { InfoTip } from "@/components/ui/tooltip";
import { api, errorMessage } from "@/lib/api";
import { formatMb, formatRelative } from "@/lib/format";
import type { ServerOverviewDto } from "@/lib/types";
import { cn } from "@/lib/utils";

const collectorLabels: Record<string, string> = {
  probe: "Capability detection",
  connection: "Connection",
  metrics: "CPU / memory metrics",
  waits: "Wait statistics",
  fileio: "File I/O",
  databases: "Databases & files",
  backups: "Backup history",
  config: "Instance settings",
};

export function OverviewTab({ serverId, onOpenTab }: { serverId: number; onOpenTab: (tab: string) => void }) {
  const { data, isLoading, error } = useQuery({
    queryKey: ["server", serverId, "overview"],
    queryFn: () => api<ServerOverviewDto>(`/servers/${serverId}/overview`),
    refetchInterval: 30_000,
  });

  if (isLoading) return <Skeleton className="h-64" />;
  if (error || !data) return <p className="text-sm text-destructive">{errorMessage(error)}</p>;

  const m = data.latest;
  const noData = !m && data.databases.total === 0 && data.collectors.length === 0;

  return (
    <div className="flex flex-col gap-5">
      {noData && (
        <Card>
          <CardContent className="p-5 text-sm text-muted-foreground">
            The collector has not stored data for this server yet. The Worker (or the agent) starts within a minute: metrics every
            collection interval, waits and file I/O every 5 minutes, databases and backups every 15 minutes.
          </CardContent>
        </Card>
      )}

      <div className="grid gap-5 lg:grid-cols-3">
        <Card>
          <CardHeader className="flex-row items-center gap-4">
            <HealthBadge score={data.health?.score} deductions={data.health?.deductions} size="lg" />
            <div>
              <CardTitle>Health score</CardTitle>
              <CardDescription>
                {data.health ? `Updated ${formatRelative(data.health.calculatedUtc)}` : "Calculated after the first collection"}
              </CardDescription>
            </div>
          </CardHeader>
          <CardContent>
            {data.health && data.health.deductions.length > 0 ? (
              <ul className="flex flex-col gap-2">
                {data.health.deductions.map((d) => (
                  <li key={d.rule} className="flex gap-2 text-sm">
                    <span className="w-9 shrink-0 font-mono text-destructive">-{d.points}</span>
                    <span>{d.message}</span>
                  </li>
                ))}
              </ul>
            ) : (
              data.health && <p className="text-sm text-success">No problems found.</p>
            )}
          </CardContent>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>Right now</CardTitle>
            <CardDescription>{m ? `Last sample ${formatRelative(m.time)}` : "No sample yet"}</CardDescription>
          </CardHeader>
          <CardContent className="grid grid-cols-2 gap-4 sm:grid-cols-3">
            <Stat label="SQL Server CPU" tip="CPU used by SQL Server. Sustained values above 80-90% mean CPU pressure." value={m?.sqlCpuPct != null ? `${m.sqlCpuPct.toFixed(0)}%` : "-"} warn={(m?.sqlCpuPct ?? 0) >= 75} />
            <Stat label="Other CPU" tip="CPU used by everything else on the machine. High values mean another process competes with SQL Server." value={m?.otherCpuPct != null ? `${m.otherCpuPct.toFixed(0)}%` : "-"} warn={(m?.otherCpuPct ?? 0) >= 30} />
            <Stat label="Page life expectancy" tip="Seconds a data page stays in memory. Low or dropping values mean memory pressure or large scans." value={m?.pageLifeExpectancy != null ? `${m.pageLifeExpectancy.toLocaleString()} s` : "-"} warn={m?.pageLifeExpectancy != null && m.pageLifeExpectancy < 300} />
            <Stat label="Memory used / target" tip="Memory SQL Server uses now vs. what it wants. Used far below target right after a restart is normal." value={m ? `${formatMb(m.totalServerMemoryMb)} / ${formatMb(m.targetServerMemoryMb)}` : "-"} />
            <Stat label="Batch requests / s" tip="How many T-SQL batches the server runs per second - a simple measure of workload." value={m?.batchRequestsPerSec != null ? m.batchRequestsPerSec.toLocaleString(undefined, { maximumFractionDigits: 1 }) : "-"} />
            <Stat label="Connections / blocked" tip="User connections, and how many requests were waiting for another session's lock at the last sample." value={m ? `${m.userConnections ?? "-"} / ${m.blockedSessions ?? 0}` : "-"} warn={(m?.blockedSessions ?? 0) > 0} />
            <Stat label="Memory grants pending" tip="Queries waiting for memory before they can start. Anything above 0 for long is a memory problem." value={m?.memoryGrantsPending?.toString() ?? "-"} warn={(m?.memoryGrantsPending ?? 0) > 0} />
            <Stat label="Databases" tip="Databases on this server, and how many have critical findings (missing backups, offline, ...)." value={`${data.databases.total} (${data.databases.withCritical} critical)`} warn={data.databases.withCritical > 0} onClick={() => onOpenTab("databases")} />
            <Stat label="Data / log size" tip="Total size of all data and log files." value={`${formatMb(data.databases.totalDataMb)} / ${formatMb(data.databases.totalLogMb)}`} />
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-5 lg:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-1.5">
              Disk space <InfoTip>Free space of the volumes that hold database files. Below 20% is a warning, below 10% critical.</InfoTip>
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            {!data.volumesSupported && <p className="text-sm text-muted-foreground">Not available on this platform or for this login (see Setup → features).</p>}
            {data.volumesSupported && data.volumes.length === 0 && <p className="text-sm text-muted-foreground">No sample yet.</p>}
            {data.volumes.map((v) => (
              <div key={v.mountPoint}>
                <div className="flex justify-between text-sm">
                  <span className="font-mono">{v.mountPoint}</span>
                  <span className={cn(v.freePct < 10 ? "text-destructive" : v.freePct < 20 ? "text-warning" : "text-muted-foreground")}>
                    {formatMb(v.freeMb)} free of {formatMb(v.totalMb)} ({v.freePct}%)
                  </span>
                </div>
                <div className="mt-1 h-2 overflow-hidden rounded-full bg-muted">
                  <div
                    className={cn("h-full", v.freePct < 10 ? "bg-destructive" : v.freePct < 20 ? "bg-warning" : "bg-primary")}
                    style={{ width: `${Math.max(2, 100 - v.freePct)}%` }}
                  />
                </div>
              </div>
            ))}
          </CardContent>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader className="flex-row items-center">
            <div className="flex-1">
              <CardTitle>Configuration advice</CardTitle>
              <CardDescription>Best-practice checks of instance settings, version and tempdb.</CardDescription>
            </div>
            <button className="text-sm text-primary hover:underline cursor-pointer" onClick={() => onOpenTab("configuration")}>
              All settings
            </button>
          </CardHeader>
          <CardContent>
            <AdviceList items={data.configAdvice.slice(0, 6)} empty="Settings follow best practices." />
            {data.configAdvice.length > 6 && <p className="mt-2 text-xs text-muted-foreground">+{data.configAdvice.length - 6} more on the Configuration tab</p>}
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-1.5">
            Collection status <InfoTip>When each collector last ran for this server, and whether it worked. "Source" shows the Worker or the agent that collected it.</InfoTip>
          </CardTitle>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-muted-foreground">
              <tr>
                <th className="py-1.5 font-medium">Collector</th>
                <th className="py-1.5 font-medium">Last run</th>
                <th className="py-1.5 font-medium">Duration</th>
                <th className="py-1.5 font-medium">Source</th>
                <th className="py-1.5 font-medium">Result</th>
              </tr>
            </thead>
            <tbody>
              {data.collectors.map((c) => (
                <tr key={c.collector} className="border-t">
                  <td className="py-1.5">{collectorLabels[c.collector] ?? c.collector}</td>
                  <td className="py-1.5 text-muted-foreground">{formatRelative(c.lastRunUtc)}</td>
                  <td className="py-1.5 text-muted-foreground">{c.durationMs} ms</td>
                  <td className="py-1.5 font-mono text-xs">{c.source}</td>
                  <td className="py-1.5">
                    {c.success ? (
                      <CheckCircle2 className="size-4 text-success" aria-label="OK" />
                    ) : (
                      <span className="flex items-start gap-1 text-destructive">
                        <XCircle className="mt-0.5 size-4 shrink-0" /> <span className="text-xs">{c.error}</span>
                      </span>
                    )}
                  </td>
                </tr>
              ))}
              {data.collectors.length === 0 && (
                <tr>
                  <td colSpan={5} className="py-3 text-muted-foreground">No collection yet.</td>
                </tr>
              )}
            </tbody>
          </table>
        </CardContent>
      </Card>
    </div>
  );
}

function Stat({
  label,
  tip,
  value,
  warn,
  onClick,
}: {
  label: string;
  tip: string;
  value: ReactNode;
  warn?: boolean;
  onClick?: () => void;
}) {
  return (
    <div className={cn(onClick && "cursor-pointer")} onClick={onClick}>
      <div className="flex items-center gap-1 text-xs text-muted-foreground">
        {label} <InfoTip>{tip}</InfoTip>
      </div>
      <div className={cn("mt-0.5 text-lg font-semibold tabular-nums", warn && "text-warning")}>{value}</div>
    </div>
  );
}
