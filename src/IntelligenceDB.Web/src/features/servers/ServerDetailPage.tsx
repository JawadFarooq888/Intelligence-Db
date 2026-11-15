import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, ArrowLeft, FileCode2, Pencil, RefreshCw } from "lucide-react";
import { useState, type ReactNode } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { toast } from "sonner";
import { CapabilityRow, EnvironmentBadge, PlatformBadge, StatusBadge } from "@/components/badges";
import { Button } from "@/components/ui/button";
import { Badge, Card, CardContent, CardDescription, CardHeader, CardTitle, Skeleton } from "@/components/ui/primitives";
import { InfoTip } from "@/components/ui/tooltip";
import { api, errorMessage } from "@/lib/api";
import { useCanManageServers } from "@/lib/auth";
import { authTypeLabels, formatDateTime, formatMemoryMb, formatRelative, formatUptime, platformLabels } from "@/lib/format";
import type { ServerDetailDto, TestConnectionResultDto } from "@/lib/types";
import { cn } from "@/lib/utils";
import { LoginScriptsDialog } from "./LoginScriptsDialog";
import { ServerFormDialog } from "./ServerFormDialog";
import { ConfigurationTab } from "./tabs/ConfigurationTab";
import { OverviewTab } from "./tabs/OverviewTab";
import { PerformanceTab } from "./tabs/PerformanceTab";
import { ServerDatabasesTab } from "./tabs/ServerDatabasesTab";
import { HealthBadge } from "@/components/monitoring";

const tabs = [
  { id: "overview", label: "Overview" },
  { id: "performance", label: "Performance" },
  { id: "databases", label: "Databases & backups" },
  { id: "configuration", label: "Configuration" },
  { id: "setup", label: "Connection & features" },
];

