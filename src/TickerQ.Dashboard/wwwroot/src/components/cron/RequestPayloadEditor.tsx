import { useId, useMemo, useRef, useState, type ReactNode } from "react";
import { Braces, FileJson, ListTree, Pencil, Plus, ShieldCheck, Trash2 } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { cn } from "@/lib/utils";
import type {
  FunctionInfoDto,
  FunctionRequestContractDto,
  FunctionRequestExampleDto,
} from "@/services/api-types";
import {
  classifyExampleAction,
  emptyValueForNode,
  exceedsSchemaRenderDepth,
  initStructuredDraft,
  isRawPayloadJsonValid,
  parseStructuredInput,
  resolveRootObject,
  resolveSchemaNode,
  sameJsonValue,
  syncStructuredDraft,
  type ResolvedProperty,
} from "@/lib/request-schema";

interface JsonSchemaProperty {
  type?: string | string[];
  title?: string;
  description?: string;
  default?: unknown;
  enum?: unknown[];
}

interface ObjectJsonSchema {
  type?: string;
  properties?: Record<string, JsonSchemaProperty>;
  required?: string[];
}

// ── Compact trigger (lives in the parent form) ────────────────────────────────
//
// The payload field is intentionally small in the surrounding form: a header,
// a one-line summary of the current value, and a button that opens the dedicated
// editing Dialog. All the real editing — structured tree, raw JSON, examples —
// happens inside the modal so no screen has to duplicate it.
export function RequestPayloadEditor({
  functionInfo,
  value,
  onChange,
  onValidityChange,
  error,
}: {
  functionInfo?: FunctionInfoDto;
  value: string;
  onChange: (value: string) => void;
  onValidityChange?: (valid: boolean) => void;
  error?: string;
}) {
  const contract = functionInfo?.requestContract ?? null;
  const [open, setOpen] = useState(false);

  if (functionInfo && !contract && !functionInfo.requestType) {
    return (
      <div className="rounded-lg border border-border/50 bg-surface-0/30 p-3 text-[12px] text-muted-foreground">
        This function does not accept a request payload.
      </div>
    );
  }

  const summary = summarizePayload(value);

  return (
    <div className="space-y-1.5">
      <EditorHeader functionInfo={functionInfo} contract={contract} />
      <button
        type="button"
        onClick={() => setOpen(true)}
        aria-invalid={!!error}
        className={cn(
          "flex w-full items-center justify-between gap-3 rounded-lg border border-input bg-surface-0/30 px-2.5 py-2 text-left outline-none transition-colors hover:border-ring focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive",
        )}
      >
        <span
          className={cn(
            "min-w-0 flex-1 truncate font-mono text-[11px]",
            summary.empty ? "text-muted-foreground" : "text-foreground",
          )}
        >
          {summary.text}
        </span>
        <span className="flex shrink-0 items-center gap-1 text-[11px] font-medium text-primary">
          <Pencil className="h-3 w-3" /> Edit
        </span>
      </button>
      {error && <p className="text-[11px] text-destructive">{error}</p>}

      {open && (
        <PayloadDialog
          open={open}
          onOpenChange={setOpen}
          functionInfo={functionInfo}
          contract={contract}
          value={value}
          onChange={onChange}
          onValidityChange={onValidityChange}
        />
      )}
    </div>
  );
}

function EditorHeader({
  functionInfo,
  contract,
}: {
  functionInfo?: FunctionInfoDto;
  contract: FunctionRequestContractDto | null;
}) {
  return (
    <div className="flex items-start justify-between gap-3">
      <div>
        <div className="flex items-center gap-1.5 text-[11px] font-medium text-foreground">
          <FileJson className="h-3.5 w-3.5 text-primary" />
          Request payload
          {contract?.required && <span className="text-status-error">*</span>}
        </div>
        <p className="mt-0.5 text-[10px] text-muted-foreground">
          {contract?.typeName ?? functionInfo?.requestType ?? "Legacy untyped payload"}
        </p>
      </div>
      {contract && (
        <div className="flex items-center gap-1 text-[9px] text-muted-foreground" title={contract.fingerprint ?? undefined}>
          <ShieldCheck className="h-3 w-3" /> v{functionInfo?.contractVersion ?? 1}
        </div>
      )}
    </div>
  );
}

