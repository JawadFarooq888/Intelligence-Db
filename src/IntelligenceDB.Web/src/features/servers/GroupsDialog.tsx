import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, Pencil, Plus, Trash2, X } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/primitives";
import { api, errorMessage } from "@/lib/api";
import type { ServerGroupDto } from "@/lib/types";

/** Create, rename and delete server groups. Deleting a group keeps its servers (they become ungrouped). */
export function GroupsDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (o: boolean) => void }) {
  const queryClient = useQueryClient();
  const [newName, setNewName] = useState("");
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editName, setEditName] = useState("");
  const [confirmDelete, setConfirmDelete] = useState<number | null>(null);

  const { data: groups = [] } = useQuery({
    queryKey: ["server-groups"],
    queryFn: () => api<ServerGroupDto[]>("/server-groups"),
    enabled: open,
  });

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ["server-groups"] });
    void queryClient.invalidateQueries({ queryKey: ["servers"] });
  };

  const create = useMutation({
    mutationFn: () => api<ServerGroupDto>("/server-groups", { method: "POST", body: { name: newName } }),
    onSuccess: () => {
      setNewName("");
      refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const rename = useMutation({
    mutationFn: (g: ServerGroupDto) =>
      api<ServerGroupDto>(`/server-groups/${g.id}`, { method: "PUT", body: { name: editName, description: g.description } }),
    onSuccess: () => {
      setEditingId(null);
      refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const remove = useMutation({
    mutationFn: (id: number) => api<void>(`/server-groups/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      setConfirmDelete(null);
      refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Server groups</DialogTitle>
          <DialogDescription>
            Groups organise servers and limit what users can see (Settings → Users). Deleting a group does not delete its servers.
          </DialogDescription>
        </DialogHeader>

        <form
          className="flex gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (newName.trim()) create.mutate();
          }}
        >
          <Input value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="New group name, e.g. Finance" aria-label="New group name" />
          <Button type="submit" disabled={!newName.trim() || create.isPending}>
            <Plus /> Add
          </Button>
        </form>

        <ul className="divide-y rounded-lg border">
          {groups.length === 0 && <li className="p-3 text-sm text-muted-foreground">No groups yet.</li>}
          {groups.map((g) => (
            <li key={g.id} className="flex items-center gap-2 p-2 pl-3">
              {editingId === g.id ? (
                <>
                  <Input value={editName} onChange={(e) => setEditName(e.target.value)} className="h-8" autoFocus aria-label="Group name" />
                  <Button size="icon" variant="ghost" className="size-8" onClick={() => rename.mutate(g)} aria-label="Save">
                    <Check />
                  </Button>
                  <Button size="icon" variant="ghost" className="size-8" onClick={() => setEditingId(null)} aria-label="Cancel">
                    <X />
                  </Button>
                </>
              ) : confirmDelete === g.id ? (
                <>
                  <span className="flex-1 text-sm">
                    Delete <b>{g.name}</b>? {g.serverCount} server(s) become ungrouped.
                  </span>
                  <Button size="sm" variant="destructive" onClick={() => remove.mutate(g.id)} disabled={remove.isPending}>
                    Delete
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => setConfirmDelete(null)}>
                    Cancel
                  </Button>
                </>
              ) : (
                <>
                  <span className="flex-1 text-sm font-medium">{g.name}</span>
                  <span className="text-xs text-muted-foreground">{g.serverCount} server(s)</span>
                  <Button
                    size="icon"
                    variant="ghost"
                    className="size-8"
                    onClick={() => {
                      setEditingId(g.id);
                      setEditName(g.name);
                    }}
                    aria-label={`Rename ${g.name}`}
                  >
                    <Pencil />
                  </Button>
                  <Button size="icon" variant="ghost" className="size-8" onClick={() => setConfirmDelete(g.id)} aria-label={`Delete ${g.name}`}>
                    <Trash2 className="text-destructive" />
                  </Button>
                </>
              )}
            </li>
          ))}
        </ul>
      </DialogContent>
    </Dialog>
  );
}
