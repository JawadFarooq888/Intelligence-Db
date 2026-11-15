import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Cpu, MemoryStick, Plus, RefreshCw, Timer } from "lucide-react";
import { Link } from "react-router";
import { toast } from "sonner";
import { EnvironmentBadge, PlatformBadge, StatusDot } from "@/components/badges";
import { HealthBadge } from "@/components/monitoring";
import { Button } from "@/components/ui/button";
import { Card, CardContent, Skeleton } from "@/components/ui/primitives";
import { InfoTip } from "@/components/ui/tooltip";
import { api, errorMessage } from "@/lib/api";
import { useCanManageServers } from "@/lib/auth";
import { formatMemoryMb, formatRelative, formatUptime, statusLabels } from "@/lib/format";
import type { DashboardDto, ServerCardDto } from "@/lib/types";
import { cn } from "@/lib/utils";

export function DashboardPage() {
  const queryClient = useQueryClient();
  const canManage = useCanManageServers();

  const { data, isLoading, error } = useQuery({
    queryKey: ["dashboard"],
    queryFn: () => api<DashboardDto>("/dashboard"),
    refetchInterval: 60_000,
  });

  const refresh = useMutation({
    mutationFn: () => api<DashboardDto>("/dashboard/refresh", { method: "POST" }),
    onSuccess: (fresh) => {
      queryClient.setQueryData(["dashboard"], fresh);
      void queryClient.invalidateQueries({ queryKey: ["servers"] });
      toast.success(`Checked ${fresh.totalServers - fresh.disabled} server(s).`);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Dashboard</h1>
          <p className="text-sm text-muted-foreground">Reachability, version and access problems of every server you can see.</p>
        </div>
        <div className="ml-auto flex gap-2">
          <Button variant="outline" onClick={() => refresh.mutate()} disabled={refresh.isPending || !data?.totalServers}>
            <RefreshCw className={cn(refresh.isPending && "animate-spin")} /> Check all now
          </Button>
          {canManage && (
            <Button asChild>
              <Link to="/servers?add=1">
                <Plus /> Add server
              </Link>
            </Button>
          )}
        </div>
      </div>

      {error && <p className="text-sm text-destructive">{errorMessage(error)}</p>}

      <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
        <Tile label="Servers" value={data?.totalServers} loading={isLoading} tip="All servers you have access to." />
        <Tile label="Online" value={data?.online} loading={isLoading} tone="success" tip="The last connection test succeeded." />
        <Tile
          label="Problems"
          value={data?.unreachable}
          loading={isLoading}
          tone="destructive"
          tip="The last test failed: the server could not be reached, or the login was rejected."
        />
        <Tile label="Not checked" value={data?.notChecked} loading={isLoading} tip="Servers that have never been tested." />
        <Tile label="Disabled" value={data?.disabled} loading={isLoading} tip="Monitoring is turned off for these servers." />
      </div>

      <section>
        <div className="mb-3 flex items-center gap-1.5">
          <h2 className="font-semibold">Servers</h2>
          <InfoTip>
            Servers with problems are shown first. The 0-100 health score starts at 100 and loses points for problems: high CPU,
            memory pressure, missing backups, low disk space, CHECKDB overdue, blocking. Hover a score to see why.
          </InfoTip>
        </div>

        {isLoading ? (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-40" />
            ))}
          </div>
        ) : data && data.servers.length === 0 ? (
          <Card>
            <CardContent className="flex flex-col items-center gap-3 p-10 text-center">
              <p className="font-medium">No servers yet</p>
              <p className="max-w-md text-sm text-muted-foreground">
                Add your first SQL Server. IntelligenceDB tests the connection and detects the version, platform and the
                permissions of the login automatically.
              </p>
              {canManage && (
                <Button asChild>
                  <Link to="/servers?add=1">
                    <Plus /> Add server
                  </Link>
                </Button>
              )}
            </CardContent>
          </Card>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {data?.servers.map((s) => <ServerCard key={s.id} server={s} />)}
          </div>
        )}
      </section>
    </div>
  );
}

