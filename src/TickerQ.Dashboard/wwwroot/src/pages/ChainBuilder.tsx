import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { useQueries, useQuery } from "@tanstack/react-query";
import {
  ArrowDownToLine,
  FlaskConical,
  GitBranch,
  GitFork,
  GitMerge,
  LayoutTemplate,
  Loader2,
  FilePenLine,
  Maximize2,
  Plus,
  PlayCircle,
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
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
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
  baseRunCondition,
  groupedRunCondition,
  isGroupedRunCondition,
  runConditionHsl,
  type BaseRunCondition,
} from "@/lib/cron/run-condition";
import {
  describeRetryPolicy,
  parseIntervalsList,
} from "@/lib/cron/status-config";
import { cn } from "@/lib/utils";
import {
  chainNodeToRequest,
  normalizeRequestlessPayloads,
  requestToChainNode,
  toDatetimeLocal,
  tickersToChainNode,
  type ChainNode,
} from "@/lib/cron/chain-node";
import { cronToChainNode, chainNodeToCronTemplate } from "@/lib/cron/cron-chain";
import { findDuplicateTemplateName } from "@/lib/cron/saved-definitions";
import { CronExpressionPreview } from "@/components/cron/CronExpressionPreview";
import { CronOverlapField } from "@/components/cron/CronOverlapField";
import { ChainTestResultsDialog } from "@/components/cron/ChainTestResultsDialog";
import { LogsPanel } from "@/components/cron/LogsPanel";
import { CronSchedulePresets } from "@/components/cron/CronSchedulePresets";
import { decodeRequestPayload, encodeRequestPayload } from "@/lib/request-payload";
import {
  qk,
  useAddTimeTickerChain,
  useAllFunctions,
  useChainTickers,
  useCronTicker,
  useAddCronTicker,
  useUpdateCronTicker,
  useReplaceTimeTickerChain,
  useActivateDraft,
  useCreateDefinition,
  useDefinition,
  useDefinitions,
  useUpdateDefinition,
} from "@/services/hooks";
import { dashboardApi } from "@/services/dashboard-api";
import { isReadOnly } from "@/lib/runtime-config";
import type { ChainDeleteBehavior, FunctionInfoDto, SavedDefinitionPayload, SavedTickerDefinitionKind, StaleAction, TestRunChainResponse, TestRunResponse } from "@/services/api-types";
import { SaveDefinitionDialog, type SaveDefinitionValues } from "@/components/cron/SaveDefinitionDialog";
import { ActivateDraftDialog } from "@/components/cron/ActivateDraftDialog";
import {
  initialPayloadForFunction,
  RequestPayloadEditor,
  validateRequestPayload,
} from "@/components/cron/RequestPayloadEditor";
import { createDraftState, selectFunction, type FunctionDraftState } from "@/lib/function-draft";
import {
  computeChainFlowLayout,
  PARALLEL_GROUP_FUNCTION,
} from "@/lib/cron/chain-flow-layout";
import {
  GROUP_CHILD_CONDITIONS,
  PARALLEL_OUTCOME_CONDITIONS,
  parallelChildRole,
  validateParallelGroups,
  type ParallelChildRole,
} from "@/lib/cron/parallel-chain";


// Backend now supports unbounded chain depth (probe-and-extend in the EF Core
// persistence layer walks descendants past the fast 2-level projection). Keeping
// a large UI-side guardrail to prevent runaway tree growth from the "add slot"
// affordance; bump if you need very deep workflows.
const MAX_CHILDREN = 5;
const MAX_PARALLEL_ITEMS = 10;
const MAX_DEPTH = 20;
const INITIAL_SLOTS = 3;

const NODE_W = 220;
const NODE_H = 72;
const SLOT_H = 56;
const H_GAP = 32;
const V_GAP = 56;
const MIN_ZOOM = 0.15;
const CONNECTOR_COLOR = "hsl(var(--muted-foreground) / 0.42)";
const JOIN_PATH_COLOR = "hsl(var(--workflow-sync) / 0.82)";

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
    requestValid: true,
    onStale: "Restart",
    deleteBehavior: "Cascade",
    timeoutSeconds: 0,
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

function makeParallelGroup(): ChainNode {
  const job = { ...makeSlot(false), runCondition: "GroupOnSuccess" as const };
  return {
    ...makeSlot(true),
    functionName: PARALLEL_GROUP_FUNCTION,
    description: "Group",
    runCondition: "OnAnyTerminalStatus",
    children: [job],
  };
}

function childLimit(node: ChainNode) {
  return node.functionName === "$tickerq.parallel-group"
    ? MAX_PARALLEL_ITEMS
    : MAX_CHILDREN;
}

function countActive(node: ChainNode): number {
  const self = node.active ? 1 : 0;
  return self + node.children.reduce((s, c) => s + countActive(c), 0);
}

function cloneTree(node: ChainNode): ChainNode {
  return { ...node, children: node.children.map(cloneTree) };
}


function layoutChain(root: ChainNode) {
  return computeChainFlowLayout(root, {
    nodeWidth: NODE_W,
    horizontalGap: H_GAP,
    verticalGap: V_GAP,
    nodeHeight: node => node.active ? NODE_H : SLOT_H,
  });
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
  const group: ChainNode = {
    ...makeSlot(true),
    functionName: "$tickerq.parallel-group",
    description: "Group",
    runCondition: "OnAnyTerminalStatus",
    children: [],
  };
  for (let i = 0; i < 3; i++) {
    group.children.push({
      ...makeSlot(true),
      functionName: fns[i + 1] || "",
      runCondition: "GroupOnSuccess",
      children: [],
    });
  }
  group.children.push({
    ...makeSlot(true),
    functionName: fns[4] || "",
    runCondition: "OnSuccess",
    children: [],
  });
  root.children.push(group);
  return root;
}

const CHAIN_TEMPLATES: ChainTemplate[] = [
  { id: "fan-out",       name: "Fan-out",            description: "One root job triggers multiple parallel children. Great for distributing work.", icon: <GitFork className="h-4 w-4" />,         build: buildFanOut },
  { id: "sequential",    name: "Sequential Pipeline", description: "Jobs run one after another in a linear chain: Root → Child → Grandchild.",       icon: <ArrowDownToLine className="h-4 w-4" />, build: buildSequential },
  { id: "error-handler", name: "Error Handler",      description: "Root with a success path and a failure path for error recovery workflows.",      icon: <ShieldAlert className="h-4 w-4" />,    build: buildErrorHandler },
  { id: "fan-in",        name: "Group with Join",    description: "Jobs start together, then matching conditions run after the group settles.",          icon: <Zap className="h-4 w-4" />,             build: buildFanIn },
];

// ── Skeleton slot ──
function SkeletonSlot({
  onClick,
  onRemove,
  label = "Add job here",
}: {
  onClick: () => void;
  onRemove?: () => void;
  label?: string;
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
              {label}
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
  groupRole = null,
  isSelected,
  onClick,
}: {
  node: ChainNode;
  isRoot?: boolean;
  groupRole?: ParallelChildRole | null;
  isSelected?: boolean;
  onClick: () => void;
}) {
  const isParallelGroup = node.functionName === PARALLEL_GROUP_FUNCTION;
  const displayName = isParallelGroup
    ? "Group"
    : node.functionName || (isRoot ? "Select function..." : "Untitled job");
  const activeParallelChildren = isParallelGroup
    ? node.children.filter(child => child.active)
    : [];
  const branchCount = activeParallelChildren.filter(
    child => isGroupedRunCondition(child.runCondition)
  ).length;
  const outcomeCount = activeParallelChildren.length - branchCount;
  return (
    <div style={{ width: NODE_W }}>
      <button
        type="button"
        onClick={onClick}
        aria-label={`${isRoot ? "Edit root job" : "Edit job"}: ${displayName}`}
        className={cn(
          "w-full rounded-xl border bg-card p-3 text-left cursor-pointer transition-all duration-200",
          isRoot && "border-primary/40",
          isSelected && "ring-1 ring-primary/50"
        )}
      >
        <div className="flex items-center gap-2 mb-1.5">
          {isRoot && <GitBranch className="h-3 w-3 text-primary shrink-0" />}
          {isParallelGroup && <GitMerge className="h-3 w-3 text-workflow-sync shrink-0" />}
          <span className="font-mono text-[11px] font-semibold truncate text-foreground">
            {displayName}
          </span>
        </div>
        {node.description.trim().length > 0 && (
          <span
            className="block text-[10px] text-muted-foreground line-clamp-2 mb-1.5"
            title={node.description}
          >
            {node.description}
          </span>
        )}
        <div className="flex items-center gap-1.5">
          {!isRoot && !isParallelGroup ? (
            <RunConditionBadge
              condition={node.runCondition}
              description={groupRole === "branch"
                ? "This grouped condition is evaluated against the job before the group"
                : undefined}
            />
          ) : null}
          {isParallelGroup && (
            <span className="text-[10px] text-workflow-sync/80">
              {branchCount} {branchCount === 1 ? "grouped job" : "grouped jobs"}
              {outcomeCount > 0 && ` · ${outcomeCount} ${outcomeCount === 1 ? "final job" : "final jobs"}`}
            </span>
          )}
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
      </button>
    </div>
  );
}

