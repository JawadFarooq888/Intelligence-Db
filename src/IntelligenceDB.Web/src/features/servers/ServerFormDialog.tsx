import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, Loader2, PlugZap, XCircle } from "lucide-react";
import { useState, type FormEvent, type ReactNode } from "react";
import { toast } from "sonner";
import { CapabilityRow } from "@/components/badges";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Checkbox, Field, Input, Label, Select } from "@/components/ui/primitives";
import { InfoTip } from "@/components/ui/tooltip";
import { ApiError, api, errorMessage } from "@/lib/api";
import { authTypeLabels, environmentLabels, platformLabels } from "@/lib/format";
import type {
  AgentDto,
  AuthType,
  CredentialDto,
  CredentialInput,
  PlatformType,
  ServerDetailDto,
  ServerEnvironment,
  ServerGroupDto,
  ServerUpsertRequest,
  TestConnectionResultDto,
} from "@/lib/types";

interface CredState {
  authType: AuthType;
  userName: string;
  secret: string;
  tenantId: string;
  clientId: string;
  hasStoredSecret: boolean;
}

interface FormState {
  displayName: string;
  host: string;
  port: string;
  instanceName: string;
  databaseName: string;
  declaredPlatform: PlatformType;
  environment: ServerEnvironment;
  groupId: string;
  agentId: string;
  tags: string;
  isEnabled: boolean;
  encrypt: boolean;
  trustServerCertificate: boolean;
  connectTimeoutSeconds: string;
  collectionIntervalSeconds: string;
  monitoring: CredState;
  useAdmin: boolean;
  admin: CredState;
}

const emptyCred: CredState = { authType: "SqlLogin", userName: "", secret: "", tenantId: "", clientId: "", hasStoredSecret: false };

function credFrom(dto: CredentialDto | null): CredState {
  if (!dto) return { ...emptyCred };
  return {
    authType: dto.authType,
    userName: dto.userName ?? "",
    secret: "",
    tenantId: dto.tenantId ?? "",
    clientId: dto.clientId ?? "",
    hasStoredSecret: dto.hasSecret,
  };
}

function initialState(existing?: ServerDetailDto): FormState {
  if (!existing) {
    return {
      displayName: "",
      host: "",
      port: "",
      instanceName: "",
      databaseName: "",
      declaredPlatform: "OnPremises",
      environment: "Production",
      groupId: "",
      agentId: "",
      tags: "",
      isEnabled: true,
      encrypt: true,
      trustServerCertificate: false,
      connectTimeoutSeconds: "15",
      collectionIntervalSeconds: "60",
      monitoring: { ...emptyCred, userName: "IntelligenceDB_Monitor" },
      useAdmin: false,
      admin: { ...emptyCred, userName: "IntelligenceDB_Admin" },
    };
  }

  const s = existing.server;
  return {
    displayName: s.displayName,
    host: s.host,
    port: s.port?.toString() ?? "",
    instanceName: s.instanceName ?? "",
    databaseName: s.databaseName ?? "",
    declaredPlatform: s.declaredPlatform,
    environment: s.environment,
    groupId: s.groupId?.toString() ?? "",
    agentId: s.agentId ?? "",
    tags: s.tags.join(", "),
    isEnabled: s.isEnabled,
    encrypt: existing.encrypt,
    trustServerCertificate: existing.trustServerCertificate,
    connectTimeoutSeconds: existing.connectTimeoutSeconds.toString(),
    collectionIntervalSeconds: existing.collectionIntervalSeconds.toString(),
    monitoring: credFrom(existing.monitoring),
    useAdmin: existing.admin != null,
    admin: existing.admin ? credFrom(existing.admin) : { ...emptyCred, userName: "IntelligenceDB_Admin" },
  };
}

function toCredInput(c: CredState): CredentialInput {
  return {
    authType: c.authType,
    userName: c.userName.trim() || null,
    // Empty secret = keep the stored one (edit). The API never returns secrets.
    secret: c.secret || null,
    tenantId: c.tenantId.trim() || null,
    clientId: c.clientId.trim() || null,
  };
}

function toNumber(v: string, fallback: number | null): number | null {
  const n = Number.parseInt(v, 10);
  return Number.isFinite(n) ? n : fallback;
}