function Tile({
  label,
  value,
  loading,
  tip,
  tone,
}: {
  label: string;
  value: number | undefined;
  loading: boolean;
  tip: string;
  tone?: "success" | "destructive";
}) {
  return (
    <Card className="p-4">
      <div className="flex items-center gap-1 text-xs font-medium text-muted-foreground">
        {label}
        <InfoTip>{tip}</InfoTip>
      </div>
      {loading ? (
        <Skeleton className="mt-2 h-7 w-12" />
      ) : (
        <div
          className={cn(
            "mt-1 text-2xl font-semibold tabular-nums",
            tone === "success" && value ? "text-success" : "",
            tone === "destructive" && value ? "text-destructive" : "",
          )}
        >
          {value ?? 0}
        </div>
      )}
    </Card>
  );
}

function ServerCard({ server: s }: { server: ServerCardDto }) {
  const queryClient = useQueryClient();
  const test = useMutation({
    mutationFn: () => api(`/servers/${s.id}/test`, { method: "POST" }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["dashboard"] });
      void queryClient.invalidateQueries({ queryKey: ["servers"] });
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <Card className={cn("flex flex-col", s.isEnabled && s.status !== "Online" && s.status !== "Unknown" && "border-destructive/50")}>
      <div className="flex items-start gap-3 p-4 pb-2">
        <HealthBadge score={s.isEnabled ? s.healthScore : null} />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5">
            <StatusDot status={s.status} enabled={s.isEnabled} />
            <Link to={`/servers/${s.id}`} className="block truncate font-semibold hover:underline">
              {s.displayName}
            </Link>
          </div>
          <p className="truncate text-xs text-muted-foreground">
            {s.host}
            {s.agentName && ` · via ${s.agentName}`}
          </p>
        </div>
        <EnvironmentBadge environment={s.environment} />
        <PlatformBadge platform={s.platform} />
      </div>

      <div className="flex flex-wrap gap-x-4 gap-y-1 px-4 text-xs text-muted-foreground">
        <span>{s.versionLabel ?? statusLabels[s.status]}</span>
        {s.cpuCount != null && (
          <span className="inline-flex items-center gap-1" title="Logical CPUs visible to SQL Server">
            <Cpu className="size-3" /> {s.cpuCount}
          </span>
        )}
        {s.physicalMemoryMb != null && (
          <span className="inline-flex items-center gap-1" title="Physical memory of the host">
            <MemoryStick className="size-3" /> {formatMemoryMb(s.physicalMemoryMb)}
          </span>
        )}
        {s.sqlStartTimeUtc && (
          <span className="inline-flex items-center gap-1" title="Time since SQL Server last started">
            <Timer className="size-3" /> {formatUptime(s.sqlStartTimeUtc)}
          </span>
        )}
      </div>

      <div className="flex-1 px-4 py-3">
        {s.issues.length === 0 ? (
          <p className="text-xs text-success">No problems found.</p>
        ) : (
          <ul className="flex flex-col gap-1">
            {s.issues.slice(0, 3).map((issue) => (
              <li key={issue} className="flex gap-1.5 text-xs">
                <AlertTriangle className="mt-0.5 size-3 shrink-0 text-warning" />
                <span>{issue}</span>
              </li>
            ))}
            {s.issues.length > 3 && <li className="text-xs text-muted-foreground">+{s.issues.length - 3} more</li>}
          </ul>
        )}
      </div>

      <div className="flex items-center gap-2 border-t px-4 py-2">
        <span className="text-xs text-muted-foreground">Checked {formatRelative(s.lastCheckedUtc)}</span>
        <Button variant="ghost" size="sm" className="ml-auto" onClick={() => test.mutate()} disabled={test.isPending || !s.isEnabled}>
          <RefreshCw className={cn(test.isPending && "animate-spin")} /> Test
        </Button>
        <Button variant="outline" size="sm" asChild>
          <Link to={`/servers/${s.id}`}>Open</Link>
        </Button>
      </div>
    </Card>
  );
}
