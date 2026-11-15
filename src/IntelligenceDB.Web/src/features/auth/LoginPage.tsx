import { Database, Loader2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { Navigate, useLocation, useNavigate } from "react-router";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle, Field, Input } from "@/components/ui/primitives";
import { errorMessage } from "@/lib/api";
import { useAuth } from "@/lib/auth";

export function LoginPage() {
  const { status, login, verifyTwoFactor } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? "/";

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [twoFactorToken, setTwoFactorToken] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [useRecovery, setUseRecovery] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (status === "authenticated") return <Navigate to={from} replace />;

  const submitPassword = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const token = await login(email, password);
      if (token) setTwoFactorToken(token);
      else navigate(from, { replace: true });
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  const submitCode = async (e: FormEvent) => {
    e.preventDefault();
    if (!twoFactorToken) return;
    setError(null);
    setBusy(true);
    try {
      await verifyTwoFactor(twoFactorToken, code, useRecovery);
      navigate(from, { replace: true });
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex min-h-full items-center justify-center bg-muted/30 p-4">
      <Card className="w-full max-w-sm">
        <CardHeader className="items-center text-center">
          <div className="mb-2 flex size-11 items-center justify-center rounded-xl bg-primary/10">
            <Database className="size-6 text-primary" />
          </div>
          <CardTitle className="text-xl">IntelligenceDB</CardTitle>
          <CardDescription>
            {twoFactorToken ? "Enter the code from your authenticator app." : "Sign in to monitor your SQL Servers."}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {!twoFactorToken ? (
            <form className="flex flex-col gap-4" onSubmit={submitPassword}>
              <Field label="Email" htmlFor="email">
                <Input id="email" type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} />
              </Field>
              <Field label="Password" htmlFor="password">
                <Input
                  id="password"
                  type="password"
                  autoComplete="current-password"
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </Field>
              {error && <p className="text-sm text-destructive">{error}</p>}
              <Button type="submit" disabled={busy}>
                {busy && <Loader2 className="animate-spin" />} Sign in
              </Button>
            </form>
          ) : (
            <form className="flex flex-col gap-4" onSubmit={submitCode}>
              <Field label={useRecovery ? "Recovery code" : "6-digit code"} htmlFor="code">
                <Input
                  id="code"
                  autoComplete="one-time-code"
                  inputMode={useRecovery ? "text" : "numeric"}
                  autoFocus
                  required
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </Field>
              {error && <p className="text-sm text-destructive">{error}</p>}
              <Button type="submit" disabled={busy}>
                {busy && <Loader2 className="animate-spin" />} Verify
              </Button>
              <div className="flex justify-between text-xs">
                <button type="button" className="text-primary hover:underline cursor-pointer" onClick={() => setUseRecovery((v) => !v)}>
                  {useRecovery ? "Use authenticator code" : "Use a recovery code"}
                </button>
                <button
                  type="button"
                  className="text-muted-foreground hover:underline cursor-pointer"
                  onClick={() => {
                    setTwoFactorToken(null);
                    setCode("");
                    setError(null);
                  }}
                >
                  Back
                </button>
              </div>
            </form>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
