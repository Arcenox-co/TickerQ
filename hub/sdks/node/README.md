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
    .withRequest({ to: '', subject: '', body: '' })
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

The default value provides both **type inference** and the **example JSON** sent to the Hub.

```ts
sdk.function('ProcessOrder', {
    priority: TickerTaskPriority.High,
    maxConcurrency: 3,
    requestType: 'OrderRequest',
})
    .withRequest({ orderId: 0, customerId: '', items: [''], total: 0 })
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

### With primitive request

```ts
sdk.function('ResizeImage')
    .withRequest('default-url')
    .handle(async (ctx, signal) => {
        console.log(ctx.request); // string
    });
```

## Function Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `cronExpression` | `string` | — | Cron schedule (6-field, second precision) |
| `priority` | `TickerTaskPriority` | `Normal` | `High`, `Normal`, `Low`, or `LongRunning` |
| `maxConcurrency` | `number` | `0` (unlimited) | Max parallel executions for this function |
| `requestType` | `string` | auto-detected | Type name sent to Hub for documentation |

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
    .withRequest({ to: '', subject: '' })
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
