import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { Checkbox, Label, Select } from "@/components/ui/primitives";
import { api, errorMessage } from "@/lib/api";
import type { DatabaseRowDto } from "@/lib/types";
import { DatabasesTable } from "./DatabasesTable";

export function DatabasesPage() {
  const [server, setServer] = useState("");
  const [problemsOnly, setProblemsOnly] = useState(false);
  const [includeDropped, setIncludeDropped] = useState(false);

  const { data = [], isLoading, error } = useQuery({
    queryKey: ["databases", includeDropped],
    queryFn: () => api<DatabaseRowDto[]>(`/databases?includeDropped=${includeDropped}`),
    refetchInterval: 60_000,
  });

  const servers = useMemo(() => [...new Map(data.map((d) => [d.serverId, d.serverName])).entries()], [data]);
  const rows = useMemo(
    () =>
      data.filter(
        (d) => (server === "" || String(d.serverId) === server) && (!problemsOnly || d.criticalCount + d.warningCount > 0),
      ),
    [data, server, problemsOnly],
  );
  const critical = data.filter((d) => d.criticalCount > 0).length;

  return (
    <div className="flex flex-col gap-5">
      <div>
        <h1 className="text-2xl font-semibold">Databases</h1>
        <p className="text-sm text-muted-foreground">
          {data.length} database(s) on all servers you can see{critical > 0 && `, ${critical} with critical findings (missing backups, offline, ...)`}.
        </p>
      </div>
      {error && <p className="text-sm text-destructive">{errorMessage(error)}</p>}
      <DatabasesTable
        rows={rows}
        showServer
        loading={isLoading}
        toolbar={
          <>
            <Select className="w-auto" value={server} onChange={(e) => setServer(e.target.value)} aria-label="Server">
              <option value="">All servers</option>
              {servers.map(([id, name]) => (
                <option key={id} value={id}>
                  {name}
                </option>
              ))}
            </Select>
            <div className="flex items-center gap-2">
              <Checkbox id="problems" checked={problemsOnly} onChange={(e) => setProblemsOnly(e.target.checked)} />
              <Label htmlFor="problems" className="font-normal">
                Only with problems
              </Label>
            </div>
            <div className="flex items-center gap-2">
              <Checkbox id="dropped" checked={includeDropped} onChange={(e) => setIncludeDropped(e.target.checked)} />
              <Label htmlFor="dropped" className="font-normal">
                Show dropped
              </Label>
            </div>
          </>
        }
      />
    </div>
  );
}
