import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, KeyRound, Plus, Power, Trash2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Badge, Card, CardContent, CardDescription, CardHeader, CardTitle, Field, Input } from "@/components/ui/primitives";
import { InfoTip } from "@/components/ui/tooltip";
import { api, errorMessage } from "@/lib/api";
import { formatRelative } from "@/lib/format";
import type { AgentCreatedDto, AgentDto } from "@/lib/types";

/** Admin: on-premises agents that reach SQL Servers inside customer networks. */
export function AgentsCard() {
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [name, setName] = useState("");
  const [created, setCreated] = useState<AgentCreatedDto | null>(null);
  const [deleting, setDeleting] = useState<AgentDto | null>(null);
  const [regenerating, setRegenerating] = useState<AgentDto | null>(null);

  const { data: agents = [] } = useQuery({ queryKey: ["agents"], queryFn: () => api<AgentDto[]>("/agents"), refetchInterval: 30_000 });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ["agents"] });

  const create = useMutation({
    mutationFn: () => api<AgentCreatedDto>("/agents", { method: "POST", body: { name, isEnabled: true } }),
    onSuccess: (r) => {
      setAdding(false);
      setName("");
      setCreated(r);
      refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const regenerate = useMutation({
    mutationFn: (a: AgentDto) => api<AgentCreatedDto>(`/agents/${a.id}/regenerate-key`, { method: "POST" }),
    onSuccess: (r) => {
      setRegenerating(null);
      setCreated(r);
      refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const toggle = useMutation({
    mutationFn: (a: AgentDto) => api<AgentDto>(`/agents/${a.id}`, { method: "PUT", body: { name: a.name, isEnabled: !a.isEnabled } }),
    onSuccess: refresh,
    onError: (e) => toast.error(errorMessage(e)),
  });

  const remove = useMutation({
    mutationFn: (a: AgentDto) => api<void>(`/agents/${a.id}`, { method: "DELETE" }),
    onSuccess: () => {
      setDeleting(null);
      refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (name.trim()) create.mutate();
  };

  return (
    <Card>
      <CardHeader className="flex-row items-start">
        <div className="flex-1">
          <CardTitle className="flex items-center gap-1.5">
            Agents
            <InfoTip>
              An agent is a small Windows service installed on one machine inside a network the portal cannot reach (office, data
              centre). It monitors the SQL Servers there and sends the data out to the portal over HTTPS - no inbound firewall ports.
            </InfoTip>
          </CardTitle>
          <CardDescription>Choose the agent under "Connect via" when you add a server that lives in that network.</CardDescription>
        </div>
        <Button onClick={() => setAdding(true)}>
          <Plus /> Add agent
        </Button>
      </CardHeader>
      <CardContent className="overflow-x-auto">
        {agents.length === 0 ? (
          <p className="text-sm text-muted-foreground">No agents yet. Servers are monitored directly from the portal.</p>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left text-muted-foreground">
              <tr>
                <th className="py-1.5 font-medium">Name</th>
                <th className="py-1.5 font-medium">Status</th>
                <th className="py-1.5 font-medium">Machine / version</th>
                <th className="py-1.5 font-medium">Servers</th>
                <th className="py-1.5 font-medium">Key</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {agents.map((a) => (
                <tr key={a.id} className="border-t">
                  <td className="py-2 font-medium">{a.name}</td>
                  <td className="py-2">
                    {!a.isEnabled ? (
                      <Badge variant="secondary">Disabled</Badge>
                    ) : a.isOnline ? (
                      <Badge variant="success">Online</Badge>
                    ) : (
                      <Badge variant="destructive">Offline</Badge>
                    )}
                    <div className="text-xs text-muted-foreground">seen {formatRelative(a.lastSeenUtc)}</div>
                  </td>
                  <td className="py-2">
                    {a.machineName ?? "-"}
                    <div className="text-xs text-muted-foreground">
                      {a.version ? `v${a.version}` : "not connected yet"} {a.lastIp && `· ${a.lastIp}`}
                    </div>
                  </td>
                  <td className="py-2 tabular-nums">{a.serverCount}</td>
                  <td className="py-2 font-mono text-xs">{a.keyPrefix}…</td>
                  <td className="py-2">
                    <div className="flex justify-end gap-1">
                      <Button variant="ghost" size="icon" className="size-8" title="New key" aria-label="Generate a new key" onClick={() => setRegenerating(a)}>
                        <KeyRound />
                      </Button>
                      <Button variant="ghost" size="icon" className="size-8" title={a.isEnabled ? "Disable" : "Enable"} aria-label={a.isEnabled ? "Disable agent" : "Enable agent"} onClick={() => toggle.mutate(a)}>
                        <Power className={a.isEnabled ? "" : "text-muted-foreground"} />
                      </Button>
                      <Button variant="ghost" size="icon" className="size-8" title="Delete" aria-label="Delete agent" onClick={() => setDeleting(a)}>
                        <Trash2 className="text-destructive" />
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </CardContent>

      <Dialog open={adding} onOpenChange={setAdding}>
        <DialogContent>
          <form onSubmit={submit} className="flex flex-col gap-4">
            <DialogHeader>
              <DialogTitle>Add agent</DialogTitle>
              <DialogDescription>Give it a name that says where it runs, e.g. "Karachi office" or "DC1".</DialogDescription>
            </DialogHeader>
            <Field label="Name" htmlFor="agent-name">
              <Input id="agent-name" autoFocus required value={name} onChange={(e) => setName(e.target.value)} />
            </Field>
            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => setAdding(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={create.isPending || !name.trim()}>
                Create agent
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      <Dialog open={created != null} onOpenChange={(o) => !o && setCreated(null)}>
        <DialogContent className="max-w-2xl">
          {created && <InstallInstructions created={created} onDone={() => setCreated(null)} />}
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={regenerating != null}
        onOpenChange={(o) => !o && setRegenerating(null)}
        title={`New key for ${regenerating?.name ?? "agent"}?`}
        description="The current key stops working immediately. The agent must be reconfigured with the new key."
        confirmLabel="Generate new key"
        busy={regenerate.isPending}
        onConfirm={() => regenerating && regenerate.mutate(regenerating)}
      />

      <ConfirmDialog
        open={deleting != null}
        onOpenChange={(o) => !o && setDeleting(null)}
        title={`Delete agent ${deleting?.name ?? ""}?`}
        description="Only possible when no server uses it any more. Uninstall the service on the machine afterwards."
        confirmLabel="Delete agent"
        destructive
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting)}
      />
    </Card>
  );
}

function InstallInstructions({ created, onDone }: { created: AgentCreatedDto; onDone: () => void }) {
  const portal = window.location.origin;
  const command = `.\\install-agent.ps1 -PortalUrl "${portal}" -AgentId "${created.agent.id}" -AgentKey "${created.key}"`;

  const copy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      toast.success("Copied.");
    } catch {
      toast.error("Copy failed - select the text and copy it manually.");
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <DialogHeader>
        <DialogTitle>Install agent "{created.agent.name}"</DialogTitle>
        <DialogDescription>The key is shown only now. Copy it before closing this window.</DialogDescription>
      </DialogHeader>

      <div>
        <p className="mb-1 text-xs font-medium text-muted-foreground">Agent key</p>
        <div className="flex gap-2">
          <code className="flex-1 break-all rounded-md border bg-muted/50 p-2 text-xs">{created.key}</code>
          <Button variant="outline" size="icon" onClick={() => void copy(created.key)} aria-label="Copy key">
            <Copy />
          </Button>
        </div>
      </div>

      <ol className="list-decimal space-y-2 pl-5 text-sm">
        <li>
          Pick a Windows machine in the network of the SQL Servers (it needs to reach them on their SQL port, and reach this portal over
          HTTPS).
        </li>
        <li>
          Download the <b>intelligencedb-agent-win-x64</b> package (GitHub → Actions → latest successful run → Artifacts) and unzip it, e.g.
          to <code>C:\IntelligenceDB.Agent</code>.
        </li>
        <li>Open PowerShell as Administrator in that folder and run:</li>
      </ol>
      <div className="flex gap-2">
        <pre className="flex-1 overflow-x-auto rounded-md border bg-muted/50 p-2 text-xs">{command}</pre>
        <Button variant="outline" size="icon" onClick={() => void copy(command)} aria-label="Copy command">
          <Copy />
        </Button>
      </div>
      <p className="text-sm text-muted-foreground">
        The script installs the "IntelligenceDB Agent" Windows service and starts it. Within a minute the agent shows as Online here.
        Then add servers with "Connect via: {created.agent.name}".
      </p>
      <DialogFooter>
        <Button onClick={onDone}>I copied the key</Button>
      </DialogFooter>
    </div>
  );
}
