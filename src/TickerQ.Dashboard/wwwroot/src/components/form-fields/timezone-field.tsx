import { useMemo, useState, type ReactNode } from "react";
import type { Control, FieldPath, FieldValues } from "react-hook-form";
import { ChevronsUpDown, Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import {
  Command,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command";
import {
  FormField,
  FormItem,
  FormMessage,
  useFormField,
} from "@/components/ui/form";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { TZ_DISPLAY_ALIASES, listIanaTimezones } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import { FieldInfo } from "./field-info";
import { FieldLabel } from "./field-label";

function supportedTimeZones(): string[] {
  const intl = Intl as typeof Intl & {
    supportedValuesOf?: (key: "timeZone") => string[];
  };
  let supported: string[] = [];
  try {
    supported = intl.supportedValuesOf?.("timeZone") ?? [];
  } catch {
    supported = [];
  }

  return [...new Set([
    "UTC",
    ...listIanaTimezones(),
    ...TZ_DISPLAY_ALIASES.map((alias) => alias.displayId),
    ...supported,
  ])].sort((left, right) => left.localeCompare(right));
}

const ALL_TIME_ZONES = supportedTimeZones();
const COMMON_TIME_ZONES = [...new Set([
  "UTC",
  ...listIanaTimezones(),
  ...TZ_DISPLAY_ALIASES.map((alias) => alias.displayId),
])];

export function TimezoneCombobox({
  value,
  onChange,
  disabled = false,
  ariaLabel = "Time zone",
  className,
  id,
  ariaDescribedBy,
  ariaInvalid,
}: {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  ariaLabel?: string;
  className?: string;
  id?: string;
  ariaDescribedBy?: string;
  ariaInvalid?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const query = search.trim();
  const matching = useMemo(() => {
    const candidates = query
      ? ALL_TIME_ZONES.filter((zone) => zone.toLowerCase().includes(query.toLowerCase()))
      : COMMON_TIME_ZONES;
    const withCurrent = value && !candidates.some((zone) => zone.toLowerCase() === value.toLowerCase())
      ? [value, ...candidates]
      : candidates;
    return withCurrent.slice(0, 120);
  }, [query, value]);
  const canUseCustom = query.length > 0 &&
    !ALL_TIME_ZONES.some((zone) => zone.toLowerCase() === query.toLowerCase());

  function select(zone: string) {
    onChange(zone);
    setOpen(false);
    setSearch("");
  }

  return (
    <Popover
      open={open}
      onOpenChange={(nextOpen) => {
        setOpen(nextOpen);
        if (nextOpen) setSearch("");
      }}
    >
      <PopoverTrigger asChild>
        <Button
          type="button"
          id={id}
          variant="outline"
          role="combobox"
          aria-label={ariaLabel}
          aria-describedby={ariaDescribedBy}
          aria-invalid={ariaInvalid || undefined}
          aria-expanded={open}
          disabled={disabled}
          className={cn(
            "h-9 w-full min-w-0 justify-between bg-surface-0 px-3 text-xs font-normal",
            className,
          )}
          title={value || undefined}
        >
          <span className={cn("truncate font-mono", !value && "font-sans text-muted-foreground")}>
            {value || "Select or enter a time zone"}
          </span>
          <ChevronsUpDown className="size-3.5 shrink-0 text-muted-foreground" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="start"
        side="bottom"
        sideOffset={4}
        collisionPadding={12}
        className="w-(--radix-popover-trigger-width) min-w-72 p-0"
        onEscapeKeyDown={(event) => event.stopPropagation()}
      >
        <Command shouldFilter={false} defaultValue={value} loop>
          <CommandInput
            aria-label="Search or enter a time zone"
            placeholder="Search or enter an exact time zone…"
            value={search}
            onValueChange={setSearch}
            maxLength={128}
            className="text-xs"
          />
          <CommandList className="max-h-[min(18rem,calc(var(--radix-popover-content-available-height)-3rem))]">
            {matching.length > 0 && (
              <CommandGroup heading={query ? "Matching time zones" : "Common time zones"}>
                {matching.map((zone) => (
                  <CommandItem
                    key={zone}
                    value={zone}
                    data-checked={zone === value}
                    onSelect={() => select(zone)}
                    className="cursor-pointer text-xs"
                  >
                    <span className="min-w-0 truncate font-mono">{zone}</span>
                  </CommandItem>
                ))}
              </CommandGroup>
            )}
            {canUseCustom && (
              <CommandGroup heading="Custom identifier">
                <CommandItem
                  value={`custom ${query}`}
                  onSelect={() => select(query)}
                  className="cursor-pointer text-xs"
                >
                  <Plus className="size-3.5 text-muted-foreground" />
                  <span className="min-w-0 break-all">Use “{query}”</span>
                </CommandItem>
              </CommandGroup>
            )}
            {matching.length === 0 && !canUseCustom && (
              <p className="p-3 text-xs text-muted-foreground">
                Enter an exact IANA or runtime-supported time-zone identifier.
              </p>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}

function TimezoneFormControl({
  value,
  onChange,
  disabled,
}: {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
}) {
  const { error, formItemId, formDescriptionId, formMessageId } = useFormField();
  return (
    <TimezoneCombobox
      id={formItemId}
      value={value}
      onChange={onChange}
      disabled={disabled}
      ariaDescribedBy={error
        ? `${formDescriptionId} ${formMessageId}`
        : formDescriptionId}
      ariaInvalid={!!error}
    />
  );
}

export function TimezoneField<T extends FieldValues>({
  control,
  name,
  label = "Time zone",
  info,
  required,
  disabled,
  labelClassName,
}: {
  control: Control<T>;
  name: FieldPath<T>;
  label?: string;
  info?: ReactNode;
  required?: boolean;
  disabled?: boolean;
  labelClassName?: string;
}) {
  return (
    <FormField
      control={control}
      name={name}
      render={({ field }) => (
        <FormItem>
          <FieldLabel
            label={label}
            required={required}
            className={labelClassName}
            labelEnd={info ? <FieldInfo>{info}</FieldInfo> : undefined}
          />
          <TimezoneFormControl
            value={field.value ? String(field.value) : ""}
            onChange={field.onChange}
            disabled={disabled ?? field.disabled}
          />
          <FormMessage />
        </FormItem>
      )}
    />
  );
}
