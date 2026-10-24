import { useState } from "react";
import { AlertTriangle, Check, Cloud, Copy, KeyRound, Laptop, PlugZap } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import type { AssistantConnection } from "@/lib/assistant-connection";

const hostedSnippet = `// dotnet add package Microsoft.Extensions.AI.OpenAI
var chatClient = new OpenAIClient(builder.Configuration["OpenAI:ApiKey"])
    .GetChatClient("gpt-4o-mini")
    .AsIChatClient();

options.AddDashboard(dashboard => dashboard
    .AddAssistant(assistant => assistant
        .UseChatClient(chatClient)
        .WithModelName("gpt-4o-mini")));`;

const localSnippet = `// Start a local model first: ollama pull llama3.1
var chatClient = new OpenAIClient(
        new ApiKeyCredential("ollama"), // Ollama ignores the key
        new OpenAIClientOptions { Endpoint = new Uri("http://localhost:11434/v1") })
    .GetChatClient("llama3.1")
    .AsIChatClient();

options.AddDashboard(dashboard => dashboard
    .AddAssistant(assistant => assistant
        .UseChatClient(chatClient)
        .WithModelName("llama3.1")));`;

const options = [
  {
    key: "hosted",
    icon: Cloud,
    title: "Hosted model",
    badge: "API key",
    badgeIcon: KeyRound,
    description: "OpenAI, Azure OpenAI, Anthropic, or another provider. Messages and tool results go to that provider.",
    snippet: hostedSnippet,
  },
  {
    key: "local",
    icon: Laptop,
    title: "Local model",
    badge: "No API key",
    badgeIcon: Laptop,
    description: "Ollama, LM Studio, or another OpenAI-compatible server on this machine. Messages stay local.",
    snippet: localSnippet,
  },
] as const;

/** Shown in place of the conversation when no model can answer. */
export function AssistantSetup({ connection }: { connection: AssistantConnection }) {
  const [selected, setSelected] = useState<(typeof options)[number]["key"]>("local");
  const [copied, setCopied] = useState(false);
  const option = options.find((item) => item.key === selected) ?? options[0];
  const failed = connection.state === "failed";

  async function copy() {
    try {
      await navigator.clipboard.writeText(option.snippet);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      toast.error("Could not copy the snippet");
    }
  }

  return (
    <div className="mx-auto w-full max-w-3xl space-y-5 py-6">
      <div className={cn(
        "flex items-start gap-3 rounded-2xl border p-4",
        failed ? "border-status-error/30 bg-status-error/[0.06]" : "border-status-warning/30 bg-status-warning/[0.06]",
      )}>
        <span className={cn(
          "grid h-9 w-9 shrink-0 place-items-center rounded-xl",
          failed ? "bg-status-error/15 text-status-error" : "bg-status-warning/15 text-status-warning",
        )}>
          {failed ? <AlertTriangle className="h-4 w-4" /> : <PlugZap className="h-4 w-4" />}
        </span>
        <div className="min-w-0">
          <p className="text-[13px] font-semibold">
            {failed ? "The AI model could not be connected" : "No AI model is connected"}
          </p>
          <p className="mt-1 text-[12px] leading-relaxed text-muted-foreground">
            {failed
              ? "The host configured a chat client, but creating it failed. This usually means a missing API key or an unreachable endpoint. Check the host logs and configuration, then restart the application."
              : "This dashboard has no chat client, so the assistant cannot answer yet and nothing is sent anywhere. Connect a hosted model with an API key, or a local model that needs no key."}
          </p>
        </div>
      </div>

      <div className="grid gap-3 sm:grid-cols-2" role="radiogroup" aria-label="Model type">
        {options.map((item) => {
          const active = item.key === selected;
          const Icon = item.icon;
          const BadgeIcon = item.badgeIcon;
          return (
            <button
              key={item.key}
              type="button"
              role="radio"
              aria-checked={active}
              onClick={() => setSelected(item.key)}
              className={cn(
                "rounded-xl border p-4 text-left transition-colors",
                active ? "border-primary bg-primary/[0.07] ring-1 ring-primary/30" : "border-border bg-card hover:border-primary/40",
              )}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="flex items-center gap-2 text-[12.5px] font-semibold">
                  <Icon className={cn("h-4 w-4", active ? "text-primary" : "text-muted-foreground")} />
                  {item.title}
                </span>
                <span className="inline-flex items-center gap-1 rounded-full border border-border px-2 py-0.5 text-[10px] text-muted-foreground">
                  <BadgeIcon className="h-3 w-3" />{item.badge}
                </span>
              </div>
              <p className="mt-2 text-[11.5px] leading-relaxed text-muted-foreground">{item.description}</p>
            </button>
          );
        })}
      </div>

      <div className="overflow-hidden rounded-xl border border-border bg-surface-1">
        <div className="flex items-center justify-between gap-2 border-b border-border px-3 py-2">
          <span className="text-[10.5px] font-medium text-muted-foreground">Program.cs · {option.title.toLowerCase()}</span>
          <button
            type="button"
            onClick={() => void copy()}
            className="inline-flex h-7 items-center gap-1.5 rounded-md border border-border px-2 text-[10.5px] text-muted-foreground transition-colors hover:bg-surface-2 hover:text-foreground"
          >
            {copied ? <Check className="h-3 w-3 text-status-healthy" /> : <Copy className="h-3 w-3" />}
            {copied ? "Copied" : "Copy"}
          </button>
        </div>
        <pre className="overflow-x-auto p-4 font-mono text-[11.5px] leading-relaxed text-foreground/90 scrollbar-thin"><code>{option.snippet}</code></pre>
      </div>

      <p className="text-[11px] leading-relaxed text-muted-foreground">
        Any <code className="rounded bg-surface-2 px-1 py-0.5 text-[10.5px]">IChatClient</code> works if its model supports tool calling.
        Keep API keys in server configuration; the dashboard never sees them. Restart the application after changing the configuration.
      </p>
    </div>
  );
}