function toRequest(f: FormState, existing?: ServerDetailDto): ServerUpsertRequest {
  return {
    displayName: f.displayName.trim(),
    host: f.host.trim(),
    port: toNumber(f.port, null),
    instanceName: f.instanceName.trim() || null,
    databaseName: f.databaseName.trim() || null,
    declaredPlatform: f.declaredPlatform,
    environment: f.environment,
    groupId: toNumber(f.groupId, null),
    agentId: f.agentId || null,
    tags: f.tags
      .split(",")
      .map((t) => t.trim())
      .filter(Boolean),
    isEnabled: f.isEnabled,
    encrypt: f.encrypt,
    trustServerCertificate: f.trustServerCertificate,
    connectTimeoutSeconds: toNumber(f.connectTimeoutSeconds, 15) ?? 15,
    collectionIntervalSeconds: toNumber(f.collectionIntervalSeconds, 60) ?? 60,
    monitoring: toCredInput(f.monitoring),
    admin: f.useAdmin ? toCredInput(f.admin) : null,
    removeAdmin: !f.useAdmin && existing?.admin != null,
    rowVersion: existing?.rowVersion ?? null,
  };
}

const platformHints: Record<PlatformType, string> = {
  Unknown: "IntelligenceDB detects the platform during the connection test.",
  OnPremises: "Host name, FQDN or IP. Add the instance name for a named instance, or a port if it is not 1433.",
  AzureSqlManagedInstance: "Use <instance>.<dns-zone>.database.windows.net. Public endpoint uses port 3342.",
  AzureSqlDatabase: "Use <server>.database.windows.net and enter the database name. One entry per database.",
  AwsRds: "Use the RDS endpoint, e.g. mydb.abc123.eu-west-1.rds.amazonaws.com, port 1433.",
  GoogleCloudSql: "Use the instance's public or private IP address, port 1433.",
};

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  existing?: ServerDetailDto;
  onSaved?: (server: ServerDetailDto) => void;
}

export function ServerFormDialog({ open, onOpenChange, existing, onSaved }: Props) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl">
        {/* Re-mount the form each time the dialog opens so it starts from fresh values. */}
        {open && <ServerForm existing={existing} onDone={onOpenChange} onSaved={onSaved} />}
      </DialogContent>
    </Dialog>
  );
}

