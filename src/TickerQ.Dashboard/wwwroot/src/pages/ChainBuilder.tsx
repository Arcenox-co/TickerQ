import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useQueries } from "@tanstack/react-query";
import {
  ArrowDownToLine,
  GitBranch,
  GitFork,
  LayoutTemplate,
  Loader2,
  Plus,
  RotateCcw,
  Settings2,
  ShieldAlert,
  Trash2,
  X,
  Zap,
} from "lucide-react";
import { toast } from "sonner";
import { nowAsDatetimeLocal } from "@/components/form-fields/datetime-field";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { RunConditionBadge } from "@/components/cron/RunConditionBadge";
import {
  RUN_CONDITION_CONFIG,
  RUN_CONDITION_OPTIONS,
  runConditionHsl,
  type RunCondition,
} from "@/lib/cron/run-condition";
import {
  describeRetryPolicy,
  parseIntervalsList,
} from "@/lib/cron/status-config";
import { parseUtc } from "@/lib/cron/format";
import { cn } from "@/lib/utils";
import {
  qk,
  useAddTimeTickerChain,
  useAllFunctions,
  useChainTickers,
  useDeleteTimeTicker,
} from "@/services/hooks";
import { dashboardApi } from "@/services/dashboard-api";
import { isReadOnly } from "@/lib/runtime-config";
import type { TimeTickerFlatDto, TimeTickerNode } from "@/services/api-types";

export interface ChainNode {
  id: string;
  functionName: string;
  runCondition: RunCondition;
  retries: number;
  retryIntervalsSeconds: string;
  description: string;
  requestJson: string;
  active: boolean;
  scheduledAt: string;
  children: ChainNode[];
}

// Backend now supports unbounded chain depth (probe-and-extend in the EF Core
// persistence layer walks descendants past the fast 2-level projection). Keeping
// a large UI-side guardrail to prevent runaway tree growth from the "add slot"
// affordance; bump if you need very deep workflows.
const MAX_CHILDREN = 5;
const MAX_DEPTH = 20;
const INITIAL_SLOTS = 3;

const NODE_W = 220;
const NODE_H = 72;
const SLOT_H = 56;
const H_GAP = 32;
const V_GAP = 56;

function generateId() {
  return `new-${Math.random().toString(36).slice(2, 8)}`;
}

function makeSlot(active = false): ChainNode {
  return {
    id: generateId(),
    functionName: "",
    runCondition: "OnSuccess",
    retries: 0,
    retryIntervalsSeconds: "",
    description: "",
    requestJson: "",
    active,
    scheduledAt: "",
    children: [],
  };
}

function makeInitialTree(): ChainNode {
  const root: ChainNode = { ...makeSlot(true), children: [] };
  for (let i = 0; i < INITIAL_SLOTS; i++) root.children.push(makeSlot(false));
  return root;
}

function countActive(node: ChainNode): number {
  const self = node.active ? 1 : 0;
  return self + node.children.reduce((s, c) => s + countActive(c), 0);
}

function cloneTree(node: ChainNode): ChainNode {
  return { ...node, children: node.children.map(cloneTree) };
}

// ── Edit-mode helpers ──
function safeAtob(b64: string): string {
  try {
    return decodeURIComponent(escape(atob(b64)));
  } catch {
    try {
      return atob(b64);
    } catch {
      return b64;
    }
  }
}

