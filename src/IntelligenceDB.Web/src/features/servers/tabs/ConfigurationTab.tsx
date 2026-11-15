import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { useMemo } from "react";
import { DataTable } from "@/components/DataTable";
import { AdviceList } from "@/components/monitoring";
import { Badge, Card, CardContent, CardDescription, CardHeader, CardTitle, Skeleton } from "@/components/ui/primitives";
import { api, errorMessage } from "@/lib/api";
import type { ConfigSettingDto, ServerOverviewDto } from "@/lib/types";

/** Best-practice advice plus every sp_configure setting. Read-only: changes need approval (Phase 5). */
export function ConfigurationTab({ serverId }: { serverId: number }) {
  const { data, isLoading, error } = useQuery({
    queryKey: ["server", serverId, "overview"],
    queryFn: () => api<ServerOverviewDto>(`/servers/${serverId}/overview`),
  });

  const advised = useMemo(() => new Set((data?.configAdvice ?? []).map((a) => a.target).filter(Boolean)), [data]);

  const columns = useMemo<ColumnDef<ConfigSettingDto, unknown>[]>(
    () => [
      {
        accessorKey: "name",
        header: "Setting",
        cell: ({ row }) => (
          <div>
            <span className="font-mono text-xs">{row.original.name}</span>
            {advised.has(row.original.name) && (
              <Badge variant="warning" className="ml-2">
                see advice
              </Badge>
            )}
          </div>
        ),
      },
      { accessorKey: "valueInUse", header: "In use", cell: ({ getValue }) => <span className="tabular-nums">{Number(getValue()).toLocaleString()}</span> },
      {
        accessorKey: "value",
        header: "Configured",
        cell: ({ row }) => (
          <span className="tabular-nums">
            {row.original.value.toLocaleString()}
            {row.original.pendingRestart && (
              <Badge variant="warning" className="ml-2">
                {row.original.isDynamic ? "run RECONFIGURE" : "needs restart"}
              </Badge>
            )}
          </span>
        ),
      },
      { id: "advanced", accessorFn: (c) => (c.isAdvanced ? "advanced" : ""), header: "Type" },
      { accessorKey: "description", header: "Description", cell: ({ getValue }) => <span className="text-xs text-muted-foreground">{String(getValue() ?? "")}</span> },
    ],
    [advised],
  );

  if (isLoading) return <Skeleton className="h-64" />;
  if (error || !data) return <p className="text-sm text-destructive">{errorMessage(error)}</p>;

  return (
    <div className="flex flex-col gap-5">
      <Card>
        <CardHeader>
          <CardTitle>Advice</CardTitle>
          <CardDescription>
            Each finding explains why it matters. IntelligenceDB never changes settings by itself; configuration changes always need
            approval (automation, Phase 5).
          </CardDescription>
        </CardHeader>
        <CardContent>
          <AdviceList items={data.configAdvice} empty="Settings follow best practices." />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>All instance settings (sp_configure)</CardTitle>
        </CardHeader>
        <CardContent>
          {data.settings.length === 0 ? (
            <p className="text-sm text-muted-foreground">Not collected yet, or not available on this platform (Azure SQL Database has no instance settings).</p>
          ) : (
            <DataTable
              data={data.settings}
              columns={columns}
              searchPlaceholder="Filter settings..."
              initialPageSize={25}
              csv={{
                fileName: `server-${serverId}-settings`,
                columns: [
                  { header: "Setting", value: (c) => c.name },
                  { header: "In use", value: (c) => c.valueInUse },
                  { header: "Configured", value: (c) => c.value },
                  { header: "Advanced", value: (c) => c.isAdvanced },
                  { header: "Dynamic", value: (c) => c.isDynamic },
                  { header: "Description", value: (c) => c.description },
                ],
              }}
            />
          )}
        </CardContent>
      </Card>
    </div>
  );
}
