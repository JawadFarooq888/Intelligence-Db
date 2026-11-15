import { useQuery } from "@tanstack/react-query";
import { useMemo, useState, type ReactNode } from "react";
import { TimeChart, type ChartSeries } from "@/components/charts/TimeChart";
import { Card, CardContent, CardDescription, CardHeader, CardTitle, Skeleton } from "@/components/ui/primitives";
import { InfoTip } from "@/components/ui/tooltip";
import { api, errorMessage } from "@/lib/api";
import type { MetricPoint, PerformanceDto } from "@/lib/types";
import { cn } from "@/lib/utils";

const ranges = [
  { hours: 1, label: "1 h" },
  { hours: 6, label: "6 h" },
  { hours: 24, label: "24 h" },
  { hours: 72, label: "3 days" },
  { hours: 168, label: "7 days" },
];

function series(points: MetricPoint[], name: string, pick: (p: MetricPoint) => number | null, extra?: Partial<ChartSeries>): ChartSeries {
  return { name, data: points.map((p) => [p.time, pick(p)]), ...extra };
}

export function PerformanceTab({ serverId }: { serverId: number }) {
  const [hours, setHours] = useState(24);
  const { data, isLoading, error } = useQuery({
    queryKey: ["server", serverId, "performance", hours],
    queryFn: () => api<PerformanceDto>(`/servers/${serverId}/performance?hours=${hours}`),
    refetchInterval: 60_000,
  });

  const s = data?.series ?? [];
  const charts = useMemo(
    () => ({
      cpu: [
        series(s, "SQL Server", (p) => p.sqlCpuPct, { area: true, stack: "cpu" }),
        series(s, "Other processes", (p) => p.otherCpuPct, { area: true, stack: "cpu" }),
      ],
      memory: [series(s, "Used", (p) => p.totalServerMemoryMb), series(s, "Target", (p) => p.targetServerMemoryMb)],
      batches: [series(s, "Batch requests/s", (p) => p.batchRequestsPerSec, { area: true })],
      ple: [series(s, "Page life expectancy", (p) => p.pageLifeExpectancy)],
      sessions: [series(s, "User connections", (p) => p.userConnections), series(s, "Blocked", (p) => p.blockedSessions, { color: "#ef4444" })],
    }),
    [s],
  );

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm text-muted-foreground">Range</span>
        <div className="inline-flex rounded-md border p-0.5">
          {ranges.map((r) => (
            <button
              key={r.hours}
              className={cn(
                "rounded px-3 py-1 text-sm cursor-pointer",
                hours === r.hours ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground",
              )}
              onClick={() => setHours(r.hours)}
            >
              {r.label}
            </button>
          ))}
        </div>
        {data && <span className="text-xs text-muted-foreground">Points are {data.bucketMinutes}-minute averages</span>}
      </div>

      {error && <p className="text-sm text-destructive">{errorMessage(error)}</p>}
      {isLoading ? (
        <Skeleton className="h-64" />
      ) : s.length === 0 ? (
        <Card>
          <CardContent className="p-5 text-sm text-muted-foreground">No metric samples in this range yet.</CardContent>
        </Card>
      ) : (
        <div className="grid gap-5 lg:grid-cols-2">
          <ChartCard title="CPU" tip="CPU used by SQL Server and by everything else on the machine.">
            <TimeChart series={charts.cpu} unit="%" yMax={100} />
          </ChartCard>
          <ChartCard title="Memory (MB)" tip="Memory SQL Server uses now (Used) and wants to use (Target).">
            <TimeChart series={charts.memory} unit=" MB" />
          </ChartCard>
          <ChartCard title="Workload" tip="T-SQL batches per second.">
            <TimeChart series={charts.batches} decimals={1} />
          </ChartCard>
          <ChartCard title="Page life expectancy (s)" tip="How long data pages stay in memory. Sudden drops mean big scans or memory pressure.">
            <TimeChart series={charts.ple} unit=" s" />
          </ChartCard>
          <ChartCard title="Sessions" tip="User connections and blocked requests (blocking chains come in Phase 4).">
            <TimeChart series={charts.sessions} />
          </ChartCard>
        </div>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-1.5">
            Top waits
            <InfoTip>
              What SQL Server spent time waiting for in this range. Harmless background waits are already removed. The biggest waits
              point to the bottleneck.
            </InfoTip>
          </CardTitle>
          <CardDescription>Collected every 5 minutes as the change since the previous sample.</CardDescription>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          {data && data.waits.length === 0 ? (
            <p className="text-sm text-muted-foreground">No wait samples yet (needs two samples, about 5 minutes).</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="text-left text-muted-foreground">
                <tr>
                  <th className="py-1.5 font-medium">Wait type</th>
                  <th className="py-1.5 font-medium">Share</th>
                  <th className="py-1.5 text-right font-medium">Total (s)</th>
                  <th className="py-1.5 text-right font-medium">Avg ms</th>
                  <th className="py-1.5 font-medium pl-4">What it means</th>
                </tr>
              </thead>
              <tbody>
                {data?.waits.map((w) => (
                  <tr key={w.waitType} className="border-t align-top">
                    <td className="py-2">
                      <div className="font-mono text-xs">{w.waitType}</div>
                      <div className="text-xs text-muted-foreground">{w.category}</div>
                    </td>
                    <td className="w-40 py-2">
                      <div className="flex items-center gap-2">
                        <div className="h-2 flex-1 overflow-hidden rounded-full bg-muted">
                          <div className="h-full bg-primary" style={{ width: `${w.percent}%` }} />
                        </div>
                        <span className="w-10 text-right text-xs tabular-nums">{w.percent}%</span>
                      </div>
                    </td>
                    <td className="py-2 text-right tabular-nums">{(w.waitTimeMs / 1000).toLocaleString(undefined, { maximumFractionDigits: 1 })}</td>
                    <td className="py-2 text-right tabular-nums">{w.avgMsPerWait}</td>
                    <td className="py-2 pl-4 text-xs text-muted-foreground">{w.explanation}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-1.5">
            File latency
            <InfoTip>Average time per read and write for each database file. As a rule of thumb: under 5 ms is very good, over 20 ms is slow storage.</InfoTip>
          </CardTitle>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          {data && data.files.length === 0 ? (
            <p className="text-sm text-muted-foreground">No file I/O samples yet.</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="text-left text-muted-foreground">
                <tr>
                  <th className="py-1.5 font-medium">Database / file</th>
                  <th className="py-1.5 text-right font-medium">Avg read ms</th>
                  <th className="py-1.5 text-right font-medium">Avg write ms</th>
                  <th className="py-1.5 text-right font-medium">Reads</th>
                  <th className="py-1.5 text-right font-medium">Writes</th>
                  <th className="py-1.5 text-right font-medium">MB read / written</th>
                </tr>
              </thead>
              <tbody>
                {data?.files.map((f) => (
                  <tr key={`${f.databaseName}-${f.fileId}`} className="border-t">
                    <td className="py-1.5">
                      {f.databaseName} <span className="text-xs text-muted-foreground">file {f.fileId}</span>
                    </td>
                    <td className={cn("py-1.5 text-right tabular-nums", f.avgReadMs > 20 && "text-destructive")}>{f.avgReadMs}</td>
                    <td className={cn("py-1.5 text-right tabular-nums", f.avgWriteMs > 20 && "text-destructive")}>{f.avgWriteMs}</td>
                    <td className="py-1.5 text-right tabular-nums">{f.reads.toLocaleString()}</td>
                    <td className="py-1.5 text-right tabular-nums">{f.writes.toLocaleString()}</td>
                    <td className="py-1.5 text-right tabular-nums">
                      {f.mbRead.toLocaleString()} / {f.mbWritten.toLocaleString()}
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

function ChartCard({ title, tip, children }: { title: string; tip: string; children: ReactNode }) {
  return (
    <Card>
      <CardHeader className="pb-1">
        <CardTitle className="flex items-center gap-1.5 text-sm">
          {title} <InfoTip>{tip}</InfoTip>
        </CardTitle>
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}
