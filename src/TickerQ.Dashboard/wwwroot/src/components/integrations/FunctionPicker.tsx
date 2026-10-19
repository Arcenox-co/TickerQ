import { useState } from "react";
import { ChevronsUpDown } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Command,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { useAllFunctions } from "@/services/hooks";
import { cn } from "@/lib/utils";

export function FunctionPicker({
  value,
  onChange,
}: {
  value: string;
  onChange: (value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const functions = useAllFunctions();
  const names = [
    ...new Set((functions.data ?? []).map((f) => f.functionName)),
  ].sort((a, b) => a.localeCompare(b));
  const query = search.trim();
  const matching = names.filter((name) =>
    name.toLowerCase().includes(query.toLowerCase()),
  );
  const canUseName =
    query.length > 0 &&
    !names.some((name) => name.toLowerCase() === query.toLowerCase());

  function select(name: string) {
    onChange(name);
    setOpen(false);
  }

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (next) setSearch("");
      }}
    >
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          role="combobox"
          aria-label="Function"
          aria-expanded={open}
          className="w-full min-w-0 justify-between px-3 text-xs font-normal"
          title={value || undefined}
        >
          <span className={cn("truncate", !value && "text-muted-foreground")}>
            {value || "Select a function"}
          </span>
          <ChevronsUpDown className="size-3.5 text-muted-foreground" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="start"
        side="bottom"
        sideOffset={4}
        collisionPadding={12}
        className="w-(--radix-popover-trigger-width) p-0"
        onEscapeKeyDown={(event) => event.stopPropagation()}
      >
        <Command shouldFilter={false} defaultValue={value} loop>
          <CommandInput
            aria-label="Search functions"
            placeholder="Search functions…"
            value={search}
            onValueChange={setSearch}
            maxLength={256}
            className="text-xs"
          />
          <CommandList className="max-h-[min(16rem,calc(var(--radix-popover-content-available-height)-3rem))]">
            {functions.isPending && (
              <p role="status" className="p-3 text-xs text-muted-foreground">
                Loading functions…
              </p>
            )}
            {functions.isError && (
              <div
                role="status"
                className="space-y-2 p-3 text-xs text-muted-foreground"
              >
                <p>Could not load functions. Retry or enter the exact name.</p>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => void functions.refetch()}
                >
                  Retry
                </Button>
              </div>
            )}
            {matching.length > 0 && (
              <CommandGroup heading="Registered functions">
                {matching.map((name) => (
                  <CommandItem
                    key={name}
                    value={name}
                    data-checked={name === value}
                    onSelect={() => select(name)}
                    className="cursor-pointer text-xs"
                  >
                    <span className="min-w-0 break-all">{name}</span>
                  </CommandItem>
                ))}
              </CommandGroup>
            )}
            {!functions.isPending &&
              !functions.isError &&
              matching.length === 0 && (
                <p className="p-3 text-xs text-muted-foreground">
                  {query
                    ? "No registered functions match."
                    : "No registered functions. Type an exact function name to use it."}
                </p>
              )}
            {canUseName && (
              <CommandGroup heading="Exact function name">
                <CommandItem
                  value={query}
                  onSelect={() => select(query)}
                  className="cursor-pointer text-xs"
                >
                  <span className="min-w-0 break-all">Use “{query}”</span>
                </CommandItem>
              </CommandGroup>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
