import type { AuthSession } from "./types";

/** Error from the API, parsed from an RFC 7807 problem response. */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail?: string;
  readonly errors: Record<string, string[]>;

  constructor(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
    super(detail && detail !== title ? `${title} ${detail}` : title);
    this.status = status;
    this.title = title;
    this.detail = detail;
    this.errors = errors ?? {};
  }

  /** First error message for a field (keys are camelCase, e.g. "monitoring.secret"). */
  field(name: string): string | undefined {
    const direct = this.errors[name];
    if (direct?.length) return direct[0];
    // ASP.NET model-binding errors use PascalCase / "$.field" keys.
    const key = Object.keys(this.errors).find((k) => k.replace(/^\$\./, "").toLowerCase() === name.toLowerCase());
    return key ? this.errors[key]?.[0] : undefined;
  }
}

// The access token lives only in memory (never localStorage). The refresh token is an HttpOnly cookie.
let accessToken: string | null = null;
let sessionExpiredHandler: (() => void) | null = null;
let refreshInFlight: Promise<AuthSession | null> | null = null;

const CSRF_HEADER = { "X-Requested-With": "IntelligenceDB" };

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function onSessionExpired(handler: () => void) {
  sessionExpiredHandler = handler;
}

/** Uses the refresh cookie to get a new access token. Concurrent callers share one request. */
export function refreshSession(): Promise<AuthSession | null> {
  if (!refreshInFlight) {
    refreshInFlight = (async () => {
      try {
        const res = await fetch("/api/auth/refresh", {
          method: "POST",
          headers: CSRF_HEADER,
          credentials: "same-origin",
        });
        if (!res.ok) {
          accessToken = null;
          return null;
        }
        const session = (await res.json()) as AuthSession;
        accessToken = session.accessToken;
        return session;
      } catch {
        return null;
      }
    })().finally(() => {
      refreshInFlight = null;
    });
  }
  return refreshInFlight;
}

interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: unknown;
  signal?: AbortSignal;
}

async function send(path: string, options: RequestOptions): Promise<Response> {
  const headers: Record<string, string> = { ...CSRF_HEADER, Accept: "application/json, text/plain" };
  if (options.body !== undefined) headers["Content-Type"] = "application/json";
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;

  return fetch(`/api${path}`, {
    method: options.method ?? "GET",
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    credentials: "same-origin",
    signal: options.signal,
  });
}

async function toError(res: Response): Promise<ApiError> {
  try {
    const problem = (await res.json()) as {
      title?: string;
      detail?: string;
      errors?: Record<string, string[]>;
    };
    return new ApiError(res.status, problem.title ?? res.statusText, problem.detail, problem.errors);
  } catch {
    return new ApiError(res.status, res.status === 429 ? "Too many requests. Wait a moment and try again." : res.statusText || "Request failed");
  }
}

/** Calls the API. On 401 it refreshes the session once and retries. */
export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let res = await send(path, options);

  if (res.status === 401 && !path.startsWith("/auth/login")) {
    const session = await refreshSession();
    if (session) {
      res = await send(path, options);
    } else {
      sessionExpiredHandler?.();
    }
  }

  if (!res.ok) throw await toError(res);
  if (res.status === 204) return undefined as T;

  const type = res.headers.get("content-type") ?? "";
  return (type.includes("json") ? await res.json() : await res.text()) as T;
}

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof Error) return error.message;
  return "Something went wrong.";
}
