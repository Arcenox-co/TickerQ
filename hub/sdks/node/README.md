# @tickerq/sdk

Node.js SDK for [TickerQ](https://tickerq.net) — connect your Node.js application to TickerQ Hub for distributed job scheduling.

## Installation

```bash
npm install @tickerq/sdk
```

**Requirements:** Node.js >= 18

## Quick Start

```ts
import express from 'express';
import { TickerQSdk, TickerTaskPriority } from '@tickerq/sdk';

const app = express();
app.use(express.raw({ type: 'application/json' }));

// 1. Initialize SDK
const sdk = new TickerQSdk((opts) =>
    opts
        .setApiKey('your-api-key')
        .setApiSecret('your-api-secret')
        .setCallbackUri('https://your-app.com')
        .setNodeName('my-node'),
);

// 2. Register functions
sdk.function('SendEmail', { priority: TickerTaskPriority.High })
    .withRequest(
        { to: '', subject: '', body: '' },
        { schema: { type: 'object', required: ['to', 'subject', 'body'], properties: {
            to: { type: 'string' }, subject: { type: 'string' }, body: { type: 'string' },
        } } },
    )
    .handle(async (ctx, signal) => {
        console.log(`Sending email to ${ctx.request.to}`);
    });

// 3. Mount endpoints & start
sdk.expressHandlers().mount(app);

await sdk.start();
app.listen(3000);
```

## Registering Functions

### With typed request

The default value provides TypeScript inference and the legacy example. Add `requestContract`
to publish an authoritative Draft 2020-12 schema. The SDK canonicalizes the schema and computes
the same deterministic `sha256:` fingerprint as the .NET runtime.

```ts
sdk.function('ProcessOrder', {
    priority: TickerTaskPriority.High,
    maxConcurrency: 3,
    requestType: 'OrderRequest',
})
    .withRequest(
      { orderId: 0, customerId: '', items: [''], total: 0 },
      {
        schema: {
            type: 'object',
            additionalProperties: false,
            required: ['orderId', 'customerId', 'items', 'total'],
            properties: {
                orderId: { type: 'integer', minimum: 1 },
                customerId: { type: 'string', minLength: 1 },
                items: { type: 'array', items: { type: 'string' } },
                total: { type: 'number', minimum: 0 },
            },
        },
        examples: [{
            key: 'standard-order',
            summary: 'Typical order',
            value: { orderId: 42, customerId: 'customer-7', items: ['sku-1'], total: 19.99 },
        }],
      },
    )
    .handle(async (ctx, signal) => {
        ctx.request.orderId;    // number
        ctx.request.customerId; // string
        ctx.request.items;      // string[]
    });
```

### Without request

```ts
sdk.function('DatabaseCleanup', {
    cronExpression: '0 0 3 * * *',
    priority: TickerTaskPriority.LongRunning,
})
    .handle(async (ctx, signal) => {
        console.log(`Running cleanup for ${ctx.functionName}`);
    });
```

### Typed results and direct-parent results

Use `withResult` to publish a canonical result contract and type `ctx.setResult`. Results are JSON-encoded,
base64-wrapped in a versioned envelope, and included only in a final successful status update. `null` is an
explicit present result; not calling `setResult` means no result. Each execution attempt has a fresh sink.

```ts
sdk.function('CreateReport')
    .withResult(
        { reportId: '' },
        { schema: { type: 'object', required: ['reportId'], properties: {
            reportId: { type: 'string' },
        } } },
    )
    .handle(async (ctx) => {
        const parent = ctx.getParentResult<{ sourceId: string }>();
        if (ctx.hasParentResult) console.log(parent?.sourceId);
        ctx.setResult({ reportId: 'report-1' });
    });
```

Only the direct parent's result is exposed. Unknown envelope versions, invalid base64, non-JSON media types
when deserializing, and decoded payloads over 1 MiB are rejected.

### With an object-wrapped scalar request

Canonical request contracts require an object-root schema. Wrap scalar values in a named property:

```ts
sdk.function('ResizeImage')
    .withRequest(
        { url: '' },
        { schema: { type: 'object', required: ['url'], properties: { url: { type: 'string' } } } },
    )
    .handle(async (ctx, signal) => {
        console.log(ctx.request.url); // string
    });
```

## Function Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `cronExpression` | `string` | — | Cron schedule (6-field, second precision) |
| `priority` | `TickerTaskPriority` | `Normal` | `High`, `Normal`, `Low`, or `LongRunning` |
| `maxConcurrency` | `number` | `0` (unlimited) | Max parallel executions for this function |
| `requestType` | `string` | auto-detected | Type name sent to Hub for documentation |
| `requestContract` | `TickerRequestContractDefinition` | — | Authoritative JSON Schema, examples, media type, and contract version sent to Hub |
| `resultType` | `string` | auto-detected | Result type name sent to Hub for documentation |
| `resultContract` | `TickerResultContractDefinition` | — | Authoritative result JSON Schema metadata (normally set by `withResult`) |

Browser-side validation is advisory. The scheduler validates the serialized payload again before
any persistence write. Schema or version drift is checked again before invocation and does not
consume a retry attempt.

## SDK Configuration

```ts
const sdk = new TickerQSdk((opts) =>
    opts
        .setApiKey('your-api-key')          // Required — Hub API key
        .setApiSecret('your-api-secret')    // Required — Hub API secret
        .setCallbackUri('https://...')       // Required — URL where Hub sends execution callbacks
        .setNodeName('my-node')             // Required — Unique node identifier
        .setTimeoutMs(30000)                // Optional — HTTP timeout (default: 30s)
        .setAllowSelfSignedCerts(true),     // Optional — Skip TLS verification (dev only)
);
```

## Mounting Endpoints

The SDK exposes two HTTP endpoints that the Hub calls:

- `POST /execute` — Receives function execution requests
- `POST /resync` — Re-syncs function registry with the Hub

### Express

```ts
sdk.expressHandlers().mount(app);

// Or with a prefix
sdk.expressHandlers('/tickerq').mount(app);
```

### Raw Node.js HTTP

```ts
import { createServer } from 'node:http';

const handler = sdk.createHandler();
const server = createServer(handler);
server.listen(3000);
```

## Lifecycle

```ts
// Start — freezes function registry, syncs with Hub
await sdk.start();

// Check status
console.log(sdk.isStarted);

// Graceful shutdown — waits for running tasks to complete
await sdk.stop();         // default 30s timeout
await sdk.stop(60_000);   // custom timeout
```

## Handler Context

Every handler receives a `TickerFunctionContext` and an `AbortSignal`:

```ts
sdk.function('MyJob')
    .handle(async (ctx, signal) => {
        ctx.id;            // string — unique execution ID
        ctx.functionName;  // string — registered function name
        ctx.type;          // TickerType — TimeTicker or CronTickerOccurrence
        ctx.retryCount;    // number — current retry attempt
        ctx.scheduledFor;  // Date — when this execution was scheduled
        ctx.isDue;         // boolean

        // Use signal for cancellation
        if (signal.aborted) return;
    });
```

With a typed request:

```ts
sdk.function('SendEmail')
    .withRequest(
        { to: '', subject: '' },
        { schema: { type: 'object', required: ['to', 'subject'], properties: {
            to: { type: 'string' }, subject: { type: 'string' },
        } } },
    )
    .handle(async (ctx, signal) => {
        ctx.request.to;      // string — fully typed
        ctx.request.subject; // string
    });
```

## Priority Levels

| Priority | Behavior |
|----------|----------|
| `TickerTaskPriority.High` | Executed first |
| `TickerTaskPriority.Normal` | Default priority |
| `TickerTaskPriority.Low` | Executed when no higher priority tasks are queued |
| `TickerTaskPriority.LongRunning` | Bypasses worker concurrency limit |

## Custom Logger

```ts
import type { TickerQLogger } from '@tickerq/sdk';

const logger: TickerQLogger = {
    info: (msg, ...args) => console.log(msg, ...args),
    warn: (msg, ...args) => console.warn(msg, ...args),
    error: (msg, ...args) => console.error(msg, ...args),
};

const sdk = new TickerQSdk((opts) => opts
    .setApiKey('...')
    .setApiSecret('...')
    .setCallbackUri('...')
    .setNodeName('...'),
    logger,
);
```

## Zero Dependencies

The SDK has **no runtime dependencies**. It uses only Node.js built-in modules (`node:http`, `node:https`, `node:crypto`). Express is an optional peer dependency for the `expressHandlers()` convenience method.

## License

Dual-licensed under [MIT](LICENSE) and [Apache 2.0](LICENSE). Choose whichever you prefer.