// ── The dedicated editing Dialog ──────────────────────────────────────────────
//
// One source of truth: the `value` JSON string owned by the parent form. The
// structured tree reads the parsed value and the raw editor reads the text, so
// switching modes never loses data — both write back to the same string. Raw
// JSON stays available as a fallback for whole payloads (and any nested subtree)
// that the structured tree cannot model.
function PayloadDialog({
  open,
  onOpenChange,
  functionInfo,
  contract,
  value,
  onChange,
  onValidityChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  functionInfo?: FunctionInfoDto;
  contract: FunctionRequestContractDto | null;
  value: string;
  onChange: (value: string) => void;
  onValidityChange?: (valid: boolean) => void;
}) {
  const structuredRoot = useMemo(() => resolveRootObject(contract?.schemaJson), [contract]);
  const canUseFields = !!contract && !!structuredRoot;
  const [rawMode, setRawMode] = useState(!canUseFields);
  const invalidPaths = useRef<Set<string>>(new Set());
  const errorId = useId();

  const reportValidity = (path: string, valid: boolean) => {
    if (valid) invalidPaths.current.delete(path);
    else invalidPaths.current.add(path);
    onValidityChange?.(invalidPaths.current.size === 0);
  };

  const applyStructuredValue = (nextPayload: Record<string, unknown>) => {
    onChange(JSON.stringify(nextPayload, null, 2));
  };

  const applyExternalValue = (next: string) => {
    invalidPaths.current.clear();
    onValidityChange?.(true);
    onChange(next);
  };

  const applyRawValue = (next: string) => {
    invalidPaths.current.clear();
    if (!isRawPayloadJsonValid(next)) invalidPaths.current.add("$raw");
    onValidityChange?.(invalidPaths.current.size === 0);
    onChange(next);
  };

  const switchToStructured = () => {
    if (!isRawPayloadJsonValid(value)) return;
    invalidPaths.current.delete("$raw");
    onValidityChange?.(invalidPaths.current.size === 0);
    setRawMode(false);
  };

  const switchToRaw = () => {
    invalidPaths.current.clear();
    onValidityChange?.(isRawPayloadJsonValid(value));
    setRawMode(true);
  };

  const payload = parseObject(value);
  const rawValid = isRawPayloadJsonValid(value);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-xl">
        <DialogHeader className="gap-1 border-b border-border/60 p-4">
          <DialogTitle className="flex items-center gap-2 text-sm">
            <FileJson className="h-4 w-4 text-primary" />
            Request payload
            {contract?.required && <span className="text-status-error">*</span>}
          </DialogTitle>
          <DialogDescription className="text-[11px]">
            {contract?.typeName ?? functionInfo?.requestType ?? "Legacy untyped payload"}
          </DialogDescription>
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-3 overflow-y-auto scrollbar-thin p-4">
          {canUseFields && (
            <div className="flex items-center gap-2">
              <div className="inline-flex h-8 items-center rounded-lg bg-muted p-[3px] text-[11px]">
                <ModeButton active={!rawMode} onClick={switchToStructured} disabled={rawMode && !rawValid}>
                  <ListTree className="mr-1 h-3 w-3" /> Fields
                </ModeButton>
                <ModeButton active={rawMode} onClick={switchToRaw}>
                  <Braces className="mr-1 h-3 w-3" /> Raw JSON
                </ModeButton>
              </div>
              {rawMode && !rawValid && (
                <span className="text-[10px] text-destructive">Fix the JSON to switch back to fields.</span>
              )}
            </div>
          )}

          {contract && contract.examples.length > 0 && (
            <ExamplePicker
              examples={contract.examples}
              currentValue={value}
              onApply={applyExternalValue}
            />
          )}

          {canUseFields && !rawMode ? (
            <div className="space-y-3">
              {structuredRoot!.properties.map((property) => (
                <SchemaNodeField
                  key={property.name}
                  root={structuredRoot!.root}
                  schema={property.schema}
                  path={property.name}
                  label={property.title || humanize(property.name)}
                  description={property.description}
                  required={property.required}
                  value={payload[property.name]}
                  onChange={(next) => applyStructuredValue(updateKey(payload, property.name, next))}
                  onValidityChange={(valid) => reportValidity(property.name, valid)}
                />
              ))}
              {structuredRoot!.properties.length === 0 && (
                <p className="text-[11px] text-muted-foreground">This payload has no editable fields.</p>
              )}
              <p className="text-[10px] text-muted-foreground">
                Basic checks only — the server validates the full contract.
              </p>
            </div>
          ) : (
            <div className="space-y-1.5">
              <label htmlFor={`${errorId}-raw`} className="sr-only">
                Request payload JSON
              </label>
              <textarea
                id={`${errorId}-raw`}
                value={value}
                onChange={(event) => applyRawValue(event.target.value)}
                placeholder={functionInfo?.requestExample ?? '{"key":"value"}'}
                rows={12}
                aria-invalid={!rawValid}
                aria-describedby={!rawValid ? errorId : undefined}
                className="w-full rounded-lg border border-input bg-transparent px-2.5 py-2 font-mono text-xs outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive"
              />
              {!rawValid && (
                <p id={errorId} className="text-[11px] text-destructive">
                  Enter valid JSON.
                </p>
              )}
            </div>
          )}
        </div>

        <DialogFooter className="m-0">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Done
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Example filler ────────────────────────────────────────────────────────────
//
// Loading an example is an explicit, non-reversible overwrite, so it is modeled
// as an action (button / dropdown item) rather than a stateful <select>. When the
// current payload already matches the chosen example — or is blank — the value is
// applied silently; otherwise we confirm before clobbering the user's data. The
// single-example button also reflects that state in its label: it invites a
// `fill` when blank, offers a confirmed `reset` when the payload has drifted, and
// disables itself once the payload already matches the generated example.
function ExamplePicker({
  examples,
  currentValue,
  onApply,
}: {
  examples: FunctionRequestExampleDto[];
  currentValue: string;
  onApply: (value: string) => void;
}) {
  const [pending, setPending] = useState<FunctionRequestExampleDto | null>(null);

  const choose = (example: FunctionRequestExampleDto) => {
    if (classifyExampleAction(currentValue, example.valueJson) === "reset") {
      setPending(example);
    } else {
      onApply(prettyJson(example.valueJson));
    }
  };

  const confirmReplace = () => {
    if (pending) onApply(prettyJson(pending.valueJson));
    setPending(null);
  };

  const single =
    examples.length === 1
      ? { example: examples[0], action: classifyExampleAction(currentValue, examples[0].valueJson) }
      : null;

  return (
    <>
      {single ? (
        <Button
          type="button"
          variant="outline"
          size="xs"
          disabled={single.action === "matching"}
          onClick={() => choose(single.example)}
        >
          {single.action === "fill"
            ? "Fill with generated example"
            : single.action === "reset"
              ? "Reset to generated example"
              : "Using generated example"}
        </Button>
      ) : (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button type="button" variant="outline" size="xs">
              Fill with an example
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start" className="w-auto min-w-52 max-w-xs">
            {examples.map((example) => (
              <DropdownMenuItem key={example.key} onSelect={() => choose(example)}>
                <span className="truncate">{example.summary || example.key}</span>
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>
      )}

      <AlertDialog open={!!pending} onOpenChange={(open) => !open && setPending(null)}>
        <AlertDialogContent size="sm">
          <AlertDialogHeader>
            <AlertDialogTitle>Replace current payload?</AlertDialogTitle>
            <AlertDialogDescription>
              Your existing payload values will be replaced with this example. This can’t be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={confirmReplace}>Replace payload</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}

function ModeButton({
  active,
  disabled,
  onClick,
  children,
}: {
  active: boolean;
  disabled?: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={active}
      className={cn(
        "inline-flex h-full items-center rounded-md px-2 font-medium transition-colors disabled:opacity-40",
        active ? "bg-card text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground",
      )}
    >
      {children}
    </button>
  );
}

// ── Recursive field renderer ──────────────────────────────────────────────────
//
// Resolves the node one level at a time (so self-referential schemas only expand
// when the user drills in) and dispatches to the matching control. Anything the
// resolver cannot model — tuples, sealed empty objects, multi-branch unions —
// renders a raw-JSON box for just that subtree, preserving the wire value.
function SchemaNodeField({
  root,
  schema,
  path,
  label,
  description,
  required,
  value,
  onChange,
  onValidityChange,
  depth = 0,
}: {
  root: unknown;
  schema: unknown;
  path: string;
  label: string;
  description?: string;
  required: boolean;
  value: unknown;
  onChange: (value: unknown) => void;
  onValidityChange: (valid: boolean) => void;
  depth?: number;
}) {
  const resolved = useMemo(() => resolveSchemaNode(root, schema), [root, schema]);
  const missing = required && (value === undefined || value === null || value === "");

  const labelRow = (
    <div className="flex items-center gap-1.5 text-[11px] font-medium text-foreground">
      <span>{label}</span>
      {required && <span className="text-status-error">*</span>}
      {missing && <span className="text-[10px] font-normal text-destructive">Required</span>}
    </div>
  );

  const composite = resolved.kind === "object" || resolved.kind === "array" || resolved.kind === "dictionary";
  if (composite && exceedsSchemaRenderDepth(depth)) {
    return (
      <label className="block space-y-1">
        {labelRow}
        <RawSubtreeField value={value} onChange={onChange} onValidityChange={onValidityChange} />
        <span className="block text-[10px] text-muted-foreground">
          Recursive schema depth reached. Edit this subtree as raw JSON.
        </span>
      </label>
    );
  }

  // Object / array / dictionary render as bordered groups; primitives render inline.
  if (resolved.kind === "object") {
    return (
      <fieldset className={cn("space-y-2 rounded-lg border p-2.5", missing ? "border-destructive/60" : "border-border/60")}>
        <legend className="px-1">{labelRow}</legend>
        {description && <p className="px-1 text-[10px] text-muted-foreground">{description}</p>}
        <ObjectFields
          root={root}
          properties={resolved.properties}
          path={path}
          value={asObject(value)}
          onChange={onChange}
          onValidityChange={onValidityChange}
          depth={depth}
        />
      </fieldset>
    );
  }

  if (resolved.kind === "array") {
    return (
      <fieldset className={cn("space-y-2 rounded-lg border p-2.5", missing ? "border-destructive/60" : "border-border/60")}>
        <legend className="px-1">{labelRow}</legend>
        {description && <p className="px-1 text-[10px] text-muted-foreground">{description}</p>}
        <ArrayField
          root={root}
          items={resolved.items}
          path={path}
          value={Array.isArray(value) ? value : []}
          onChange={onChange}
          onValidityChange={onValidityChange}
          depth={depth}
        />
      </fieldset>
    );
  }

  if (resolved.kind === "dictionary") {
    return (
      <fieldset className={cn("space-y-2 rounded-lg border p-2.5", missing ? "border-destructive/60" : "border-border/60")}>
        <legend className="px-1">{labelRow}</legend>
        {description && <p className="px-1 text-[10px] text-muted-foreground">{description}</p>}
        <DictionaryField
          root={root}
          values={resolved.values}
          path={path}
          value={asObject(value)}
          onChange={onChange}
          onValidityChange={onValidityChange}
          depth={depth}
        />
      </fieldset>
    );
  }

  if (resolved.kind === "unsupported") {
    return (
      <label className="block space-y-1">
        {labelRow}
        <RawSubtreeField value={value} onChange={onChange} onValidityChange={onValidityChange} />
        <span className="block text-[10px] text-muted-foreground">
          {description ?? "Edited as raw JSON (unsupported schema construct)."}
        </span>
      </label>
    );
  }

  // Primitive / enum leaves.
  return (
    <label className="block space-y-1">
      {labelRow}
      <PrimitiveControl resolved={resolved} value={value} onChange={onChange} />
      {description && <span className="block text-[10px] text-muted-foreground">{description}</span>}
    </label>
  );
}

function ObjectFields({
  root,
  properties,
  path,
  value,
  onChange,
  onValidityChange,
  depth,
}: {
  root: unknown;
  properties: ResolvedProperty[];
  path: string;
  value: Record<string, unknown>;
  onChange: (value: unknown) => void;
  onValidityChange: (valid: boolean) => void;
  depth: number;
}) {
  // Aggregate child validity for this subtree so a bad leaf bubbles up as one bit.
  const invalid = useRef<Set<string>>(new Set());
  const report = (key: string, valid: boolean) => {
    if (valid) invalid.current.delete(key);
    else invalid.current.add(key);
    onValidityChange(invalid.current.size === 0);
  };

  return (
    <div className="space-y-2.5">
      {properties.map((property) => (
        <SchemaNodeField
          key={property.name}
          root={root}
          schema={property.schema}
          path={`${path}.${property.name}`}
          label={property.title || humanize(property.name)}
          description={property.description}
          required={property.required}
          value={value[property.name]}
          onChange={(next) => onChange(updateKey(value, property.name, next))}
          onValidityChange={(valid) => report(property.name, valid)}
          depth={depth + 1}
        />
      ))}
    </div>
  );
}

function ArrayField({
  root,
  items,
  path,
  value,
  onChange,
  onValidityChange,
  depth,
}: {
  root: unknown;
  items: unknown;
  path: string;
  value: unknown[];
  onChange: (value: unknown) => void;
  onValidityChange: (valid: boolean) => void;
  depth: number;
}) {
  const invalid = useRef<Set<number>>(new Set());
  const report = (index: number, valid: boolean) => {
    if (valid) invalid.current.delete(index);
    else invalid.current.add(index);
    onValidityChange(invalid.current.size === 0);
  };

  const setItem = (index: number, next: unknown) => {
    const copy = value.slice();
    copy[index] = next;
    onChange(copy);
  };

  const removeItem = (index: number) => {
    invalid.current.clear();
    onValidityChange(true);
    onChange(value.filter((_, i) => i !== index));
  };

  const addItem = () => {
    onChange([...value, emptyValueForNode(root, items ?? {})]);
  };

  return (
    <div className="space-y-2">
      {value.length === 0 && <p className="text-[10px] text-muted-foreground">No items yet.</p>}
      {value.map((item, index) => (
        <div key={index} className="flex items-start gap-1.5">
          <div className="min-w-0 flex-1">
            <SchemaNodeField
              root={root}
              schema={items ?? {}}
              path={`${path}[${index}]`}
              label={`Item ${index + 1}`}
              required={false}
              value={item}
              onChange={(next) => setItem(index, next)}
              onValidityChange={(valid) => report(index, valid)}
              depth={depth + 1}
            />
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={`Remove item ${index + 1}`}
            className="mt-0.5 text-muted-foreground hover:text-destructive"
            onClick={() => removeItem(index)}
          >
            <Trash2 className="h-3.5 w-3.5" />
          </Button>
        </div>
      ))}
      <Button type="button" variant="outline" size="xs" onClick={addItem}>
        <Plus className="mr-1 h-3 w-3" /> Add item
      </Button>
    </div>
  );
}

function DictionaryField({
  root,
  values,
  path,
  value,
  onChange,
  onValidityChange,
  depth,
}: {
  root: unknown;
  values: unknown;
  path: string;
  value: Record<string, unknown>;
  onChange: (value: unknown) => void;
  onValidityChange: (valid: boolean) => void;
  depth: number;
}) {
  // Keys are edited locally so an in-progress rename does not clobber the object
  // (renaming key-by-key would otherwise thrash the map). Committed on blur.
  const entries = Object.entries(value);
  const invalid = useRef<Set<string>>(new Set());
  const report = (key: string, valid: boolean) => {
    if (valid) invalid.current.delete(key);
    else invalid.current.add(key);
    onValidityChange(invalid.current.size === 0);
  };

  const setEntryValue = (key: string, next: unknown) => {
    onChange({ ...value, [key]: next });
  };

  const renameKey = (from: string, to: string) => {
    if (from === to) return;
    const rebuilt: Record<string, unknown> = {};
    for (const [key, val] of entries) rebuilt[key === from ? to : key] = val;
    onChange(rebuilt);
  };

  const removeKey = (key: string) => {
    invalid.current.delete(key);
    onValidityChange(invalid.current.size === 0);
    const rest = { ...value };
    delete rest[key];
    onChange(rest);
  };

  const addKey = () => {
    let key = "key";
    let n = 1;
    while (key in value) key = `key${++n}`;
    onChange({ ...value, [key]: values ? emptyValueForNode(root, values) : "" });
  };

  return (
    <div className="space-y-2">
      {entries.length === 0 && <p className="text-[10px] text-muted-foreground">No entries yet.</p>}
      {entries.map(([key, val]) => (
        <div key={key} className="flex items-start gap-1.5">
          <DictionaryKeyInput keyName={key} onCommit={(next) => renameKey(key, next)} taken={value} />
          <div className="min-w-0 flex-1">
            {values ? (
              <SchemaNodeField
                root={root}
                schema={values}
                path={`${path}.${key}`}
                label="Value"
                required={false}
                value={val}
                onChange={(next) => setEntryValue(key, next)}
                onValidityChange={(valid) => report(key, valid)}
                depth={depth + 1}
              />
            ) : (
              <RawSubtreeField
                value={val}
                onChange={(next) => setEntryValue(key, next)}
                onValidityChange={(valid) => report(key, valid)}
              />
            )}
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={`Remove entry ${key}`}
            className="mt-0.5 text-muted-foreground hover:text-destructive"
            onClick={() => removeKey(key)}
          >
            <Trash2 className="h-3.5 w-3.5" />
          </Button>
        </div>
      ))}
      <Button type="button" variant="outline" size="xs" onClick={addKey}>
        <Plus className="mr-1 h-3 w-3" /> Add entry
      </Button>
    </div>
  );
}

// A dictionary key is buffered locally and committed on blur/Enter so the parent
// object is not rebuilt on every keystroke (which would move focus and reorder).
function DictionaryKeyInput({
  keyName,
  taken,
  onCommit,
}: {
  keyName: string;
  taken: Record<string, unknown>;
  onCommit: (next: string) => void;
}) {
  const [draft, setDraft] = useState(keyName);
  const [seen, setSeen] = useState(keyName);
  if (seen !== keyName) {
    setSeen(keyName);
    setDraft(keyName);
  }
  const duplicate = draft !== keyName && draft in taken;
  const commit = () => {
    if (!draft || duplicate) {
      setDraft(keyName);
      return;
    }
    onCommit(draft);
  };
  return (
    <Input
      aria-label="Entry key"
      value={draft}
      onChange={(event) => setDraft(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          event.currentTarget.blur();
        }
      }}
      aria-invalid={duplicate}
      className="h-8 w-28 shrink-0 font-mono text-[11px] aria-invalid:border-destructive"
    />
  );
}

function PrimitiveControl({
  resolved,
  value,
  onChange,
}: {
  resolved: { kind: "string" | "number" | "integer" | "boolean" } | { kind: "enum"; enumValues: unknown[] };
  value: unknown;
  onChange: (value: unknown) => void;
}) {
  if (resolved.kind === "enum") {
    return (
      <select
        value={value == null ? "" : JSON.stringify(value)}
        onChange={(event) => onChange(event.target.value ? JSON.parse(event.target.value) : undefined)}
        className="h-8 w-full rounded-lg border border-input bg-card px-2 text-xs"
      >
        <option value="">Select…</option>
        {resolved.enumValues.map((item) => (
          <option key={JSON.stringify(item)} value={JSON.stringify(item)}>
            {String(item)}
          </option>
        ))}
      </select>
    );
  }

  if (resolved.kind === "boolean") {
    return (
      <select
        value={value == null ? "" : String(value)}
        onChange={(event) => onChange(event.target.value === "" ? undefined : event.target.value === "true")}
        className="h-8 w-full rounded-lg border border-input bg-card px-2 text-xs"
      >
        <option value="">Select…</option>
        <option value="true">True</option>
        <option value="false">False</option>
      </select>
    );
  }

  if (resolved.kind === "number" || resolved.kind === "integer") {
    return (
      <Input
        type="number"
        step={resolved.kind === "integer" ? 1 : "any"}
        value={typeof value === "number" ? value : ""}
        onChange={(event) => onChange(event.target.value === "" ? undefined : Number(event.target.value))}
      />
    );
  }

  return (
    <Input
      value={typeof value === "string" ? value : ""}
      onChange={(event) => onChange(event.target.value || undefined)}
    />
  );
}

// Raw-JSON escape hatch for a single subtree the structured tree cannot model.
// The draft text is local so an in-progress "{" survives re-renders; the parsed
// value is published to the parent only when valid, and the parse error is
// surfaced with aria-invalid/aria-describedby.
function RawSubtreeField({
  value,
  onChange,
  onValidityChange,
}: {
  value: unknown;
  onChange: (value: unknown) => void;
  onValidityChange: (valid: boolean) => void;
}) {
  const [draft, setDraft] = useState(() => initStructuredDraft(value));
  const [seenValue, setSeenValue] = useState(value);
  const errorId = useId();

  if (!sameJsonValue(seenValue, value)) {
    setSeenValue(value);
    setDraft((current) => syncStructuredDraft(current, seenValue, value));
  }

  const handleChange = (text: string) => {
    const parsed = parseStructuredInput(text);
    if (parsed.status === "invalid") {
      setDraft({ text, error: parsed.error });
      onValidityChange(false);
      return;
    }
    setDraft({ text, error: null });
    onValidityChange(true);
    onChange(parsed.status === "empty" ? undefined : parsed.value);
  };

  return (
    <>
      <textarea
        rows={3}
        value={draft.text}
        onChange={(event) => handleChange(event.target.value)}
        aria-invalid={!!draft.error}
        aria-describedby={draft.error ? errorId : undefined}
        className="w-full rounded-lg border border-input bg-transparent px-2 py-1.5 font-mono text-xs aria-invalid:border-destructive"
      />
      {draft.error && (
        <span id={errorId} className="block text-[10px] text-destructive">
          {draft.error}
        </span>
      )}
    </>
  );
}

// ── Shared helpers (also used by the create/edit/chain screens) ───────────────

// Shared form initializer; colocated with the editor to keep schema parsing identical.
// eslint-disable-next-line react-refresh/only-export-components
export function initialPayloadForFunction(functionInfo?: FunctionInfoDto): string {
  if (!functionInfo?.requestContract) return functionInfo?.requestExample ?? "";
  const first = functionInfo.requestContract.examples[0];
  if (first) return prettyJson(first.valueJson);
  const schema = parseSchema(functionInfo.requestContract);
  if (!schema?.properties) return "{}";
  const payload: Record<string, unknown> = {};
  for (const [name, property] of Object.entries(schema.properties)) {
    if (property.default !== undefined) payload[name] = property.default;
  }
  return JSON.stringify(payload, null, 2);
}

// Shared submit guard; uses the same parsed schema as the generated controls.
// eslint-disable-next-line react-refresh/only-export-components
export function validateRequestPayload(functionInfo: FunctionInfoDto | undefined, value: string): string | null {
  const contract = functionInfo?.requestContract;
  if (!contract) return null;
  if (!value.trim()) return contract.required ? "Request payload is required." : null;

  let payload: unknown;
  try { payload = JSON.parse(value); } catch { return "Request payload must be valid JSON."; }
  const schema = parseSchema(contract);
  if (!schema?.properties || payload == null || Array.isArray(payload) || typeof payload !== "object") {
    return schema?.properties ? "Request payload must be a JSON object." : null;
  }

  const object = payload as Record<string, unknown>;
  for (const name of schema.required ?? []) {
    if (!(name in object) || object[name] == null || object[name] === "") return `${humanize(name)} is required.`;
  }
  for (const [name, property] of Object.entries(schema.properties)) {
    if (!(name in object) || object[name] == null) continue;
    const expected = Array.isArray(property.type) ? property.type.filter((item) => item !== "null") : [property.type];
    if (!matchesType(object[name], expected)) return `${humanize(name)} has an invalid type.`;
    if (property.enum && !property.enum.some((item) => JSON.stringify(item) === JSON.stringify(object[name]))) {
      return `${humanize(name)} must use an allowed value.`;
    }
  }
  return null;
}

function summarizePayload(value: string): { text: string; empty: boolean } {
  if (!value.trim()) return { text: "No payload — click to edit", empty: true };
  let parsed: unknown;
  try {
    parsed = JSON.parse(value);
  } catch {
    return { text: collapse(value), empty: false };
  }
  if (parsed && !Array.isArray(parsed) && typeof parsed === "object") {
    const keys = Object.keys(parsed as Record<string, unknown>);
    if (keys.length === 0) return { text: "Empty object — click to edit", empty: true };
  }
  return { text: collapse(JSON.stringify(parsed)), empty: false };
}

function collapse(text: string): string {
  const oneLine = text.replace(/\s+/g, " ").trim();
  return oneLine.length > 80 ? `${oneLine.slice(0, 79)}…` : oneLine;
}

function parseSchema(contract: FunctionRequestContractDto | null): ObjectJsonSchema | null {
  if (!contract?.schemaJson) return null;
  try { return JSON.parse(contract.schemaJson) as ObjectJsonSchema; } catch { return null; }
}

function parseObject(value: string): Record<string, unknown> {
  try {
    const parsed = JSON.parse(value || "{}");
    return parsed && !Array.isArray(parsed) && typeof parsed === "object" ? parsed : {};
  } catch { return {}; }
}

function asObject(value: unknown): Record<string, unknown> {
  return value && !Array.isArray(value) && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

function updateKey(payload: Record<string, unknown>, name: string, value: unknown): Record<string, unknown> {
  const next = { ...payload };
  if (value === undefined) delete next[name]; else next[name] = value;
  return next;
}

function prettyJson(value: string): string {
  try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; }
}

function matchesType(value: unknown, expected: (string | undefined)[]): boolean {
  if (expected.includes(undefined)) return true;
  if (expected.includes("integer") && typeof value === "number" && Number.isInteger(value)) return true;
  if (expected.includes("number") && typeof value === "number" && Number.isFinite(value)) return true;
  if (expected.includes("array") && Array.isArray(value)) return true;
  if (expected.includes("object") && value !== null && !Array.isArray(value) && typeof value === "object") return true;
  return expected.includes(typeof value);
}

function humanize(value: string): string {
  return value.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[_-]+/g, " ").replace(/^./, (char) => char.toUpperCase());
}