// ── Editor panel ──
function formatTestResult(payload: string): string {
  const decoded = decodeRequestPayload(payload);
  try {
    return JSON.stringify(JSON.parse(decoded), null, 2);
  } catch {
    return decoded;
  }
}

function NodeEditor({
  node,
  isRoot,
  recurring = false,
  parallelRole = null,
  functionNames,
  functions,
  onUpdate,
  onDeactivate,
  onClose,
}: {
  node: ChainNode;
  isRoot: boolean;
  recurring?: boolean;
  parallelRole?: ParallelChildRole | null;
  functionNames: string[];
  functions: FunctionInfoDto[];
  onUpdate: (updates: Partial<ChainNode>) => void;
  onDeactivate?: () => void;
  onClose: () => void;
}) {
  const draftBooks = useRef<Record<string, FunctionDraftState>>({});
  const isParallelGroup = node.functionName === "$tickerq.parallel-group";
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<TestRunResponse | null>(null);

  async function runTest() {
    const functionInfo = functions.find((item) => item.functionName === node.functionName);
    const validationError = validateRequestPayload(functionInfo, node.requestJson);
    if (!node.functionName || validationError) {
      toast.error(validationError || "Select a function first");
      return;
    }
    setTesting(true);
    setTestResult(null);
    try {
      const result = await dashboardApi.testRunFunction({
        function: node.functionName,
        request: encodeRequestPayload(node.requestJson),
        timeoutSeconds: node.timeoutSeconds > 0 ? node.timeoutSeconds : 30,
      });
      setTestResult(result);
      if (result.success) toast.success("Test run completed");
      else toast.error("Test run did not complete successfully");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Test run failed");
    } finally {
      setTesting(false);
    }
  }

  return (
    <Sheet open onOpenChange={(o) => !o && onClose()}>
      <SheetContent
        side="right"
        className="flex w-[360px] sm:max-w-[360px] flex-col border-l border-border bg-surface-1 p-0 shadow-none gap-0"
      >
        <SheetHeader className="px-5 pt-5 pb-3 border-b border-border shrink-0">
          <SheetTitle className="text-sm font-semibold flex items-center gap-2">
            <Settings2 className="h-4 w-4 text-primary" />
            {isParallelGroup
              ? "Group"
              : isRoot
                ? "Edit Root Job"
                : parallelRole === "branch"
                  ? "Edit Group Job"
                  : parallelRole === "outcome"
                    ? "Edit Final Job"
                    : "Edit Child Job"}
          </SheetTitle>
          <SheetDescription className="text-[11px] text-muted-foreground">
            {isParallelGroup
              ? "TickerQ starts every grouped job whose condition matches, waits for their subtrees to settle, then evaluates the final jobs."
              : parallelRole === "branch"
                ? "This job belongs to the group. The group waits for it and its descendants to settle."
                : parallelRole === "outcome"
                  ? "This final job runs after the group settles when its aggregate condition matches."
              : "Configure the job's function, start rule, and retries."}
          </SheetDescription>
        </SheetHeader>
        <div className="flex-1 overflow-y-auto p-5 space-y-4">
          <div className="space-y-1.5">
            <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
              Function
            </Label>
            {isParallelGroup ? (
              <div className="rounded-md border border-status-info/30 bg-status-info/5 px-3 py-2 text-xs">
                <div className="font-medium text-foreground">Group</div>
                <div className="mt-1 text-[11px] leading-relaxed text-muted-foreground">
                  Each grouped job evaluates the job before the group, then matching jobs run together. Final jobs use the group's aggregate result.
                </div>
              </div>
            ) : functionNames.length > 0 ? (
              <Select
                value={node.functionName}
                onValueChange={(v) => {
                  const functionInfo = functions.find((item) => item.functionName === v);
                  const currentBook = draftBooks.current[node.id] ?? {
                    ...createDraftState(),
                    active: node.functionName,
                  };
                  const selection = selectFunction(
                    currentBook,
                    v,
                    node.requestJson,
                    () => initialPayloadForFunction(functionInfo),
                  );
                  draftBooks.current[node.id] = selection.state;
                  onUpdate({
                    functionName: v,
                    requestJson: selection.value,
                    requestValid: true,
                  });
                }}
              >
                <SelectTrigger aria-label="Function" className="h-9 w-full bg-surface-0 border-border text-xs">
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
                aria-label="Function"
                value={node.functionName}
                placeholder="FunctionName"
                onChange={(e) => onUpdate({ functionName: e.target.value })}
                className="h-9 bg-surface-0 border-border text-xs"
              />
            )}
          </div>

          {isRoot && !isParallelGroup && (
            <div className="space-y-1.5">
              <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">Chain deletion</Label>
              <Select value={node.deleteBehavior ?? "Cascade"} onValueChange={(value) => onUpdate({ deleteBehavior: value as ChainDeleteBehavior })}>
                <SelectTrigger aria-label="Chain deletion behavior" className="h-9 w-full bg-surface-0 border-border text-xs"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="Cascade">Cascade — delete the whole chain</SelectItem>
                  <SelectItem value="Restrict">Restrict — wait for children to finish</SelectItem>
                </SelectContent>
              </Select>
              <p className="text-[11px] leading-relaxed text-muted-foreground">Restrict waits until every descendant is Done, DueDone, Failed, Cancelled, or Skipped. The restriction lifts automatically; retention ages and active-job safety checks still apply.</p>
            </div>
          )}

          {!isRoot && !isParallelGroup && (
            <div className="space-y-1.5">
              <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
                Starts when
              </Label>
              <Select
                value={parallelRole === "branch"
                  ? baseRunCondition(node.runCondition)
                  : node.runCondition}
                onValueChange={(v) => {
                  const condition = v as BaseRunCondition;
                  onUpdate({
                    runCondition: parallelRole === "branch"
                      ? groupedRunCondition(condition)
                      : condition,
                  });
                }}
              >
                <SelectTrigger aria-label="Start rule" className="h-9 w-full bg-surface-0 border-border text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(parallelRole === "branch"
                    ? GROUP_CHILD_CONDITIONS
                    : parallelRole === "outcome"
                      ? PARALLEL_OUTCOME_CONDITIONS
                    : RUN_CONDITION_OPTIONS
                  ).map((opt) => (
                    <SelectItem key={opt} value={opt} className="text-xs">
                      <span className="font-medium">
                        {parallelRole && opt === "InProgress"
                          ? "When group starts"
                          : RUN_CONDITION_CONFIG[opt].label}
                      </span>
                      <code className="ml-1 font-mono text-[10px] text-muted-foreground">
                        · {opt}
                      </code>
                      <span className="text-muted-foreground ml-1">
                        — {parallelRole && opt === "InProgress"
                          ? "Keep this job inside the concurrent group"
                          : RUN_CONDITION_CONFIG[opt].description}
                      </span>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {parallelRole === "branch" && (
                <p className="text-[10px] leading-relaxed text-muted-foreground/70">
                  This job remains inside the group. Its condition is evaluated against the job immediately before the group.
                </p>
              )}
              {parallelRole === "outcome" && (
                <p className="text-[10px] leading-relaxed text-muted-foreground/70">
                  This final job remains after the group and evaluates the group's aggregate result.
                </p>
              )}
            </div>
          )}

          {!isParallelGroup && (
            <>
              <div className="space-y-1.5">
                <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
                  Description
                </Label>
                <Input
                  aria-label="Description"
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
                      aria-label="Retries"
                      type="number"
                      min={0}
                      max={100}
                      value={node.retries}
                      onChange={(e) => onUpdate({ retries: Number(e.target.value) })}
                      className="h-9 bg-surface-0 border-border text-xs"
                    />
                  </div>
                  <div className="space-y-1.5">
                    <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
                      Intervals (sec)
                    </Label>
                    <Input
                      aria-label="Retry intervals in seconds"
                      type="text"
                      value={node.retryIntervalsSeconds}
                      onChange={(e) => onUpdate({ retryIntervalsSeconds: e.target.value })}
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

              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1.5">
                  <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
                    If stale
                  </Label>
                  <Select
                    value={node.onStale}
                    onValueChange={(value) => onUpdate({ onStale: value as StaleAction })}
                  >
                    <SelectTrigger aria-label="Stale action" className="h-9 w-full bg-surface-0 border-border text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="Restart" className="text-xs">Restart</SelectItem>
                      <SelectItem value="Cancel" className="text-xs">Cancel</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-1.5">
                  <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
                    Timeout (sec)
                  </Label>
                  <Input
                    aria-label="Timeout in seconds"
                    type="number"
                    min={0}
                    step={1}
                    value={node.timeoutSeconds}
                    onChange={(event) => onUpdate({ timeoutSeconds: Number(event.target.value) })}
                    className="h-9 bg-surface-0 border-border text-xs"
                  />
                  <p className="text-[10px] text-muted-foreground/60">0 inherits the scheduler default</p>
                </div>
              </div>
            </>
          )}

          {isRoot && !recurring && (
            <div className="space-y-1.5">
              <Label className="text-[10px] text-muted-foreground uppercase tracking-wider">
                Schedule
              </Label>
              <div className="relative">
                <Input
                  aria-label="Schedule"
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

          {!isParallelGroup && (
            <>
              <RequestPayloadEditor
                key={`${node.id}:${node.functionName}`}
                functionInfo={functions.find((item) => item.functionName === node.functionName)}
                value={node.requestJson}
                onChange={(requestJson) => onUpdate({ requestJson })}
                onValidityChange={(requestValid) => onUpdate({ requestValid })}
              />
              <div className="rounded-lg border border-border/60 bg-surface-0/35 p-3 space-y-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-8 w-full text-xs"
                  disabled={testing || !node.functionName || !node.requestValid}
                  onClick={() => void runTest()}
                >
                  {testing ? <Loader2 className="mr-1.5 h-3 w-3 animate-spin" /> : <PlayCircle className="mr-1.5 h-3 w-3" />}
                  {testing ? "Running test…" : "Test this job"}
                </Button>
                <p className="text-[10px] leading-relaxed text-muted-foreground/70">
                  Runs once on this node without creating a ticker record. Your function still executes real application code and can cause side effects.
                </p>
                {testResult && (
                  <>
                    <div className={cn(
                      "rounded-md border px-2.5 py-2 text-[10px]",
                      testResult.success
                        ? "border-status-healthy/30 bg-status-healthy/5 text-status-healthy"
                        : "border-status-error/30 bg-status-error/5 text-status-error"
                    )}>
                      <div className="font-semibold">
                        {testResult.success ? "Passed" : testResult.status} · {testResult.elapsedMilliseconds} ms
                      </div>
                      {testResult.result && (
                        <div className="mt-2 space-y-1 text-muted-foreground">
                          <div>Result · {testResult.resultMediaType || "application/octet-stream"}</div>
                          <pre className="max-h-32 overflow-auto whitespace-pre-wrap break-all rounded border border-border/60 bg-surface-1/70 p-2 font-mono text-[10px] text-foreground">
                            {formatTestResult(testResult.result)}
                          </pre>
                        </div>
                      )}
                    </div>
                    <LogsPanel
                      functionName={node.functionName}
                      status={testResult.status}
                      tickerId={testResult.executionId}
                      exceptionMessage={testResult.error}
                      skippedReason={testResult.status === "Skipped" ? testResult.error : null}
                    />
                  </>
                )}
              </div>
            </>
          )}
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
              {isParallelGroup
                ? "Remove Group"
                : parallelRole === "branch"
                  ? "Remove Group Job"
                  : parallelRole === "outcome"
                    ? "Remove Final Job"
                    : "Remove Job"}
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
  const location = useLocation();
  const cronId = searchParams.get("cron") ?? (location.pathname.endsWith("/cron-tickers/new-chain") ? "new" : null);
  const recurring = cronId !== null;
  const existingCronId = cronId && cronId !== "new" ? cronId : null;
  const editId = recurring ? null : searchParams.get("edit");
  const draftId = recurring || editId ? null : searchParams.get("draft");
  const templateEditId = searchParams.get("editTemplate");
  const templateId = templateEditId ?? searchParams.get("template");
  const isEditingTemplate = !!templateEditId;
  const backPath = recurring ? (existingCronId ? `/cron-tickers/${existingCronId}` : "/cron-tickers") : "/time-tickers";
  const [expression, setExpression] = useState("0 9 * * *");
  const [skipIfAlreadyRunning, setSkipIfAlreadyRunning] = useState(false);
  const cronQuery = useCronTicker(existingCronId ?? "");
  const cronPayload = useQuery({
    queryKey: ["cron-tickers", existingCronId, "request"],
    queryFn: ({ signal }) => dashboardApi.getCronTickerRequest(existingCronId!, signal),
    enabled: !!existingCronId,
  });
  const addCronMutation = useAddCronTicker();
  const updateCronMutation = useUpdateCronTicker();
  const isEditing = !!editId;

  const { data: fns } = useAllFunctions();
  const functionNames = useMemo(
    () => (fns ?? []).map((f) => f.functionName),
    [fns]
  );
  const chainMutation = useAddTimeTickerChain();
  const replaceChainMutation = useReplaceTimeTickerChain();
  const templateQuery = useDefinitions("Template", recurring ? "CronTicker" : "TimeTicker");
  const draftQuery = useDefinition(draftId);
  const selectedTemplateQuery = useDefinition(templateId);
  const selectedTemplateIsValid = !selectedTemplateQuery.data || (
    selectedTemplateQuery.data.kind === "Template" &&
    selectedTemplateQuery.data.target === (recurring ? "CronTicker" : "TimeTicker")
  );
  const createDefinitionMutation = useCreateDefinition();
  const updateDefinitionMutation = useUpdateDefinition();
  const activateDraftMutation = useActivateDraft();

  // Edit-mode prefill: walk the existing chain (root + descendants) and fetch
  // each node's payload in parallel, then convert into the builder's editable
  // tree. Editing fails closed until every request payload succeeds; submit uses
  // the server-side atomic replace endpoint and never deletes the original first.
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
  const payloadsLoading = payloadQueries.some((q) => q.isLoading || q.isFetching);
  const payloadsFailed = payloadQueries.some(
    (q) => q.isError || (q.isSuccess && q.data == null)
  );
  const hydrationFailed = editChainQuery.isError || payloadsFailed ||
    (recurring && (cronQuery.isError || cronPayload.isError)) ||
    (!!draftId && draftQuery.isError) ||
    (!!templateId && (selectedTemplateQuery.isError || !selectedTemplateIsValid));
  const allLoaded =
    isEditing &&
    !!editTickers?.length &&
    !payloadsLoading &&
    !hydrationFailed &&
    payloadQueries.length === editTickers.length &&
    payloadQueries.every((q) => q.isSuccess && q.data != null);

  const [root, setRoot] = useState<ChainNode>(() => ({ ...makeInitialTree(), deleteBehavior: recurring ? "Restrict" : "Cascade" }));
  const [prefilled, setPrefilled] = useState(false);

  // Initialize the editable draft once when its complete server snapshot arrives.
  // Guarded render updates avoid an extra stale paint and never overwrite subsequent edits.
  if (isEditing && !prefilled && allLoaded && editId && editTickers) {
    const payloadMap = new Map<string, string>();
    editTickers.forEach((ticker, i) => {
      const raw = payloadQueries[i]?.data?.payload;
      if (raw) payloadMap.set(ticker.id, decodeRequestPayload(raw));
    });
    const tree = tickersToChainNode(editTickers, editId, payloadMap);
    if (tree) { setRoot(tree); setPrefilled(true); }
  }
  if (existingCronId && !prefilled && cronQuery.data && cronPayload.isSuccess) {
    setRoot(cronToChainNode(cronQuery.data, cronPayload.data?.payload ?? null));
    setExpression(cronQuery.data.expression);
    setSkipIfAlreadyRunning(cronQuery.data.skipIfAlreadyRunning ?? false);
    setPrefilled(true);
  }
  if (draftId && !prefilled && draftQuery.data && fns !== undefined) {
    const saved = draftQuery.data;
    const scheduledAt = saved.payload.executionTime
      ? toDatetimeLocal(saved.payload.executionTime)
      : nowAsDatetimeLocal();
    setRoot(normalizeRequestlessPayloads(
      requestToChainNode(saved.payload.root, scheduledAt),
      fns,
    ));
    setPrefilled(true);
  }
  if (templateId && !prefilled && selectedTemplateQuery.data && selectedTemplateIsValid && fns !== undefined) {
    const saved = selectedTemplateQuery.data;
    setRoot(normalizeRequestlessPayloads(
      requestToChainNode(
        saved.payload.root,
        recurring ? "" : (saved.payload.executionTime ? toDatetimeLocal(saved.payload.executionTime) : nowAsDatetimeLocal())
      ),
      fns,
    ));
    if (recurring) {
      setExpression(saved.payload.expression ?? expression);
      setSkipIfAlreadyRunning(saved.payload.skipIfAlreadyRunning ?? false);
    }
    setPrefilled(true);
  }
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [zoom, setZoom] = useState(1);
  const [isPanning, setIsPanning] = useState(false);
  const [panStart, setPanStart] = useState({ x: 0, y: 0 });
  const [showTemplates, setShowTemplates] = useState(false);
  const [saveKind, setSaveKind] = useState<SavedTickerDefinitionKind | null>(null);
  const [activateDraftOpen, setActivateDraftOpen] = useState(false);
  const [leavePromptOpen, setLeavePromptOpen] = useState(false);
  const [leaveAfterSave, setLeaveAfterSave] = useState(false);
  const [previousRoot, setPreviousRoot] = useState<ChainNode | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [testingChain, setTestingChain] = useState(false);
  const [chainTestResult, setChainTestResult] = useState<TestRunChainResponse | null>(null);
  const [chainTestOpen, setChainTestOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const executionId = chainTestResult?.rootExecutionId;
    if (!executionId || chainTestResult.completed) return;

    const controller = new AbortController();
    let timer: ReturnType<typeof window.setTimeout> | undefined;

    const poll = async () => {
      try {
        const next = await dashboardApi.getTestRunChain(executionId, controller.signal);
        if (controller.signal.aborted) return;
        setChainTestResult(next);
        if (next.completed) {
          setTestingChain(false);
          if (next.success) toast.success("Chain test completed");
          else toast.error("Chain test finished with errors", {
            description: next.error ?? undefined,
          });
          return;
        }
        timer = window.setTimeout(poll, 250);
      } catch (error) {
        if (controller.signal.aborted) return;
        setTestingChain(false);
        toast.error("Could not refresh the chain test", {
          description: error instanceof Error ? error.message : "Unknown error",
        });
      }
    };

    timer = window.setTimeout(poll, 150);
    return () => {
      controller.abort();
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [chainTestResult?.rootExecutionId, chainTestResult?.completed]);

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
    const parent = findParent(newRoot, id);
    node.active = false;
    node.functionName = "";
    node.runCondition = parent?.functionName === PARALLEL_GROUP_FUNCTION
      ? "GroupOnSuccess"
      : "OnSuccess";
    node.retries = 0;
    node.retryIntervalsSeconds = "";
    node.description = "";
    node.requestJson = "";
    node.onStale = "Restart";
    node.timeoutSeconds = 0;
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
    if (!parent || parent.children.length >= childLimit(parent)) return;
    const slot = makeSlot(false);
    if (parent.functionName === "$tickerq.parallel-group")
      slot.runCondition = "GroupOnSuccess";
    parent.children.push(slot);
    setRoot(newRoot);
  }
  function addParallelGroup(parentId: string) {
    const newRoot = cloneTree(root);
    const parent = findNode(newRoot, parentId);
    if (
      !parent ||
      parent.functionName === PARALLEL_GROUP_FUNCTION ||
      parent.children.length >= childLimit(parent) ||
      getNodeDepth(newRoot, parentId) >= MAX_DEPTH - 1
    ) return;

    const group = makeParallelGroup();
    parent.children.push(group);
    setRoot(newRoot);
    setSelectedId(group.id);
  }
  function addGroupChild(groupId: string, role: ParallelChildRole) {
    const newRoot = cloneTree(root);
    const group = findNode(newRoot, groupId);
    if (
      !group ||
      group.functionName !== PARALLEL_GROUP_FUNCTION ||
      group.children.length >= MAX_PARALLEL_ITEMS
    ) return;

    const slot = makeSlot(false);
    slot.runCondition = role === "branch" ? "GroupOnSuccess" : "OnSuccess";
    group.children.push(slot);
    setRoot(newRoot);
  }
  function updateNode(id: string, updates: Partial<ChainNode>) {
    setRoot((prev) => updateNodeInTree(prev, id, updates));
  }

  const handleWheel = useCallback((e: React.WheelEvent) => {
    e.preventDefault();
    setZoom((z) => Math.max(MIN_ZOOM, Math.min(2, z * (e.deltaY > 0 ? 0.92 : 1.08))));
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

  const fitCanvas = useCallback(() => {
    const element = containerRef.current;
    if (!element) return;
    const rect = element.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return;
    const layout = layoutChain(root);
    const canvasWidth = layout.width;
    const canvasHeight = layout.height;
    const scaleX = (rect.width - 60) / Math.max(1, canvasWidth);
    const scaleY = (rect.height - 60) / Math.max(1, canvasHeight);
    const scale = Math.max(MIN_ZOOM, Math.min(scaleX, scaleY, 1));
    setZoom(scale);
    setPan({
      x: (rect.width - canvasWidth * scale) / 2,
      y: Math.max(24, (rect.height - canvasHeight * scale) / 2),
    });
  }, [root]);

  useEffect(() => {
    if ((isEditing || existingCronId || draftId || templateId) && !prefilled) return;
    const element = containerRef.current;
    if (!element) return;
    fitCanvas();
    const observer = new ResizeObserver(fitCanvas);
    observer.observe(element);
    return () => observer.disconnect();
    // Fit the complete initial draft, then only on viewport changes; preserve user zoom during edits.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [prefilled, isEditing, existingCronId, draftId, templateId]);

  function validateTree(n: ChainNode): string | null {
    if (n.active) {
      if (!n.functionName)
        return "Every active job needs a function selected";
      if (!n.requestValid)
        return `Complete or correct the structured JSON fields for "${n.functionName}"`;
      if (
        n.retryIntervalsSeconds.trim().length > 0 &&
        parseIntervalsList(n.retryIntervalsSeconds) === null
      ) {
        return `Retry intervals for "${n.functionName}" must be comma-separated whole numbers between 1 and 3600`;
      }
      if (!Number.isInteger(n.timeoutSeconds) || n.timeoutSeconds < 0) {
        return `Timeout for "${n.functionName}" must be a whole number of seconds (0 or greater)`;
      }
      const requestError = validateRequestPayload(
        (fns ?? []).find((item) => item.functionName === n.functionName),
        n.requestJson
      );
      if (requestError) return `${n.functionName}: ${requestError}`;
    }
    for (const c of n.children) {
      const err = validateTree(c);
      if (err) return err;
    }
    return null;
  }

  function definitionPayload(executionTimeOverride?: string): SavedDefinitionPayload {
    const scheduledAt = executionTimeOverride === undefined
      ? root.scheduledAt
      : executionTimeOverride;
    return {
      executionTime: recurring
        ? null
        : scheduledAt
          ? new Date(scheduledAt).toISOString()
          : null,
      expression: recurring ? expression.trim() : null,
      skipIfAlreadyRunning: recurring ? skipIfAlreadyRunning : null,
      root: chainNodeToRequest(root),
    };
  }

  function validateDefinitionCanvas() {
    const error = validateParallelGroups(root) ?? validateTree(root);
    if (error) {
      toast.error(error);
      return false;
    }
    if (recurring && !expression.trim()) {
      toast.error("Set a cron expression.");
      return false;
    }
    return true;
  }

  async function saveDefinition(kind: SavedTickerDefinitionKind, values: SaveDefinitionValues) {
    if (kind === "Template" && countActive(root) <= 1) {
      toast.error("Templates are available for chains with at least two jobs.");
      setSaveKind(null);
      return null;
    }
    if (!validateDefinitionCanvas()) return null;
    const target = recurring ? "CronTicker" as const : "TimeTicker" as const;
    const request = {
      kind,
      target,
      name: values.name,
      description: values.description || null,
      includeRequestData: values.includeRequestData,
      payload: kind === "Template"
        ? { ...definitionPayload(), executionTime: null }
        : definitionPayload(values.executionTime),
    };
    try {
      const saved = kind === "Draft" && draftQuery.data
        ? await updateDefinitionMutation.mutateAsync({
            id: draftQuery.data.id,
            body: { ...request, version: draftQuery.data.version },
          })
        : kind === "Template" && isEditingTemplate && selectedTemplateQuery.data
          ? await updateDefinitionMutation.mutateAsync({
              id: selectedTemplateQuery.data.id,
              body: { ...request, version: selectedTemplateQuery.data.version },
            })
          : await createDefinitionMutation.mutateAsync(request);
      if (kind === "Draft" && values.executionTime !== root.scheduledAt) {
        setRoot(current => ({ ...current, scheduledAt: values.executionTime }));
      }
      toast.success(kind === "Draft"
        ? "Draft saved"
        : isEditingTemplate
          ? "Template updated"
          : "Template saved");
      setSaveKind(null);
      if (leaveAfterSave) {
        setLeaveAfterSave(false);
        navigate(backPath);
      } else if (kind === "Draft" && !draftId) {
        navigate(`${location.pathname}?draft=${saved.id}`, { replace: true });
      }
      return saved;
    } catch (error) {
      toast.error(kind === "Draft" ? "Could not save the draft" : "Could not save the template", {
        description: error instanceof Error ? error.message : "Unknown error",
      });
      return null;
    }
  }

  function requestDraftActivation() {
    if (!draftQuery.data || draftQuery.data.activatedRootTickerId) return;
    if (!validateDefinitionCanvas()) return;
    setActivateDraftOpen(true);
  }

  async function handleActivateDraft(executionTime: string | null) {
    if (!draftQuery.data || draftQuery.data.activatedRootTickerId) return;
    if (!validateDefinitionCanvas()) return;
    setSubmitting(true);
    try {
      const localExecutionTime = executionTime ?? "";
      const updated = await updateDefinitionMutation.mutateAsync({
        id: draftQuery.data.id,
        body: {
          kind: "Draft",
          target: "TimeTicker",
          name: draftQuery.data.name,
          description: draftQuery.data.description,
          includeRequestData: draftQuery.data.includesRequestData,
          payload: definitionPayload(localExecutionTime),
          version: draftQuery.data.version,
        },
      });
      const result = await activateDraftMutation.mutateAsync({
        id: updated.id,
        version: updated.version,
        executionTime: localExecutionTime
          ? new Date(localExecutionTime).toISOString()
          : new Date().toISOString(),
      });
      setActivateDraftOpen(false);
      toast.success("Draft activated and scheduled");
      navigate(`/time-tickers?ticker=${result.rootId}`);
    } catch (error) {
      toast.error("Could not activate the draft", {
        description: error instanceof Error ? error.message : "Unknown error",
      });
    } finally {
      setSubmitting(false);
    }
  }

  function handleBack() {
    if (isEditing || existingCronId || !root.functionName) {
      navigate(backPath);
      return;
    }
    setLeavePromptOpen(true);
  }


  async function handleSubmit() {
    if (isEditing && (!prefilled || hydrationFailed || !allLoaded)) {
      toast.error("The complete original chain has not loaded. Retry before replacing it.");
      return;
    }
    const validationError = validateParallelGroups(root) ?? validateTree(root);
    if (validationError) {
      toast.error(validationError);
      return;
    }
    if (recurring && !expression.trim()) {
      toast.error("Set a cron expression.");
      return;
    }
    if (existingCronId && (!prefilled || hydrationFailed)) return;
    setSubmitting(true);
    try {
      if (recurring) {
        const node = chainNodeToRequest(root);
        const hasActiveChildren = root.children.some(child => child.active);
        const body = { function: node.function, description: node.description, expression,
          request: node.request ?? "", retries: node.retries ?? 0,
          retryIntervalsSeconds: node.retryIntervalsSeconds ?? [], onStale: node.onStale,
          timeoutSeconds: node.timeoutSeconds ?? 0, skipIfAlreadyRunning,
          chain: hasActiveChildren ? chainNodeToCronTemplate(root) : null };
        if (existingCronId) {
          await updateCronMutation.mutateAsync({ id: existingCronId, body: { ...body, replaceChain: true } });
          navigate(`/cron-tickers/${existingCronId}`);
        } else {
          const created = await addCronMutation.mutateAsync(body);
          navigate(`/cron-tickers/${created.id}`);
        }
        toast.success(hasActiveChildren
          ? "Recurring chain saved. Each future run creates a new chain."
          : "Recurring job saved.");
        return;
      }
      const executionTime = root.scheduledAt
        ? new Date(root.scheduledAt).toISOString()
        : new Date().toISOString();
      const body = {
        executionTime,
        root: chainNodeToRequest(root),
      };
      const result = isEditing && editId
        ? await replaceChainMutation.mutateAsync({ rootId: editId, body })
        : await chainMutation.mutateAsync(body);
      const jobLabel = result.createdCount === 1 ? "job" : "jobs";
      toast.success(
        isEditing
          ? `Chain replaced — ${result.createdCount} ${jobLabel} scheduled`
          : `Chain created — ${result.createdCount} ${jobLabel} scheduled`
      );
      navigate(backPath);
    } catch (err) {
      toast.error(isEditing ? "Failed to replace chain" : "Failed to create chain", {
        description: err instanceof Error ? err.message : "Unknown error",
      });
    } finally {
      setSubmitting(false);
    }
  }

  async function handleTestChain() {
    if (testingChain && chainTestResult) {
      setChainTestOpen(true);
      return;
    }

    const validationError = validateParallelGroups(root) ?? validateTree(root);
    if (validationError) {
      toast.error(validationError);
      return;
    }
    setTestingChain(true);
    setChainTestResult(null);
    setChainTestOpen(true);
    try {
      const result = await dashboardApi.testRunChain({
        executionTime: new Date().toISOString(),
        root: chainNodeToRequest(root),
      });
      setChainTestResult(result);
      if (result.completed) {
        setTestingChain(false);
        if (result.success) toast.success("Chain test completed");
        else toast.error("Chain test finished with errors", {
          description: result.error ?? undefined,
        });
      }
    } catch (error) {
      setTestingChain(false);
      setChainTestOpen(false);
      toast.error("Chain test failed", {
        description: error instanceof Error ? error.message : "Unknown error",
      });
    }
  }

  const layout = layoutChain(root);
  const { positions, edges, merges, stages } = layout;
  const maxX = layout.width + 60;
  const maxY = layout.height + 100;
  const nodeMap = new Map<string, ChainNode>();
  function collectNodes(n: ChainNode) {
    nodeMap.set(n.id, n);
    n.children.forEach(collectNodes);
  }
  collectNodes(root);

  const totalActive = countActive(root);
  const selectedNode = selectedId ? findNode(root, selectedId) : null;
  const selectedParent = selectedId ? findParent(root, selectedId) : null;
  const selectedParallelRole = selectedNode
    ? parallelChildRole(selectedParent, selectedNode)
    : null;
  const isSelectedRoot = selectedId === root.id;

  return (
    <div className="relative flex h-[calc(100dvh-44px)] flex-col -m-4 sm:-m-6">
      {(isEditing || !!existingCronId || !!draftId || !!templateId) && !prefilled && (
        <div className="absolute inset-0 z-50 bg-background/60 backdrop-blur-[1px] flex items-center justify-center">
          {hydrationFailed ? (
            <div role="alert" className="max-w-sm rounded-lg border border-destructive/40 bg-background p-4 text-center shadow-lg">
              <ShieldAlert className="mx-auto mb-2 h-5 w-5 text-destructive" />
              <p className="text-sm font-medium">The complete chain payload could not be loaded.</p>
              <p className="mt-1 text-xs text-muted-foreground">Replacement is blocked so the original data cannot be erased.</p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="mt-3"
                onClick={() => {
                  void editChainQuery.refetch();
                  void cronQuery.refetch();
                  void cronPayload.refetch();
                  void draftQuery.refetch();
                  void selectedTemplateQuery.refetch();
                  payloadQueries.forEach((query) => void query.refetch());
                }}
              >
                Retry loading
              </Button>
            </div>
          ) : (
            <>
              <Loader2 className="h-4 w-4 animate-spin text-primary mr-2" />
              <span className="text-sm text-muted-foreground">Loading complete chain…</span>
            </>
          )}
        </div>
      )}
      {/* Toolbar */}
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 border-b border-border bg-surface-0/50 shrink-0">
        <div className="flex items-center gap-3">
          <button
            onClick={handleBack}
            className="shrink-0 whitespace-nowrap text-xs text-muted-foreground hover:text-foreground transition-colors"
          >
            ← Back to list
          </button>
          <div className="h-4 w-px bg-border" />
          <GitBranch className="h-3.5 w-3.5 text-primary" />
          <span className="font-mono text-sm font-semibold">
            {isEditingTemplate
              ? (recurring ? "Edit Recurring Template" : "Edit Time-Ticker Template")
              : recurring
              ? (existingCronId ? "Edit Recurring Chain" : "New Recurring Chain")
              : isEditing
                ? "Edit Chain Job"
                : draftId
                  ? "Edit Draft"
                  : "New Chain Job"}
          </span>
          {(isEditing || isEditingTemplate) && (
            <button
              type="button"
              onClick={handleBack}
              className="text-[11px] text-muted-foreground hover:text-foreground transition-colors"
            >
              Cancel edit
            </button>
          )}
        </div>
        <div className="flex w-full flex-wrap items-center gap-2 text-[11px] text-muted-foreground lg:w-auto">
          <span className="tabular-nums">
            {totalActive} {totalActive === 1 ? "job" : "jobs"} configured
          </span>
          {!isEditingTemplate && <Button
            variant="outline"
            size="sm"
            className="h-7 text-xs border-border text-muted-foreground"
            onClick={() => setShowTemplates(true)}
          >
            <LayoutTemplate className="h-3 w-3 mr-1.5" /> Use Templates
          </Button>}
          {totalActive > 1 && !isEditingTemplate && (
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs"
              onClick={() => setSaveKind("Template")}
              disabled={!root.functionName || submitting || ((isEditing || !!existingCronId || !!draftId || !!templateId) && !prefilled)}
            >
              <LayoutTemplate className="h-3 w-3 mr-1.5" /> Save template
            </Button>
          )}
          {!recurring && !isEditing && !isEditingTemplate && (
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs"
              onClick={() => setSaveKind("Draft")}
              disabled={!root.functionName || submitting || (!!draftId && !prefilled)}
            >
              <FilePenLine className="h-3 w-3 mr-1.5" /> Save draft
            </Button>
          )}
          <div className="flex items-center gap-1">
            <button
              onClick={fitCanvas}
              className="p-1 rounded bg-surface-2 hover:bg-surface-3 transition-colors"
              title="Fit the complete chain to the canvas"
              aria-label="Fit chain to view"
            >
              <Maximize2 className="h-3 w-3" />
            </button>
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
              onClick={() => setZoom((z) => Math.max(MIN_ZOOM, z - 0.1))}
              className="px-1.5 py-0.5 rounded bg-surface-2 hover:bg-surface-3 transition-colors"
            >
              −
            </button>
          </div>
          <Button
            variant="outline"
            size="sm"
            className="h-7 text-xs"
            onClick={() => void handleTestChain()}
            disabled={!root.functionName || submitting || ((isEditing || !!existingCronId || !!draftId || !!templateId) && !prefilled) || hydrationFailed}
          >
            {testingChain ? (
              <Loader2 className="h-3 w-3 mr-1.5 animate-spin" />
            ) : (
              <FlaskConical className="h-3 w-3 mr-1.5" />
            )}
            {testingChain ? "View test run" : "Test chain"}
          </Button>
          <Button
            type="button"
            variant="gradient"
            size="sm"
            className="h-7 text-xs"
            onClick={() => isEditingTemplate
              ? setSaveKind("Template")
              : draftId
                ? requestDraftActivation()
                : void handleSubmit()}
            disabled={!root.functionName || submitting || testingChain || updateDefinitionMutation.isPending || (isEditingTemplate && totalActive <= 1) || ((isEditing || !!existingCronId || !!draftId || !!templateId) && !prefilled) || hydrationFailed || !!draftQuery.data?.activatedRootTickerId}
          >
            {submitting || (isEditingTemplate && updateDefinitionMutation.isPending) ? (
              <>
                <Loader2 className="h-3 w-3 mr-1.5 animate-spin" />
                {isEditingTemplate ? "Saving…" : draftId ? "Activating…" : isEditing ? "Replacing…" : "Creating…"}
              </>
            ) : isEditingTemplate ? (
              totalActive > 1 ? "Save template changes" : "Add a child to save"
            ) : draftId ? (
              draftQuery.data?.activatedRootTickerId ? "Already activated" : "Review activation"
            ) : isEditing ? (
              "Replace Chain"
            ) : (
              recurring ? "Save recurring chain" : "Create Chain"
            )}
          </Button>
        </div>
      </div>

      <ChainTestResultsDialog
        open={chainTestOpen}
        result={chainTestResult}
        onClose={() => setChainTestOpen(false)}
      />

      <ActivateDraftDialog
        open={activateDraftOpen}
        draftName={draftQuery.data?.name}
        initialExecutionTime={root.scheduledAt}
        pending={submitting || activateDraftMutation.isPending || updateDefinitionMutation.isPending}
        onOpenChange={setActivateDraftOpen}
        onActivate={handleActivateDraft}
      />

      <SaveDefinitionDialog
        open={saveKind !== null}
        kind={saveKind ?? "Template"}
        editing={saveKind === "Template" && isEditingTemplate}
        initialName={saveKind === "Draft"
          ? (draftQuery.data?.name ?? "")
          : isEditingTemplate
            ? (selectedTemplateQuery.data?.name ?? "")
            : ""}
        initialDescription={saveKind === "Draft"
          ? (draftQuery.data?.description ?? "")
          : isEditingTemplate
            ? (selectedTemplateQuery.data?.description ?? "")
            : ""}
        initialIncludeRequestData={saveKind === "Draft"
          ? (draftQuery.data?.includesRequestData ?? true)
          : isEditingTemplate
            ? (selectedTemplateQuery.data?.includesRequestData ?? true)
            : true}
        initialExecutionTime={saveKind === "Draft" ? root.scheduledAt : ""}
        pending={createDefinitionMutation.isPending || updateDefinitionMutation.isPending}
        validateName={saveKind === "Template" ? name =>
          findDuplicateTemplateName(
            templateQuery.data ?? [],
            name,
            isEditingTemplate ? selectedTemplateQuery.data?.id : undefined,
          )
            ? "A template with this name already exists."
            : null
        : undefined}
        onOpenChange={open => {
          if (!open) {
            setSaveKind(null);
            setLeaveAfterSave(false);
          }
        }}
        onSave={values => saveDefinition(saveKind ?? "Template", values)}
      />

      <Dialog open={leavePromptOpen} onOpenChange={setLeavePromptOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Keep this work?</DialogTitle>
            <DialogDescription>
              Save the configuration for later, or discard it and return to the list. No ticker has been scheduled yet.
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-2 py-1">
            {!recurring && !isEditingTemplate && (
              <Button
                variant="outline"
                className="h-auto justify-start px-3 py-2.5 text-left"
                onClick={() => {
                  setLeavePromptOpen(false);
                  setLeaveAfterSave(true);
                  setSaveKind("Draft");
                }}
              >
                <FilePenLine className="mr-2 h-4 w-4 text-primary" />
                <span><span className="block text-xs font-medium">Save as draft</span><span className="block text-[11px] font-normal text-muted-foreground">Continue editing and activate it later.</span></span>
              </Button>
            )}
            {totalActive > 1 && <Button
              variant="outline"
              className="h-auto justify-start px-3 py-2.5 text-left"
              onClick={() => {
                setLeavePromptOpen(false);
                setLeaveAfterSave(true);
                setSaveKind("Template");
              }}
            >
              <LayoutTemplate className="mr-2 h-4 w-4 text-primary" />
              <span>
                <span className="block text-xs font-medium">{isEditingTemplate ? "Save template changes" : "Save as template"}</span>
                <span className="block text-[11px] font-normal text-muted-foreground">
                  {isEditingTemplate ? "Update this reusable workflow." : "Reuse the structure for a future schedule."}
                </span>
              </span>
            </Button>}
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setLeavePromptOpen(false)}>Keep editing</Button>
            <Button variant="destructive" onClick={() => navigate(backPath)}>Discard</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {recurring && (
        <section aria-label="Recurring chain settings" className="shrink-0 border-b border-border bg-card px-4 py-2.5">
          <div className="grid items-start gap-3 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
            <div className="min-w-0 space-y-1.5">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <Label htmlFor="chain-cron-expression" className="text-[11px] text-muted-foreground">Cron schedule</Label>
                <CronSchedulePresets onSelect={setExpression} />
              </div>
              <Input id="chain-cron-expression" value={expression} onChange={e => setExpression(e.target.value)} className="h-7 w-full font-mono text-xs" />
              <CronExpressionPreview expression={expression} stacked />
            </div>
            <div className="min-w-0">
              <CronOverlapField checked={skipIfAlreadyRunning} onChange={setSkipIfAlreadyRunning} isChain compact />
            </div>
          </div>
          <p className="mt-2 flex items-start gap-1.5 border-t border-border/50 pt-2 text-[11px] leading-4 text-muted-foreground">
            <GitBranch aria-hidden="true" className="mt-0.5 h-3 w-3 shrink-0 text-primary/70" />
            Each run creates a new chain. Edits affect future runs; existing chains keep their steps.
          </p>
        </section>
      )}
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
            Click a dashed slot to activate it · Use + group below any job, add grouped jobs, then add final jobs based on the aggregate result · Max{" "}
            {MAX_DEPTH} levels, {MAX_CHILDREN} children per job · up to {MAX_PARALLEL_ITEMS} group jobs
            {" · "}Paths show direct transitions; pills show each job's start rule; cyan marks group synchronization
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
            {/* The coordinator card connects to each job that starts with the
                group. Synchronization is drawn once before conditional jobs. */}
            {[...stages]
              .sort((left, right) =>
                right.bounds.width * right.bounds.height -
                left.bounds.width * left.bounds.height)
              .map(stage => {
              const branchRoots = stage.branchRootIds
                .map(id => positions.find(position => position.id === id))
                .filter((position): position is NonNullable<typeof position> => !!position)
                .sort((left, right) => left.x - right.x);
              const groupPosition = positions.find(position => position.id === stage.groupId);
              const groupBottom = groupPosition ? groupPosition.y + NODE_H : 0;
              const branchTop = branchRoots.length > 0
                ? Math.min(...branchRoots.map(branch => branch.y))
                : 0;
              const forkY = (groupBottom + branchTop) / 2;
              const branchCenters = branchRoots.map(branch => branch.x + NODE_W / 2);
              const forkColor = JOIN_PATH_COLOR;
              return (
                <g key={`stage-frame-${stage.groupId}`}>
                  <rect
                    x={stage.bounds.x}
                    y={stage.bounds.y}
                    width={stage.bounds.width}
                    height={stage.bounds.height}
                    rx={14}
                    fill="hsl(var(--workflow-sync) / 0.035)"
                    stroke="hsl(var(--workflow-sync) / 0.28)"
                    strokeWidth={1.5}
                  />
                  <line
                    x1={stage.bounds.x}
                    x2={stage.bounds.x + stage.bounds.width}
                    y1={groupBottom}
                    y2={groupBottom}
                    stroke="hsl(var(--workflow-sync) / 0.22)"
                  />
                  {groupPosition && branchCenters.length > 0 && (
                    <g opacity={0.68}>
                      <path
                        d={`M ${groupPosition.x + NODE_W / 2} ${groupBottom} L ${groupPosition.x + NODE_W / 2} ${forkY} M ${Math.min(...branchCenters)} ${forkY} L ${Math.max(...branchCenters)} ${forkY}`}
                        fill="none"
                        stroke={forkColor}
                        strokeWidth={2}
                      />
                      {branchRoots.map(branch => {
                        const branchNode = nodeMap.get(branch.id);
                        const targetX = branch.x + NODE_W / 2;
                        return (
                          <g
                            key={`parallel-fork-${stage.groupId}-${branch.id}`}
                            opacity={branchNode?.active ? 1 : 0.38}
                          >
                            <line
                              x1={targetX}
                              x2={targetX}
                              y1={forkY}
                              y2={branch.y}
                              stroke={forkColor}
                              strokeWidth={branchNode?.active ? 2 : 1.5}
                              strokeDasharray={branchNode?.active ? undefined : "3 4"}
                            />
                            <polygon
                              points={`${targetX - 3},${branch.y - 5} ${targetX + 3},${branch.y - 5} ${targetX},${branch.y}`}
                              fill={forkColor}
                            />
                          </g>
                        );
                      })}
                    </g>
                  )}
                  {stage.outcomeBounds && (
                    <g>
                      <rect
                        x={stage.outcomeBounds.x}
                        y={stage.outcomeBounds.y}
                        width={stage.outcomeBounds.width}
                        height={stage.outcomeBounds.height}
                        rx={12}
                        fill="hsl(var(--surface-1) / 0.42)"
                        stroke="hsl(var(--border) / 0.72)"
                      />
                      <text
                        x={stage.outcomeBounds.x + 12}
                        y={stage.outcomeBounds.y + 16}
                        fontSize={7}
                        fontWeight={650}
                        letterSpacing={0.8}
                        fill="hsl(var(--muted-foreground) / 0.8)"
                      >
                        AFTER GROUP SETTLES
                      </text>
                    </g>
                  )}
                </g>
              );
            })}
            {edges.map((edge) => {
                const parent = positions.find(position => position.id === edge.fromId);
                const child = positions.find(position => position.id === edge.toId);
                if (!parent) return null;
                if (!child) return null;
                const childNode = nodeMap.get(edge.toId);
                const parentNode = nodeMap.get(parent.id);
                if (parentNode?.functionName === PARALLEL_GROUP_FUNCTION) return null;
                const isActive = childNode?.active;
                const color = isActive
                  ? runConditionHsl(childNode?.runCondition)
                  : CONNECTOR_COLOR;
                const parentH = parentNode?.active ? NODE_H : SLOT_H;
                const x1 = parent.x + NODE_W / 2;
                const y1 = parent.y + parentH;
                const x2 = child.x + NODE_W / 2;
                const y2 = child.y;
                const midY = (y1 + y2) / 2;
                return (
                  <g key={`edge-${edge.fromId}-${edge.toId}`} opacity={isActive ? 0.7 : 0.25}>
                    <path
                      d={`M ${x1} ${y1} C ${x1} ${midY}, ${x2} ${midY}, ${x2} ${y2}`}
                      fill="none"
                      stroke={color}
                      strokeWidth={isActive ? 2 : 1.5}
                      strokeDasharray={isActive ? undefined : "3 4"}
                    />
                    <polygon
                      points={`${x2 - 3},${y2 - 5} ${x2 + 3},${y2 - 5} ${x2},${y2}`}
                      fill={color}
                    />
                  </g>
                );
              })}
            {merges.map((merge, mergeIndex) => {
              const stage = stages.find(item => item.groupId === merge.groupId);
              if (!stage || merge.sourceIds.length === 0) return null;
              const targets = merge.targetIds
                .map(id => {
                  const position = positions.find(item => item.id === id);
                  const node = nodeMap.get(id);
                  return position && node ? { id, position, node } : null;
                })
                .filter((target): target is NonNullable<typeof target> => !!target);
              const footerHeight = 20;
              const stageBottom = stage.bounds.y + stage.bounds.height;
              const footerY = stageBottom - footerHeight;
              const outcomeBranchY = targets.length > 0
                ? Math.min(...targets.map(target => target.position.y)) - 24
                : null;
              const targetCenters = targets.map(target => target.position.x + NODE_W / 2);
              return (
                <g key={`merge-${mergeIndex}`}>
                  <rect
                    x={stage.bounds.x + 1}
                    y={footerY}
                    width={stage.bounds.width - 2}
                    height={footerHeight - 1}
                    fill="hsl(var(--workflow-sync) / 0.07)"
                  />
                  <line
                    x1={stage.bounds.x + 1}
                    x2={stage.bounds.x + stage.bounds.width - 1}
                    y1={footerY}
                    y2={footerY}
                    stroke={JOIN_PATH_COLOR}
                    strokeWidth={1.5}
                  />
                  <text
                    x={stage.bounds.x + 14}
                    y={footerY + 14}
                    fontSize={8}
                    fontWeight={650}
                    letterSpacing={0.65}
                    fill={JOIN_PATH_COLOR}
                  >
                    ALL BRANCHES
                  </text>
                  <text
                    x={stage.bounds.x + stage.bounds.width - 14}
                    y={footerY + 14}
                    textAnchor="end"
                    fontSize={7}
                    fontWeight={500}
                    letterSpacing={0.25}
                    fill="hsl(var(--muted-foreground) / 0.72)"
                  >
                    WAIT FOR EVERY BRANCH
                  </text>
                  {outcomeBranchY !== null && (
                    <>
                      <path
                        d={`M ${merge.anchorX} ${stageBottom} L ${merge.anchorX} ${outcomeBranchY}`}
                        fill="none"
                        stroke={JOIN_PATH_COLOR}
                        strokeWidth={2}
                      />
                      <line
                        x1={Math.min(...targetCenters)}
                        x2={Math.max(...targetCenters)}
                        y1={outcomeBranchY}
                        y2={outcomeBranchY}
                        stroke={JOIN_PATH_COLOR}
                        strokeWidth={2}
                      />
                    </>
                  )}
                  {outcomeBranchY !== null && targets.map(({ id, position, node }) => {
                    const color = node.active
                      ? runConditionHsl(node.runCondition)
                      : CONNECTOR_COLOR;
                    const targetX = position.x + NODE_W / 2;
                    const targetY = position.y;
                    return (
                      <g
                        key={`outcome-edge-${merge.groupId}-${id}`}
                        opacity={node.active ? 0.7 : 0.25}
                      >
                        <path
                          d={`M ${targetX} ${outcomeBranchY} L ${targetX} ${targetY}`}
                          fill="none"
                          stroke={color}
                          strokeWidth={node.active ? 2 : 1.5}
                          strokeDasharray={node.active ? undefined : "3 4"}
                        />
                        <polygon
                          points={`${targetX - 3},${targetY - 5} ${targetX + 3},${targetY - 5} ${targetX},${targetY}`}
                          fill={color}
                        />
                        <circle
                          cx={targetX}
                          cy={outcomeBranchY}
                          r={2.5}
                          fill={JOIN_PATH_COLOR}
                        />
                      </g>
                    );
                  })}
                </g>
              );
            })}
          </svg>

          {/* Nodes */}
          {positions.map((pos) => {
            const node = nodeMap.get(pos.id);
            if (!node) return null;
            const parent = findParent(root, pos.id);
            const parallelRole = parallelChildRole(parent, node);
            const depth = getNodeDepth(root, pos.id);
            const hasChildCapacity = node.children.length < childLimit(node);
            const canAddDescendant = depth < MAX_DEPTH;
            const canAddParallelStage =
              node.functionName !== PARALLEL_GROUP_FUNCTION &&
              hasChildCapacity &&
              depth < MAX_DEPTH - 1;
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
                      groupRole={parallelRole}
                      isSelected={selectedId === pos.id}
                      onClick={() =>
                        setSelectedId(pos.id === selectedId ? null : pos.id)
                      }
                    />
                    {node.functionName === PARALLEL_GROUP_FUNCTION ? (
                      hasChildCapacity && canAddDescendant && (
                        <div className="mt-1 flex items-center gap-1 rounded-md border border-workflow-sync/20 bg-surface-0/85 p-0.5">
                          <button
                            type="button"
                            onClick={(event) => {
                              event.stopPropagation();
                              addGroupChild(pos.id, "branch");
                            }}
                            className="rounded px-1.5 py-0.5 text-[9px] font-medium text-workflow-sync/75 transition-colors hover:bg-workflow-sync/10 hover:text-workflow-sync"
                            title="Add a job that starts with the other jobs in this group"
                          >
                            + grouped job
                          </button>
                          <span className="h-3 w-px bg-workflow-sync/20" />
                          <button
                            type="button"
                            onClick={(event) => {
                              event.stopPropagation();
                              addGroupChild(pos.id, "outcome");
                            }}
                            className="rounded px-1.5 py-0.5 text-[9px] font-medium text-muted-foreground transition-colors hover:bg-primary/10 hover:text-primary"
                            title="Add a final job evaluated after every grouped job settles"
                          >
                            + final job
                          </button>
                        </div>
                      )
                    ) : (
                      hasChildCapacity && canAddDescendant && (
                        <div className="mt-1 flex items-center gap-1 rounded-md border border-border/60 bg-surface-0/90 p-0.5 text-[9px] shadow-sm">
                          <button
                            type="button"
                            onClick={(event) => {
                              event.stopPropagation();
                              addSlot(pos.id);
                            }}
                            className="inline-flex items-center gap-1 rounded px-1.5 py-0.5 font-medium text-muted-foreground transition-colors hover:bg-primary/10 hover:text-primary"
                            title="Add a normal child job"
                          >
                            <Plus className="h-2.5 w-2.5" /> child job
                          </button>
                          {canAddParallelStage && (
                            <>
                              <span className="h-3 w-px bg-border/70" />
                              <button
                                type="button"
                                onClick={(event) => {
                                  event.stopPropagation();
                                  addParallelGroup(pos.id);
                                }}
                                className="inline-flex items-center gap-1 rounded px-1.5 py-0.5 font-medium text-workflow-sync/80 transition-colors hover:bg-workflow-sync/10 hover:text-workflow-sync"
                                title="Add a durable group with a synchronization point"
                              >
                                <GitFork className="h-2.5 w-2.5" /> group
                              </button>
                            </>
                          )}
                        </div>
                      )
                    )}
                  </div>
                ) : (
                  <SkeletonSlot
                    onClick={() => activateSlot(pos.id)}
                    label={parallelRole === "branch"
                      ? "Add grouped job"
                      : parallelRole === "outcome"
                        ? "Add final job"
                        : "Add job here"}
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
          recurring={recurring}
          parallelRole={selectedParallelRole}
          functionNames={functionNames}
          functions={fns ?? []}
          onUpdate={(updates) => updateNode(selectedId!, updates)}
          onDeactivate={
            isSelectedRoot
              ? undefined
              : selectedNode.functionName === PARALLEL_GROUP_FUNCTION || selectedParallelRole
                ? () => removeSlot(selectedId!)
                : () => deactivateNode(selectedId!)
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
            {(templateQuery.data?.length ?? 0) > 0 && (
              <div className="pb-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground/70">
                Your templates
              </div>
            )}
            {templateQuery.data?.map(template => (
              <button
                key={template.id}
                onClick={() => {
                  setPreviousRoot(cloneTree(root));
                  const scheduledAt = template.payload.executionTime
                    ? toDatetimeLocal(template.payload.executionTime)
                    : nowAsDatetimeLocal();
                  setRoot(requestToChainNode(template.payload.root, recurring ? "" : scheduledAt));
                  if (recurring) {
                    setExpression(template.payload.expression ?? expression);
                    setSkipIfAlreadyRunning(template.payload.skipIfAlreadyRunning ?? false);
                  }
                  setSelectedId(null);
                  setShowTemplates(false);
                }}
                className="group w-full rounded-xl border border-primary/20 bg-primary/[0.035] p-4 text-left transition-all duration-200 hover:border-primary/50"
              >
                <div className="mb-2 flex items-center gap-3">
                  <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary/10 text-primary">
                    <LayoutTemplate className="h-4 w-4" />
                  </div>
                  <div className="min-w-0">
                    <div className="truncate text-xs font-semibold text-foreground">{template.name}</div>
                    <div className="mt-0.5 text-[9px] uppercase tracking-wider text-primary/70">Saved template</div>
                  </div>
                </div>
                <p className="text-[11px] leading-relaxed text-muted-foreground">
                  {template.description || `${template.payload.root.function} workflow`}
                </p>
              </button>
            ))}
            {templateQuery.isLoading && (
              <div className="flex items-center justify-center gap-2 py-4 text-xs text-muted-foreground">
                <Loader2 className="h-3.5 w-3.5 animate-spin" /> Loading saved templates…
              </div>
            )}
            <div className="pt-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground/70">
              Starter layouts
            </div>
            {CHAIN_TEMPLATES.map((tpl) => (
              <button
                key={tpl.id}
                onClick={() => {
                  setPreviousRoot(cloneTree(root));
                  setRoot({ ...tpl.build(functionNames), deleteBehavior: root.deleteBehavior });
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
