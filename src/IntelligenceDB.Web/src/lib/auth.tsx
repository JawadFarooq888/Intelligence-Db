import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, onSessionExpired, refreshSession, setAccessToken } from "./api";
import type { AuthSession, LoginResponse, Role, UserDto } from "./types";

type Status = "loading" | "authenticated" | "anonymous";

interface AuthContextValue {
  user: UserDto | null;
  status: Status;
  /** Returns a two-factor token when a second step is needed, otherwise null. */
  login: (email: string, password: string) => Promise<string | null>;
  verifyTwoFactor: (token: string, code: string, isRecoveryCode: boolean) => Promise<void>;
  logout: () => Promise<void>;
  reloadUser: () => Promise<void>;
  hasRole: (...roles: Role[]) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null);
  const [status, setStatus] = useState<Status>("loading");
  const queryClient = useQueryClient();

  const applySession = useCallback((session: AuthSession | null) => {
    setAccessToken(session?.accessToken ?? null);
    setUser(session?.user ?? null);
    setStatus(session ? "authenticated" : "anonymous");
  }, []);

  // Restore the session from the refresh cookie on page load.
  useEffect(() => {
    onSessionExpired(() => {
      applySession(null);
      queryClient.clear();
    });
    void refreshSession().then(applySession);
  }, [applySession, queryClient]);

  const login = useCallback(
    async (email: string, password: string) => {
      const res = await api<LoginResponse>("/auth/login", { method: "POST", body: { email, password } });
      if (res.requiresTwoFactor) return res.twoFactorToken;
      applySession(res.session);
      return null;
    },
    [applySession],
  );

  const verifyTwoFactor = useCallback(
    async (twoFactorToken: string, code: string, isRecoveryCode: boolean) => {
      const res = await api<LoginResponse>("/auth/login/2fa", {
        method: "POST",
        body: { twoFactorToken, code, isRecoveryCode },
      });
      applySession(res.session);
    },
    [applySession],
  );

  const logout = useCallback(async () => {
    try {
      await api<void>("/auth/logout", { method: "POST" });
    } finally {
      applySession(null);
      queryClient.clear();
    }
  }, [applySession, queryClient]);

  const reloadUser = useCallback(async () => {
    setUser(await api<UserDto>("/auth/me"));
  }, []);

  const hasRole = useCallback((...roles: Role[]) => !!user && user.roles.some((r) => roles.includes(r)), [user]);

  const value = useMemo(
    () => ({ user, status, login, verifyTwoFactor, logout, reloadUser, hasRole }),
    [user, status, login, verifyTwoFactor, logout, reloadUser, hasRole],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
  return ctx;
}

/** Admin or DBA: may add / edit / delete servers. */
// eslint-disable-next-line react-refresh/only-export-components
export function useCanManageServers() {
  return useAuth().hasRole("Admin", "DBA");
}
