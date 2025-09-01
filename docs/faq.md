## Why do I get migration or database errors?
Some migration errors (e.g., referential actions, rows not affected) may require manual changes to migration files (set `OnDelete` to `NoAction`). See GitHub issues for more details.

## Is TickerQ ready for production?
TickerQ is rapidly evolving and may have issues with timer-based triggers or migrations. For mission-critical production use, evaluate stability and check the latest issues and releases.
# FAQ

## Why am I getting 403 responses from the dashboard?
Check for endpoint filters that may block TickerQ's dashboard. See [issue #155](https://github.com/Arcenox-co/TickerQ/issues/155#issuecomment-3175214745).

## How do I gracefully cancel jobs?
Use `CancellationToken` in your job methods.

## How do I pass dynamic data to jobs?
Use the `Request` object.

More FAQs at [TickerQ FAQ](https://tickerq.arcenox.com/faq/index.html).

## Can I edit jobs after scheduling?
Jobs scheduled via the dashboard can be edited, but changes may not persist after a service restart unless the job is re-created and persisted.

## How does TickerQ handle missed jobs or cancellations?
TickerQ supports application lifetime hooks to handle missed jobs and cancellations during restarts, ensuring reliability.

## How do I handle exceptions in jobs?
You can set a global exception handler in your options:
```csharp
options.SetExceptionHandler<MyExceptionHandlerClass>();
```
This allows you to manage failures and custom error logic for scheduled jobs.

## How do priorities affect execution order?
Functions decorated with `TickerTaskPriority.High` are scheduled ahead of `Normal` and `Low`. `LongRunning` tasks are isolated on the default TaskScheduler.

## How do I change how often missed jobs are scanned?
Call `UpdateMissedJobCheckDelay(TimeSpan)` (minimum 30 seconds; default 1 minute).

## How do I seed initial jobs?
Use `UseTickerSeeder` inside EF Core operational store configuration to create bootstrap tickers.

## What is `InstanceIdentifier` used for?
Set via `SetInstanceIdentifier` to tag a node in distributed scenarios (e.g., diagnostics, targeted cleanup, or node-aware cancellation).
