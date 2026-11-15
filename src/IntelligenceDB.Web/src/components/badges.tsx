import { CheckCircle2, CircleDashed, KeyRound, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/primitives";
import { InfoTip } from "@/components/ui/tooltip";
import { platformLabels, platformShort, statusLabels } from "@/lib/format";
import type { CapabilityDto, ConnectionStatus, PlatformType, ServerEnvironment } from "@/lib/types";
import { cn } from "@/lib/utils";

export function StatusBadge({ status, enabled = true }: { status: ConnectionStatus; enabled?: boolean }) {
  if (!enabled) return <Badge variant="secondary">Disabled</Badge>;
  switch (status) {
    case "Online":
      return (
        <Badge variant="success">
          <CheckCircle2 className="size-3" /> {statusLabels[status]}
        </Badge>
      );
    case "Unreachable":
      return (
        <Badge variant="destructive">
          <XCircle className="size-3" /> {statusLabels[status]}
        </Badge>
      );
    case "AuthFailed":
      return (
        <Badge variant="destructive">
          <KeyRound className="size-3" /> {statusLabels[status]}
        </Badge>
      );
    default:
      return (
        <Badge variant="secondary">
          <CircleDashed className="size-3" /> {statusLabels[status]}
        </Badge>
      );
  }
}

export function StatusDot({ status, enabled = true }: { status: ConnectionStatus; enabled?: boolean }) {
  const color = !enabled
    ? "bg-muted-foreground/40"
    : status === "Online"
      ? "bg-success"
      : status === "Unknown"
        ? "bg-muted-foreground/60"
        : "bg-destructive";
  return <span className={cn("inline-block size-2.5 shrink-0 rounded-full", color)} aria-hidden />;
}

export function PlatformBadge({ platform }: { platform: PlatformType }) {
  return (
    <Badge variant="outline" title={platformLabels[platform]}>
      {platformShort[platform]}
    </Badge>
  );
}

export function EnvironmentBadge({ environment }: { environment: ServerEnvironment }) {
  return (
    <Badge variant={environment === "Production" ? "warning" : "secondary"}>
      {environment === "Production" ? "PROD" : environment}
    </Badge>
  );
}

/** One capability row: supported tick, or cross with reason and what to do instead. */
export function CapabilityRow({ cap }: { cap: CapabilityDto }) {
  return (
    <li className="flex gap-3 py-2.5">
      {cap.isSupported ? (
        <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-success" aria-label="Supported" />
      ) : (
        <XCircle className="mt-0.5 size-4 shrink-0 text-destructive" aria-label="Not supported" />
      )}
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1.5 text-sm font-medium">
          {cap.title}
          {cap.description && <InfoTip>{cap.description}</InfoTip>}
        </div>
        {cap.reason && <p className="text-xs text-muted-foreground">{cap.reason}</p>}
        {!cap.isSupported && cap.alternative && (
          <p className="mt-0.5 text-xs">
            <span className="font-medium">What to do: </span>
            {cap.alternative}
          </p>
        )}
      </div>
    </li>
  );
}
