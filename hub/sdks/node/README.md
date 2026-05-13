# @tickerq/sdk

Node.js SDK for [TickerQ](https://tickerq.net) — run Node.js jobs from TickerQ Hub through outbound gRPC streams.

## Installation

```bash
npm install @tickerq/sdk
```

**Requirements:** Node.js >= 18

## Quick Start

```ts
import { createTickerSdk, TickerTaskPriority } from '@tickerq/sdk';

// 1. Initialize SDK
const sdk = createTickerSdk({
    apiKey: 'tq_sdk_...',
    nodeName: 'my-node',
});

// 2. Register functions
sdk.function('SendEmail', { priority: TickerTaskPriority.High })
    .withRequest({ to: '', subject: '', body: '' })
    .handle(async (ctx, signal) => {
        console.log(`Sending email to ${ctx.request.to}`);
    });

// 3. Start outbound Hub/control + scheduler worker streams
await sdk.start();
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
const sdk = createTickerSdk({
    apiKey: 'tq_sdk_...',            // Required — Hub-issued SDK token
    nodeName: 'my-node',             // Optional — defaults to host name
    timeoutMs: 30000,                // Optional — gRPC operation timeout (default: 30s)
    allowSelfSignedCerts: true,      // Optional — local scheduler/dev only
});
```

## Hub And Remote Execution

The Node SDK matches the .NET SDK remote-execution model:

- Startup calls `HubService.SyncNodesFunctions` on `https://grpc.hub.tickerq.net/`.
- The SDK opens a persistent Hub control stream for resync, remove-function, signature rotation, and heartbeat commands.
- The SDK opens a scheduler worker stream to the `ApplicationUrl` returned by Hub. Dispatch, cancellation, ticker CRUD, status updates, and request payloads flow over that stream.
- No inbound HTTP server, callback URL, or API secret is required.

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
        ctx.log.info('Starting MyJob'); // forwarded to dashboard logs

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

const sdk = createTickerSdk({
    apiKey: '...',
    nodeName: '...',
}, logger);
```

## Runtime Dependencies

The SDK uses `@grpc/grpc-js` and `@grpc/proto-loader` for Hub and scheduler streams.

## License

Dual-licensed under [MIT](LICENSE) and [Apache 2.0](LICENSE). Choose whichever you prefer.
