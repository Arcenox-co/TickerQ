using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TickerQ")]
[assembly: InternalsVisibleTo("TickerQ.EntityFrameworkCore")]
[assembly: InternalsVisibleTo("TickerQ.MongoDB")]
[assembly: InternalsVisibleTo("TickerQ.Dashboard")]
[assembly: InternalsVisibleTo("TickerQ.Tests")]
[assembly: InternalsVisibleTo("TickerQ.SDK")]
[assembly: InternalsVisibleTo("TickerQ.RemoteExecutor")]
[assembly: InternalsVisibleTo("TickerQ.Instrumentation.OpenTelemetry")]
[assembly: InternalsVisibleTo("TickerQ.Caching.StackExchangeRedis")]
[assembly: InternalsVisibleTo("TickerQ.EntityFrameworkCore.Tests")]
[assembly: InternalsVisibleTo("TickerQ.Caching.StackExchangeRedis.Tests")]
[assembly: InternalsVisibleTo("TickerQ.MongoDB.Tests")]
// To be testable using NSubsitute
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]