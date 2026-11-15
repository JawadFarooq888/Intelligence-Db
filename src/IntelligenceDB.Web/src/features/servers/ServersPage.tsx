import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { FolderTree, Pencil, Plus, RefreshCw, Trash2 } from "lucide-react";
import { useMemo, useState, type ReactNode } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { toast } from "sonner";
import { EnvironmentBadge, PlatformBadge, StatusBadge } from "@/components/badges";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { DataTable } from "@/components/DataTable";
import { Button } from "@/components/ui/button";
import { Badge, Select } from "@/components/ui/primitives";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { api, errorMessage } from "@/lib/api";
import { useCanManageServers } from "@/lib/auth";
import { formatRelative, platformLabels, statusLabels } from "@/lib/format";
import type { ServerDetailDto, ServerGroupDto, ServerSummaryDto } from "@/lib/types";
import { GroupsDialog } from "./GroupsDialog";
import { ServerFormDialog } from "./ServerFormDialog";

export function ServersPage() {
  const canManage = useCanManageServers();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [params, setParams] = useSearchParams();

  const [formOpen, setFormOpen] = useState(params.get("add") === "1");
  const [editing, setEditing] = useState<ServerDetailDto | undefined>();
  const [groupsOpen, setGroupsOpen] = useState(false);
  const [deleting, setDeleting] = useState<ServerSummaryDto | null>(null);
  const groupFilter = params.get("group") ?? "";

  const { data: servers = [], isLoading } = useQuery({
    queryKey: ["servers"],
    queryFn: () => api<ServerSummaryDto[]>("/servers"),
  });
  const { data: groups = [] } = useQuery({
    queryKey: ["server-groups"],
    queryFn: () => api<ServerGroupDto[]>("/server-groups"),
  });

  const visible = useMemo(
    () =>
      groupFilter === ""
        ? servers
        : groupFilter === "none"
          ? servers.filter((s) => s.groupId == null)
          : servers.filter((s) => String(s.groupId) === groupFilter),
    [servers, groupFilter],
  );

  const test = useMutation({
    mutationFn: (id: number) => api(`/servers/${id}/test`, { method: "POST" }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["servers"] });
      void queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const remove = useMutation({
    mutationFn: (id: number) => api<void>(`/servers/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success(`${deleting?.displayName} removed.`);
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: ["servers"] });
      void queryClient.invalidateQueries({ queryKey: ["dashboard"] });
      void queryClient.invalidateQueries({ queryKey: ["server-groups"] });
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const openEdit = async (id: number) => {
    try {
      const detail = await queryClient.fetchQuery({
        queryKey: ["server", id],
        queryFn: () => api<ServerDetailDto>(`/servers/${id}`),
      });
      setEditing(detail);
      setFormOpen(true);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  };

  const columns = useMemo<ColumnDef<ServerSummaryDto, unknown>[]>(
    () => [
      {
        accessorKey: "displayName",
        header: "Name",
        cell: ({ row }) => (
          <div className="min-w-0">
            <div className="font-medium">{row.original.displayName}</div>
            {row.original.tags.length > 0 && (
              <div className="mt-0.5 flex flex-wrap gap-1">
                {row.original.tags.map((t) => (
                  <Badge key={t} variant="outline" className="px-1.5 py-0 text-[10px]">
                    {t}
                  </Badge>
                ))}
              </div>
            )}
          </div>
        ),
      },
      {
        id: "host",
        accessorFn: (s) => `${s.host}${s.instanceName ? `\\${s.instanceName}` : ""}${s.port ? `,${s.port}` : ""}${s.databaseName ? ` / ${s.databaseName}` : ""}`,
        header: "Host",
        cell: ({ getValue }) => <span className="font-mono text-xs">{String(getValue())}</span>,
      },
      {
        id: "platform",
        accessorFn: (s) => platformLabels[s.effectivePlatform],
        header: "Platform",
        cell: ({ row }) => <PlatformBadge platform={row.original.effectivePlatform} />,
      },
      {
        accessorKey: "environment",
        header: "Env",
        cell: ({ row }) => <EnvironmentBadge environment={row.original.environment} />,
      },
      { id: "group", accessorFn: (s) => s.groupName ?? "", header: "Group" },
      {
        id: "status",
        accessorFn: (s) => (s.isEnabled ? statusLabels[s.lastStatus] : "Disabled"),
        header: "Status",
        cell: ({ row }) => (
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                <StatusBadge status={row.original.lastStatus} enabled={row.original.isEnabled} />
              </span>
            </TooltipTrigger>
            <TooltipContent>
              {row.original.lastError ?? `Last checked ${formatRelative(row.original.lastCheckedUtc)}`}
            </TooltipContent>
          </Tooltip>
        ),
      },
      { id: "version", accessorFn: (s) => s.versionLabel ?? "", header: "Version" },
      {
        id: "checked",
        accessorFn: (s) => s.lastCheckedUtc ?? "",
        header: "Checked",
        cell: ({ row }) => <span className="text-muted-foreground">{formatRelative(row.original.lastCheckedUtc)}</span>,
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => (
          <div className="flex justify-end gap-1" onClick={(e) => e.stopPropagation()}>
            <IconAction label="Test connection" onClick={() => test.mutate(row.original.id)} disabled={test.isPending && test.variables === row.original.id}>
              <RefreshCw className={test.isPending && test.variables === row.original.id ? "animate-spin" : ""} />
            </IconAction>
            {canManage && (
              <>
                <IconAction label="Edit" onClick={() => void openEdit(row.original.id)}>
                  <Pencil />
                </IconAction>
                <IconAction label="Remove" onClick={() => setDeleting(row.original)}>
                  <Trash2 className="text-destructive" />
                </IconAction>
              </>
            )}
          </div>
        ),
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [canManage, test.isPending, test.variables],
  );

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Servers</h1>
          <p className="text-sm text-muted-foreground">Every SQL Server instance and Azure SQL database IntelligenceDB monitors.</p>
        </div>
        {canManage && (
          <div className="ml-auto flex gap-2">
            <Button variant="outline" onClick={() => setGroupsOpen(true)}>
              <FolderTree /> Groups
            </Button>
            <Button
              onClick={() => {
                setEditing(undefined);
                setFormOpen(true);
              }}
            >
              <Plus /> Add server
            </Button>
          </div>
        )}
      </div>

      <DataTable
        data={visible}
        columns={columns}
        searchPlaceholder="Filter by name, host, version..."
        emptyMessage={isLoading ? "Loading..." : "No servers match."}
        onRowClick={(s) => navigate(`/servers/${s.id}`)}
        csv={{
          fileName: "intelligencedb-servers",
          columns: [
            { header: "Name", value: (s) => s.displayName },
            { header: "Host", value: (s) => s.host },
            { header: "Port", value: (s) => s.port },
            { header: "Instance", value: (s) => s.instanceName },
            { header: "Database", value: (s) => s.databaseName },
            { header: "Platform", value: (s) => platformLabels[s.effectivePlatform] },
            { header: "Environment", value: (s) => s.environment },
            { header: "Group", value: (s) => s.groupName },
            { header: "Tags", value: (s) => s.tags.join("; ") },
            { header: "Status", value: (s) => (s.isEnabled ? statusLabels[s.lastStatus] : "Disabled") },
            { header: "Version", value: (s) => s.versionLabel },
            { header: "Edition", value: (s) => s.edition },
            { header: "Last checked (UTC)", value: (s) => s.lastCheckedUtc },
          ],
        }}
        toolbar={
          <Select
            className="w-auto"
            value={groupFilter}
            aria-label="Filter by group"
            onChange={(e) => {
              const next = new URLSearchParams(params);
              if (e.target.value) next.set("group", e.target.value);
              else next.delete("group");
              setParams(next, { replace: true });
            }}
          >
            <option value="">All groups</option>
            <option value="none">(no group)</option>
            {groups.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name} ({g.serverCount})
              </option>
            ))}
          </Select>
        }
      />

      <ServerFormDialog
        open={formOpen}
        existing={editing}
        onOpenChange={(o) => {
          setFormOpen(o);
          if (!o && params.has("add")) {
            const next = new URLSearchParams(params);
            next.delete("add");
            setParams(next, { replace: true });
          }
        }}
        onSaved={(saved) => {
          if (!editing) navigate(`/servers/${saved.server.id}`);
        }}
      />

      <GroupsDialog open={groupsOpen} onOpenChange={setGroupsOpen} />

      <ConfirmDialog
        open={deleting != null}
        onOpenChange={(o) => !o && setDeleting(null)}
        title={`Remove ${deleting?.displayName ?? "server"}?`}
        description={
          <>
            IntelligenceDB stops monitoring this server and deletes its stored credentials. Nothing is changed on the SQL Server
            itself. The audit log keeps the history.
          </>
        }
        confirmLabel="Remove server"
        destructive
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
      />
    </div>
  );
}

function IconAction({
  label,
  onClick,
  disabled,
  children,
}: {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  children: ReactNode;
}) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button variant="ghost" size="icon" className="size-8" onClick={onClick} disabled={disabled} aria-label={label}>
          {children}
        </Button>
      </TooltipTrigger>
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}
