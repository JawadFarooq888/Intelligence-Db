import { useQuery } from "@tanstack/react-query";
import { api, errorMessage } from "@/lib/api";
import type { DatabaseRowDto } from "@/lib/types";
import { DatabasesTable } from "@/features/databases/DatabasesTable";

export function ServerDatabasesTab({ serverId }: { serverId: number }) {
  const { data = [], isLoading, error } = useQuery({
    queryKey: ["server", serverId, "databases"],
    queryFn: () => api<DatabaseRowDto[]>(`/servers/${serverId}/databases`),
    refetchInterval: 60_000,
  });

  return (
    <div className="flex flex-col gap-3">
      {error && <p className="text-sm text-destructive">{errorMessage(error)}</p>}
      <p className="text-sm text-muted-foreground">
        Sizes, files and CHECKDB are collected every 15 minutes; backup history from msdb every 15 minutes. Click a database for files,
        growth and backup history.
      </p>
      <DatabasesTable rows={data} showServer={false} loading={isLoading} />
    </div>
  );
}
