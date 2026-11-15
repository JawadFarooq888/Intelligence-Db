import { useQuery } from "@tanstack/react-query";
import { FolderTree, Search, Server } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { Input } from "@/components/ui/primitives";
import { api } from "@/lib/api";
import type { SearchHitDto } from "@/lib/types";
import { cn } from "@/lib/utils";

/** Search box in the top bar (Ctrl+K). Finds servers and groups; databases/jobs/indexes come in later phases. */
export function GlobalSearch() {
  const [text, setText] = useState("");
  const [term, setTerm] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const navigate = useNavigate();

  // Debounce typing so we do not call the API on every keystroke.
  useEffect(() => {
    const t = setTimeout(() => setTerm(text.trim()), 250);
    return () => clearTimeout(t);
  }, [text]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        inputRef.current?.focus();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const { data: hits = [], isFetching } = useQuery({
    queryKey: ["search", term],
    queryFn: ({ signal }) => api<SearchHitDto[]>(`/search?q=${encodeURIComponent(term)}`, { signal }),
    enabled: term.length >= 2,
    staleTime: 10_000,
  });

  const go = (hit: SearchHitDto) => {
    setOpen(false);
    setText("");
    navigate(hit.url);
  };

  return (
    <div className="relative w-full max-w-md">
      <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
      <Input
        ref={inputRef}
        value={text}
        placeholder="Search servers, groups...  (Ctrl+K)"
        className="pl-8"
        aria-label="Global search"
        onChange={(e) => {
          setText(e.target.value);
          setOpen(true);
          setActive(0);
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        onKeyDown={(e) => {
          if (e.key === "ArrowDown") {
            e.preventDefault();
            setActive((a) => Math.min(a + 1, hits.length - 1));
          } else if (e.key === "ArrowUp") {
            e.preventDefault();
            setActive((a) => Math.max(a - 1, 0));
          } else if (e.key === "Enter" && hits[active]) {
            go(hits[active]);
          } else if (e.key === "Escape") {
            setOpen(false);
            inputRef.current?.blur();
          }
        }}
      />
      {open && term.length >= 2 && (
        <div className="absolute left-0 right-0 top-full z-40 mt-1 overflow-hidden rounded-lg border bg-popover shadow-lg">
          {hits.length === 0 ? (
            <p className="px-3 py-3 text-sm text-muted-foreground">{isFetching ? "Searching..." : "No matches."}</p>
          ) : (
            <ul className="max-h-80 overflow-y-auto py-1">
              {hits.map((hit, i) => (
                <li key={`${hit.kind}-${hit.id}`}>
                  <button
                    type="button"
                    className={cn(
                      "flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-accent cursor-pointer",
                      i === active && "bg-accent",
                    )}
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => go(hit)}
                  >
                    {hit.kind === "server" ? (
                      <Server className="size-4 text-muted-foreground" />
                    ) : (
                      <FolderTree className="size-4 text-muted-foreground" />
                    )}
                    <span className="font-medium">{hit.title}</span>
                    {hit.subtitle && <span className="truncate text-xs text-muted-foreground">{hit.subtitle}</span>}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
