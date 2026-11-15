import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Loader2, Pencil, Plus, Trash2 } from "lucide-react";
import { useMemo, useState, type FormEvent } from "react";
import { toast } from "sonner";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { DataTable } from "@/components/DataTable";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Badge, Card, CardContent, CardDescription, CardHeader, CardTitle, Checkbox, Field, Input, Label, Select } from "@/components/ui/primitives";
import { InfoTip } from "@/components/ui/tooltip";
import { api, errorMessage } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatRelative } from "@/lib/format";
import type { Role, ServerGroupDto, UserAdminDto } from "@/lib/types";

const roleHelp: Record<Role, string> = {
  Admin: "Everything, including users and settings.",
  DBA: "Monitoring, server management and approved actions.",
  Operator: "Monitoring and SQL Agent job control only.",
  Viewer: "Read-only.",
};

export function UsersCard() {
  const queryClient = useQueryClient();
  const { user: me } = useAuth();
  const [editing, setEditing] = useState<UserAdminDto | "new" | null>(null);
  const [deleting, setDeleting] = useState<UserAdminDto | null>(null);

  const { data: users = [] } = useQuery({ queryKey: ["users"], queryFn: () => api<UserAdminDto[]>("/users") });
  const { data: groups = [] } = useQuery({ queryKey: ["server-groups"], queryFn: () => api<ServerGroupDto[]>("/server-groups") });

  const remove = useMutation({
    mutationFn: (id: string) => api<void>(`/users/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      setDeleting(null);
      toast.success("User deleted.");
      void queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const columns = useMemo<ColumnDef<UserAdminDto, unknown>[]>(
    () => [
      {
        accessorKey: "displayName",
        header: "Name",
        cell: ({ row }) => (
          <div>
            <div className="font-medium">{row.original.displayName}</div>
            <div className="text-xs text-muted-foreground">{row.original.email}</div>
          </div>
        ),
      },
      { accessorKey: "role", header: "Role", cell: ({ row }) => <Badge variant="outline">{row.original.role}</Badge> },
      {
        id: "scope",
        accessorFn: (u) => (u.allServerGroups || u.role === "Admin" ? "All groups" : `${u.groupIds.length} group(s)`),
        header: "Server access",
      },
      {
        id: "flags",
        header: "Status",
        accessorFn: (u) => `${u.twoFactorEnabled ? "2FA" : ""} ${u.isLockedOut ? "Locked" : ""}`,
        cell: ({ row }) => (
          <div className="flex gap-1">
            {row.original.twoFactorEnabled && <Badge variant="success">2FA</Badge>}
            {row.original.isLockedOut && <Badge variant="destructive">Locked</Badge>}
          </div>
        ),
      },
      {
        id: "lastLogin",
        accessorFn: (u) => u.lastLoginUtc ?? "",
        header: "Last sign-in",
        cell: ({ row }) => <span className="text-muted-foreground">{formatRelative(row.original.lastLoginUtc)}</span>,
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => (
          <div className="flex justify-end gap-1">
            <Button variant="ghost" size="icon" className="size-8" onClick={() => setEditing(row.original)} aria-label="Edit user">
              <Pencil />
            </Button>
            {row.original.id !== me?.id && (
              <Button variant="ghost" size="icon" className="size-8" onClick={() => setDeleting(row.original)} aria-label="Delete user">
                <Trash2 className="text-destructive" />
              </Button>
            )}
          </div>
        ),
      },
    ],
    [me?.id],
  );

  return (
    <Card>
      <CardHeader className="flex-row items-start">
        <div className="flex-1">
          <CardTitle>Users</CardTitle>
          <CardDescription>Portal users, their role and which server groups they can see.</CardDescription>
        </div>
        <Button onClick={() => setEditing("new")}>
          <Plus /> Add user
        </Button>
      </CardHeader>
      <CardContent>
        <DataTable
          data={users}
          columns={columns}
          searchPlaceholder="Filter users..."
          initialPageSize={10}
          csv={{
            fileName: "intelligencedb-users",
            columns: [
              { header: "Name", value: (u) => u.displayName },
              { header: "Email", value: (u) => u.email },
              { header: "Role", value: (u) => u.role },
              { header: "All groups", value: (u) => u.allServerGroups },
              { header: "2FA", value: (u) => u.twoFactorEnabled },
              { header: "Last sign-in (UTC)", value: (u) => u.lastLoginUtc },
            ],
          }}
        />
      </CardContent>

      <UserDialog
        key={editing === "new" ? "new" : (editing?.id ?? "closed")}
        open={editing != null}
        user={editing === "new" ? null : editing}
        groups={groups}
        onOpenChange={(o) => !o && setEditing(null)}
      />

      <ConfirmDialog
        open={deleting != null}
        onOpenChange={(o) => !o && setDeleting(null)}
        title={`Delete ${deleting?.displayName ?? "user"}?`}
        description="The user can no longer sign in. Their entries in the audit log are kept."
        confirmLabel="Delete user"
        destructive
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
      />
    </Card>
  );
}

function UserDialog({
  open,
  user,
  groups,
  onOpenChange,
}: {
  open: boolean;
  user: UserAdminDto | null;
  groups: ServerGroupDto[];
  onOpenChange: (o: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState(user?.email ?? "");
  const [displayName, setDisplayName] = useState(user?.displayName ?? "");
  const [role, setRole] = useState<Role>((user?.role as Role) || "Viewer");
  const [allGroups, setAllGroups] = useState(user?.allServerGroups ?? true);
  const [groupIds, setGroupIds] = useState<number[]>(user?.groupIds ?? []);
  const [password, setPassword] = useState("");
  const [unlock, setUnlock] = useState(false);

  const save = useMutation({
    mutationFn: () =>
      user
        ? api<void>(`/users/${user.id}`, {
            method: "PUT",
            body: { displayName, role, allServerGroups: allGroups, groupIds, unlock, newPassword: password || null },
          })
        : api<void>("/users", { method: "POST", body: { email, displayName, password, role, allServerGroups: allGroups, groupIds } }),
    onSuccess: () => {
      toast.success(user ? "User updated." : "User created.");
      void queryClient.invalidateQueries({ queryKey: ["users"] });
      onOpenChange(false);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  const toggleGroup = (id: number, on: boolean) => setGroupIds((g) => (on ? [...new Set([...g, id])] : g.filter((x) => x !== id)));

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={submit} className="flex flex-col gap-4">
          <DialogHeader>
            <DialogTitle>{user ? `Edit ${user.displayName}` : "Add user"}</DialogTitle>
            <DialogDescription>Each user has one role. Admins always see every server.</DialogDescription>
          </DialogHeader>

          {!user && (
            <Field label="Email" htmlFor="u-email">
              <Input id="u-email" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
            </Field>
          )}
          <Field label="Display name" htmlFor="u-name">
            <Input id="u-name" required value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
          </Field>
          <Field label="Role" htmlFor="u-role" hint={roleHelp[role]}>
            <Select id="u-role" value={role} onChange={(e) => setRole(e.target.value as Role)}>
              {(Object.keys(roleHelp) as Role[]).map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </Select>
          </Field>

          {role !== "Admin" && (
            <div className="flex flex-col gap-2">
              <div className="flex items-center gap-2">
                <Checkbox id="u-all" checked={allGroups} onChange={(e) => setAllGroups(e.target.checked)} />
                <Label htmlFor="u-all" className="font-normal">
                  Can see all server groups
                </Label>
                <InfoTip>Untick to limit this user to specific groups. Servers without a group are then hidden from them.</InfoTip>
              </div>
              {!allGroups && (
                <div className="grid max-h-40 gap-1 overflow-y-auto rounded-md border p-2 sm:grid-cols-2">
                  {groups.length === 0 && <p className="text-xs text-muted-foreground">Create server groups first (Servers → Groups).</p>}
                  {groups.map((g) => (
                    <label key={g.id} className="flex items-center gap-2 text-sm">
                      <Checkbox checked={groupIds.includes(g.id)} onChange={(e) => toggleGroup(g.id, e.target.checked)} />
                      {g.name}
                    </label>
                  ))}
                </div>
              )}
            </div>
          )}

          <Field
            label={user ? "New password (optional)" : "Initial password"}
            htmlFor="u-pw"
            hint="At least 12 characters with upper and lower case, a digit and a symbol."
          >
            <Input id="u-pw" type="password" autoComplete="new-password" required={!user} value={password} onChange={(e) => setPassword(e.target.value)} />
          </Field>

          {user?.isLockedOut && (
            <div className="flex items-center gap-2">
              <Checkbox id="u-unlock" checked={unlock} onChange={(e) => setUnlock(e.target.checked)} />
              <Label htmlFor="u-unlock" className="font-normal">
                Unlock this account
              </Label>
            </div>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={save.isPending}>
              {save.isPending && <Loader2 className="animate-spin" />} {user ? "Save" : "Create user"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
