using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TickerQ")]
[assembly: InternalsVisibleTo("TickerQ.EntityFrameworkCore")]
[assembly: InternalsVisibleTo("TickerQ.Dashboard")]
[assembly: InternalsVisibleTo("TickerQ.Tests")]
[assembly: InternalsVisibleTo("TickerQ.SDK")]
[assembly: InternalsVisibleTo("TickerQ.RemoteExecutor")]
[assembly: InternalsVisibleTo("TickerQ.Instrumentation.OpenTelemetry")]
[assembly: InternalsVisibleTo("TickerQ.Caching.StackExchangeRedis")]
// To be testable using NSubsitute
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]