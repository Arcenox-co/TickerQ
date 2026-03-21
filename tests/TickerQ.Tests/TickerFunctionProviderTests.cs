using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Tests for <see cref="TickerFunctionProvider"/>.
/// Because the provider is fully static, each test must reset state via <see cref="ResetProvider"/>.
/// </summary>
[Collection("TickerFunctionProviderState")]
public class TickerFunctionProviderTests : IDisposable
{
    public TickerFunctionProviderTests()
    {
        ResetProvider();
    }

    public void Dispose()
    {
        ResetProvider();
    }

    /// <summary>
    /// Clears every static field (public frozen dictionaries + private callback delegates)
    /// so that each test starts with a clean slate.
    /// </summary>
    private static void ResetProvider()
    {
        var type = typeof(TickerFunctionProvider);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        type.GetField("TickerFunctions", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>.Empty);
        type.GetField("TickerFunctionRequestTypes", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, Type)>.Empty);
        type.GetField("TickerFunctionRequestInfos", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, string)>.Empty);
        type.GetField("_functionRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestTypeRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestInfoRegistrations", flags)!.SetValue(null, null);
        type.GetProperty("IsBuilt", flags)!.SetValue(null, false);
    }

    private static Task NoOpDelegate(CancellationToken ct, IServiceProvider sp, TickerQ.Utilities.Base.TickerFunctionContext ctx)
        => Task.CompletedTask;

    // ---------------------------------------------------------------
    // 1. RegisterFunctions – register a function, verify after Build
    // ---------------------------------------------------------------
    [Fact]
    public void RegisterFunctions_And_Build_FunctionExists()
    {
        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["MyFunc"] = ("*/5 * * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        };

        TickerFunctionProvider.RegisterFunctions(functions);
        TickerFunctionProvider.Build();

        Assert.NotNull(TickerFunctionProvider.TickerFunctions);
        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("MyFunc"));
        Assert.Equal("*/5 * * * *", TickerFunctionProvider.TickerFunctions["MyFunc"].cronExpression);
    }

    // ---------------------------------------------------------------
    // 2. RegisterFunctions with capacity hint – doesn't break anything
    // ---------------------------------------------------------------
    [Fact]
    public void RegisterFunctions_WithCapacity_DoesNotThrow()
    {
        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["CapFunc"] = ("0 0 * * *", TickerTaskPriority.High, NoOpDelegate, 0)
        };

        TickerFunctionProvider.RegisterFunctions(functions, 100);
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("CapFunc"));
    }

    // ---------------------------------------------------------------
    // 3. RegisterRequestType – verify after Build
    // ---------------------------------------------------------------
    [Fact]
    public void RegisterRequestType_And_Build_TypeExists()
    {
        var requestTypes = new Dictionary<string, (string, Type)>
        {
            ["ReqFunc"] = ("MyRequestType", typeof(string))
        };

        TickerFunctionProvider.RegisterRequestType(requestTypes);
        TickerFunctionProvider.Build();

        Assert.NotNull(TickerFunctionProvider.TickerFunctionRequestTypes);
        Assert.True(TickerFunctionProvider.TickerFunctionRequestTypes.ContainsKey("ReqFunc"));
        Assert.Equal(typeof(string), TickerFunctionProvider.TickerFunctionRequestTypes["ReqFunc"].Item2);
    }

    // ---------------------------------------------------------------
    // 4. RegisterRequestInfo – verify after Build
    // ---------------------------------------------------------------
    [Fact]
    public void RegisterRequestInfo_And_Build_InfoExists()
    {
        var infos = new Dictionary<string, (string RequestType, string RequestExampleJson)>
        {
            ["InfoFunc"] = ("SomeType", "{\"id\":1}")
        };

        TickerFunctionProvider.RegisterRequestInfo(infos);
        TickerFunctionProvider.Build();

        Assert.NotNull(TickerFunctionProvider.TickerFunctionRequestInfos);
        Assert.True(TickerFunctionProvider.TickerFunctionRequestInfos.ContainsKey("InfoFunc"));
        Assert.Equal("{\"id\":1}", TickerFunctionProvider.TickerFunctionRequestInfos["InfoFunc"].RequestExampleJson);
    }

    // ---------------------------------------------------------------
    // 5. Build – creates frozen dictionaries, callbacks execute
    // ---------------------------------------------------------------
    [Fact]
    public async Task Build_CreatesFrozenDictionaries_And_CallbacksExecute()
    {
        bool callbackExecuted = false;

        // Use the function delegate to track execution during Build
        TickerFunctionDelegate trackingDelegate = (ct, sp, ctx) =>
        {
            callbackExecuted = true;
            return Task.CompletedTask;
        };

        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["TrackFunc"] = ("0 * * * *", TickerTaskPriority.Low, trackingDelegate, 0)
        };

        TickerFunctionProvider.RegisterFunctions(functions);
        TickerFunctionProvider.Build();

        // Frozen dictionaries should be non-null
        Assert.NotNull(TickerFunctionProvider.TickerFunctions);
        Assert.NotNull(TickerFunctionProvider.TickerFunctionRequestTypes);
        Assert.NotNull(TickerFunctionProvider.TickerFunctionRequestInfos);

        // The stored delegate should be the one we registered
        var storedDelegate = TickerFunctionProvider.TickerFunctions["TrackFunc"].Delegate;
        Assert.NotNull(storedDelegate);

        // Execute the delegate to confirm it is our tracking delegate
        await storedDelegate(CancellationToken.None, null!, null!);
        Assert.True(callbackExecuted);
    }

    // ---------------------------------------------------------------
    // 6. Build called twice – second Build doesn't crash or lose data
    // ---------------------------------------------------------------
    [Fact]
    public void Build_CalledTwice_DoesNotCrashOrLoseData()
    {
        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["DoubleFunc"] = ("0 0 1 * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        };

        TickerFunctionProvider.RegisterFunctions(functions);
        TickerFunctionProvider.Build();

        // Second Build should not throw and data should still be present
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("DoubleFunc"));
        Assert.Single(TickerFunctionProvider.TickerFunctions);
    }

    // ---------------------------------------------------------------
    // 7. UpdateCronExpressionsFromIConfiguration – mock IConfiguration
    // ---------------------------------------------------------------
    [Fact]
    public void UpdateCronExpressionsFromIConfiguration_UpdatesExpressions()
    {
        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["CronFunc"] = ("%CronSettings:Schedule%", TickerTaskPriority.High, NoOpDelegate, 0)
        };

        TickerFunctionProvider.RegisterFunctions(functions);

        var configuration = Substitute.For<IConfiguration>();
        configuration["CronSettings:Schedule"].Returns("0 30 * * *");

        TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(configuration);
        TickerFunctionProvider.Build();

        Assert.Equal("0 30 * * *", TickerFunctionProvider.TickerFunctions["CronFunc"].cronExpression);
    }

    [Fact]
    public void UpdateCronExpressionsFromIConfiguration_NoConfigValue_KeepsOriginal()
    {
        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["KeepFunc"] = ("%Missing:Key%", TickerTaskPriority.Normal, NoOpDelegate, 0)
        };

        TickerFunctionProvider.RegisterFunctions(functions);

        var configuration = Substitute.For<IConfiguration>();
        configuration["Missing:Key"].Returns((string)null!);

        TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(configuration);
        TickerFunctionProvider.Build();

        // Original expression (with %) should be preserved when config key not found
        Assert.Equal("%Missing:Key%", TickerFunctionProvider.TickerFunctions["KeepFunc"].cronExpression);
    }

    // ---------------------------------------------------------------
    // 8. Function lookup after Build – functions retrievable
    // ---------------------------------------------------------------
    [Fact]
    public void FunctionLookup_AfterBuild_RetrievableByKey()
    {
        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["Alpha"] = ("0 0 * * *", TickerTaskPriority.High, NoOpDelegate, 0),
            ["Beta"]  = ("0 0 1 * *", TickerTaskPriority.Low, NoOpDelegate, 0),
            ["Gamma"] = ("*/10 * * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        };

        TickerFunctionProvider.RegisterFunctions(functions);
        TickerFunctionProvider.Build();

        Assert.Equal(3, TickerFunctionProvider.TickerFunctions.Count);
        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("Alpha"));
        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("Beta"));
        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("Gamma"));

        Assert.Equal(TickerTaskPriority.High, TickerFunctionProvider.TickerFunctions["Alpha"].Priority);
        Assert.Equal(TickerTaskPriority.Low, TickerFunctionProvider.TickerFunctions["Beta"].Priority);
    }

    // ---------------------------------------------------------------
    // Null-argument guard tests
    // ---------------------------------------------------------------
    [Fact]
    public void RegisterFunctions_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TickerFunctionProvider.RegisterFunctions(null!));
    }

    [Fact]
    public void RegisterRequestType_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TickerFunctionProvider.RegisterRequestType(null!));
    }

    [Fact]
    public void RegisterRequestInfo_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TickerFunctionProvider.RegisterRequestInfo(null!));
    }

    // ---------------------------------------------------------------
    // Build with no registrations – produces empty dictionaries
    // ---------------------------------------------------------------
    [Fact]
    public void Build_NoRegistrations_ProducesEmptyDictionaries()
    {
        TickerFunctionProvider.Build();

        Assert.NotNull(TickerFunctionProvider.TickerFunctions);
        Assert.Empty(TickerFunctionProvider.TickerFunctions);
        Assert.NotNull(TickerFunctionProvider.TickerFunctionRequestTypes);
        Assert.Empty(TickerFunctionProvider.TickerFunctionRequestTypes);
        Assert.NotNull(TickerFunctionProvider.TickerFunctionRequestInfos);
        Assert.Empty(TickerFunctionProvider.TickerFunctionRequestInfos);
    }
}
