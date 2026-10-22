import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Clock3, FilePenLine, LayoutTemplate, Loader2, Pencil, Play, Search, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { toDatetimeLocal } from "@/lib/cron/chain-node";
import { filterAndSortSavedDefinitions, type DefinitionCreatedOrder } from "@/lib/cron/saved-definitions";
import { useActivateDraft, useDefinitions, useDeleteDefinition } from "@/services/hooks";
import type { SavedTickerDefinition, SavedTickerDefinitionTarget } from "@/services/api-types";
import { ActivateDraftDialog } from "@/components/cron/ActivateDraftDialog";

function DefinitionCard({
  definition,
  onEdit,
  onEditTemplate,
  onActivate,
  onDelete,
  activating,
  deleting,
}: {
  definition: SavedTickerDefinition;
  onEdit: () => void;
  onEditTemplate?: () => void;
  onActivate?: () => void;
  onDelete: () => void;
  activating: boolean;
  deleting: boolean;
}) {
  const isDraft = definition.kind === "Draft";
  const activated = !!definition.activatedRootTickerId;
  const activationPending = definition.activationInProgress && !activated;
  return (
    <article className="rounded-xl border border-border bg-card p-3.5">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            {isDraft ? <FilePenLine className="h-3.5 w-3.5 text-primary" /> : <LayoutTemplate className="h-3.5 w-3.5 text-primary" />}
            <h3 className="truncate text-xs font-semibold">{definition.name}</h3>
          </div>
          <p className="mt-1 line-clamp-2 text-[11px] leading-4 text-muted-foreground">
            {definition.description || `${definition.payload.root.function} ${isDraft ? "draft" : "template"}`}
          </p>
        </div>
        {(activated || activationPending) && (
          <span className={`rounded-full border px-2 py-0.5 text-[9px] font-semibold uppercase tracking-wide ${
            activated
              ? "border-status-success/25 bg-status-success/10 text-status-success"
              : "border-status-warning/25 bg-status-warning/10 text-status-warning"
          }`}>
            {activated ? "Activated" : "Activation pending"}
          </span>
        )}
      </div>
      <div className="mt-3 flex items-center gap-1.5 text-[10px] text-muted-foreground/70">
        <Clock3 className="h-3 w-3" />
        {isDraft && definition.payload.executionTime
          ? `Scheduled ${new Date(definition.payload.executionTime).toLocaleString()}`
          : `Created ${new Date(definition.createdAtUtc).toLocaleString()}`}
        {!definition.includesRequestData && <span>· payloads excluded</span>}
      </div>
      <div className="mt-3 flex items-center gap-2 border-t border-border/60 pt-3">
        <Button size="sm" variant="outline" className="h-7 text-[11px]" onClick={onEdit}>
          {activated ? "View job" : isDraft ? "Edit" : "Use template"}
        </Button>
        {!isDraft && onEditTemplate && (
          <Button size="sm" variant="ghost" className="h-7 text-[11px]" onClick={onEditTemplate}>
            <Pencil className="mr-1 h-3 w-3" /> Edit
          </Button>
        )}
        {isDraft && !activated && onActivate && (
          <Button type="button" size="sm" variant="gradient" className="h-7 text-[11px]" onClick={onActivate} disabled={activating}>
            {activating ? <Loader2 className="mr-1 h-3 w-3 animate-spin" /> : <Play className="mr-1 h-3 w-3" />}
            {activationPending ? "Resume" : "Review activation"}
          </Button>
        )}
        <Button
          size="icon-sm"
          variant="ghost"
          className="ml-auto text-muted-foreground hover:text-status-error"
          aria-label={`Delete ${definition.name}`}
          onClick={onDelete}
          disabled={deleting}
        >
          {deleting ? <Loader2 className="h-3 w-3 animate-spin" /> : <Trash2 className="h-3 w-3" />}
        </Button>
      </div>
    </article>
  );
}