function ServerForm({
  existing,
  onDone,
  onSaved,
}: {
  existing?: ServerDetailDto;
  onDone: (open: boolean) => void;
  onSaved?: (server: ServerDetailDto) => void;
}) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState<FormState>(() => initialState(existing));
  const [testResult, setTestResult] = useState<TestConnectionResultDto | null>(null);
  const [fieldError, setFieldError] = useState<ApiError | null>(null);

  const { data: groups = [] } = useQuery({
    queryKey: ["server-groups"],
    queryFn: () => api<ServerGroupDto[]>("/server-groups"),
  });
  const { data: agents = [] } = useQuery({
    queryKey: ["agents"],
    queryFn: () => api<AgentDto[]>("/agents"),
  });

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => setForm((f) => ({ ...f, [key]: value }));

  const test = useMutation({
    mutationFn: () =>
      api<TestConnectionResultDto>(`/servers/test${existing ? `?existingId=${existing.server.id}` : ""}`, {
        method: "POST",
        body: toRequest(form, existing),
      }),
    onMutate: () => {
      setFieldError(null);
      setTestResult(null);
    },
    onSuccess: setTestResult,
    onError: (e) => {
      if (e instanceof ApiError && Object.keys(e.errors).length) setFieldError(e);
      else toast.error(errorMessage(e));
    },
  });

  const save = useMutation({
    mutationFn: () =>
      existing
        ? api<ServerDetailDto>(`/servers/${existing.server.id}`, { method: "PUT", body: toRequest(form, existing) })
        : api<ServerDetailDto>("/servers", { method: "POST", body: toRequest(form) }),
    onMutate: () => setFieldError(null),
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ["servers"] });
      void queryClient.invalidateQueries({ queryKey: ["dashboard"] });
      void queryClient.invalidateQueries({ queryKey: ["server-groups"] });
      queryClient.setQueryData(["server", saved.server.id], saved);
      const status = saved.server.lastStatus;
      if (status === "Online") toast.success(`${saved.server.displayName} saved and connected.`);
      else toast.warning(`${saved.server.displayName} saved, but the connection test failed: ${saved.server.lastError ?? status}`);
      onSaved?.(saved);
      onDone(false);
    },
    onError: (e) => {
      if (e instanceof ApiError && Object.keys(e.errors).length) setFieldError(e);
      else toast.error(errorMessage(e));
    },
  });

  const err = (name: string) => fieldError?.field(name);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-5">
      <DialogHeader>
        <DialogTitle>{existing ? `Edit ${existing.server.displayName}` : "Add server"}</DialogTitle>
        <DialogDescription>
          Use the read-only monitoring login for everyday monitoring. An admin login is optional and only used for actions you
          confirm.
        </DialogDescription>
      </DialogHeader>

      {fieldError && <p className="rounded-md bg-destructive/10 p-2 text-sm text-destructive">Please fix the highlighted fields.</p>}

      <Section title="Connection">
        <Field
          label={
            <>
              Connect via{" "}
              <InfoTip>
                "Directly" means the portal connects to the server itself (cloud servers, or servers in the portal's network). Choose
                an agent for servers inside a network the portal cannot reach, such as an office or data centre.
              </InfoTip>
            </>
          }
          htmlFor="agent"
          error={err("agentId")}
          hint={agents.length === 0 ? "Add agents in Settings → Agents to monitor servers inside other networks." : undefined}
          className="sm:col-span-4"
        >
          <Select id="agent" value={form.agentId} onChange={(e) => set("agentId", e.target.value)}>
            <option value="">Directly from the portal</option>
            {agents.map((a) => (
              <option key={a.id} value={a.id} disabled={!a.isEnabled}>
                Agent: {a.name} {a.isOnline ? "(online)" : "(offline)"}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Display name" htmlFor="displayName" error={err("displayName")} className="sm:col-span-2">
          <Input id="displayName" required value={form.displayName} onChange={(e) => set("displayName", e.target.value)} placeholder="PROD-SQL-01" />
        </Field>
        <Field
          label={
            <>
              Platform <InfoTip>Where the server runs. IntelligenceDB also detects it and shows a warning if they differ.</InfoTip>
            </>
          }
          htmlFor="platform"
          hint={platformHints[form.declaredPlatform]}
          className="sm:col-span-2"
        >
          <Select id="platform" value={form.declaredPlatform} onChange={(e) => set("declaredPlatform", e.target.value as PlatformType)}>
            {(Object.keys(platformLabels) as PlatformType[]).map((p) => (
              <option key={p} value={p}>
                {p === "Unknown" ? "Detect automatically" : platformLabels[p]}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Host" htmlFor="host" error={err("host")} className="sm:col-span-2">
          <Input id="host" required value={form.host} onChange={(e) => set("host", e.target.value)} placeholder="sql01.contoso.local" />
        </Field>
        <Field label="Port" htmlFor="port" error={err("port")} hint="Empty = default (1433 or SQL Browser)">
          <Input id="port" inputMode="numeric" value={form.port} onChange={(e) => set("port", e.target.value)} placeholder="1433" />
        </Field>
        <Field label="Instance name" htmlFor="instance" error={err("instanceName")} hint="Only for named instances">
          <Input id="instance" value={form.instanceName} onChange={(e) => set("instanceName", e.target.value)} placeholder="SQL2022" />
        </Field>
        <Field
          label="Database"
          htmlFor="database"
          error={err("databaseName")}
          hint={form.declaredPlatform === "AzureSqlDatabase" ? "Required for Azure SQL Database" : "Empty = master"}
          className="sm:col-span-2"
        >
          <Input id="database" value={form.databaseName} onChange={(e) => set("databaseName", e.target.value)} />
        </Field>
        <Field label="Connect timeout (s)" htmlFor="timeout" error={err("connectTimeoutSeconds")}>
          <Input id="timeout" inputMode="numeric" value={form.connectTimeoutSeconds} onChange={(e) => set("connectTimeoutSeconds", e.target.value)} />
        </Field>
        <div className="flex flex-col justify-end gap-2 sm:col-span-3">
          <CheckboxRow
            id="encrypt"
            checked={form.encrypt}
            onChange={(v) => set("encrypt", v)}
            label="Encrypt connection (TLS)"
            tip="Recommended. Required by Azure SQL. Traffic between IntelligenceDB and the server is encrypted."
          />
          <CheckboxRow
            id="trust"
            checked={form.trustServerCertificate}
            onChange={(v) => set("trustServerCertificate", v)}
            label="Trust server certificate"
            tip="Skips TLS certificate validation. Only for test servers with self-signed certificates - it allows man-in-the-middle attacks."
          />
        </div>
      </Section>

      <Section title="Organisation">
        <Field label="Environment" htmlFor="env">
          <Select id="env" value={form.environment} onChange={(e) => set("environment", e.target.value as ServerEnvironment)}>
            {(Object.keys(environmentLabels) as ServerEnvironment[]).map((v) => (
              <option key={v} value={v}>
                {environmentLabels[v]}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Group" htmlFor="group" error={err("groupId")}>
          <Select id="group" value={form.groupId} onChange={(e) => set("groupId", e.target.value)}>
            <option value="">(no group)</option>
            {groups.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Tags" htmlFor="tags" error={err("tags")} hint="Comma separated, e.g. finance, tier1" className="sm:col-span-2">
          <Input id="tags" value={form.tags} onChange={(e) => set("tags", e.target.value)} />
        </Field>
        <Field
          label={
            <>
              Collection interval (s){" "}
              <InfoTip>How often the collector (Phase 2) samples this server. Lower = more detail, slightly more load.</InfoTip>
            </>
          }
          htmlFor="interval"
          error={err("collectionIntervalSeconds")}
        >
          <Input id="interval" inputMode="numeric" value={form.collectionIntervalSeconds} onChange={(e) => set("collectionIntervalSeconds", e.target.value)} />
        </Field>
        <div className="flex items-end sm:col-span-3">
          <CheckboxRow id="enabled" checked={form.isEnabled} onChange={(v) => set("isEnabled", v)} label="Monitoring enabled" />
        </div>
      </Section>

      <Section
        title={
          <>
            Monitoring login{" "}
            <InfoTip>A read-only login (VIEW SERVER STATE, VIEW ANY DEFINITION, msdb read roles). See Login scripts on the server page.</InfoTip>
          </>
        }
      >
        <CredentialFields prefix="monitoring" value={form.monitoring} onChange={(v) => set("monitoring", v)} err={err} />
      </Section>

      <Section
        title={
          <>
            Admin login (optional){" "}
            <InfoTip>Used only for control actions you confirm (start/stop jobs, kill session, index maintenance). Every use is audited.</InfoTip>
          </>
        }
      >
        <div className="sm:col-span-4">
          <CheckboxRow
            id="useAdmin"
            checked={form.useAdmin}
            onChange={(v) => set("useAdmin", v)}
            label={existing?.admin ? "Keep an admin login for this server (untick to remove it)" : "Configure an admin login"}
          />
        </div>
        {form.useAdmin && <CredentialFields prefix="admin" value={form.admin} onChange={(v) => set("admin", v)} err={err} />}
      </Section>

      {(test.isPending || testResult) && <TestResultPanel pending={test.isPending} result={testResult} />}

      <DialogFooter className="sticky bottom-0 -mx-6 -mb-6 border-t bg-background px-6 py-3">
        <Button type="button" variant="outline" onClick={() => test.mutate()} disabled={test.isPending || save.isPending}>
          {test.isPending ? <Loader2 className="animate-spin" /> : <PlugZap />} Test connection
        </Button>
        <Button type="submit" disabled={save.isPending}>
          {save.isPending && <Loader2 className="animate-spin" />}
          {existing ? "Save changes" : "Add server"}
        </Button>
      </DialogFooter>
    </form>
  );
}

function Section({ title, children }: { title: ReactNode; children: ReactNode }) {
  return (
    <fieldset className="rounded-lg border p-4">
      <legend className="flex items-center gap-1 px-1 text-sm font-semibold">{title}</legend>
      <div className="grid gap-4 sm:grid-cols-4">{children}</div>
    </fieldset>
  );
}

function CheckboxRow({
  id,
  checked,
  onChange,
  label,
  tip,
}: {
  id: string;
  checked: boolean;
  onChange: (v: boolean) => void;
  label: string;
  tip?: string;
}) {
  return (
    <div className="flex items-center gap-2">
      <Checkbox id={id} checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <Label htmlFor={id} className="font-normal">
        {label}
      </Label>
      {tip && <InfoTip>{tip}</InfoTip>}
    </div>
  );
}

function CredentialFields({
  prefix,
  value,
  onChange,
  err,
}: {
  prefix: "monitoring" | "admin";
  value: CredState;
  onChange: (v: CredState) => void;
  err: (name: string) => string | undefined;
}) {
  const t = value.authType;
  const set = <K extends keyof CredState>(key: K, v: CredState[K]) => onChange({ ...value, [key]: v });
  const needsUser = t === "SqlLogin" || t === "EntraPassword" || t === "EntraServicePrincipal";
  const needsSecret = needsUser;
  const secretPlaceholder = value.hasStoredSecret ? "•••••••• (unchanged - type to replace)" : "";

  return (
    <>
      <Field label="Authentication" htmlFor={`${prefix}-auth`} error={err(`${prefix}.authType`)} className="sm:col-span-4">
        <Select id={`${prefix}-auth`} value={t} onChange={(e) => set("authType", e.target.value as AuthType)}>
          {(Object.keys(authTypeLabels) as AuthType[]).map((a) => (
            <option key={a} value={a}>
              {authTypeLabels[a]}
            </option>
          ))}
        </Select>
      </Field>

      {t === "Windows" && (
        <p className="text-xs text-muted-foreground sm:col-span-4">
          Uses the Windows account IntelligenceDB runs as (service account or gMSA). Works when IntelligenceDB runs on a
          domain-joined Windows host; Linux containers need Kerberos configuration.
        </p>
      )}
      {t === "EntraDefault" && (
        <p className="text-xs text-muted-foreground sm:col-span-4">
          Uses the default Azure credential chain of the host (environment variables, workload identity, managed identity, Azure
          CLI).
        </p>
      )}
      {t === "EntraPassword" && (
        <p className="text-xs text-muted-foreground sm:col-span-4">
          Microsoft is retiring password sign-in for Entra accounts with MFA. Prefer a service principal or managed identity.
        </p>
      )}

      {needsUser && (
        <Field
          label={t === "EntraServicePrincipal" ? "Application (client) ID" : t === "EntraPassword" ? "User (UPN)" : "Login name"}
          htmlFor={`${prefix}-user`}
          error={err(`${prefix}.userName`)}
          className="sm:col-span-2"
        >
          <Input id={`${prefix}-user`} autoComplete="off" value={value.userName} onChange={(e) => set("userName", e.target.value)} />
        </Field>
      )}
      {needsSecret && (
        <Field
          label={t === "EntraServicePrincipal" ? "Client secret" : "Password"}
          htmlFor={`${prefix}-secret`}
          error={err(`${prefix}.secret`)}
          hint="Stored encrypted. Never shown again."
          className="sm:col-span-2"
        >
          <Input
            id={`${prefix}-secret`}
            type="password"
            autoComplete="new-password"
            value={value.secret}
            placeholder={secretPlaceholder}
            onChange={(e) => set("secret", e.target.value)}
          />
        </Field>
      )}
      {t === "EntraServicePrincipal" && (
        <Field label="Tenant ID (optional)" htmlFor={`${prefix}-tenant`} error={err(`${prefix}.tenantId`)} className="sm:col-span-2">
          <Input id={`${prefix}-tenant`} value={value.tenantId} onChange={(e) => set("tenantId", e.target.value)} />
        </Field>
      )}
      {t === "EntraManagedIdentity" && (
        <Field
          label="Client ID (optional)"
          htmlFor={`${prefix}-client`}
          error={err(`${prefix}.clientId`)}
          hint="Only for a user-assigned managed identity"
          className="sm:col-span-2"
        >
          <Input id={`${prefix}-client`} value={value.clientId} onChange={(e) => set("clientId", e.target.value)} />
        </Field>
      )}
    </>
  );
}

export function TestResultPanel({ pending, result }: { pending: boolean; result: TestConnectionResultDto | null }) {
  if (pending || !result) {
    return (
      <div className="flex items-center gap-2 rounded-lg border p-4 text-sm text-muted-foreground">
        <Loader2 className="size-4 animate-spin" /> Connecting and detecting capabilities...
      </div>
    );
  }

  return (
    <div className="rounded-lg border p-4">
      <div className="flex items-start gap-2">
        {result.success ? (
          <CheckCircle2 className="mt-0.5 size-5 shrink-0 text-success" />
        ) : (
          <XCircle className="mt-0.5 size-5 shrink-0 text-destructive" />
        )}
        <div className="min-w-0 flex-1">
          <p className="font-medium">
            {result.success ? `Connected in ${result.elapsedMs} ms` : "Connection failed"}
            {result.success && result.detectedPlatform && ` - ${platformLabels[result.detectedPlatform]}`}
          </p>
          {result.success ? (
            <p className="text-sm text-muted-foreground">
              {result.versionLabel} · {result.edition}
              {result.isSysadmin && " · login is sysadmin (more than monitoring needs)"}
            </p>
          ) : (
            <p className="break-words text-sm text-destructive">{result.error}</p>
          )}
        </div>
      </div>

      {result.platformWarning && (
        <p className="mt-3 flex gap-1.5 text-sm">
          <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" /> {result.platformWarning}
        </p>
      )}

      {result.adminTested && (
        <p className="mt-2 text-sm">
          <span className="font-medium">Admin login: </span>
          {result.adminSuccess ? <span className="text-success">connected</span> : <span className="text-destructive">{result.adminError}</span>}
        </p>
      )}

      {result.capabilities.length > 0 && (
        <div className="mt-3">
          <p className="text-sm font-medium">
            Features on this server ({result.capabilities.filter((c) => c.isSupported).length} of {result.capabilities.length} available)
          </p>
          <ul className="divide-y">
            {result.capabilities.map((c) => (
              <CapabilityRow key={c.key} cap={c} />
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
