using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.DependencyInjection;

namespace TickerQ.Caching.StackExchangeRedis.Tests.DependencyInjection;

public class ServiceExtensionTests
{
    [Fact]
    public void AddStackExchangeRedis_NoConfiguration_ThrowsInvalidOperation()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // ExternalProviderConfigServiceAction is invoked inside AddTickerQ, so the exception
        // is thrown there — not lazily during BuildServiceProvider.
        Assert.Throws<InvalidOperationException>(() =>
        {
            // No Configuration, no ConfigurationOptions, no ConnectionMultiplexer
            services.AddTickerQ(options =>
            {
                options.AddStackExchangeRedis(_ => { });
            });
        });
    }

    [Fact]
    public void AddStackExchangeRedis_WithConfigurationString_DoesNotThrow()
    {
        // Regression: existing string-based configuration must still be accepted.
        var services = new ServiceCollection();
        services.AddLogging();

        var exception = Record.Exception(() =>
        {
            services.AddTickerQ(options =>
            {
                options.AddStackExchangeRedis(redis =>
                {
                    redis.Configuration = "localhost:6379,abortConnect=false";
                });
            });
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AddStackExchangeRedis_WithConnectionMultiplexer_DoesNotThrow()
    {
        // New capability: providing a pre-built IConnectionMultiplexer should be sufficient.
        var services = new ServiceCollection();
        services.AddLogging();

        var mockMultiplexer = Substitute.For<IConnectionMultiplexer>();

        var exception = Record.Exception(() =>
        {
            services.AddTickerQ(options =>
            {
                options.AddStackExchangeRedis(redis =>
                {
                    redis.ConnectionMultiplexer = mockMultiplexer;
                });
            });
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AddStackExchangeRedis_WithExistingMultiplexerInDI_PreservesExistingRegistration()
    {
        // Keyed services: TickerQ registers IConnectionMultiplexer under key "tickerq".
        // It never touches the host app's unkeyed IConnectionMultiplexer slot.
        // With the OLD code, AddSingleton (unkeyed) would have been appended — DI resolves the LAST
        // descriptor, so the host's registration would be silently overridden. This proves it is not.
        var services = new ServiceCollection();
        services.AddLogging();

        var preRegisteredMultiplexer = Substitute.For<IConnectionMultiplexer>();

        // Host app registers IConnectionMultiplexer BEFORE AddTickerQ
        services.AddSingleton(preRegisteredMultiplexer);

        services.AddTickerQ(options =>
        {
            options.AddStackExchangeRedis(redis =>
            {
                redis.Configuration = "localhost:6379,abortConnect=false";
            });
        });

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IConnectionMultiplexer>();

        // TickerQ must NOT have touched the unkeyed IConnectionMultiplexer slot
        Assert.True(ReferenceEquals(preRegisteredMultiplexer, resolved),
            "The pre-registered IConnectionMultiplexer was overridden. TickerQ should use its own keyed slot and leave the host's unkeyed registration untouched.");
    }

    [Fact]
    public void AddStackExchangeRedis_HostAndTickerQMultiplexers_AreIndependentKeyedSlots()
    {
        // Proves that the host's unkeyed IConnectionMultiplexer and TickerQ's keyed one
        // are completely independent — different instances, different DI slots.
        var services = new ServiceCollection();
        services.AddLogging();

        var hostMultiplexer = Substitute.For<IConnectionMultiplexer>();
        var tickerQMultiplexer = Substitute.For<IConnectionMultiplexer>();

        services.AddSingleton(hostMultiplexer);

        services.AddTickerQ(options =>
        {
            options.AddStackExchangeRedis(redis =>
            {
                redis.ConnectionMultiplexer = tickerQMultiplexer;
            });
        });

        var provider = services.BuildServiceProvider();
        var resolvedHost = provider.GetRequiredService<IConnectionMultiplexer>();
        var resolvedTickerQ = provider.GetRequiredKeyedService<IConnectionMultiplexer>("tickerq");

        Assert.True(ReferenceEquals(hostMultiplexer, resolvedHost),
            "The host's unkeyed IConnectionMultiplexer should be unchanged.");
        Assert.True(ReferenceEquals(tickerQMultiplexer, resolvedTickerQ),
            "TickerQ's keyed IConnectionMultiplexer should be the instance passed via options.");
        Assert.False(ReferenceEquals(resolvedHost, resolvedTickerQ),
            "Host and TickerQ multiplexers must be independent — different instances.");
    }

    [Fact]
    public void AddStackExchangeRedis_WithConnectionMultiplexerFactory_DoesNotThrow()
    {
        // ConnectionMultiplexerFactory is inherited from RedisCacheOptions. Callers who set
        // only this property (the standard RedisCacheOptions pattern) must not hit the
        // InvalidOperationException — validation must accept it as a fourth valid option.
        var services = new ServiceCollection();
        services.AddLogging();

        var mockMultiplexer = Substitute.For<IConnectionMultiplexer>();

        var exception = Record.Exception(() =>
        {
            services.AddTickerQ(options =>
            {
                options.AddStackExchangeRedis(redis =>
                {
                    redis.ConnectionMultiplexerFactory = () => Task.FromResult(mockMultiplexer);
                });
            });
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AddStackExchangeRedis_WithConnectionMultiplexerFactory_ResolvesInstanceFromFactory()
    {
        // The keyed IConnectionMultiplexer singleton must be built by invoking the factory
        // (mirroring what Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache does
        // in its sync Connect() path via .GetAwaiter().GetResult()).
        // The resolved instance must be the exact one the factory returned.
        var services = new ServiceCollection();
        services.AddLogging();

        var mockMultiplexer = Substitute.For<IConnectionMultiplexer>();

        services.AddTickerQ(options =>
        {
            options.AddStackExchangeRedis(redis =>
            {
                redis.ConnectionMultiplexerFactory = () => Task.FromResult(mockMultiplexer);
            });
        });

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IConnectionMultiplexer>("tickerq");

        Assert.True(ReferenceEquals(mockMultiplexer, resolved),
            "The resolved keyed IConnectionMultiplexer should be the exact instance returned by ConnectionMultiplexerFactory.");
    }


    [Fact]
    public void AddStackExchangeRedis_WithConnectionMultiplexer_ResolvesProvidedInstance()
    {
        // TickerQRedisOptionBuilder had no ConnectionMultiplexer property,
        // so there was no way to pass a pre-built multiplexer at all.
        // The fix adds the property and registers it under the "tickerq" keyed slot.
        var services = new ServiceCollection();
        services.AddLogging();

        var mockMultiplexer = Substitute.For<IConnectionMultiplexer>();

        services.AddTickerQ(options =>
        {
            options.AddStackExchangeRedis(redis =>
            {
                redis.ConnectionMultiplexer = mockMultiplexer;
            });
        });

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IConnectionMultiplexer>("tickerq");

        Assert.True(ReferenceEquals(mockMultiplexer, resolved),
            "The resolved keyed IConnectionMultiplexer should be the exact instance passed via options.ConnectionMultiplexer.");
    }

    [Fact]
    public void AddStackExchangeRedis_WithConnectionMultiplexer_RegistersKeyedDistributedCache()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mockMultiplexer = Substitute.For<IConnectionMultiplexer>();

        services.AddTickerQ(options =>
        {
            options.AddStackExchangeRedis(redis =>
            {
                redis.ConnectionMultiplexer = mockMultiplexer;
            });
        });

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IDistributedCache) &&
            d.IsKeyedService &&
            d.ServiceKey is "tickerq");

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddStackExchangeRedis_WithConnectionMultiplexer_RegistersIConnectionMultiplexerAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mockMultiplexer = Substitute.For<IConnectionMultiplexer>();

        services.AddTickerQ(options =>
        {
            options.AddStackExchangeRedis(redis =>
            {
                redis.ConnectionMultiplexer = mockMultiplexer;
            });
        });

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IConnectionMultiplexer) &&
            d.IsKeyedService &&
            d.ServiceKey is "tickerq");

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
