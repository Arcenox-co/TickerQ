# TickerQ documentation dashboard demo

This local-only fixture produces the screenshots used by the 5.x documentation. It exercises
the current source projects, a host-owned SQLite migration, persisted execution logs, saved work,
integrations, clustering, progress, grouped chains, request/result payloads, and the optional
assistant history tables.

The host applies its checked-in `InitialTickerQSchema` migration at startup. That migration contains
the complete current 17-table EF model, including execution-log, saved-work, cluster-control, and
assistant tables. Use a disposable database: the seed deliberately creates failed and skipped work.

Build once, then start the primary node:

```bash
export TICKERQ_LICENSE_PATH=/absolute/path/to/tickerq.tqlicense
export DOCS_DEMO_DB=/tmp/tickerq-docs-demo/tickerq-docs-demo.db
export DOCS_DEMO_KEYS=/tmp/tickerq-docs-demo/data-protection
dotnet build samples/TickerQ.Docs.Demo/TickerQ.Docs.Demo.csproj -c Release
DOCS_DEMO_PORT=5290 DOCS_DEMO_NODE=docs-eu-1 \
  dotnet run --no-build -c Release --project samples/TickerQ.Docs.Demo
```

To reproduce the Nodes page, start a second terminal against the same database and Data Protection
key ring:

```bash
export TICKERQ_LICENSE_PATH=/absolute/path/to/tickerq.tqlicense
export DOCS_DEMO_DB=/tmp/tickerq-docs-demo/tickerq-docs-demo.db
export DOCS_DEMO_KEYS=/tmp/tickerq-docs-demo/data-protection
DOCS_DEMO_PORT=5291 DOCS_DEMO_NODE=docs-eu-2 \
  dotnet run --no-build -c Release --project samples/TickerQ.Docs.Demo
```

Seed the runtime once, then open the dashboard:

```bash
curl -X POST http://127.0.0.1:5290/demo/seed
```

The seed adds four schedule policies (two of them composed), cron tickers that use them, a disabled
cron ticker, and a one-time job that the Business hours policy moves to the next allowed time.

Open `http://127.0.0.1:5290/tickerq/dashboard` and sign in with `admin` / `admin`. Integration
destinations used for documentation must remain disabled so screenshot preparation never sends an
external webhook. The bundled `DocsDemoChatClient` is deterministic and makes no network calls;
replace it with a real `IChatClient` in application code. It reports itself as a local model, and
prompts that ask to list or create schedule policies call the dashboard's real tools, so the chat shows
genuine proposal cards. Set `DOCS_DEMO_ASSISTANT=off` to show the dashboard without a connected model.
