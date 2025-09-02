# TickerQ

Robust. Adaptive. Precise.

TickerQ is a fast, reflection-free background task scheduler for .NET, built with source generators, EF Core integration, cron + time-based execution, and a real-time dashboard.

## Features
- Time and Cron Scheduling
- Stateless Core with source generator
- EF Core Persistence
- Live Dashboard UI
- Retry Policies & Throttling
- Dependency Injection support
- Multi-node distributed coordination

## Documentation Structure
- [Getting Started](getting-started.md)
- [Usage](usage.md)
- [Configuration](configuration.md)
- [API Reference](api-reference.md)
- [Dashboard](dashboard.md)
- [Dependency Injection](dependency-injection.md)
- [Entity Framework Core](entity-framework.md)
- [Samples](samples.md)
- [FAQ](faq.md)
- [Architecture](architecture.md)
- [Start Here](start-here.md)
- [Glossary](glossary.md)
- [Recipes](recipes.md)
- [Troubleshooting](troubleshooting.md)

For more details, see the [official docs](https://tickerq.arcenox.com/intro/what-is-tickerq.html).

---

## Comparison: TickerQ vs Hangfire & Quartz

TickerQ was designed to address limitations found in other .NET scheduling libraries:
- **Async/threading support:** TickerQ is truly async and highly performant.
- **EF Core integration:** Built-in persistence and recovery after restarts.
- **Stateless core:** Source generators and Roslyn analyzers provide compile-time safety and zero reflection.
- **Native DI:** Flexible job activators and plugin-style extensibility.
- **Modern dashboard:** Real-time job management and analytics.

For a deeper dive, see the [YouTube introduction](https://www.youtube.com/watch/x0dfj95Cj0U).

| Feature                | TickerQ         | Hangfire        | Quartz.NET      |
|------------------------|-----------------|-----------------|-----------------|
| Reflection-free        | ✅              | ❌              | ❌             |
| Compile-time safety    | ✅              | ❌              | ❌             |
| Native DI              | ✅              | ⚠️ Limited      | ⚠️ Limited     |
| Dashboard              | ✅              | ✅              | ✅             |
| EF Core persistence    | ✅              | ✅              | ✅             |
| Timer & Cron jobs      | ✅              | ✅              | ✅             |
| Distributed support    | ✅              | ⚠️ Limited      | ⚠️ Limited     |

**Note:** TickerQ is new and evolving. Some issues (e.g., timer job persistence, migrations) may affect production stability. Check the [GitHub issues](https://github.com/arcenox/TickerQ/issues) for the latest status.

Nick Chapsas Review
> For a full walkthrough, see the [YouTube video](https://www.youtube.com/watch/x0dfj95Cj0U).
