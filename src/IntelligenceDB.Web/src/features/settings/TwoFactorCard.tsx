import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ShieldCheck } from "lucide-react";
import { QRCodeSVG } from "qrcode.react";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Badge, Card, CardContent, CardDescription, CardHeader, CardTitle, Field, Input } from "@/components/ui/primitives";
import { api, errorMessage } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { TwoFactorSetupDto, TwoFactorStatusDto } from "@/lib/types";

/** Authenticator-app (TOTP) two-factor authentication. */
export function TwoFactorCard() {
  const queryClient = useQueryClient();
  const { reloadUser } = useAuth();
  const [setup, setSetup] = useState<TwoFactorSetupDto | null>(null);
  const [code, setCode] = useState("");
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);

  const { data: status } = useQuery({
    queryKey: ["2fa"],
    queryFn: () => api<TwoFactorStatusDto>("/auth/2fa"),
  });

  const done = () => {
    setCode("");
    void queryClient.invalidateQueries({ queryKey: ["2fa"] });
    void reloadUser();
  };

  const start = useMutation({
    mutationFn: () => api<TwoFactorSetupDto>("/auth/2fa/setup", { method: "POST" }),
    onSuccess: setSetup,
    onError: (e) => toast.error(errorMessage(e)),
  });

  const enable = useMutation({
    mutationFn: () => api<{ codes: string[] }>("/auth/2fa/enable", { method: "POST", body: { code } }),
    onSuccess: (r) => {
      setSetup(null);
      setRecoveryCodes(r.codes);
      toast.success("Two-factor authentication is on.");
      done();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const disable = useMutation({
    mutationFn: () => api<void>("/auth/2fa/disable", { method: "POST", body: { code } }),
    onSuccess: () => {
      toast.success("Two-factor authentication is off.");
      done();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <ShieldCheck className="size-4" /> Two-factor authentication
          {status && <Badge variant={status.enabled ? "success" : "secondary"}>{status.enabled ? "On" : "Off"}</Badge>}
        </CardTitle>
        <CardDescription>Ask for a code from an authenticator app (Microsoft Authenticator, Google Authenticator, 1Password...) at sign-in.</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {recoveryCodes && (
          <div className="rounded-lg border border-warning/60 bg-warning/10 p-3 text-sm">
            <p className="font-medium">Save these recovery codes now. Each works once if you lose your phone. They are not shown again.</p>
            <pre className="mt-2 grid grid-cols-2 gap-1 font-mono text-xs">{recoveryCodes.join("\n")}</pre>
            <Button variant="outline" size="sm" className="mt-2" onClick={() => setRecoveryCodes(null)}>
              I saved them
            </Button>
          </div>
        )}

        {status?.enabled ? (
          <>
            <p className="text-sm text-muted-foreground">{status.recoveryCodesLeft} recovery code(s) left.</p>
            <div className="flex items-end gap-2">
              <Field label="Code from your app" htmlFor="2fa-disable" className="flex-1">
                <Input id="2fa-disable" inputMode="numeric" value={code} onChange={(e) => setCode(e.target.value)} />
              </Field>
              <Button variant="destructive" onClick={() => disable.mutate()} disabled={!code || disable.isPending}>
                Turn off
              </Button>
            </div>
          </>
        ) : setup ? (
          <>
            <ol className="list-decimal space-y-1 pl-5 text-sm">
              <li>Scan this QR code with your authenticator app (or type the key).</li>
              <li>Enter the 6-digit code the app shows.</li>
            </ol>
            <div className="flex flex-wrap items-center gap-4">
              <div className="rounded-lg bg-white p-2">
                <QRCodeSVG value={setup.authenticatorUri} size={148} />
              </div>
              <code className="rounded bg-muted px-2 py-1 text-xs">{setup.sharedKey}</code>
            </div>
            <div className="flex items-end gap-2">
              <Field label="6-digit code" htmlFor="2fa-enable" className="flex-1">
                <Input id="2fa-enable" inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} />
              </Field>
              <Button onClick={() => enable.mutate()} disabled={!code || enable.isPending}>
                Turn on
              </Button>
            </div>
          </>
        ) : (
          <Button className="self-start" onClick={() => start.mutate()} disabled={start.isPending}>
            Set up two-factor authentication
          </Button>
        )}
      </CardContent>
    </Card>
  );
}
