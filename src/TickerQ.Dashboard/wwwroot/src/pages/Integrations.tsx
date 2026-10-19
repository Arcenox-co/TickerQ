import { useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Activity,
  ArrowRight,
  Check,
  ChevronRight,
  Clock3,
  ListFilter,
  Loader2,
  Pencil,
  Plug,
  Plus,
  RefreshCw,
  Search,
  Send,
  Trash2,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { DataCard, PageHeader } from "@/components/cron/PageHeader";
import { ConfirmDialog } from "@/components/cron/ConfirmDialog";
import { DestinationEditor } from "@/components/integrations/DestinationEditor";
import { MonitoringPanel } from "@/components/integrations/MonitoringPanel";
import { RuleEditor } from "@/components/integrations/RuleEditor";
import { ProviderIcon } from "@/components/integrations/Fields";
import {
  errorText,
  eventOptions,
  newDestination,
  providerDescription,
  providerName,
  removeDestination,
} from "@/components/integrations/configuration";
import { dashboardApi } from "@/services/dashboard-api";
import { getRuntimeConfig } from "@/lib/runtime-config";
import { cn } from "@/lib/utils";
import type {
  IntegrationConfiguration,
  IntegrationDestination,
  IntegrationRule,
  IntegrationSnapshot,
} from "@/services/integration-types";

type Editor =
  | {
      type: "destination";
      value: IntegrationDestination;
      base: IntegrationConfiguration;
    }
  | {
      type: "rule";
      value: IntegrationRule;
      isNew: boolean;
      base: IntegrationConfiguration;
    };
type Confirmation = {
  type: "test" | "delete-destination" | "delete-rule";
  id: string;
};

export default function IntegrationsPage() {
  const enabled = getRuntimeConfig().integrationsEnabled;
  const query = useQuery({
    queryKey: ["integrations"],
    queryFn: ({ signal }) => dashboardApi.getIntegrations(signal),
    enabled,
    refetchOnWindowFocus: false,
    refetchInterval: 30000,
  });
  const header = (
    <PageHeader
      title="Integrations"
      description="Connect your team's tools and route the alerts that matter."
    />
  );
  if (!enabled)
    return (
      <div className="space-y-5">
        {header}
        <Empty
          icon={<Plug />}
          title="Integrations are not enabled"
          description="Add the TickerQ.Integrations package to your host and configure AddIntegrations to connect your tools."
        />
      </div>
    );
  if (query.isPending)
    return (
      <div className="space-y-5">
        {header}
        <DataCard className="flex items-center gap-2 p-6 text-sm text-muted-foreground">
          <Loader2 className="size-4 animate-spin" />
          Loading integrations…
        </DataCard>
      </div>
    );
  if (query.isError)
    return (
      <div className="space-y-5">
        {header}
        <Empty
          icon={<Plug />}
          title="Could not load integrations"
          description={errorText(query.error)}
        >
          <Button
            size="sm"
            variant="outline"
            onClick={() => void query.refetch()}
          >
            Try again
          </Button>
        </Empty>
      </div>
    );
  if (!query.data.canManage)
    return (
      <div className="space-y-5">
        {header}
        <Empty
          icon={<Plug />}
          title="Management access required"
          description="Ask your administrator for permission to manage integrations. The dashboard must also allow changes."
        />
      </div>
    );
  return (
    <IntegrationManager
      snapshot={query.data}
      refreshing={query.isFetching}
      reload={async () => {
        const result = await query.refetch();
        if (result.isError) throw result.error;
      }}
    />
  );
}

function IntegrationManager({
  snapshot,
  refreshing,
  reload,
}: {
  snapshot: IntegrationSnapshot;
  refreshing: boolean;
  reload: () => Promise<void>;
}) {
  const [tab, setTab] = useState("connections");
  const [search, setSearch] = useState("");
  const [picker, setPicker] = useState(false);
  const [editor, setEditor] = useState<Editor | null>(null);
  const [confirm, setConfirm] = useState<Confirmation | null>(null);
  const [busy, setBusy] = useState(false);
  const config = snapshot.configuration;
  function connect(provider: string) {
    setPicker(false);
    setEditor({
      type: "destination",
      value: newDestination(provider),
      base: structuredClone(config),
    });
  }
  function createRule(destinationId?: string) {
    setEditor({
      type: "rule",
      isNew: true,
      base: structuredClone(config),
      value: {
        id: crypto.randomUUID(),
        name: "Job failures",
        enabled: true,
        destinationIds: destinationId
          ? [destinationId]
          : config.destinations.length === 1
            ? [config.destinations[0].id]
            : [],
        events: ["failed", "timeout_cancelled"],
        includeFunctions: [],
        excludeFunctions: [],
        cooldownSeconds: 60,
      },
    });
  }
  async function refresh() {
    try {
      await reload();
    } catch (error) {
      toast.error("Could not refresh integrations", {
        description: errorText(error),
      });
    }
  }
  async function save(next: IntegrationConfiguration, message: string) {
    setBusy(true);
    try {
      await dashboardApi.saveIntegrations(next);
      setEditor(null);
      toast.success(message);
      await refresh();
    } finally {
      setBusy(false);
    }
  }
  async function confirmAction() {
    if (!confirm || busy) return;
    setBusy(true);
    try {
      if (confirm.type === "test") {
        const result = await dashboardApi.testIntegration(confirm.id);
        if (result.outcome === "Sent")
          toast.success("Test accepted", {
            description:
              "Check your destination to confirm the message arrived.",
          });
        else toast.error("Test failed", { description: result.message });
        await refresh();
      } else if (confirm.type === "delete-destination") {
        await save(
          removeDestination(config, confirm.id),
          "Integration removed",
        );
      } else {
        await save(
          { ...config, rules: config.rules.filter((r) => r.id !== confirm.id) },
          "Alert rule removed",
        );
      }
      setConfirm(null);
    } catch (error) {
      toast.error("Could not complete action", {
        description: errorText(error),
      });
    } finally {
      setBusy(false);
    }
  }
  const removedRules =
    confirm?.type === "delete-destination"
      ? config.rules.filter(
          (r) =>
            r.destinationIds.includes(confirm.id) &&
            r.destinationIds.length === 1,
        ).length
      : 0;
  const targetName =
    confirm?.type === "delete-rule"
      ? config.rules.find((r) => r.id === confirm.id)?.name
      : config.destinations.find((d) => d.id === confirm?.id)?.name;
  const connections = config.destinations.filter((d) =>
    `${d.name} ${providerName(d.provider)} ${d.endpointHost ?? ""}`
      .toLowerCase()
      .includes(search.toLowerCase()),
  );

  return (
    <div className="space-y-6">
      <PageHeader
        title="Integrations"
        description="Connect your team's tools and route the alerts that matter."
      >
        <Button
          size="sm"
          variant="outline"
          disabled={busy || refreshing}
          onClick={() => void refresh()}
        >
          <RefreshCw className={cn("size-3.5", refreshing && "animate-spin")} />
          Refresh
        </Button>
        <Button
          size="sm"
          disabled={
            busy ||
            config.destinations.length >= 32 ||
            !snapshot.providers.length
          }
          onClick={() => setPicker(true)}
        >
          <Plus className="size-3.5" />
          Add integration
        </Button>
      </PageHeader>
      {!snapshot.persistent && (
        <p className="rounded-lg border border-amber-500/20 bg-amber-500/5 p-3 text-xs text-amber-700 dark:text-amber-300">
          This host uses in-memory storage. Integrations and rules will be lost
          when it restarts.
        </p>
      )}
      <Tabs value={tab} onValueChange={setTab} className="gap-5">
        <TabsList
          className="w-full max-w-full flex-wrap border border-border bg-surface-1 p-1 sm:w-fit group-data-[orientation=horizontal]/tabs:h-auto sm:group-data-[orientation=horizontal]/tabs:h-10"
          aria-label="Integration sections"
        >
          <TabsTrigger
            value="connections"
            className="gap-1.5 px-2 text-[11px] sm:gap-2 sm:px-3 sm:text-xs [&_svg]:hidden sm:[&_svg]:block"
          >
            <Plug className="size-3.5" />
            Connections
            <Count value={config.destinations.length} />
          </TabsTrigger>
          <TabsTrigger
            value="rules"
            className="gap-1.5 px-2 text-[11px] sm:gap-2 sm:px-3 sm:text-xs [&_svg]:hidden sm:[&_svg]:block"
          >
            <ListFilter className="size-3.5" />
            Alert rules
            <Count value={config.rules.length} />
          </TabsTrigger>
          <TabsTrigger
            value="monitoring"
            className="gap-1.5 px-2 text-[11px] sm:gap-2 sm:px-3 sm:text-xs"
          >
            <Clock3 className="hidden size-3.5 sm:block" />
            Monitoring
            <Count value={config.monitors?.length ?? 0} />
          </TabsTrigger>
          <TabsTrigger
            value="activity"
            className="gap-1.5 px-2 text-[11px] sm:gap-2 sm:px-3 sm:text-xs [&_svg]:hidden sm:[&_svg]:block"
          >
            <Activity className="size-3.5" />
            Activity
          </TabsTrigger>
        </TabsList>
        <TabsContent value="connections" className="space-y-4">
          {config.destinations.length === 0 ? (
            <DataCard className="p-5 sm:p-6">
              <div className="mb-5 space-y-1">
                <h2 className="text-sm font-semibold">
                  Connect your first integration
                </h2>
                <p className="text-xs leading-relaxed text-muted-foreground">
                  Choose an application, add its webhook, then create an alert
                  rule.
                </p>
              </div>
              <ProviderPicker providers={snapshot.providers} onPick={connect} />
            </DataCard>
          ) : (
            <>
              <div className="flex flex-wrap items-center justify-between gap-3">
                <p className="text-xs text-muted-foreground">
                  Saved connections are shared across alert rules.
                </p>
                {config.destinations.length > 3 && (
                  <div className="relative">
                    <Search className="absolute top-2.5 left-3 size-3.5 text-muted-foreground" />
                    <Input
                      aria-label="Search integrations"
                      placeholder="Search integrations…"
                      className="h-8 pl-9 text-xs"
                      value={search}
                      onChange={(e) => setSearch(e.target.value)}
                    />
                  </div>
                )}
              </div>
              {connections.length === 0 && (
                <Empty
                  icon={<Search />}
                  title="No integrations found"
                  description="Try a different name or application."
                />
              )}
              <div className="grid gap-4 xl:grid-cols-2">
                {connections.map((d) => {
                  const rules = config.rules.filter((r) =>
                    r.destinationIds.includes(d.id),
                  );
                  const activeRules = rules.filter((r) => r.enabled).length;
                  const latest = snapshot.recentDeliveries
                    .filter((s) => s.destinationId === d.id)
                    .sort((a, b) => b.atUtc.localeCompare(a.atUtc))[0];
                  return (
                    <DataCard key={d.id} className="flex min-w-0 flex-col">
                      <div className="flex items-start gap-3 p-4">
                        <ProviderIcon provider={d.provider} />
                        <div className="min-w-0 flex-1">
                          <h2
                            className="truncate text-sm font-semibold"
                            title={d.name}
                          >
                            {d.name}
                          </h2>
                          <p className="mt-1 truncate text-xs text-muted-foreground">
                            {providerName(d.provider)}
                            {d.endpointHost ? ` · ${d.endpointHost}` : ""}
                          </p>
                        </div>
                        <Status enabled={d.enabled} />
                      </div>
                      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 pb-4 text-xs">
                        <button
                          className="inline-flex items-center gap-1.5 text-muted-foreground hover:text-primary"
                          onClick={() => setTab("rules")}
                        >
                          <ListFilter className="size-3.5" />
                          {activeRules} active{" "}
                          {activeRules === 1 ? "rule" : "rules"}
                          <ChevronRight className="size-3" />
                        </button>
                        {latest ? (
                          <button
                            className={cn(
                              "inline-flex items-center gap-1.5",
                              latest.outcome === "Sent"
                                ? "text-muted-foreground hover:text-primary"
                                : "text-status-error",
                            )}
                            onClick={() => setTab("activity")}
                          >
                            {latest.outcome === "Sent" ? (
                              <Check className="size-3.5" />
                            ) : (
                              <Activity className="size-3.5" />
                            )}
                            Last delivery:{" "}
                            {latest.outcome === "Sent"
                              ? "accepted"
                              : latest.outcome.toLowerCase()}
                          </button>
                        ) : (
                          <span className="text-muted-foreground/70">
                            No deliveries yet
                          </span>
                        )}
                      </div>
                      {activeRules === 0 && (
                        <div className="mx-4 mb-4 flex items-center justify-between gap-3 rounded-md bg-surface-1 px-3 py-2.5">
                          <p className="text-[11px] text-muted-foreground">
                            {rules.length
                              ? "Enable a rule to start receiving alerts."
                              : "Add a rule to start receiving alerts."}
                          </p>
                          <Button
                            size="sm"
                            variant="ghost"
                            className="h-7 shrink-0 text-xs text-primary"
                            disabled={busy || config.rules.length >= 100}
                            onClick={() =>
                              rules.length ? setTab("rules") : createRule(d.id)
                            }
                          >
                            {rules.length ? "View rules" : "Add rule"}
                            <ArrowRight className="size-3" />
                          </Button>
                        </div>
                      )}
                      <div className="mt-auto flex flex-wrap items-center gap-2 border-t border-border px-4 py-3">
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={busy}
                          onClick={() =>
                            setEditor({
                              type: "destination",
                              value: structuredClone(d),
                              base: structuredClone(config),
                            })
                          }
                        >
                          <Pencil className="size-3.5" />
                          Configure<span className="sr-only"> {d.name}</span>
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          disabled={busy || !d.hasUrl}
                          onClick={() => setConfirm({ type: "test", id: d.id })}
                        >
                          <Send className="size-3.5" />
                          Send test<span className="sr-only"> to {d.name}</span>
                        </Button>
                        <Button
                          size="icon-sm"
                          variant="ghost"
                          className="ml-auto text-muted-foreground hover:text-status-error"
                          aria-label={`Remove integration ${d.name}`}
                          disabled={busy}
                          onClick={() =>
                            setConfirm({ type: "delete-destination", id: d.id })
                          }
                        >
                          <Trash2 className="size-3.5" />
                        </Button>
                      </div>
                    </DataCard>
                  );
                })}
              </div>
            </>
          )}
        </TabsContent>
        <TabsContent value="rules" className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-xs text-muted-foreground">
              Choose the events, functions, and integrations for each rule.
            </p>
            <Button
              size="sm"
              variant="outline"
              disabled={
                busy ||
                !config.destinations.length ||
                config.rules.length >= 100
              }
              onClick={() => createRule()}
            >
              <Plus className="size-3.5" />
              Create rule
            </Button>
          </div>
          {config.rules.length === 0 ? (
            <Empty
              icon={<ListFilter />}
              title={
                config.destinations.length
                  ? "Choose what gets your attention"
                  : "Connect an integration first"
              }
              description={
                config.destinations.length
                  ? "Create a rule for job failures, timeouts, or other events. You can target all functions or just a few."
                  : "Add Slack, Teams, or a webhook before creating your first alert rule."
              }
            >
              <Button
                size="sm"
                variant="outline"
                onClick={() =>
                  config.destinations.length ? createRule() : setPicker(true)
                }
              >
                {config.destinations.length
                  ? "Create your first rule"
                  : "Add integration"}
                <ArrowRight className="size-3.5" />
              </Button>
            </Empty>
          ) : (
            <div className="space-y-3">
              {config.rules.map((r) => (
                <DataCard key={r.id} className="p-4">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0 space-y-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <h2 className="break-words text-sm font-semibold">
                          {r.name}
                        </h2>
                        <Status enabled={r.enabled} />
                      </div>
                      <div className="flex flex-wrap gap-1.5">
                        {r.events.map((event) => (
                          <span
                            key={event}
                            className="rounded border border-border bg-surface-1 px-2 py-0.5 text-[11px] text-muted-foreground"
                          >
                            {eventOptions.find((e) => e.value === event)
                              ?.label ?? event}
                          </span>
                        ))}
                      </div>
                    </div>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={busy}
                      onClick={() =>
                        setEditor({
                          type: "rule",
                          isNew: false,
                          value: structuredClone(r),
                          base: structuredClone(config),
                        })
                      }
                    >
                      <Pencil className="size-3.5" />
                      Edit<span className="sr-only"> {r.name}</span>
                    </Button>
                  </div>
                  <div className="mt-4 grid gap-3 text-xs sm:grid-cols-2">
                    <div>
                      <p className="mb-1 text-[11px] text-muted-foreground">
                        Functions
                      </p>
                      <p className="break-words">
                        {r.includeFunctions.length
                          ? r.includeFunctions.join(", ")
                          : "All functions"}
                      </p>
                      {r.excludeFunctions.length > 0 && (
                        <p className="mt-1 break-words text-[11px] text-muted-foreground">
                          Except {r.excludeFunctions.join(", ")}
                        </p>
                      )}
                    </div>
                    <div>
                      <p className="mb-1 text-[11px] text-muted-foreground">
                        Send to
                      </p>
                      <p className="break-words">
                        {r.destinationIds
                          .map((id) => {
                            const d = config.destinations.find(
                              (d) => d.id === id,
                            );
                            return d
                              ? `${d.name}${d.enabled ? "" : " (paused)"}`
                              : "Unavailable integration";
                          })
                          .join(", ")}
                      </p>
                    </div>
                  </div>
                  <div className="mt-4 flex items-center justify-between border-t border-border pt-3">
                    <span className="flex items-center gap-1.5 text-[11px] text-muted-foreground">
                      <Clock3 className="size-3.5" />
                      {r.cooldownSeconds
                        ? `${r.cooldownSeconds}s cooldown per function and event`
                        : "No cooldown · every matching event"}
                    </span>
                    <Button
                      size="icon-sm"
                      variant="ghost"
                      className="text-muted-foreground hover:text-status-error"
                      aria-label={`Remove rule ${r.name}`}
                      disabled={busy}
                      onClick={() =>
                        setConfirm({ type: "delete-rule", id: r.id })
                      }
                    >
                      <Trash2 className="size-3.5" />
                    </Button>
                  </div>
                </DataCard>
              ))}
            </div>
          )}
        </TabsContent>
        <TabsContent value="monitoring">
          <MonitoringPanel
            config={config}
            statuses={snapshot.monitors ?? []}
            available={snapshot.monitoringAvailable ?? false}
            pending={snapshot.monitoringPendingDeliveries ?? 0}
            intervalSeconds={snapshot.monitoringCheckIntervalSeconds ?? 30}
            busy={busy}
            onSave={save}
          />
        </TabsContent>
        <TabsContent value="activity" className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-3">
            <Metric
              label="Recent attempts"
              value={snapshot.recentDeliveries.length}
            />
            <Metric
              label="Suppressed by cooldown"
              value={snapshot.suppressed}
            />
            <Metric label="Dropped from queue" value={snapshot.dropped} />
          </div>
          <DataCard>
            <div className="border-b border-border p-4">
              <h2 className="text-sm font-medium">Delivery activity</h2>
              <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
                Monitoring deliveries are shared across replicas and retained
                for up to 7 days (50 latest attempts). Other event and test
                attempts are local to this node and reset on restart.
              </p>
            </div>
            {snapshot.recentDeliveries.length === 0 ? (
              <div className="p-8 text-center">
                <Activity className="mx-auto mb-3 size-6 text-muted-foreground/60" />
                <p className="text-sm font-medium">No delivery attempts yet</p>
                <p className="mt-1 text-xs text-muted-foreground">
                  Send a test from a connection, or wait for an event that
                  matches a rule.
                </p>
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full min-w-[540px] text-left text-xs">
                  <thead className="bg-surface-1 text-[11px] text-muted-foreground">
                    <tr>
                      <th className="px-4 py-3 font-medium">Integration</th>
                      <th className="px-4 py-3 font-medium">Result</th>
                      <th className="px-4 py-3 font-medium">Details</th>
                      <th className="px-4 py-3 font-medium">Time</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {[...snapshot.recentDeliveries]
                      .sort((a, b) => b.atUtc.localeCompare(a.atUtc))
                      .map((delivery, i) => (
                        <tr key={`${delivery.atUtc}-${i}`}>
                          <td className="max-w-44 break-words px-4 py-3 font-medium">
                            {config.destinations.find(
                              (d) => d.id === delivery.destinationId,
                            )?.name ?? "Removed integration"}
                          </td>
                          <td className="px-4 py-3">
                            <span
                              className={cn(
                                "rounded-full px-2 py-1 text-[11px]",
                                delivery.outcome === "Sent"
                                  ? "bg-status-healthy/10 text-status-healthy"
                                  : "bg-status-error/10 text-status-error",
                              )}
                            >
                              {delivery.outcome === "Sent"
                                ? "Accepted"
                                : delivery.outcome}
                            </span>
                          </td>
                          <td className="max-w-sm px-4 py-3 text-muted-foreground">
                            {delivery.message}
                          </td>
                          <td className="whitespace-nowrap px-4 py-3 text-muted-foreground">
                            {new Date(delivery.atUtc).toLocaleString()}
                          </td>
                        </tr>
                      ))}
                  </tbody>
                </table>
              </div>
            )}
          </DataCard>
        </TabsContent>
      </Tabs>
      <Dialog open={picker} onOpenChange={setPicker}>
        <DialogContent className="sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>Add integration</DialogTitle>
            <DialogDescription className="text-xs">
              Choose where to send your job alerts.
            </DialogDescription>
          </DialogHeader>
          <ProviderPicker providers={snapshot.providers} onPick={connect} />
        </DialogContent>
      </Dialog>
      {editor?.type === "destination" && (
        <DestinationEditor
          key={editor.value.id}
          initial={editor.value}
          tokens={snapshot.templateTokens}
          onClose={() => setEditor(null)}
          onSave={(value) =>
            save(
              {
                ...editor.base,
                destinations: editor.base.destinations.some(
                  (d) => d.id === value.id,
                )
                  ? editor.base.destinations.map((d) =>
                      d.id === value.id ? value : d,
                    )
                  : [...editor.base.destinations, value],
              },
              "Integration saved",
            )
          }
        />
      )}
      {editor?.type === "rule" && (
        <RuleEditor
          key={editor.value.id}
          initial={editor.value}
          isNew={editor.isNew}
          destinations={editor.base.destinations}
          onClose={() => setEditor(null)}
          onSave={(value) =>
            save(
              {
                ...editor.base,
                rules: editor.isNew
                  ? [...editor.base.rules, value]
                  : editor.base.rules.map((r) =>
                      r.id === value.id ? value : r,
                    ),
              },
              "Alert rule saved",
            )
          }
        />
      )}
      <ConfirmDialog
        open={confirm !== null}
        onOpenChange={(open) => {
          if (!open) setConfirm(null);
        }}
        title={
          confirm?.type === "test"
            ? `Send test to ${targetName}?`
            : confirm?.type === "delete-rule"
              ? `Remove ${targetName}?`
              : `Remove ${targetName}?`
        }
        description={
          confirm?.type === "test"
            ? "Sends a sample failure message using the saved connection, even if it is paused. No job will run."
            : confirm?.type === "delete-destination"
              ? `This removes the integration from all alert rules and monitors. Monitors using only this connection are also removed.${removedRules ? ` ${removedRules} ${removedRules === 1 ? "rule uses" : "rules use"} only this integration and will also be removed.` : " Rules with other integrations will keep working."} Changes take effect immediately.`
              : "This rule will stop sending alerts. Your integrations will stay connected."
        }
        confirmLabel={confirm?.type === "test" ? "Send test" : "Remove"}
        confirmVariant={confirm?.type === "test" ? "default" : "destructive"}
        isPending={busy}
        onConfirm={confirmAction}
      />
    </div>
  );
}
function Count({ value }: { value: number }) {
  return (
    <span className="hidden rounded bg-surface-2 px-1.5 text-[10px] tabular-nums text-muted-foreground sm:inline">
      {value}
    </span>
  );
}
function Status({ enabled }: { enabled: boolean }) {
  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center gap-1.5 rounded-full border px-2 py-0.5 text-[10px] font-medium",
        enabled
          ? "border-primary/20 bg-primary/5 text-primary"
          : "border-border bg-surface-1 text-muted-foreground",
      )}
    >
      <span
        className={cn(
          "size-1 rounded-full",
          enabled ? "bg-primary" : "bg-muted-foreground",
        )}
      />
      {enabled ? "Enabled" : "Paused"}
    </span>
  );
}
function Metric({ label, value }: { label: string; value: number }) {
  return (
    <DataCard className="px-4 py-3">
      <p className="text-[11px] text-muted-foreground">{label}</p>
      <p className="mt-1 text-xl font-semibold tabular-nums">{value}</p>
    </DataCard>
  );
}
function ProviderPicker({
  providers,
  onPick,
}: {
  providers: string[];
  onPick: (provider: string) => void;
}) {
  if (!providers.length)
    return (
      <p className="text-xs text-muted-foreground">
        No integration providers are available on this host.
      </p>
    );
  return (
    <div className="grid gap-3 sm:grid-cols-3">
      {providers.map((provider) => (
        <button
          key={provider}
          type="button"
          onClick={() => onPick(provider)}
          className="group flex min-w-0 flex-col items-start rounded-lg border border-border bg-surface-1/50 p-4 text-left transition-colors hover:border-primary/40 hover:bg-surface-2/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <ProviderIcon provider={provider} />
          <span className="mt-4 text-sm font-medium">
            {providerName(provider)}
          </span>
          <span className="mt-1 text-xs leading-relaxed text-muted-foreground">
            {providerDescription(provider)}
          </span>
          <span className="mt-4 flex items-center gap-1.5 text-xs text-primary">
            Connect
            <ArrowRight className="size-3.5 transition-transform group-hover:translate-x-0.5" />
          </span>
        </button>
      ))}
    </div>
  );
}
function Empty({
  title,
  description,
  icon,
  children,
}: {
  title: string;
  description: string;
  icon: ReactNode;
  children?: ReactNode;
}) {
  return (
    <DataCard className="flex flex-col items-center p-8 text-center">
      <span className="mb-4 flex size-11 items-center justify-center rounded-xl border border-border bg-surface-1 text-muted-foreground [&_svg]:size-5">
        {icon}
      </span>
      <h2 className="text-sm font-semibold">{title}</h2>
      <p className="mt-2 max-w-md text-xs leading-relaxed text-muted-foreground">
        {description}
      </p>
      {children && <div className="mt-5">{children}</div>}
    </DataCard>
  );
}
