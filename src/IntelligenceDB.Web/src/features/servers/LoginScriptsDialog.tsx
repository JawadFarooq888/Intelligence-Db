import { useQuery } from "@tanstack/react-query";
import { Copy, Download } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Select } from "@/components/ui/primitives";
import { api, errorMessage } from "@/lib/api";
import { platformLabels, platformScriptKey } from "@/lib/format";
import type { PlatformType } from "@/lib/types";
import { cn } from "@/lib/utils";

type Kind = "monitoring" | "admin";

const scriptPlatforms: PlatformType[] = ["OnPremises", "AzureSqlManagedInstance", "AzureSqlDatabase", "AwsRds", "GoogleCloudSql"];

/** Shows the ready-made T-SQL that creates the monitoring / admin login for a platform. */
export function LoginScriptsDialog({
  open,
  onOpenChange,
  platform,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  platform: PlatformType;
}) {
  const [kind, setKind] = useState<Kind>("monitoring");
  const [selected, setSelected] = useState<PlatformType>(platform === "Unknown" ? "OnPremises" : platform);
  const key = platformScriptKey[selected];

  const { data: script, isLoading, error } = useQuery({
    queryKey: ["login-script", kind, key],
    queryFn: () => api<string>(`/servers/login-scripts/${kind}/${key}`),
    enabled: open,
    staleTime: Infinity,
  });

  const copy = async () => {
    if (!script) return;
    try {
      await navigator.clipboard.writeText(script);
      toast.success("Script copied.");
    } catch {
      toast.error("Copy failed - select the text and copy it manually.");
    }
  };

  const download = () => {
    if (!script) return;
    const url = URL.createObjectURL(new Blob([script], { type: "text/plain" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = `intelligencedb-${kind}-login-${key}.sql`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl">
        <DialogHeader>
          <DialogTitle>Login scripts</DialogTitle>
          <DialogDescription>
            Run these on the SQL Server (as an administrator) to create least-privilege logins for IntelligenceDB. Replace the
            password placeholders first.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-wrap items-center gap-2">
          <div className="inline-flex rounded-md border p-0.5">
            {(["monitoring", "admin"] as Kind[]).map((k) => (
              <button
                key={k}
                type="button"
                onClick={() => setKind(k)}
                className={cn(
                  "rounded px-3 py-1 text-sm cursor-pointer",
                  kind === k ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground",
                )}
              >
                {k === "monitoring" ? "Monitoring (read-only)" : "Admin (optional)"}
              </button>
            ))}
          </div>
          <Select className="w-auto" value={selected} onChange={(e) => setSelected(e.target.value as PlatformType)} aria-label="Platform">
            {scriptPlatforms.map((p) => (
              <option key={p} value={p}>
                {platformLabels[p]}
              </option>
            ))}
          </Select>
          <div className="ml-auto flex gap-2">
            <Button variant="outline" size="sm" onClick={() => void copy()} disabled={!script}>
              <Copy /> Copy
            </Button>
            <Button variant="outline" size="sm" onClick={download} disabled={!script}>
              <Download /> Download .sql
            </Button>
          </div>
        </div>

        {error ? (
          <p className="text-sm text-destructive">{errorMessage(error)}</p>
        ) : (
          <pre className="max-h-[55vh] overflow-auto rounded-lg border bg-muted/40 p-4 text-xs leading-relaxed">
            {isLoading ? "Loading..." : script}
          </pre>
        )}
      </DialogContent>
    </Dialog>
  );
}