export function SavedDefinitionsPanel({
  open,
  target,
  onOpenChange,
}: {
  open: boolean;
  target: SavedTickerDefinitionTarget;
  onOpenChange: (open: boolean) => void;
}) {
  const navigate = useNavigate();
  const definitions = useDefinitions(undefined, target);
  const activate = useActivateDraft();
  const remove = useDeleteDefinition();
  const [busyId, setBusyId] = useState<string | null>(null);
  const [activationDefinition, setActivationDefinition] = useState<SavedTickerDefinition | null>(null);
  const [search, setSearch] = useState("");
  const [createdOrder, setCreatedOrder] = useState<DefinitionCreatedOrder>("newest");
  const drafts = useMemo(() => definitions.data?.filter(item => item.kind === "Draft") ?? [], [definitions.data]);
  const templates = useMemo(() => definitions.data?.filter(item => item.kind === "Template") ?? [], [definitions.data]);
  const visibleDrafts = useMemo(
    () => filterAndSortSavedDefinitions(drafts, search, createdOrder),
    [drafts, search, createdOrder],
  );
  const visibleTemplates = useMemo(
    () => filterAndSortSavedDefinitions(templates, search, createdOrder),
    [templates, search, createdOrder],
  );

  function requestDraftActivation(definition: SavedTickerDefinition) {
    setActivationDefinition(definition);
  }

  async function activateDraft(executionTime: string | null) {
    const definition = activationDefinition;
    if (!definition) return;
    setBusyId(definition.id);
    try {
      const result = await activate.mutateAsync({
        id: definition.id,
        version: definition.version,
        executionTime: executionTime
          ? new Date(executionTime).toISOString()
          : new Date().toISOString(),
      });
      toast.success("Draft activated and scheduled");
      setActivationDefinition(null);
      onOpenChange(false);
      navigate(`/time-tickers?ticker=${result.rootId}`);
    } catch (error) {
      toast.error("Could not activate the draft", { description: error instanceof Error ? error.message : "Unknown error" });
    } finally {
      setBusyId(null);
    }
  }

  async function deleteDefinition(definition: SavedTickerDefinition) {
    if (!window.confirm(`Delete “${definition.name}”?`)) return;
    setBusyId(definition.id);
    try {
      await remove.mutateAsync({ id: definition.id, version: definition.version });
      toast.success(definition.kind === "Draft" ? "Draft deleted" : "Template deleted");
    } catch (error) {
      toast.error("Could not delete the saved definition", { description: error instanceof Error ? error.message : "Unknown error" });
    } finally {
      setBusyId(null);
    }
  }

  const builderPath = target === "CronTicker" ? "/cron-tickers/new-chain?cron=new" : "/time-tickers/new-chain";
  return (
    <>
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="flex w-[420px] max-w-[calc(100vw-1rem)] flex-col gap-0 border-l border-border bg-surface-1 p-0 sm:max-w-[420px]">
        <SheetHeader className="shrink-0 border-b border-border px-5 pb-4 pt-5">
          <SheetTitle className="text-sm">Saved work</SheetTitle>
          <SheetDescription className="text-[11px]">
            Drafts are not scheduler jobs. Templates are reusable configurations.
          </SheetDescription>
        </SheetHeader>
        {definitions.isLoading ? (
          <div className="flex flex-1 items-center justify-center gap-2 text-xs text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" /> Loading…</div>
        ) : (
          <Tabs defaultValue={target === "TimeTicker" ? "drafts" : "templates"} className="flex min-h-0 flex-1 flex-col">
            <div className="shrink-0 px-4 pt-4">
              <TabsList className="grid w-full grid-cols-2">
                <TabsTrigger value="drafts" disabled={target !== "TimeTicker"}>Drafts ({drafts.length})</TabsTrigger>
                <TabsTrigger value="templates">Templates ({templates.length})</TabsTrigger>
              </TabsList>
              <div className="mt-3 flex items-center gap-2">
                <div className="relative min-w-0 flex-1">
                  <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
                  <Input
                    value={search}
                    onChange={event => setSearch(event.target.value)}
                    placeholder="Search saved work…"
                    aria-label="Search saved work"
                    className="h-8 pl-8 text-xs"
                  />
                </div>
                <select
                  value={createdOrder}
                  onChange={event => setCreatedOrder(event.target.value as DefinitionCreatedOrder)}
                  aria-label="Order by creation date"
                  className="h-8 rounded-md border border-input bg-background px-2 text-[11px] text-foreground outline-none focus:ring-2 focus:ring-ring"
                >
                  <option value="newest">Newest first</option>
                  <option value="oldest">Oldest first</option>
                </select>
              </div>
            </div>
            <TabsContent value="drafts" className="min-h-0 flex-1 space-y-3 overflow-y-auto p-4">
              {visibleDrafts.length === 0 && <p className="py-12 text-center text-xs text-muted-foreground">{search.trim() ? "No drafts match your search." : "No drafts saved yet."}</p>}
              {visibleDrafts.map(definition => (
                <DefinitionCard key={definition.id} definition={definition}
                  onEdit={() => {
                    onOpenChange(false);
                    navigate(definition.activatedRootTickerId
                      ? `/time-tickers?ticker=${definition.activatedRootTickerId}`
                      : `/time-tickers/new-chain?draft=${definition.id}`);
                  }}
                  onActivate={() => requestDraftActivation(definition)}
                  onDelete={() => void deleteDefinition(definition)}
                  activating={busyId === definition.id && activate.isPending}
                  deleting={busyId === definition.id && remove.isPending} />
              ))}
            </TabsContent>
            <TabsContent value="templates" className="min-h-0 flex-1 space-y-3 overflow-y-auto p-4">
              {visibleTemplates.length === 0 && <p className="py-12 text-center text-xs text-muted-foreground">{search.trim() ? "No templates match your search." : "No templates saved yet."}</p>}
              {visibleTemplates.map(definition => (
                <DefinitionCard key={definition.id} definition={definition}
                  onEdit={() => { onOpenChange(false); navigate(`${builderPath}${builderPath.includes("?") ? "&" : "?"}template=${definition.id}`); }}
                  onEditTemplate={() => { onOpenChange(false); navigate(`${builderPath}${builderPath.includes("?") ? "&" : "?"}editTemplate=${definition.id}`); }}
                  onDelete={() => void deleteDefinition(definition)}
                  activating={false}
                  deleting={busyId === definition.id && remove.isPending} />
              ))}
            </TabsContent>
          </Tabs>
        )}
      </SheetContent>
    </Sheet>
    <ActivateDraftDialog
      open={activationDefinition !== null}
      draftName={activationDefinition?.name}
      initialExecutionTime={activationDefinition?.payload.executionTime
        ? toDatetimeLocal(activationDefinition.payload.executionTime)
        : ""}
      pending={activate.isPending}
      onOpenChange={nextOpen => {
        if (!nextOpen && !activate.isPending) setActivationDefinition(null);
      }}
      onActivate={activateDraft}
    />
    </>
  );
}
