## Job Views & Analytics

The dashboard provides two main views:
- **Timer jobs**: Scheduled for specific times
- **Cron jobs**: Scheduled on recurring intervals

You can see job status, success/failure counts, next occurrence, and concurrency settings. The dashboard tracks all jobs and provides real-time analytics.

## Running & Managing Jobs

Jobs can be executed directly from the dashboard. You can:
- Select a function
- Set a cron/timer expression
- Add descriptions
- Configure retry logic and intervals
- Pass parameters/data objects

Monitor job execution, logs, and status directly in the dashboard UI.
# Dashboard

TickerQ provides a real-time dashboard UI for monitoring jobs, execution history, and system metrics.

## Features

## Scheduling Jobs from the Dashboard

You can schedule jobs directly from the dashboard UI:
- Select a function to run
- Set a description
- Configure retries and backoff intervals (e.g., 1, 2, 5, 10 minutes)
- Pass a request object if your job accepts parameters
- Choose to execute immediately or at a future date

The dashboard provides real-time feedback on job execution, including status, parameters, and history. Scheduled jobs can be edited, retried, or triggered on demand.

## Setup
```csharp
options.AddDashboard(uiopt =>
{
    uiopt.BasePath = "/tickerq-dashboard";
    uiopt.EnableBasicAuth = true;
});
```

See [Dashboard Overview](https://tickerq.arcenox.com/intro/dashboard-overview.html).
