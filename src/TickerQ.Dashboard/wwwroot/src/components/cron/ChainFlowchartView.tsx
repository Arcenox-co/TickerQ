import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ArrowLeft,
  Clock,
  GitBranch,
  Loader2,
  Pencil,
  RotateCcw,
  Timer,
} from "lucide-react";

import { StatusBadge } from "@/components/cron/StatusBadges";
import { RunConditionBadge } from "@/components/cron/RunConditionBadge";
import { LogsPanel } from "@/components/cron/LogsPanel";
import { ChainLogsPanel } from "@/components/cron/ChainLogsPanel";
import { Button } from "@/components/ui/button";
import { useChainTickers } from "@/services/hooks";
import { STATUS_CONFIG, statusHsl } from "@/lib/cron/status-config";
import { RUN_CONDITION_CONFIG, runConditionHsl } from "@/lib/cron/run-condition";
import { formatAbsolute, formatDuration, relativeTime } from "@/lib/cron/format";
import { useTimezone } from "@/lib/timezone";
import { cn } from "@/lib/utils";
import type { TimeTickerFlatDto } from "@/services/api-types";

// Read-only mirror of the chain builder canvas — port of the Hub's
// ChainFlowchartView. Renders the parent + descendants tree with each node's
// live status (color-coded), run-condition gates on edges, and a resizable
// merged logs panel pinned to the bottom.

interface ChainTreeNode {
  ticker: TimeTickerFlatDto;
  children: ChainTreeNode[];
}

const NODE_W = 300;
const NODE_H = 140;
const H_GAP = 64;
const V_GAP = 96;

interface Pos {
  id: string;
  x: number;
  y: number;
  parentId?: string;
}

function buildTreeFromFlat(
  list: TimeTickerFlatDto[],
  rootId: string
): ChainTreeNode | null {
  const byId = new Map(list.map((t) => [t.id, t]));
  const childrenOf = new Map<string, TimeTickerFlatDto[]>();
  for (const t of list) {
    if (t.parentId) {
      const arr = childrenOf.get(t.parentId) ?? [];
      arr.push(t);
      childrenOf.set(t.parentId, arr);
    }
  }
  const root = byId.get(rootId);
  if (!root) return null;
  const build = (t: TimeTickerFlatDto): ChainTreeNode => ({
    ticker: t,
    children: (childrenOf.get(t.id) ?? []).map(build),
  });
  return build(root);
}

function computeLayout(root: ChainTreeNode): Pos[] {
  const positions: Pos[] = [];
  function subtreeWidth(n: ChainTreeNode): number {
    if (n.children.length === 0) return NODE_W;
    const w = n.children.reduce((s, c) => s + subtreeWidth(c), 0);
    return Math.max(NODE_W, w + (n.children.length - 1) * H_GAP);
  }
  function layout(n: ChainTreeNode, x: number, y: number, parentId?: string) {
    const sw = subtreeWidth(n);
    const nx = x + sw / 2 - NODE_W / 2;
    positions.push({ id: n.ticker.id, x: nx, y, parentId });
    if (n.children.length > 0) {
      let cx = x;
      const childY = y + NODE_H + V_GAP;
      for (const child of n.children) {
        const cw = subtreeWidth(child);
        layout(child, cx, childY, n.ticker.id);
        cx += cw + H_GAP;
      }
    }
  }
  layout(root, 0, 0);
  return positions;
}

export interface ChainFlowchartViewProps {
  rootId: string;
  /** Where the Back button returns to (label is derived). */
  from?: "time-tickers" | "executions" | null;
  onBack: () => void;
  onEditChain?: (rootId: string) => void;
}

