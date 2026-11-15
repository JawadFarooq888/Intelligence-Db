import { AlertOctagon, AlertTriangle, CheckCircle2, Info } from "lucide-react";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import type { AdviceItem, AdviceSeverity, HealthDeduction } from "@/lib/types";
import { cn } from "@/lib/utils";

export function scoreTone(score: number | null | undefined) {
  if (score == null) return "text-muted-foreground border-border";
  if (score >= 80) return "text-success border-success/40 bg-success/10";
  if (score >= 50) return "text-warning border-warning/50 bg-warning/10";
  return "text-destructive border-destructive/40 bg-destructive/10";
}

/** Round 0-100 health score with the reasons for every lost point in a tooltip. */
export function HealthBadge({
  score,
  deductions,
  size = "md",
}: {
  score: number | null | undefined;
  deductions?: HealthDeduction[];
  size?: "sm" | "md" | "lg";
}) {
  const dims = size === "lg" ? "size-16 text-2xl" : size === "sm" ? "size-8 text-xs" : "size-11 text-base";
  const badge = (
    <div className={cn("flex shrink-0 items-center justify-center rounded-full border-2 font-semibold tabular-nums", dims, scoreTone(score))}>
      {score ?? "–"}
    </div>
  );

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className="cursor-help" aria-label={`Health score ${score ?? "not calculated"}`}>
          {badge}
        </span>
      </TooltipTrigger>
      <TooltipContent className="max-w-sm">
        <p className="mb-1 font-medium">Health score {score ?? "not calculated yet"}</p>
        {score == null ? (
          <p>Calculated after the collector's first run.</p>
        ) : deductions && deductions.length > 0 ? (
          <ul className="space-y-1">
            {deductions.map((d) => (
              <li key={d.rule}>
                <span className="font-medium">-{d.points}</span> {d.message}
              </li>
            ))}
          </ul>
        ) : (
          <p>Starts at 100 and loses points for problems (CPU, memory, backups, disk, CHECKDB, ...). Nothing found.</p>
        )}
      </TooltipContent>
    </Tooltip>
  );
}

const severityStyle: Record<AdviceSeverity, { icon: typeof Info; cls: string; label: string }> = {
  Critical: { icon: AlertOctagon, cls: "text-destructive", label: "Critical" },
  Warning: { icon: AlertTriangle, cls: "text-warning", label: "Warning" },
  Info: { icon: Info, cls: "text-primary", label: "Info" },
};

/** Best-practice findings with explanation and current → recommended values. */
export function AdviceList({ items, empty = "No problems found.", showTarget = false }: { items: AdviceItem[]; empty?: string; showTarget?: boolean }) {
  if (items.length === 0) {
    return (
      <p className="flex items-center gap-2 text-sm text-success">
        <CheckCircle2 className="size-4" /> {empty}
      </p>
    );
  }

  return (
    <ul className="divide-y">
      {items.map((a, i) => {
        const s = severityStyle[a.severity];
        return (
          <li key={`${a.code}-${a.target ?? ""}-${i}`} className="flex gap-3 py-2.5">
            <s.icon className={cn("mt-0.5 size-4 shrink-0", s.cls)} aria-label={s.label} />
            <div className="min-w-0 flex-1">
              <p className="text-sm font-medium">
                {a.title}
                {showTarget && a.target && <span className="ml-2 font-normal text-muted-foreground">({a.target})</span>}
              </p>
              <p className="text-xs text-muted-foreground">{a.explanation}</p>
              {(a.current || a.recommended) && (
                <p className="mt-0.5 text-xs">
                  {a.current && (
                    <>
                      Now: <span className="font-mono">{a.current}</span>
                    </>
                  )}
                  {a.current && a.recommended && " · "}
                  {a.recommended && (
                    <>
                      Recommended: <span className="font-mono">{a.recommended}</span>
                    </>
                  )}
                </p>
              )}
            </div>
          </li>
        );
      })}
    </ul>
  );
}