export function ServerDetailPage() {
  const id = Number(useParams().id);
  const canManage = useCanManageServers();
  const queryClient = useQueryClient();
  const [editOpen, setEditOpen] = useState(false);
  const [scriptsOpen, setScriptsOpen] = useState(false);
  const [params, setParams] = useSearchParams();
  const tab = params.get("tab") ?? "overview";
  const setTab = (t: string) => setParams((p) => {
    const next = new URLSearchParams(p);
    next.set("tab", t);
    return next;
  }, { replace: true });

  const { data, isLoading, error } = useQuery({
    queryKey: ["server", id],
    queryFn: () => api<ServerDetailDto>(`/servers/${id}`),
    enabled: Number.isFinite(id),
  });

  const test = useMutation({
    mutationFn: () => api<TestConnectionResultDto>(`/servers/${id}/test`, { method: "POST" }),
    onSuccess: (r) => {
      if (r.success) toast.success(`Connected in ${r.elapsedMs} ms.`);
      else toast.error(r.error ?? "Connection failed.");
      void queryClient.invalidateQueries({ queryKey: ["server", id] });
      void queryClient.invalidateQueries({ queryKey: ["servers"] });
      void queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (isLoading) return <Skeleton className="h-64" />;
  if (error || !data) {
    return (
      <div className="flex flex-col gap-3">
        <BackLink />
        <p className="text-sm text-destructive">{error ? errorMessage(error) : "Server not found."}</p>
      </div>
    );
  }

  const s = data.server;
  const info = data.info;
  const unsupported = data.capabilities.filter((c) => !c.isSupported).length;

  return (
    <div className="flex flex-col gap-5">
      <BackLink />

      <div className="flex flex-wrap items-start gap-3">
        <HealthBadge score={s.healthScore} size="lg" />
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold">{s.displayName}</h1>
            <StatusBadge status={s.lastStatus} enabled={s.isEnabled} />
            <PlatformBadge platform={s.effectivePlatform} />
            <EnvironmentBadge environment={s.environment} />
            {s.agentName && <Badge variant="outline">via agent {s.agentName}</Badge>}
          </div>
          <p className="mt-1 font-mono text-xs text-muted-foreground">
            {s.host}
            {s.instanceName && `\\${s.instanceName}`}
            {s.port && `,${s.port}`}
            {s.databaseName && ` / ${s.databaseName}`}
          </p>
          {s.tags.length > 0 && (
            <div className="mt-2 flex flex-wrap gap-1">
              {s.tags.map((t) => (
                <Badge key={t} variant="outline">
                  {t}
                </Badge>
              ))}
            </div>
          )}
        </div>
        <div className="ml-auto flex flex-wrap gap-2">
          <Button variant="outline" onClick={() => setScriptsOpen(true)}>
            <FileCode2 /> Login scripts
          </Button>
          <Button variant="outline" onClick={() => test.mutate()} disabled={test.isPending}>
            <RefreshCw className={cn(test.isPending && "animate-spin")} /> Test now
          </Button>
          {canManage && (
            <Button onClick={() => setEditOpen(true)}>
              <Pencil /> Edit
            </Button>
          )}
        </div>
      </div>

      {s.lastError && (
        <Card className="border-destructive/50 bg-destructive/5">
          <CardContent className="flex gap-2 p-4 text-sm">
            <AlertTriangle className="mt-0.5 size-4 shrink-0 text-destructive" />
            <div>
              <p className="font-medium">Last connection test failed ({formatRelative(s.lastCheckedUtc)})</p>
              <p className="break-words text-muted-foreground">{s.lastError}</p>
            </div>
          </CardContent>
        </Card>
      )}

      {data.platformWarning && (
        <Card className="border-warning/60 bg-warning/10">
          <CardContent className="flex gap-2 p-4 text-sm">
            <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" />
            {data.platformWarning}
          </CardContent>
        </Card>
      )}

      <div className="flex gap-1 overflow-x-auto border-b">
        {tabs.map((t) => (
          <button
            key={t.id}
            onClick={() => setTab(t.id)}
            className={cn(
              "-mb-px whitespace-nowrap border-b-2 px-3 py-2 text-sm cursor-pointer",
              tab === t.id ? "border-primary font-medium text-foreground" : "border-transparent text-muted-foreground hover:text-foreground",
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === "overview" && <OverviewTab serverId={id} onOpenTab={setTab} />}
      {tab === "performance" && <PerformanceTab serverId={id} />}
      {tab === "databases" && <ServerDatabasesTab serverId={id} />}
      {tab === "configuration" && <ConfigurationTab serverId={id} />}
      {tab === "setup" && (
        <>      <div className="grid gap-5 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>Server</CardTitle>
            <CardDescription>
              {info ? `Detected ${formatRelative(info.detectedUtc)} by the last successful connection test.` : "Not detected yet - run a test."}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-2">
              <Item label="Version" tip="SQL Server major version, service pack level and cumulative update (CU). Outdated-CU warnings arrive in Phase 2.">
                {info?.versionLabel ?? "-"}
                {info && <span className="ml-1 font-mono text-xs text-muted-foreground">({info.productVersion})</span>}
              </Item>
              <Item label="Edition" tip="Enterprise/Developer allow online index rebuilds and more parallelism; Standard and Express have limits.">
                {info?.edition ?? "-"}
              </Item>
              <Item label="Platform" tip="Detected from the engine edition and cloud-vendor markers.">
                {platformLabels[s.effectivePlatform]}
              </Item>
              <Item label="Host OS">{info?.hostPlatform ?? "-"}</Item>
              <Item label="CPUs" tip="Logical processors visible to SQL Server.">
                {info?.cpuCount ?? "-"}
              </Item>
              <Item label="Physical memory" tip="Memory of the host. Max server memory and other settings are shown in Phase 2.">
                {formatMemoryMb(info?.physicalMemoryMb)}
              </Item>
              <Item label="Uptime" tip="Time since SQL Server last started. Many statistics (waits, index usage) reset at restart.">
                {formatUptime(info?.sqlStartTimeUtc)}
                {info?.sqlStartTimeUtc && <span className="ml-1 text-xs text-muted-foreground">(since {formatDateTime(info.sqlStartTimeUtc)})</span>}
              </Item>
              <Item label="Collation">{info?.collation ?? "-"}</Item>
              <Item label="@@SERVERNAME">{info?.serverName ?? "-"}</Item>
              <Item label="Group">{s.groupName ?? "(no group)"}</Item>
            </dl>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Connection</CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="flex flex-col gap-3">
              <Item label="Monitoring login">
                {authTypeLabels[data.monitoring.authType]}
                {data.monitoring.userName && <div className="font-mono text-xs">{data.monitoring.userName}</div>}
              </Item>
              <Item
                label="Monitoring login is sysadmin"
                tip="A monitoring login only needs read permissions. sysadmin works but gives IntelligenceDB more power than necessary."
              >
                {info ? (info.isSysadmin ? <span className="text-warning">Yes - more than needed</span> : "No (good)") : "-"}
              </Item>
              <Item label="Admin login" tip="Optional. Used only for control actions you confirm. Every use is audited.">
                {data.admin ? (
                  <>
                    {authTypeLabels[data.admin.authType]}
                    {data.admin.userName && <div className="font-mono text-xs">{data.admin.userName}</div>}
                  </>
                ) : (
                  "Not configured"
                )}
              </Item>
              <Item label="Encryption">
                {data.encrypt ? "TLS required" : "Not required"}
                {data.trustServerCertificate && <span className="text-warning"> · certificate not validated</span>}
              </Item>
              <Item label="Collection interval">{data.collectionIntervalSeconds} s</Item>
            </dl>
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-5 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle className="flex items-center gap-1.5">
              Features on this server
              <InfoTip>
                IntelligenceDB checks what this platform and these logins allow. Unavailable features are hidden or disabled in the
                other modules, with the reason shown here.
              </InfoTip>
            </CardTitle>
            <CardDescription>
              {data.capabilities.length === 0
                ? "Run a connection test to detect features."
                : unsupported === 0
                  ? "Everything is available."
                  : `${unsupported} of ${data.capabilities.length} features are not available.`}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="divide-y">
              {data.capabilities.map((c) => (
                <CapabilityRow key={c.key} cap={c} />
              ))}
            </ul>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-1.5">
              Monitoring login permissions
              <InfoTip>Server-level permissions the monitoring login holds (from fn_my_permissions).</InfoTip>
            </CardTitle>
          </CardHeader>
          <CardContent>
            {data.permissions.length === 0 ? (
              <p className="text-sm text-muted-foreground">-</p>
            ) : (
              <div className="flex flex-wrap gap-1">
                {data.permissions.map((p) => (
                  <Badge key={p} variant="secondary" className="font-mono text-[10px]">
                    {p}
                  </Badge>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
        </>
      )}

      <ServerFormDialog open={editOpen} onOpenChange={setEditOpen} existing={data} />
      <LoginScriptsDialog open={scriptsOpen} onOpenChange={setScriptsOpen} platform={s.effectivePlatform} />
    </div>
  );
}

function BackLink() {
  return (
    <Link to="/servers" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
      <ArrowLeft className="size-4" /> Servers
    </Link>
  );
}

function Item({ label, tip, children }: { label: string; tip?: string; children: ReactNode }) {
  return (
    <div>
      <dt className="flex items-center gap-1 text-xs text-muted-foreground">
        {label}
        {tip && <InfoTip>{tip}</InfoTip>}
      </dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}