function toDatetimeLocal(iso: string | null | undefined): string {
  if (!iso) return "";
  const d = parseUtc(iso);
  if (Number.isNaN(d.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(
    d.getHours()
  )}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

/**
 * Convert a flat chain (root + descendants returned by useChainTickers) into
 * the builder's ChainNode tree. Every node gets a new `edit-<id>` id so the
 * builder treats them as fresh — the existing chain is fully replaced on
 * submit (delete-then-create, matching the Hub).
 */
function tickersToChainNode(
  tickers: TimeTickerFlatDto[],
  rootId: string,
  payloadMap: Map<string, string>
): ChainNode | null {
  const byId = new Map(tickers.map((t) => [t.id, t]));
  const childrenByParent = new Map<string, TimeTickerFlatDto[]>();
  for (const t of tickers) {
    if (t.parentId) {
      const arr = childrenByParent.get(t.parentId) ?? [];
      arr.push(t);
      childrenByParent.set(t.parentId, arr);
    }
  }
  const root = byId.get(rootId);
  if (!root) return null;
  const build = (t: TimeTickerFlatDto, isRoot: boolean): ChainNode => ({
    id: `edit-${t.id}`,
    functionName: t.functionName,
    runCondition: t.runCondition ?? "OnAnyCompletedStatus",
    retries: t.retries ?? 0,
    retryIntervalsSeconds: "",
    description: t.description ?? "",
    requestJson: payloadMap.get(t.id) ?? "",
    active: true,
    scheduledAt: isRoot ? toDatetimeLocal(t.scheduledFor) : "",
    children: (childrenByParent.get(t.id) ?? []).map((c) => build(c, false)),
  });
  return build(root, true);
}

interface Pos {
  id: string;
  x: number;
  y: number;
  parentId?: string;
}

function computeLayout(root: ChainNode): Pos[] {
  const positions: Pos[] = [];
  function subtreeWidth(n: ChainNode): number {
    const v = n.children;
    if (v.length === 0) return NODE_W;
    const w = v.reduce((s, c) => s + subtreeWidth(c), 0);
    return Math.max(NODE_W, w + (v.length - 1) * H_GAP);
  }
  function layout(n: ChainNode, x: number, y: number, parentId?: string) {
    const sw = subtreeWidth(n);
    const nx = x + sw / 2 - NODE_W / 2;
    positions.push({ id: n.id, x: nx, y, parentId });
    if (n.children.length > 0) {
      let cx = x;
      const childY = y + (n.active ? NODE_H : SLOT_H) + V_GAP;
      for (const child of n.children) {
        const cw = subtreeWidth(child);
        layout(child, cx, childY, n.id);
        cx += cw + H_GAP;
      }
    }
  }
  layout(root, 0, 0);
  return positions;
}

// ── Templates ──
interface ChainTemplate {
  id: string;
  name: string;
  description: string;
  icon: React.ReactNode;
  build: (fns: string[]) => ChainNode;
}

function buildFanOut(fns: string[]): ChainNode {
  const root: ChainNode = { ...makeSlot(true), functionName: fns[0] || "", children: [] };
  for (let i = 0; i < 3; i++) {
    root.children.push({ ...makeSlot(true), functionName: fns[i + 1] || "", runCondition: "OnSuccess" });
  }
  return root;
}
function buildSequential(fns: string[]): ChainNode {
  const root: ChainNode = { ...makeSlot(true), functionName: fns[0] || "", children: [] };
  const child: ChainNode = { ...makeSlot(true), functionName: fns[1] || "", runCondition: "OnSuccess", children: [] };
  const grandchild: ChainNode = { ...makeSlot(true), functionName: fns[2] || "", runCondition: "OnSuccess", children: [] };
  child.children.push(grandchild);
  root.children.push(child);
  return root;
}
function buildErrorHandler(fns: string[]): ChainNode {
  const root: ChainNode = { ...makeSlot(true), functionName: fns[0] || "", children: [] };
  root.children.push({ ...makeSlot(true), functionName: fns[1] || "", runCondition: "OnSuccess" });
  root.children.push({ ...makeSlot(true), functionName: fns[2] || "", runCondition: "OnFailure" });
  return root;
}
function buildFanIn(fns: string[]): ChainNode {
  const root: ChainNode = { ...makeSlot(true), functionName: fns[0] || "", children: [] };
  for (let i = 0; i < 3; i++) {
    const child: ChainNode = { ...makeSlot(true), functionName: fns[i + 1] || "", runCondition: "OnSuccess", children: [] };
    child.children.push({ ...makeSlot(true), functionName: fns[4] || "", runCondition: "OnAnyCompletedStatus" });
    root.children.push(child);
  }
  return root;
}

const CHAIN_TEMPLATES: ChainTemplate[] = [
  { id: "fan-out",       name: "Fan-out",            description: "One root job triggers multiple parallel children. Great for distributing work.", icon: <GitFork className="h-4 w-4" />,         build: buildFanOut },
  { id: "sequential",    name: "Sequential Pipeline", description: "Jobs run one after another in a linear chain: Root → Child → Grandchild.",       icon: <ArrowDownToLine className="h-4 w-4" />, build: buildSequential },
  { id: "error-handler", name: "Error Handler",      description: "Root with a success path and a failure path for error recovery workflows.",      icon: <ShieldAlert className="h-4 w-4" />,    build: buildErrorHandler },
  { id: "fan-in",        name: "Fan-in Aggregator",  description: "Parallel children each trigger a shared grandchild for result aggregation.",     icon: <Zap className="h-4 w-4" />,             build: buildFanIn },
];

// ── Skeleton slot ──
function SkeletonSlot({
  onClick,
  onRemove,
}: {
  onClick: () => void;
  onRemove?: () => void;
}) {
  return (
    <div style={{ width: NODE_W }}>
      <div className="relative group">
        <button
          onClick={onClick}
          className={cn(
            "w-full rounded-xl border-2 border-dashed border-border/50 p-3 cursor-pointer transition-all duration-300",
            "hover:border-primary/40 hover:bg-primary/5"
          )}
          style={{ height: SLOT_H }}
        >
          <div className="flex items-center justify-center gap-2 h-full">
            <Plus className="h-4 w-4 text-muted-foreground/40 group-hover:text-primary transition-colors" />
            <span className="text-[11px] text-muted-foreground/40 group-hover:text-primary/70 font-medium transition-colors">
              Add job here
            </span>
          </div>
        </button>
        {onRemove && (
          <button
            onClick={(e) => {
              e.stopPropagation();
              onRemove();
            }}
            className="absolute -top-1.5 -right-1.5 h-5 w-5 rounded-full bg-surface-2 border border-border flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity hover:bg-status-error/20 hover:border-status-error/40 hover:text-status-error"
          >
            <X className="h-2.5 w-2.5" />
          </button>
        )}
      </div>
    </div>
  );
}

// ── Active node ──
function ActiveNode({
  node,
  isRoot,
  isSelected,
  onClick,
}: {
  node: ChainNode;
  isRoot?: boolean;
  isSelected?: boolean;
  onClick: () => void;
}) {
  const conditionColor = !isRoot ? runConditionHsl(node.runCondition) : null;
  return (
    <div style={{ width: NODE_W }}>
      <div
        onClick={onClick}
        className={cn(
          "w-full rounded-xl border bg-card p-3 cursor-pointer transition-all duration-200",
          isRoot && "border-primary/40",
          isSelected && "ring-1 ring-primary/50"
        )}
        style={conditionColor ? { borderColor: conditionColor } : undefined}
      >
        <div className="flex items-center gap-2 mb-1.5">
          {isRoot && <GitBranch className="h-3 w-3 text-primary shrink-0" />}
          <span className="font-mono text-[11px] font-semibold truncate text-foreground">
            {node.functionName ||
              (isRoot ? "Select function..." : "Untitled job")}
          </span>
        </div>
        {node.description.trim().length > 0 && (
          <p
            className="text-[10px] text-muted-foreground line-clamp-2 mb-1.5"
            title={node.description}
          >
            {node.description}
          </p>
        )}
        <div className="flex items-center gap-1.5">
          {!isRoot && <RunConditionBadge condition={node.runCondition} />}
          {isRoot && (
            <span className="text-[10px] text-muted-foreground font-medium bg-surface-2 rounded px-1.5 py-0.5">
              Root
            </span>
          )}
          {node.retries > 0 && (
            <span className="text-[10px] text-muted-foreground flex items-center gap-0.5 ml-auto">
              <RotateCcw className="h-2.5 w-2.5" />
              {node.retries}
            </span>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Editor panel ──
function NodeEditor({
  node,
  isRoot,
  functionNames,
  onUpdate,
  onDeactivate,
  onClose,
}: {
  node: ChainNode;
  isRoot: boolean;
  functionNames: string[];
  onUpdate: (updates: Partial<ChainNode>) => void;
  onDeactivate?: () => void;
  onClose: () => void;
}) {
  return (
    <Sheet open onOpenChange={(o) => !o && onClose()}>
      <SheetContent
        side="right"
        className="flex w-[360px] sm:max-w-[360px] flex-col border-l border-border bg-surface-1 p-0 shadow-none gap-0"
      >
        <SheetHeader className="px-5 pt-5 pb-3 border-b border-border shrink-0">
          <SheetTitle className="text-sm font-semibold flex items-center gap-2">
            <Settings2 className="h-4 w-4 text-primary" />
            {isRoot ? "Edit Root Job" : "Edit Child Job"}
          </SheetTitle>
          <SheetDescription className="text-[11px] text-muted-foreground">
            Configure the job's function, run condition, and retries.
          </SheetDescription>
        </SheetHeader>
        <div className="flex-1 overflow-y-auto p-5 space-y-4">
          <div className="space-y-1.5">
            <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
              Function
            </Label>
            {functionNames.length > 0 ? (
              <Select
                value={node.functionName}
                onValueChange={(v) => onUpdate({ functionName: v })}
              >
                <SelectTrigger className="h-9 w-full bg-surface-0 border-border text-xs">
                  <SelectValue placeholder="Select function..." />
                </SelectTrigger>
                <SelectContent>
                  {functionNames.map((fn) => (
                    <SelectItem key={fn} value={fn} className="text-xs">
                      {fn}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            ) : (
              <Input
                value={node.functionName}
                placeholder="FunctionName"
                onChange={(e) => onUpdate({ functionName: e.target.value })}
                className="h-9 bg-surface-0 border-border text-xs"
              />
            )}
          </div>

          {!isRoot && (
            <div className="space-y-1.5">
              <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
                Run Condition
              </Label>
              <Select
                value={node.runCondition}
                onValueChange={(v) =>
                  onUpdate({ runCondition: v as RunCondition })
                }
              >
                <SelectTrigger className="h-9 w-full bg-surface-0 border-border text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {RUN_CONDITION_OPTIONS.map((opt) => (
                    <SelectItem key={opt} value={opt} className="text-xs">
                      <span className="font-medium">
                        {RUN_CONDITION_CONFIG[opt].label}
                      </span>
                      <span className="text-muted-foreground ml-1">
                        — {RUN_CONDITION_CONFIG[opt].description}
                      </span>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          <div className="space-y-1.5">
            <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
              Description
            </Label>
            <Input
              type="text"
              value={node.description}
              onChange={(e) => onUpdate({ description: e.target.value })}
              maxLength={2000}
              placeholder="Optional"
              className="h-9 bg-surface-0 border-border text-xs"
            />
          </div>

          <div className="rounded-lg border border-border/50 bg-surface-0/30 p-3 space-y-2.5">
            <div className="flex items-center gap-1.5 text-[10px] text-muted-foreground uppercase tracking-wider">
              <RotateCcw className="h-3 w-3" />
              Retry policy
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
                  Retries
                </Label>
                <Input
                  type="number"
                  min={0}
                  max={100}
                  value={node.retries}
                  onChange={(e) =>
                    onUpdate({ retries: Number(e.target.value) })
                  }
                  className="h-9 bg-surface-0 border-border text-xs"
                />
              </div>
              <div className="space-y-1.5">
                <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
                  Intervals (sec)
                </Label>
                <Input
                  type="text"
                  value={
                    node.retries > 0 ? node.retryIntervalsSeconds : ""
                  }
                  onChange={(e) =>
                    onUpdate({ retryIntervalsSeconds: e.target.value })
                  }
                  disabled={node.retries <= 0}
                  placeholder={node.retries > 0 ? "5, 30, 60" : "—"}
                  className="h-9 bg-surface-0 border-border text-xs"
                />
              </div>
            </div>
            <p className="text-[10px] text-muted-foreground/70 italic">
              →{" "}
              {describeRetryPolicy(
                node.retries,
                parseIntervalsList(node.retryIntervalsSeconds)
              )}
            </p>
          </div>

          {isRoot && (
            <div className="space-y-1.5">
              <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
                Schedule
              </Label>
              <div className="relative">
                <Input
                  type="datetime-local"
                  value={node.scheduledAt}
                  onFocus={() => {
                    if (!node.scheduledAt) onUpdate({ scheduledAt: nowAsDatetimeLocal() });
                  }}
                  onChange={(e) => onUpdate({ scheduledAt: e.target.value })}
                  className="h-9 bg-surface-0 border-border text-xs pr-14"
                />
                {node.scheduledAt && (
                  <button
                    type="button"
                    aria-label="Clear"
                    title="Clear (fire at submit time)"
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => onUpdate({ scheduledAt: "" })}
                    className="absolute right-8 top-1/2 -translate-y-1/2 rounded p-0.5 text-muted-foreground/60 hover:text-foreground hover:bg-surface-1"
                  >
                    <X className="h-3.5 w-3.5" />
                  </button>
                )}
              </div>
              <p className="text-[10px] text-muted-foreground/60">
                Empty fires at submit time
              </p>
            </div>
          )}

          <div className="space-y-1.5">
            <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
              Request payload (optional)
            </Label>
            <textarea
              value={node.requestJson}
              onChange={(e) => onUpdate({ requestJson: e.target.value })}
              placeholder='{"key": "value"}'
              className="w-full h-20 rounded-lg border border-border bg-surface-0 px-3 py-2 text-xs font-mono resize-none focus:outline-none focus:ring-1 focus:ring-primary/50"
            />
            <p className="text-[10px] text-muted-foreground/60">
              JSON, plain text, or any string the function expects.
            </p>
          </div>
        </div>
        {!isRoot && onDeactivate && (
          <div className="px-5 py-3 border-t border-border shrink-0">
            <Button
              variant="outline"
              size="sm"
              className="w-full h-8 text-xs text-status-error border-status-error/20 hover:bg-status-error/10 hover:text-status-error"
              onClick={onDeactivate}
            >
              <Trash2 className="h-3 w-3 mr-1.5" />
              Remove Job
            </Button>
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}

// ── Page ──
export default function ChainBuilderPage() {
  // The whole page is a mutation surface — blocked in read-only mode. The
  // server rejects the submit with 403 anyway; this keeps the UX honest.
  if (isReadOnly()) {
    return (
      <div className="flex flex-col items-center justify-center py-24 gap-2 text-center">
        <p className="text-sm font-medium">Read-only mode</p>
        <p className="text-[12px] text-muted-foreground">
          The chain builder is unavailable because this dashboard is running in
          observability-only mode.
        </p>
      </div>
    );
  }
  return <ChainBuilderPageInner />;
}

function ChainBuilderPageInner() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const editId = searchParams.get("edit");
  const isEditing = !!editId;

  const { data: fns } = useAllFunctions();
  const functionNames = useMemo(
    () => (fns ?? []).map((f) => f.functionName),
    [fns]
  );
  const chainMutation = useAddTimeTickerChain();
  const deleteMutation = useDeleteTimeTicker();

  // Edit-mode prefill: walk the existing chain (root + descendants) and fetch
  // each node's payload in parallel, then convert into the builder's editable
  // tree. Submit becomes delete-then-create.
  const editChainQuery = useChainTickers(editId);
  const editTickers = editChainQuery.data;
  const payloadQueries = useQueries({
    queries: (editTickers ?? []).map((t) => ({
      queryKey: qk.timeTickerRequest(t.id),
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        dashboardApi.getTimeTickerRequest(t.id, signal),
      enabled: isEditing,
    })),
  });
  const payloadsLoading = payloadQueries.some((q) => q.isLoading);
  const allLoaded = isEditing && !!editTickers && !payloadsLoading;

  const [root, setRoot] = useState<ChainNode>(() => makeInitialTree());
  const [prefilled, setPrefilled] = useState(false);

  useEffect(() => {
    if (!isEditing || prefilled || !allLoaded || !editId || !editTickers) return;
    const payloadMap = new Map<string, string>();
    editTickers.forEach((t, i) => {
      const raw = payloadQueries[i]?.data?.payload;
      if (raw) payloadMap.set(t.id, safeAtob(raw));
    });
    const tree = tickersToChainNode(editTickers, editId, payloadMap);
    if (tree) {
      setRoot(tree);
      setPrefilled(true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isEditing, prefilled, allLoaded, editId, editTickers]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [zoom, setZoom] = useState(1);
  const [isPanning, setIsPanning] = useState(false);
  const [panStart, setPanStart] = useState({ x: 0, y: 0 });
  const [showTemplates, setShowTemplates] = useState(false);
  const [previousRoot, setPreviousRoot] = useState<ChainNode | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  function findNode(node: ChainNode, id: string): ChainNode | null {
    if (node.id === id) return node;
    for (const c of node.children) {
      const f = findNode(c, id);
      if (f) return f;
    }
    return null;
  }
  function findParent(node: ChainNode, id: string): ChainNode | null {
    for (const c of node.children) {
      if (c.id === id) return node;
      const f = findParent(c, id);
      if (f) return f;
    }
    return null;
  }
  function getNodeDepth(node: ChainNode, id: string, d = 1): number {
    if (node.id === id) return d;
    for (const c of node.children) {
      const r = getNodeDepth(c, id, d + 1);
      if (r > 0) return r;
    }
    return 0;
  }
  function updateNodeInTree(
    tree: ChainNode,
    id: string,
    updates: Partial<ChainNode>
  ): ChainNode {
    if (tree.id === id) {
      return {
        ...tree,
        ...updates,
        children: updates.children ?? tree.children.map(cloneTree),
      };
    }
    return {
      ...tree,
      children: tree.children.map((c) => updateNodeInTree(c, id, updates)),
    };
  }

  function activateSlot(id: string) {
    const newRoot = cloneTree(root);
    const node = findNode(newRoot, id);
    if (!node) return;
    node.active = true;
    const d = getNodeDepth(newRoot, id);
    if (d < MAX_DEPTH && node.children.length === 0) {
      for (let i = 0; i < INITIAL_SLOTS; i++) node.children.push(makeSlot(false));
    }
    setRoot(newRoot);
    setSelectedId(id);
  }
  function deactivateNode(id: string) {
    const newRoot = cloneTree(root);
    const node = findNode(newRoot, id);
    if (!node) return;
    node.active = false;
    node.functionName = "";
    node.runCondition = "OnSuccess";
    node.retries = 0;
    node.retryIntervalsSeconds = "";
    node.description = "";
    node.requestJson = "";
    node.children = [];
    setRoot(newRoot);
    if (selectedId === id) setSelectedId(null);
  }
  function removeSlot(slotId: string) {
    const newRoot = cloneTree(root);
    const parent = findParent(newRoot, slotId);
    if (!parent) return;
    parent.children = parent.children.filter((c) => c.id !== slotId);
    setRoot(newRoot);
    if (selectedId === slotId) setSelectedId(null);
  }
  function addSlot(parentId: string) {
    const newRoot = cloneTree(root);
    const parent = findNode(newRoot, parentId);
    if (!parent || parent.children.length >= MAX_CHILDREN) return;
    parent.children.push(makeSlot(false));
    setRoot(newRoot);
  }
  function updateNode(id: string, updates: Partial<ChainNode>) {
    setRoot((prev) => updateNodeInTree(prev, id, updates));
  }

  const handleWheel = useCallback((e: React.WheelEvent) => {
    e.preventDefault();
    setZoom((z) => Math.max(0.3, Math.min(2, z * (e.deltaY > 0 ? 0.92 : 1.08))));
  }, []);
  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      if (
        e.button === 0 &&
        (e.target as HTMLElement).closest("[data-canvas]")
      ) {
        setIsPanning(true);
        setPanStart({ x: e.clientX - pan.x, y: e.clientY - pan.y });
      }
    },
    [pan]
  );
  const handleMouseMove = useCallback(
    (e: React.MouseEvent) => {
      if (isPanning)
        setPan({ x: e.clientX - panStart.x, y: e.clientY - panStart.y });
    },
    [isPanning, panStart]
  );
  const handleMouseUp = useCallback(() => setIsPanning(false), []);

  useEffect(() => {
    if (containerRef.current) {
      const rect = containerRef.current.getBoundingClientRect();
      const positions = computeLayout(root);
      const maxX = Math.max(...positions.map((p) => p.x + NODE_W)) + 40;
      const scaleX = (rect.width - 60) / maxX;
      const scale = Math.min(scaleX, 1);
      setZoom(scale);
      setPan({ x: (rect.width - maxX * scale) / 2, y: 32 });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function validateTree(n: ChainNode): string | null {
    if (n.active) {
      if (!n.functionName)
        return "Every active job needs a function selected";
      if (
        n.retries > 0 &&
        n.retryIntervalsSeconds.trim().length > 0 &&
        parseIntervalsList(n.retryIntervalsSeconds) === null
      ) {
        return `Retry intervals for "${n.functionName}" must be comma-separated whole numbers between 1 and 3600`;
      }
    }
    for (const c of n.children) {
      const err = validateTree(c);
      if (err) return err;
    }
    return null;
  }

  function toNode(node: ChainNode): TimeTickerNode {
    return {
      function: node.functionName,
      description: node.description || null,
      retries: node.retries > 0 ? node.retries : null,
      retryIntervalsSeconds:
        node.retries > 0
          ? parseIntervalsList(node.retryIntervalsSeconds)
          : null,
      request: node.requestJson || null,
      runCondition: node.runCondition,
      children: node.children.filter((c) => c.active).map(toNode),
    };
  }

  async function handleSubmit() {
    const validationError = validateTree(root);
    if (validationError) {
      toast.error(validationError);
      return;
    }
    setSubmitting(true);
    try {
      const executionTime = root.scheduledAt
        ? new Date(root.scheduledAt).toISOString()
        : new Date().toISOString();
      // Edit = delete the original root (cascades to children) + recreate the
      // new tree. Matches the Hub's chain-replace flow.
      if (isEditing && editId) {
        await deleteMutation.mutateAsync(editId);
      }
      const result = await chainMutation.mutateAsync({
        executionTime,
        root: toNode(root),
      });
      const jobLabel = result.createdCount === 1 ? "job" : "jobs";
      toast.success(
        isEditing
          ? `Chain replaced — ${result.createdCount} ${jobLabel} scheduled`
          : `Chain created — ${result.createdCount} ${jobLabel} scheduled`
      );
      navigate("/time-tickers");
    } catch (err) {
      toast.error(isEditing ? "Failed to replace chain" : "Failed to create chain", {
        description: err instanceof Error ? err.message : "Unknown error",
      });
    } finally {
      setSubmitting(false);
    }
  }

  const positions = computeLayout(root);
  const maxX = Math.max(...positions.map((p) => p.x + NODE_W), 300) + 60;
  const maxY = Math.max(...positions.map((p) => p.y + NODE_H + 40), 200) + 60;
  const nodeMap = new Map<string, ChainNode>();
  function collectNodes(n: ChainNode) {
    nodeMap.set(n.id, n);
    n.children.forEach(collectNodes);
  }
  collectNodes(root);

  const totalActive = countActive(root);
  const selectedNode = selectedId ? findNode(root, selectedId) : null;
  const isSelectedRoot = selectedId === root.id;

  return (
    <div className="relative flex flex-col h-[calc(100vh-44px-24px-24px)] -m-6">
      {isEditing && !prefilled && (
        <div className="absolute inset-0 z-50 bg-background/60 backdrop-blur-[1px] flex items-center justify-center">
          <Loader2 className="h-4 w-4 animate-spin text-primary mr-2" />
          <span className="text-sm text-muted-foreground">Loading chain…</span>
        </div>
      )}
      {/* Toolbar */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-border bg-surface-0/50 shrink-0">
        <div className="flex items-center gap-3">
          <button
            onClick={() => navigate("/time-tickers")}
            className="text-xs text-muted-foreground hover:text-foreground transition-colors"
          >
            ← Back to list
          </button>
          <div className="h-4 w-px bg-border" />
          <GitBranch className="h-3.5 w-3.5 text-primary" />
          <span className="font-mono text-sm font-semibold">
            {isEditing ? "Edit Chain Job" : "New Chain Job"}
          </span>
          {isEditing && (
            <button
              type="button"
              onClick={() => navigate("/time-tickers")}
              className="text-[11px] text-muted-foreground hover:text-foreground transition-colors"
            >
              Cancel edit
            </button>
          )}
        </div>
        <div className="flex items-center gap-3 text-[11px] text-muted-foreground">
          <span className="tabular-nums">
            {totalActive} {totalActive === 1 ? "job" : "jobs"} configured
          </span>
          <Button
            variant="outline"
            size="sm"
            className="h-7 text-xs border-border text-muted-foreground"
            onClick={() => setShowTemplates(true)}
          >
            <LayoutTemplate className="h-3 w-3 mr-1.5" /> Use Templates
          </Button>
          <div className="flex items-center gap-1">
            <button
              onClick={() => setZoom((z) => Math.min(2, z + 0.1))}
              className="px-1.5 py-0.5 rounded bg-surface-2 hover:bg-surface-3 transition-colors"
            >
              +
            </button>
            <span className="tabular-nums w-10 text-center">
              {Math.round(zoom * 100)}%
            </span>
            <button
              onClick={() => setZoom((z) => Math.max(0.3, z - 0.1))}
              className="px-1.5 py-0.5 rounded bg-surface-2 hover:bg-surface-3 transition-colors"
            >
              −
            </button>
          </div>
          <Button
            variant="gradient"
            size="sm"
            className="h-7 text-xs"
            onClick={handleSubmit}
            disabled={!root.functionName || submitting}
          >
            {submitting ? (
              <>
                <Loader2 className="h-3 w-3 mr-1.5 animate-spin" />
                {isEditing ? "Replacing…" : "Creating…"}
              </>
            ) : isEditing ? (
              "Replace Chain"
            ) : (
              "Create Chain"
            )}
          </Button>
        </div>
      </div>

      {/* Hint / Undo bar */}
      {previousRoot ? (
        <div className="flex items-center justify-between px-4 py-2 border-b border-border/50 bg-primary/5 text-[11px] text-primary shrink-0 animate-fade-in">
          <span className="font-medium">
            Template applied — your previous layout was saved.
          </span>
          <div className="flex items-center gap-2">
            <button
              onClick={() => {
                setRoot(previousRoot);
                setPreviousRoot(null);
                setSelectedId(null);
              }}
              className="px-2.5 py-1 rounded-md bg-primary/10 hover:bg-primary/20 font-semibold transition-colors"
            >
              ↩ Undo
            </button>
            <button
              onClick={() => setPreviousRoot(null)}
              className="px-2 py-1 rounded-md hover:bg-primary/10 text-primary/60 hover:text-primary transition-colors"
            >
              Dismiss
            </button>
          </div>
        </div>
      ) : (
        <div className="flex items-center gap-4 px-4 py-2 border-b border-border/50 bg-surface-0/30 text-[10px] text-muted-foreground shrink-0">
          <span>
            Click a dashed slot to activate it · Click an active node to edit · Max{" "}
            {MAX_DEPTH} levels, {MAX_CHILDREN} children per node
          </span>
        </div>
      )}

      {/* Canvas */}
      <div
        ref={containerRef}
        data-canvas
        className="flex-1 overflow-hidden bg-surface-0 relative select-none"
        style={{ cursor: isPanning ? "grabbing" : "grab" }}
        onWheel={handleWheel}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        onClick={(e) => {
          if (
            (e.target as HTMLElement).hasAttribute("data-canvas") ||
            (e.target as HTMLElement).closest("[data-canvas-bg]")
          ) {
            setSelectedId(null);
          }
        }}
      >
        <div
          className="absolute"
          style={{
            transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
            transformOrigin: "0 0",
          }}
        >
          {/* SVG edges */}
          <svg
            width={maxX}
            height={maxY}
            className="absolute top-0 left-0 pointer-events-none"
            style={{ overflow: "visible" }}
          >
            {positions
              .filter((p) => p.parentId)
              .map((child) => {
                const parent = positions.find((pp) => pp.id === child.parentId);
                if (!parent) return null;
                const childNode = nodeMap.get(child.id);
                const parentNode = nodeMap.get(parent.id);
                const isActive = childNode?.active;
                const color = isActive
                  ? runConditionHsl(childNode?.runCondition)
                  : "hsl(var(--border))";
                const parentH = parentNode?.active ? NODE_H : SLOT_H;
                const x1 = parent.x + NODE_W / 2;
                const y1 = parent.y + parentH;
                const x2 = child.x + NODE_W / 2;
                const y2 = child.y;
                const midY = (y1 + y2) / 2;
                return (
                  <g key={`edge-${child.id}`} opacity={isActive ? 0.7 : 0.25}>
                    <path
                      d={`M ${x1} ${y1} C ${x1} ${midY}, ${x2} ${midY}, ${x2} ${y2}`}
                      fill="none"
                      stroke={color}
                      strokeWidth={isActive ? 2 : 1.5}
                      strokeDasharray={isActive ? "6 4" : "3 4"}
                    />
                    <polygon
                      points={`${x2 - 3},${y2 - 5} ${x2 + 3},${y2 - 5} ${x2},${y2}`}
                      fill={color}
                    />
                  </g>
                );
              })}
          </svg>

          {/* Nodes */}
          {positions.map((pos) => {
            const node = nodeMap.get(pos.id);
            if (!node) return null;
            return (
              <div
                key={pos.id}
                className="absolute"
                style={{ left: pos.x, top: pos.y }}
              >
                {node.active ? (
                  <div className="flex flex-col items-center">
                    <ActiveNode
                      node={node}
                      isRoot={pos.id === root.id}
                      isSelected={selectedId === pos.id}
                      onClick={() =>
                        setSelectedId(pos.id === selectedId ? null : pos.id)
                      }
                    />
                    {node.children.length < MAX_CHILDREN &&
                      getNodeDepth(root, pos.id) < MAX_DEPTH && (
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            addSlot(pos.id);
                          }}
                          className="mt-1 text-[9px] text-muted-foreground/30 hover:text-primary/60 transition-colors"
                        >
                          + add slot
                        </button>
                      )}
                  </div>
                ) : (
                  <SkeletonSlot
                    onClick={() => activateSlot(pos.id)}
                    onRemove={
                      pos.id !== root.id ? () => removeSlot(pos.id) : undefined
                    }
                  />
                )}
              </div>
            );
          })}
        </div>

        {/* Dot grid */}
        <div
          data-canvas-bg
          className="absolute inset-0 pointer-events-none opacity-[0.03]"
          style={{
            backgroundImage:
              "radial-gradient(circle, currentColor 1px, transparent 1px)",
            backgroundSize: "24px 24px",
          }}
        />
      </div>

      {/* Editor panel */}
      {selectedNode && selectedNode.active && (
        <NodeEditor
          key={selectedId}
          node={selectedNode}
          isRoot={isSelectedRoot}
          functionNames={functionNames}
          onUpdate={(updates) => updateNode(selectedId!, updates)}
          onDeactivate={
            isSelectedRoot ? undefined : () => deactivateNode(selectedId!)
          }
          onClose={() => setSelectedId(null)}
        />
      )}

      {/* Templates Sheet */}
      <Sheet open={showTemplates} onOpenChange={setShowTemplates}>
        <SheetContent
          side="right"
          className="flex w-[360px] sm:max-w-[360px] flex-col border-l border-border bg-surface-1 p-0 shadow-none gap-0"
        >
          <SheetHeader className="px-5 pt-5 pb-3 border-b border-border shrink-0">
            <SheetTitle className="text-sm font-semibold flex items-center gap-2">
              <LayoutTemplate className="h-4 w-4 text-primary" />
              Chain Templates
            </SheetTitle>
            <SheetDescription className="text-[11px] text-muted-foreground">
              Pick a template to pre-fill the canvas. You can customize it after.
            </SheetDescription>
          </SheetHeader>
          <div className="flex-1 overflow-y-auto p-4 space-y-3">
            {CHAIN_TEMPLATES.map((tpl) => (
              <button
                key={tpl.id}
                onClick={() => {
                  setPreviousRoot(cloneTree(root));
                  setRoot(tpl.build(functionNames));
                  setSelectedId(null);
                  setShowTemplates(false);
                }}
                className="group w-full rounded-xl border border-border bg-card p-4 text-left transition-all duration-200 hover:border-primary/40"
              >
                <div className="flex items-center gap-3 mb-2">
                  <div className="h-8 w-8 rounded-lg bg-primary/10 flex items-center justify-center text-primary group-hover:bg-primary/20 transition-colors">
                    {tpl.icon}
                  </div>
                  <div>
                    <div className="text-xs font-semibold text-foreground">
                      {tpl.name}
                    </div>
                  </div>
                </div>
                <p className="text-[11px] text-muted-foreground leading-relaxed">
                  {tpl.description}
                </p>
              </button>
            ))}
          </div>
          <div className="px-4 py-3 border-t border-border shrink-0">
            <p className="text-[10px] text-muted-foreground/50 text-center">
              Applying a template replaces the current canvas
            </p>
          </div>
        </SheetContent>
      </Sheet>
    </div>
  );
}
