import { CheckCircle2 } from "lucide-react";
import { Link } from "react-router";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/primitives";
import type { ModuleInfo } from "@/components/layout/modules";

/**
 * Shown for sidebar modules that are built in a later phase. It states honestly what is
 * not available yet instead of showing fake data.
 */
export function ComingInPhasePage({ module: m }: { module: ModuleInfo }) {
  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-5 pt-6">
      <Card>
        <CardHeader>
          <div className="mb-1 flex size-10 items-center justify-center rounded-lg bg-primary/10">
            <m.icon className="size-5 text-primary" />
          </div>
          <CardTitle className="text-xl">{m.label}</CardTitle>
          <CardDescription>
            {m.summary} This module is built in <b>Phase {m.phase}</b> of the plan.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <p className="mb-2 text-sm font-medium">What it will do</p>
          <ul className="flex flex-col gap-2">
            {m.features.map((f) => (
              <li key={f} className="flex gap-2 text-sm text-muted-foreground">
                <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-muted-foreground/60" />
                {f}
              </li>
            ))}
          </ul>
          <p className="mt-4 text-sm">
            Available now: <Link className="text-primary hover:underline" to="/servers">add servers</Link> and check their
            capabilities - every module uses them to decide what works on each platform.
          </p>
        </CardContent>
      </Card>
    </div>
  );
}