export function ChainFlowchartView({
  rootId,
  from,
  onBack,
  onEditChain,
}: ChainFlowchartViewProps) {
  const backLabel = from === "executions" ? "Back to Executions" : "Back to list";

  const containerRef = useRef<HTMLDivElement>(null);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [zoom, setZoom] = useState(1);
  const [isPanning, setIsPanning] = useState(false);
  const [panStart, setPanStart] = useState({ x: 0, y: 0 });
  const [selectedId, setSelectedId] = useState<string | null>(null);

  // Bottom logs panel — height is user-draggable so customers can give logs
  // more or less of the viewport depending on how much they're debugging.
  const [logsPanelHeight, setLogsPanelHeight] = useState(280);
  const handleLogsResizeStart = useCallback(
    (e: React.MouseEvent) => {
      const startY = e.clientY;
      const startHeight = logsPanelHeight;
      const onMove = (moveE: MouseEvent) => {
        const delta = startY - moveE.clientY;
        setLogsPanelHeight(Math.max(150, Math.min(800, startHeight + delta)));
      };
      const onUp = () => {
        document.removeEventListener("mousemove", onMove);
        document.removeEventListener("mouseup", onUp);
      };
      document.addEventListener("mousemove", onMove);
      document.addEventListener("mouseup", onUp);
    },
    [logsPanelHeight]
  );

  const { timezone } = useTimezone();
  const chainQuery = useChainTickers(rootId);
  const list = chainQuery.data ?? [];

  const { positions, nodeMap, maxX, maxY } = useMemo(() => {
    const tree = list.length ? buildTreeFromFlat(list, rootId) : null;
    if (!tree) {
      return {
        positions: [] as Pos[],
        nodeMap: new Map<string, ChainTreeNode>(),
        maxX: 600,
        maxY: 400,
      };
    }
    const positions = computeLayout(tree);
    const nodeMap = new Map<string, ChainTreeNode>();
    const collect = (n: ChainTreeNode) => {
      nodeMap.set(n.ticker.id, n);
      n.children.forEach(collect);
    };
    collect(tree);
    const maxX =
      (positions.length ? Math.max(...positions.map((p) => p.x + NODE_W)) : 300) + 60;
    const maxY =
      (positions.length ? Math.max(...positions.map((p) => p.y + NODE_H + 40)) : 200) +
      60;
    return { positions, nodeMap, maxX, maxY };
  }, [list, rootId]);

  // Center on mount once positions are known.
  useEffect(() => {
    if (containerRef.current && positions.length > 0) {
      const rect = containerRef.current.getBoundingClientRect();
      const scaleX = (rect.width - 60) / maxX;
      const scale = Math.min(scaleX, 1);
      setZoom(scale);
      setPan({ x: (rect.width - maxX * scale) / 2, y: 32 });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [positions.length]);

  const handleWheel = useCallback((e: React.WheelEvent) => {
    setZoom((z) => Math.max(0.3, Math.min(2, z * (e.deltaY > 0 ? 0.92 : 1.08))));
  }, []);

  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      if (e.button === 0 && (e.target as HTMLElement).closest("[data-canvas]")) {
        setIsPanning(true);
        setPanStart({ x: e.clientX - pan.x, y: e.clientY - pan.y });
      }
    },
    [pan]
  );

  const handleMouseMove = useCallback(
    (e: React.MouseEvent) => {
      if (isPanning) setPan({ x: e.clientX - panStart.x, y: e.clientY - panStart.y });
    },
    [isPanning, panStart]
  );

  const handleMouseUp = useCallback(() => setIsPanning(false), []);

  const totalCount = nodeMap.size;
  const selectedNode = selectedId ? nodeMap.get(selectedId) ?? null : null;
  const rootNode = nodeMap.get(rootId);
  const canEditChain =
    !!rootNode && (rootNode.ticker.status === "Idle" || rootNode.ticker.status === "Queued");

  return (
    <div className="flex flex-col h-full">
      {/* Toolbar */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-border bg-surface-0/50 shrink-0">
        <div className="flex items-center gap-3">
          <button
            onClick={onBack}
            className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground transition-colors"
          >
            <ArrowLeft className="h-3 w-3" /> {backLabel}
          </button>
          <div className="h-4 w-px bg-border" />
          <GitBranch className="h-3.5 w-3.5 text-primary" />
          <span className="font-mono text-sm font-semibold">Chain Flowchart</span>
          {chainQuery.data && (
            <span className="text-[11px] text-muted-foreground/70 font-mono">{rootId}</span>
          )}
        </div>
        <div className="flex items-center gap-3 text-[11px] text-muted-foreground">
          {totalCount > 0 && (
            <span className="tabular-nums">
              {totalCount} {totalCount === 1 ? "job" : "jobs"}
            </span>
          )}
          {canEditChain && onEditChain && (
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs border-border text-muted-foreground"
              onClick={() => onEditChain(rootId)}
            >
              <Pencil className="h-3 w-3 mr-1.5" /> Edit chain
            </Button>
          )}
          <div className="flex items-center gap-1">
            <button
              onClick={() => setZoom((z) => Math.min(2, z + 0.1))}
              className="px-1.5 py-0.5 rounded bg-surface-2 hover:bg-surface-3 transition-colors"
            >
              +
            </button>
            <span className="tabular-nums w-10 text-center">{Math.round(zoom * 100)}%</span>
            <button
              onClick={() => setZoom((z) => Math.max(0.3, z - 0.1))}
              className="px-1.5 py-0.5 rounded bg-surface-2 hover:bg-surface-3 transition-colors"
            >
              −
            </button>
          </div>
        </div>
      </div>

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
          const t = e.target as HTMLElement;
          if (t.hasAttribute("data-canvas") || t.closest("[data-canvas-bg]")) {
            setSelectedId(null);
          }
        }}
      >
        {chainQuery.isLoading && (
          <div className="absolute inset-0 flex items-center justify-center text-xs text-muted-foreground gap-2">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading chain…
          </div>
        )}
        {chainQuery.isError && (
          <div className="absolute inset-0 flex items-center justify-center text-xs text-status-error">
            Failed to load chain.
          </div>
        )}

        {chainQuery.data && (
          <div
            className="absolute"
            style={{
              transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
              transformOrigin: "0 0",
            }}
          >
            {/* SVG edges (with arrowheads) */}
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
                  const condition = childNode?.ticker.runCondition ?? null;
                  const childStatus = childNode?.ticker.status;
                  // Pending = the gate hasn't been crossed (or won't be).
                  const isPending =
                    childStatus === "Idle" ||
                    childStatus === "Queued" ||
                    childStatus === "Skipped";
                  // Edge color from runCondition (the gate). Skipped collapses
                  // to the idle color — that branch is decisively dead.
                  const color =
                    childStatus === "Skipped"
                      ? statusHsl("Idle")
                      : condition
                        ? runConditionHsl(condition)
                        : "hsl(var(--border))";
                  const x1 = parent.x + NODE_W / 2;
                  const y1 = parent.y + NODE_H;
                  const x2 = child.x + NODE_W / 2;
                  const y2 = child.y;
                  const midY = (y1 + y2) / 2;
                  return (
                    <g key={`edge-${child.id}`} opacity={isPending ? 0.45 : 0.7}>
                      <path
                        d={`M ${x1} ${y1} C ${x1} ${midY}, ${x2} ${midY}, ${x2} ${y2}`}
                        fill="none"
                        stroke={color}
                        strokeWidth={2}
                        strokeDasharray={isPending ? "6 4" : undefined}
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
              const isRoot = pos.id === rootId;
              const t = node.ticker;
              const at = t.functionName?.indexOf("@") ?? -1;
              const bare = at > 0 ? t.functionName.slice(0, at) : t.functionName;
              const nodeName = at > 0 ? t.functionName.slice(at + 1) : null;
              const parentNode = pos.parentId ? nodeMap.get(pos.parentId) : null;
              const parentNotFired =
                parentNode?.ticker.status === "Idle" ||
                parentNode?.ticker.status === "Queued";
              const skipped = !isRoot && t.status === "Skipped";
              const conditionVar =
                parentNotFired || skipped
                  ? STATUS_CONFIG.Idle.cssVar
                  : STATUS_CONFIG[t.status].cssVar;
              const conditionColor = `hsl(${conditionVar.replace(/^var\(([^)]+)\)$/, "var($1) / 0.4")})`;
              const quiet = t.status === "Idle" || t.status === "Queued" || skipped;
              const ts = t.executedAt ?? t.scheduledFor;
              const timeLabel = quiet || !ts ? "—" : relativeTime(ts);
              const timeTooltip = quiet
                ? `Status: ${t.status}`
                : ts
                  ? `${t.executedAt ? "Executed" : "Scheduled"}: ${formatAbsolute(ts, timezone)}`
                  : "No execution time recorded";
              const elapsedLabel =
                quiet || t.elapsedTime <= 0 ? "—" : formatDuration(t.elapsedTime);
              const retriesLabel =
                t.retries > 0 ? (quiet ? "—" : `${t.retryCount}/${t.retries}`) : "—";
              return (
                <div
                  key={pos.id}
                  className="absolute"
                  style={{ left: pos.x, top: pos.y, width: NODE_W, height: NODE_H }}
                >
                  <div
                    onClick={() => setSelectedId(pos.id === selectedId ? null : pos.id)}
                    className={cn(
                      "w-full h-full rounded-xl border bg-card p-3 cursor-pointer transition-all duration-200 flex flex-col",
                      selectedId === pos.id && "ring-1 ring-primary/50"
                    )}
                    style={{ borderColor: conditionColor }}
                  >
                    <div className="flex items-center gap-2 mb-1.5">
                      {isRoot && (
                        <GitBranch className="h-3 w-3 text-primary shrink-0" />
                      )}
                      <span className="font-mono text-[11px] font-semibold truncate text-foreground">
                        {bare || "(unset)"}
                      </span>
                      <StatusBadge
                        status={t.status}
                        className="ml-auto shrink-0 text-[9px]"
                      />
                    </div>
                    <div className="text-[10px] text-muted-foreground/60 font-mono truncate mb-1">
                      {nodeName || "—"}
                    </div>
                    {t.description && t.description.trim().length > 0 && (
                      <p
                        className="text-[10px] text-muted-foreground line-clamp-2 mb-1"
                        title={t.description}
                      >
                        {t.description}
                      </p>
                    )}
                    <div className="mt-auto flex items-center gap-1.5 flex-wrap">
                      {!isRoot && t.runCondition && (
                        <span
                          className={cn(
                            "text-[10px] font-medium tracking-wide",
                            skipped && "text-status-idle"
                          )}
                          style={
                            skipped
                              ? undefined
                              : { color: runConditionHsl(t.runCondition) }
                          }
                        >
                          {RUN_CONDITION_CONFIG[t.runCondition].label}
                        </span>
                      )}
                      <span className="text-[10px] text-muted-foreground ml-auto">
                        {t.priority}
                      </span>
                    </div>
                    <div
                      className={cn(
                        "mt-2 pt-2 border-t border-border/50 flex items-center gap-2 text-[10px] flex-wrap",
                        skipped ? "text-muted-foreground/30" : "text-muted-foreground/80"
                      )}
                    >
                      <span className="flex items-center gap-1 min-w-0" title={timeTooltip}>
                        <Clock className="h-2.5 w-2.5 shrink-0" />
                        <span className="truncate">{timeLabel}</span>
                      </span>
                      <span
                        className="flex items-center gap-1 tabular-nums"
                        title={quiet ? `Status: ${t.status}` : "Elapsed time of the last run"}
                      >
                        <Timer className="h-2.5 w-2.5 shrink-0" />
                        {elapsedLabel}
                      </span>
                      <span
                        className={cn(
                          "flex items-center gap-1 tabular-nums ml-auto",
                          !quiet && t.retryCount > 0 ? "text-status-warning" : ""
                        )}
                        title={
                          t.retries > 0
                            ? quiet
                              ? `Status: ${t.status}`
                              : `${t.retryCount}/${t.retries} retries used`
                            : "No retries configured"
                        }
                      >
                        <RotateCcw className="h-2.5 w-2.5 shrink-0" />
                        {retriesLabel}
                      </span>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}

        {/* Dot grid background (matches Hub) */}
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

      {/* Resizable logs panel pinned to the bottom. Default: merged chain logs.
          Selecting a node scopes to that ticker's single-log tail. */}
      <div className="shrink-0 flex flex-col" style={{ height: logsPanelHeight }}>
        <div
          onMouseDown={handleLogsResizeStart}
          className="h-1.5 cursor-row-resize bg-border/50 hover:bg-primary/30 transition-colors shrink-0"
          title="Drag to resize"
        />
        {selectedNode ? (
          <>
            <div className="flex items-center justify-between px-4 py-2 border-y border-border bg-surface-0/40 shrink-0">
              <div className="flex items-center gap-3 text-[11px] min-w-0">
                <span className="text-muted-foreground/60 shrink-0">Logs for</span>
                <span className="font-mono font-medium truncate">
                  {selectedNode.ticker.functionName}
                </span>
                <StatusBadge
                  status={selectedNode.ticker.status}
                  className="shrink-0 text-[9px]"
                />
                <button
                  onClick={() => setSelectedId(null)}
                  className="ml-2 text-[10px] text-muted-foreground/60 hover:text-foreground transition-colors shrink-0"
                >
                  show whole chain
                </button>
              </div>
            </div>
            <div className="flex-1 min-h-0 overflow-y-auto scrollbar-thin p-3">
              <LogsPanel
                functionName={selectedNode.ticker.functionName}
                status={selectedNode.ticker.status}
                tickerId={selectedNode.ticker.id}
                exceptionMessage={selectedNode.ticker.exceptionMessage}
                skippedReason={selectedNode.ticker.skippedReason}
                executedAt={selectedNode.ticker.executedAt}
              />
            </div>
          </>
        ) : (
          <div className="flex-1 min-h-0 overflow-y-auto scrollbar-thin p-3">
            <ChainLogsPanel rootId={rootId} />
          </div>
        )}
      </div>

      {/* Detail rail (right). Stops above the logs panel so it doesn't
          obscure the log tail — bottom = logsPanelHeight + 6 (drag handle). */}
      {selectedNode && (
        <div
          className="absolute top-0 right-0 w-[320px] bg-surface-1 border-l border-border p-5 overflow-y-auto scrollbar-thin shadow-lg"
          style={{ bottom: logsPanelHeight + 6 }}
        >
          <div className="flex items-center justify-between mb-4">
            <span className="text-sm font-semibold">Job details</span>
            <button
              onClick={() => setSelectedId(null)}
              className="text-muted-foreground hover:text-foreground text-xs"
            >
              ×
            </button>
          </div>
          <div className="space-y-3 text-[11px]">
            <Detail label="Function">
              <span className="font-mono">{selectedNode.ticker.functionName || "—"}</span>
            </Detail>
            <Detail label="Status">
              <StatusBadge status={selectedNode.ticker.status} />
            </Detail>
            {selectedNode.ticker.runCondition && (
              <Detail label="Run condition">
                <RunConditionBadge condition={selectedNode.ticker.runCondition} />
              </Detail>
            )}
            <Detail label="Priority">{selectedNode.ticker.priority}</Detail>
            <Detail label="Retries">
              {selectedNode.ticker.retryCount}/{selectedNode.ticker.retries}
            </Detail>
            {selectedNode.ticker.scheduledFor && (
              <Detail label="Scheduled for">
                {formatAbsolute(selectedNode.ticker.scheduledFor, timezone)}
              </Detail>
            )}
            {selectedNode.ticker.executedAt && (
              <Detail label="Executed at">
                {formatAbsolute(selectedNode.ticker.executedAt, timezone)}
              </Detail>
            )}
            {selectedNode.ticker.exceptionMessage && (
              <Detail label="Error">
                <span className="text-status-error font-mono break-all">
                  {selectedNode.ticker.exceptionMessage}
                </span>
              </Detail>
            )}
            <Detail label="ID">
              <span className="font-mono text-muted-foreground/70 break-all">
                {selectedNode.ticker.id}
              </span>
            </Detail>
          </div>
        </div>
      )}
    </div>
  );
}

function Detail({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-[9px] uppercase tracking-wider text-muted-foreground/60">
        {label}
      </span>
      <div>{children}</div>
    </div>
  );
}
