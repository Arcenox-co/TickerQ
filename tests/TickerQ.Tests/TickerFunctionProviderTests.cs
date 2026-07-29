using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
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
        type.GetField("_snapshot", flags)!.SetValue(null, TickerFunctionRegistrySnapshot.Empty);
        type.GetField("_functionRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestTypeRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestInfoRegistrations", flags)!.SetValue(null, null);
        type.GetField("_runtimeRequestRegistrations", flags)!.SetValue(null, null);
        type.GetField("_runtimeResultRegistrations", flags)!.SetValue(null, null);
        ((System.Collections.IList)type.GetField("_pendingDescriptors", flags)!.GetValue(null)!).Clear();
        type.GetProperty("IsBuilt", flags)!.SetValue(null, false);
    }

    private static Task NoOpDelegate(CancellationToken ct, IServiceProvider sp, TickerQ.Utilities.Base.TickerFunctionContext ctx)
        => Task.CompletedTask;

    private sealed class RuntimeRequest
    {
        public string OrderId { get; set; }
    }

    [Fact]
    public void GeneratedResultPublication_UsesDescriptorFingerprintInEnvelope()
    {
        const string functionName = "GeneratedResultFunc";
        TickerFunctionProvider.RegisterResultTypeInfoResolver(
            new Dictionary<string, (Type ResultType, Func<JsonSerializerOptions, JsonTypeInfo> Resolve)>
            {
                [functionName] = (typeof(ResultPayload), _ => ResultJsonContext.Default.ResultPayload)
            });
        TickerFunctionProvider.RegisterDescriptors(
            new Dictionary<string, TickerFunctionDescriptor>
            {
                [functionName] = new TickerFunctionDescriptor(
                    functionName,
                    result: new TickerResultContract(
                        typeof(ResultPayload).FullName!,
                        schemaJson: "{\"type\":\"object\"}"))
            },
            "test");
        TickerFunctionProvider.Build();

        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors[functionName];
        var context = new TickerQ.Utilities.Base.TickerFunctionContext();
        var typeInfo = TickerFunctionProvider.GetResultTypeInfo<ResultPayload>(functionName);
        var resultContract = TickerFunctionProvider.GetResultContract(functionName);
        context.SetResult(new ResultPayload { Value = 42 }, typeInfo, resultContract);

        Assert.Equal(descriptor.Result!.Fingerprint, descriptor.Result.ContractId);
        Assert.Equal(descriptor.Result.ContractId, context.ResultSink.Envelope.ContractId);
        Assert.Equal(descriptor.Result.TypeName, context.ResultSink.Envelope.ContractType);
    }

    [Fact]
    public void ResultMetadata_SurvivesRepeatedBuild_AndBacksImmutableSnapshot()
    {
        const string functionName = "ResultFunc";
        var typeInfo = ResultJsonContext.Default.ResultPayload;
        TickerFunctionProvider.RegisterResultTypeInfoResolver(
            new Dictionary<string, (Type ResultType, Func<JsonSerializerOptions, JsonTypeInfo> Resolve)>
            {
                [functionName] = (typeof(ResultPayload), _ => typeInfo)
            });
        TickerFunctionProvider.RegisterDescriptors(
            new Dictionary<string, TickerFunctionDescriptor>
            {
                [functionName] = new TickerFunctionDescriptor(
                    functionName,
                    result: new TickerResultContract(
                        typeof(ResultPayload).FullName!,
                        schemaJson: "{\"type\":\"object\"}"))
            },
            "test");

        TickerFunctionProvider.Build();
        var captured = TickerFunctionProvider.Snapshot;

        Assert.Same(typeInfo, TickerFunctionProvider.GetResultTypeInfo<ResultPayload>(functionName));
        Assert.Same(captured.RuntimeResults, TickerFunctionProvider.RuntimeResults);
        Assert.Same(captured.Descriptors, TickerFunctionProvider.TickerFunctionDescriptors);
        Assert.Equal(captured.Descriptors[functionName].Result!.Fingerprint,
            TickerFunctionProvider.GetResultContract(functionName).ContractId);

        TickerFunctionProvider.Build();

        Assert.Same(typeInfo, TickerFunctionProvider.GetResultTypeInfo<ResultPayload>(functionName));
        Assert.Single(captured.RuntimeResults);
        Assert.Single(captured.Descriptors);
    }

    [Fact]
    public void Build_SourceGeneratedResultSchemaRejectsShapeChangingSerializerOptionsAtomically()
    {
        const string functionName = "ResultSchemaFunc";
        TickerFunctionProvider.RegisterResultTypeInfoResolver(
            new Dictionary<string, (Type ResultType, Func<JsonSerializerOptions, JsonTypeInfo> Resolve)>
            {
                [functionName] = (typeof(ResultPayload), _ => ResultJsonContext.Default.ResultPayload)
            });
        TickerFunctionProvider.RegisterDescriptors(
            new Dictionary<string, TickerFunctionDescriptor>
            {
                [functionName] = new TickerFunctionDescriptor(
                    functionName,
                    result: new TickerResultContract(
                        typeof(ResultPayload).FullName!,
                        schemaJson: "{}"))
            },
            "source-gen");

        var previous = TickerHelper.RequestJsonSerializerOptions;
        try
        {
            TickerHelper.RequestJsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                TypeInfoResolver = ResultJsonContext.Default
            };

            var error = Assert.Throws<InvalidOperationException>(TickerFunctionProvider.Build);

            Assert.Contains("PropertyNamingPolicy", error.Message, StringComparison.Ordinal);
            Assert.False(TickerFunctionProvider.IsBuilt);
            Assert.Empty(TickerFunctionProvider.TickerFunctionDescriptors);
        }
        finally
        {
            TickerHelper.RequestJsonSerializerOptions = previous;
        }
    }

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

    [Fact]
    public void RegisterRequestType_And_Build_ResolvesConfiguredRuntimeMetadata()
    {
        var finalOptions = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var previous = TickerHelper.RequestJsonSerializerOptions;
        try
        {
            TickerHelper.RequestJsonSerializerOptions = finalOptions;
            TickerFunctionProvider.RegisterRequestType(new Dictionary<string, (string, Type)>
            {
                ["InterfaceTypedFunc"] = (typeof(RuntimeRequest).FullName!, typeof(RuntimeRequest))
            });

            TickerFunctionProvider.Build();

            Assert.True(TickerFunctionProvider.TryGetRequestTypeInfo<RuntimeRequest>("InterfaceTypedFunc", out var published));
            Assert.Equal(typeof(RuntimeRequest), published.Type);
            Assert.Same(finalOptions, published.Options);
        }
        finally
        {
            TickerHelper.RequestJsonSerializerOptions = previous;
        }
    }

    [Fact]
    public void RegisterRequestTypeInfo_PublishesRuntimeMetadataAndControlsSerializationProfile()
    {
        var contractOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        var typeInfo = (JsonTypeInfo<RuntimeRequest>)contractOptions.GetTypeInfo(typeof(RuntimeRequest));

        TickerFunctionProvider.RegisterRequestType(new Dictionary<string, (string, Type)>
        {
            ["TypedFunc"] = (typeof(RuntimeRequest).FullName!, typeof(RuntimeRequest))
        });
        TickerFunctionProvider.RegisterRequestTypeInfo(
            new Dictionary<string, (Type RequestType, JsonTypeInfo JsonTypeInfo)>
            {
                ["TypedFunc"] = (typeof(RuntimeRequest), typeInfo)
            });
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TryGetRequestTypeInfo<RuntimeRequest>("TypedFunc", out var published));
        Assert.Same(typeInfo, published);

        var previous = TickerHelper.RequestJsonSerializerOptions;
        try
        {
            TickerHelper.RequestJsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = Encoding.UTF8.GetString(TickerHelper.CreateTickerRequest(
                new RuntimeRequest { OrderId = "42" }, "TypedFunc"));

            Assert.Equal("{\"OrderId\":\"42\"}", json);
        }
        finally
        {
            TickerHelper.RequestJsonSerializerOptions = previous;
        }
    }

    [Fact]
    public void RegisterRequestTypeInfo_RejectsMismatchedMetadataBeforeBuild()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var typeInfo = options.GetTypeInfo(typeof(string));

        var error = Assert.Throws<ArgumentException>(() =>
            TickerFunctionProvider.RegisterRequestTypeInfo(
                new Dictionary<string, (Type RequestType, JsonTypeInfo JsonTypeInfo)>
                {
                    ["TypedFunc"] = (typeof(RuntimeRequest), typeInfo)
                }));

        Assert.Contains("does not match request type", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterRequestTypeInfoResolver_ResolvesAgainstFinalOptionsAtBuild()
    {
        JsonSerializerOptions observed = null;
        TickerFunctionProvider.RegisterRequestTypeInfoResolver(
            new Dictionary<string, (Type RequestType, Func<JsonSerializerOptions, JsonTypeInfo> Resolve)>
            {
                ["TypedFunc"] = (typeof(RuntimeRequest), options =>
                {
                    observed = options;
                    return options.GetTypeInfo(typeof(RuntimeRequest));
                })
            });

        var finalOptions = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var previous = TickerHelper.RequestJsonSerializerOptions;
        try
        {
            TickerHelper.RequestJsonSerializerOptions = finalOptions;
            TickerFunctionProvider.Build();

            Assert.Same(finalOptions, observed);
            Assert.True(TickerFunctionProvider.TryGetRequestTypeInfo<RuntimeRequest>("TypedFunc", out _));
        }
        finally
        {
            TickerHelper.RequestJsonSerializerOptions = previous;
        }
    }

    [Fact]
    public void Build_SourceGeneratedSchemaRejectsShapeChangingSerializerOptionsAtomically()
    {
        TickerFunctionProvider.RegisterDescriptors(
            new Dictionary<string, TickerFunctionDescriptor>
            {
                ["TypedFunc"] = new TickerFunctionDescriptor(
                    "TypedFunc", request: new TickerRequestContract(typeof(RuntimeRequest).FullName!, schemaJson: "{}"))
            }, "source-gen");

        var previous = TickerHelper.RequestJsonSerializerOptions;
        try
        {
            TickerHelper.RequestJsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var error = Assert.Throws<InvalidOperationException>(TickerFunctionProvider.Build);

            Assert.Contains("PropertyNamingPolicy", error.Message, StringComparison.Ordinal);
            Assert.False(TickerFunctionProvider.IsBuilt);
            Assert.Empty(TickerFunctionProvider.TickerFunctionDescriptors);
        }
        finally
        {
            TickerHelper.RequestJsonSerializerOptions = previous;
        }
    }

    [Fact]
    public void Build_SourceGeneratedSchemaRejectsShapeChangingTypeInfoMetadataAtomically()
    {
        const string schema = "{\"type\":\"object\",\"properties\":{\"OrderId\":{\"type\":\"string\"}}}";
        TickerFunctionProvider.RegisterDescriptors(
            new Dictionary<string, TickerFunctionDescriptor>
            {
                ["TypedFunc"] = new TickerFunctionDescriptor(
                    "TypedFunc", request: new TickerRequestContract(typeof(RuntimeRequest).FullName!, schemaJson: schema))
            }, "source-gen");
        TickerFunctionProvider.RegisterRequestType(new Dictionary<string, (string, Type)>
        {
            ["TypedFunc"] = (typeof(RuntimeRequest).FullName!, typeof(RuntimeRequest))
        });

        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type == typeof(RuntimeRequest))
                typeInfo.Properties.Single(property => property.Name == "OrderId").Name = "orderId";
        });

        var previous = TickerHelper.RequestJsonSerializerOptions;
        try
        {
            TickerHelper.RequestJsonSerializerOptions = new JsonSerializerOptions { TypeInfoResolver = resolver };

            var error = Assert.Throws<InvalidOperationException>(TickerFunctionProvider.Build);

            Assert.Contains("serializer metadata", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(TickerFunctionProvider.IsBuilt);
            Assert.Empty(TickerFunctionProvider.TickerFunctionDescriptors);
        }
        finally
        {
            TickerHelper.RequestJsonSerializerOptions = previous;
        }
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
    
    [Fact]
    public void Build_CalledTwiceWithCronUpdateAfterInitialBuild_DoesNotClearFunctions()
    {
        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["CronRaceFunc"] = ("0 0 1 * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        };

        var configuration = Substitute.For<IConfiguration>();
     
        TickerFunctionProvider.RegisterFunctions(functions);
        
        TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(configuration);
        TickerFunctionProvider.Build();

        // Simulate another host that UpdateCronExpressionsFromIConfiguration and Build again.
        TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(configuration);
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("CronRaceFunc"));
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
    public void UpdateCronExpressionsFromIConfiguration_AfterInitialBuild_UpdatesExpressions()
    {
        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["CronRaceFunc"] = ("%CronSettings:Schedule%", TickerTaskPriority.Normal, NoOpDelegate, 0)
        };

        TickerFunctionProvider.RegisterFunctions(functions);
        TickerFunctionProvider.Build();

        // Simulate another host that contributes config-based cron update callback.
        var configuration = Substitute.For<IConfiguration>();
        configuration["CronSettings:Schedule"].Returns("0 30 * * *");

        TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(configuration);
       
        // Second Build should not throw and data should still be present
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("CronRaceFunc"));
        Assert.Equal("0 30 * * *", TickerFunctionProvider.TickerFunctions["CronRaceFunc"].cronExpression);
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
        Assert.NotNull(TickerFunctionProvider.TickerFunctionDescriptors);
        Assert.Empty(TickerFunctionProvider.TickerFunctionDescriptors);
    }

    // ===============================================================
    // Canonical descriptor registration path
    // ===============================================================

    private static TickerFunctionDescriptor TypedDescriptor(string name, string typeName)
        => new(name, request: new TickerRequestContract(typeName));

    // Every typed registration mode must land the same request metadata in the
    // canonical registry — this freezes the parity the old split RequestTypes /
    // RequestInfos tables failed to provide.
    [Fact]
    public void RegisterDescriptors_TypedModes_ProduceEquivalentRequestMetadata()
    {
        // Simulates the three registration modes (attribute typed, interface typed,
        // fluent typed) all describing the same request type for the same function.
        var attributeMode = new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "Orders.ProcessOrderRequest")
        };
        var interfaceMode = new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "Orders.ProcessOrderRequest")
        };

        TickerFunctionProvider.RegisterDescriptors(attributeMode);
        TickerFunctionProvider.RegisterDescriptors(interfaceMode);
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey("Orders.Process"));
        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors["Orders.Process"];
        Assert.NotNull(descriptor.Request);
        Assert.Equal("Orders.ProcessOrderRequest", descriptor.Request!.TypeName);
        Assert.True(descriptor.Request.Required);
    }

    [Fact]
    public void RegisterDescriptors_RequestLessFunction_HasNullRequest_NotEmptyMetadata()
    {
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Jobs.Cleanup"] = new TickerFunctionDescriptor("Jobs.Cleanup")
        });
        TickerFunctionProvider.Build();

        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors["Jobs.Cleanup"];
        Assert.Null(descriptor.Request);
    }

    [Fact]
    public void RegisterDescriptors_DuplicateIncompatible_FailsAtBuild_NamingBoth()
    {
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "Orders.ProcessOrderRequest")
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "Orders.DifferentRequest")
        });

        var ex = Assert.Throws<InvalidOperationException>(() => TickerFunctionProvider.Build());
        Assert.Contains("Orders.Process", ex.Message);
        Assert.Contains("Orders.ProcessOrderRequest", ex.Message);
        Assert.Contains("Orders.DifferentRequest", ex.Message);
    }

    [Fact]
    public void RegisterDescriptors_DuplicateIdentical_IsIdempotent()
    {
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "Orders.ProcessOrderRequest")
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "Orders.ProcessOrderRequest")
        });

        TickerFunctionProvider.Build();

        Assert.Single(TickerFunctionProvider.TickerFunctionDescriptors);
    }

    [Fact]
    public void RegisterDescriptors_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TickerFunctionProvider.RegisterDescriptors(null!));
    }

    // Defect 1: the dictionary key must agree with the descriptor's own FunctionName,
    // otherwise the registry would be internally inconsistent.
    [Fact]
    public void RegisterDescriptors_KeyMismatchesDescriptorName_Throws_NamingBoth()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
            {
                ["Orders.Process"] = TypedDescriptor("Orders.Renamed", "Orders.ProcessOrderRequest")
            }));

        Assert.Contains("Orders.Process", ex.Message);
        Assert.Contains("Orders.Renamed", ex.Message);
    }

    // Defect 4: the descriptor registry is externally get-only (internal atomic publication only).
    [Fact]
    public void TickerFunctionDescriptors_IsExternallyGetOnly()
    {
        var prop = typeof(TickerFunctionProvider).GetProperty(
            "TickerFunctionDescriptors",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(prop);
        Assert.True(prop!.CanRead);
        Assert.False(prop.SetMethod?.IsPublic ?? false);
    }

    // Defect 4: a failing descriptor Build must not partially publish the NEW registry.
    [Fact]
    public void FailedDescriptorBuild_DoesNotPartiallyPublishRegistry()
    {
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "Orders.ProcessOrderRequest")
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "Orders.DifferentRequest")
        });

        Assert.Throws<InvalidOperationException>(() => TickerFunctionProvider.Build());

        // No partial state leaked into the frozen registry.
        Assert.Empty(TickerFunctionProvider.TickerFunctionDescriptors);
    }

    // The runtime execution registry (TickerFunctionRequestTypes) still carries the CLR
    // Type; the wire descriptor never does — they are separate registries.
    [Fact]
    public void Descriptor_CarriesNoClrType_ExecutionRegistryDoes()
    {
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", typeof(string).FullName!)
        });
        TickerFunctionProvider.RegisterRequestType(new Dictionary<string, (string, Type)>
        {
            ["Orders.Process"] = (typeof(string).FullName!, typeof(string))
        });
        TickerFunctionProvider.Build();

        Assert.Equal(typeof(string),
            TickerFunctionProvider.TickerFunctionRequestTypes["Orders.Process"].Item2);
        // Descriptor exposes only the wire type name, no Type handle.
        Assert.Equal(typeof(string).FullName,
            TickerFunctionProvider.TickerFunctionDescriptors["Orders.Process"].Request!.TypeName);
    }

    // ===============================================================
    // A + D: transactional Build with provenance
    // ===============================================================

    // Stage valid function/type/info changes PLUS conflicting descriptors; a failed Build must not
    // publish or clear anything, and the retained registrations must publish on a valid retry.
    [Fact]
    public void FailedBuild_IsTransactional_NothingPublishedOrCleared_RetryRetainsRegistrations()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["Orders.Process"] = ("* * * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        });
        TickerFunctionProvider.RegisterRequestType(new Dictionary<string, (string, Type)>
        {
            ["Orders.Process"] = (typeof(string).FullName!, typeof(string))
        });
        TickerFunctionProvider.RegisterRequestInfo(new Dictionary<string, (string, string)>
        {
            ["Orders.Process"] = ("System.String", "{}")
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "T1")
        }, origin: "origin-A");
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "T2")
        }, origin: "origin-B");

        Assert.Throws<InvalidOperationException>(() => TickerFunctionProvider.Build());

        // Nothing published, IsBuilt untouched.
        Assert.Empty(TickerFunctionProvider.TickerFunctions);
        Assert.Empty(TickerFunctionProvider.TickerFunctionRequestTypes);
        Assert.Empty(TickerFunctionProvider.TickerFunctionRequestInfos);
        Assert.Empty(TickerFunctionProvider.TickerFunctionDescriptors);
        Assert.False(TickerFunctionProvider.IsBuilt);

        // Resolve the descriptor conflict via test reset of pending descriptors only, leaving the
        // function/type/info registrations intact — a valid retry must then publish them, proving
        // they were retained across the failed attempt.
        ClearPendingDescriptors();
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.IsBuilt);
        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("Orders.Process"));
        Assert.True(TickerFunctionProvider.TickerFunctionRequestTypes.ContainsKey("Orders.Process"));
        Assert.True(TickerFunctionProvider.TickerFunctionRequestInfos.ContainsKey("Orders.Process"));
    }

    [Fact]
    public void RegisterDescriptors_Conflict_ReportsBothOrigins()
    {
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "T1")
        }, origin: "attribute-gen");
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "T2")
        }, origin: "fluent-map");

        var ex = Assert.Throws<InvalidOperationException>(() => TickerFunctionProvider.Build());
        Assert.Contains("attribute-gen", ex.Message);
        Assert.Contains("fluent-map", ex.Message);
        Assert.Contains("Orders.Process", ex.Message);
    }

    [Fact]
    public void RegisterDescriptors_SnapshotsInputImmediately()
    {
        var input = new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = TypedDescriptor("Orders.Process", "T1")
        };
        TickerFunctionProvider.RegisterDescriptors(input);

        // Mutate the caller's dictionary after registration — must not affect what was staged.
        input["Orders.Process"] = TypedDescriptor("Orders.Process", "T2");
        input.Clear();

        TickerFunctionProvider.Build();

        Assert.Equal("T1",
            TickerFunctionProvider.TickerFunctionDescriptors["Orders.Process"].Request!.TypeName);
    }

    // ===============================================================
    // E: separate internal runtime-request metadata registry (Type + optional JsonTypeInfo)
    // ===============================================================
    [Fact]
    public void RuntimeRequests_CarryClrType_SeparateFromWireDescriptors()
    {
        TickerFunctionProvider.RegisterRequestType(new Dictionary<string, (string, Type)>
        {
            ["Orders.Process"] = (typeof(string).FullName!, typeof(string))
        });
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.RuntimeRequests.ContainsKey("Orders.Process"));
        Assert.Equal(typeof(string), TickerFunctionProvider.RuntimeRequests["Orders.Process"].RequestType);
    }

    // ===============================================================
    // B: Build reconciles descriptor priority/cron from the functions registry
    // ===============================================================
    [Fact]
    public void Build_ReconcilesDescriptorPriorityAndCron_FromFunctionsRegistry()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["Orders.Process"] = ("0 0 * * *", TickerTaskPriority.High, NoOpDelegate, 0)
        });
        // Descriptor registered with stale/default scheduling metadata.
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Orders.Process"] = new TickerFunctionDescriptor("Orders.Process",
                priority: TickerTaskPriority.Low, cronExpression: "stale",
                request: new TickerRequestContract("T1"))
        });
        TickerFunctionProvider.Build();

        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors["Orders.Process"];
        Assert.Equal(TickerTaskPriority.High, descriptor.Priority);
        Assert.Equal("0 0 * * *", descriptor.CronExpression);
    }

    // ===============================================================
    // H: one atomic remote snapshot updates remote functions + descriptors, preserves local
    // ===============================================================
    [Fact]
    public void MergeRemoteSnapshot_UpdatesRemoteFunctionsAndDescriptors_PreservesLocal()
    {
        // Seed a local function + descriptor.
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["LocalJob"] = ("* * * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["LocalJob"] = new TickerFunctionDescriptor("LocalJob")
        });
        TickerFunctionProvider.Build();

        var remoteFunctions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["RemoteJob@node"] = ("0 0 * * *", TickerTaskPriority.High, NoOpDelegate, 0)
        };
        var remoteDescriptors = new Dictionary<string, TickerFunctionDescriptor>
        {
            ["RemoteJob@node"] = new TickerFunctionDescriptor("RemoteJob@node",
                request: new TickerRequestContract("Remote.Req"))
        };
        static bool IsRemote(string key) => key.Contains('@');

        TickerFunctionProvider.MergeRemoteSnapshot(remoteFunctions, remoteDescriptors, IsRemote);

        // Local preserved.
        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("LocalJob"));
        Assert.True(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey("LocalJob"));
        // Remote applied to both registries together.
        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("RemoteJob@node"));
        Assert.Equal("Remote.Req",
            TickerFunctionProvider.TickerFunctionDescriptors["RemoteJob@node"].Request!.TypeName);
    }

    // ===============================================================
    // Issue 3: Register* must snapshot inputs immediately (caller mutation after
    // registration must not affect Build).
    // ===============================================================
    [Fact]
    public void RegisterFunctions_SnapshotsInputImmediately()
    {
        var input = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["F"] = ("orig", TickerTaskPriority.Normal, NoOpDelegate, 0)
        };
        TickerFunctionProvider.RegisterFunctions(input);

        input["F"] = ("mutated", TickerTaskPriority.High, NoOpDelegate, 9);
        input["G"] = ("late", TickerTaskPriority.Low, NoOpDelegate, 0);
        input.Clear();

        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("F"));
        Assert.Equal("orig", TickerFunctionProvider.TickerFunctions["F"].cronExpression);
        Assert.False(TickerFunctionProvider.TickerFunctions.ContainsKey("G"));
    }

    [Fact]
    public void RegisterRequestType_SnapshotsInputImmediately()
    {
        var input = new Dictionary<string, (string, Type)>
        {
            ["F"] = ("System.String", typeof(string))
        };
        TickerFunctionProvider.RegisterRequestType(input);

        input["F"] = ("System.Int32", typeof(int));
        input.Clear();

        TickerFunctionProvider.Build();

        Assert.Equal(typeof(string), TickerFunctionProvider.TickerFunctionRequestTypes["F"].Item2);
    }

    [Fact]
    public void RegisterRequestInfo_SnapshotsInputImmediately()
    {
        var input = new Dictionary<string, (string, string)>
        {
            ["F"] = ("System.String", "{}")
        };
        TickerFunctionProvider.RegisterRequestInfo(input);

        input["F"] = ("Other", "{\"x\":1}");
        input.Clear();

        TickerFunctionProvider.Build();

        Assert.Equal("System.String", TickerFunctionProvider.TickerFunctionRequestInfos["F"].RequestType);
    }

    // ===============================================================
    // Issue 1: one immutable registry snapshot is the single canonical source
    // ===============================================================
    [Fact]
    public void Snapshot_AfterBuild_IsSelfConsistent_AndBacksCanonicalReaders()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["F"] = ("0 0 * * *", TickerTaskPriority.High, NoOpDelegate, 0)
        });
        TickerFunctionProvider.RegisterRequestType(new Dictionary<string, (string, Type)>
        {
            ["F"] = (typeof(string).FullName!, typeof(string))
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["F"] = TypedDescriptor("F", "System.String")
        });
        TickerFunctionProvider.Build();

        var snap = TickerFunctionProvider.Snapshot;
        Assert.True(snap.Functions.ContainsKey("F"));
        Assert.True(snap.Descriptors.ContainsKey("F"));
        Assert.True(snap.RuntimeRequests.ContainsKey("F"));

        // Canonical readers come from the one snapshot — no independently-assigned joins.
        Assert.Same(snap.Descriptors, TickerFunctionProvider.TickerFunctionDescriptors);
        Assert.Same(snap.RuntimeRequests, TickerFunctionProvider.RuntimeRequests);
    }

    [Fact]
    public void Snapshot_Captured_IsImmutable_AcrossRemoteMerge()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["LocalJob"] = ("* * * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["LocalJob"] = new TickerFunctionDescriptor("LocalJob")
        });
        TickerFunctionProvider.Build();

        var captured = TickerFunctionProvider.Snapshot;

        var remoteFunctions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["RemoteJob@node"] = ("0 0 * * *", TickerTaskPriority.High, NoOpDelegate, 0)
        };
        var remoteDescriptors = new Dictionary<string, TickerFunctionDescriptor>
        {
            ["RemoteJob@node"] = new TickerFunctionDescriptor("RemoteJob@node",
                request: new TickerRequestContract("Remote.Req"))
        };
        static bool IsRemote(string key) => key.Contains('@');

        TickerFunctionProvider.MergeRemoteSnapshot(remoteFunctions, remoteDescriptors, IsRemote);

        // The previously captured snapshot is unchanged; a new snapshot object reflects the merge.
        Assert.False(captured.Descriptors.ContainsKey("RemoteJob@node"));
        Assert.True(TickerFunctionProvider.Snapshot.Descriptors.ContainsKey("RemoteJob@node"));
        Assert.NotSame(captured, TickerFunctionProvider.Snapshot);
    }

    // ===============================================================
    // Issue 2: MergeRemoteSnapshot validation + reconciliation
    // ===============================================================
    private static (Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)> fns,
        Dictionary<string, TickerFunctionDescriptor> descs) RemotePair(
        string key, TickerTaskPriority priority = TickerTaskPriority.High, string cron = "0 0 * * *")
        => (new() { [key] = (cron, priority, NoOpDelegate, 0) },
            new() { [key] = new TickerFunctionDescriptor(key, request: new TickerRequestContract("Remote.Req")) });

    private static bool IsRemoteKey(string key) => key.Contains('@');

    [Fact]
    public void MergeRemoteSnapshot_KeyDescriptorNameMismatch_Rejected()
    {
        var fns = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["R@n"] = ("* * * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        };
        var descs = new Dictionary<string, TickerFunctionDescriptor>
        {
            ["R@n"] = new TickerFunctionDescriptor("DifferentName")
        };
        Assert.Throws<ArgumentException>(() =>
            TickerFunctionProvider.MergeRemoteSnapshot(fns, descs, IsRemoteKey));
    }

    [Fact]
    public void MergeRemoteSnapshot_FunctionWithoutDescriptor_Rejected()
    {
        var fns = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["R@n"] = ("* * * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        };
        Assert.Throws<ArgumentException>(() =>
            TickerFunctionProvider.MergeRemoteSnapshot(fns, new Dictionary<string, TickerFunctionDescriptor>(), IsRemoteKey));
    }

    [Fact]
    public void MergeRemoteSnapshot_DescriptorWithoutFunction_Rejected()
    {
        var descs = new Dictionary<string, TickerFunctionDescriptor>
        {
            ["R@n"] = new TickerFunctionDescriptor("R@n")
        };
        Assert.Throws<ArgumentException>(() =>
            TickerFunctionProvider.MergeRemoteSnapshot(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>(), descs, IsRemoteKey));
    }

    [Fact]
    public void MergeRemoteSnapshot_ReconcilesDescriptorPriorityCron_FromRemoteFunction()
    {
        var fns = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["R@n"] = ("0 0 * * *", TickerTaskPriority.High, NoOpDelegate, 0)
        };
        var descs = new Dictionary<string, TickerFunctionDescriptor>
        {
            ["R@n"] = new TickerFunctionDescriptor("R@n",
                priority: TickerTaskPriority.Low, cronExpression: "stale",
                request: new TickerRequestContract("Remote.Req"))
        };
        TickerFunctionProvider.MergeRemoteSnapshot(fns, descs, IsRemoteKey);

        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors["R@n"];
        Assert.Equal(TickerTaskPriority.High, descriptor.Priority);
        Assert.Equal("0 0 * * *", descriptor.CronExpression);
    }

    [Fact]
    public void MergeRemoteSnapshot_ValidationFailure_PublishesNothing_PreservesLocal()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["LocalJob"] = ("* * * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["LocalJob"] = new TickerFunctionDescriptor("LocalJob")
        });
        TickerFunctionProvider.Build();
        var captured = TickerFunctionProvider.Snapshot;

        // A descriptor with no matching function must be rejected before any publication.
        var descs = new Dictionary<string, TickerFunctionDescriptor> { ["R@n"] = new TickerFunctionDescriptor("R@n") };
        Assert.Throws<ArgumentException>(() =>
            TickerFunctionProvider.MergeRemoteSnapshot(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>(), descs, IsRemoteKey));

        Assert.Same(captured, TickerFunctionProvider.Snapshot);
        Assert.True(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey("LocalJob"));
        Assert.False(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey("R@n"));
    }

    // Regression: an incoming remote payload must NOT be able to overwrite a local entry by
    // colliding on its key. With the production-style predicate (only qualified name@node keys
    // are remote), a payload keyed with a bare local name is classified local and must be rejected
    // outright — the local function/descriptor stay untouched and nothing is published.
    [Fact]
    public void MergeRemoteSnapshot_IncomingKeyClassifiedLocal_Rejected_PreservesLocal()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["LocalJob"] = ("* * * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["LocalJob"] = new TickerFunctionDescriptor("LocalJob", request: new TickerRequestContract("Local.Req"))
        });
        TickerFunctionProvider.Build();
        var captured = TickerFunctionProvider.Snapshot;

        // Incoming remote payload keyed with the SAME bare local name.
        var fns = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["LocalJob"] = ("0 0 * * *", TickerTaskPriority.High, NoOpDelegate, 0)
        };
        var descs = new Dictionary<string, TickerFunctionDescriptor>
        {
            ["LocalJob"] = new TickerFunctionDescriptor("LocalJob", request: new TickerRequestContract("Remote.Req"))
        };
        // Production-style predicate: only qualified "name@node" keys are remote.
        static bool IsQualifiedRemote(string key)
        {
            var i = key.IndexOf('@');
            return i > 0 && i < key.Length - 1;
        }

        Assert.Throws<ArgumentException>(() =>
            TickerFunctionProvider.MergeRemoteSnapshot(fns, descs, IsQualifiedRemote));

        // Nothing overwritten or published: the local entry keeps its own contract.
        Assert.Same(captured, TickerFunctionProvider.Snapshot);
        Assert.Equal("Local.Req",
            TickerFunctionProvider.TickerFunctionDescriptors["LocalJob"].Request!.TypeName);
        Assert.Equal(TickerTaskPriority.Normal,
            TickerFunctionProvider.TickerFunctions["LocalJob"].Priority);
    }

    // ===============================================================
    // Issue 1: canonical single-snapshot remote unregister (backs RemoteFunctionRegistry.Unregister)
    // ===============================================================
    [Fact]
    public void UnregisterRemoteFunction_RemovesFunctionAndDescriptor_FromOneNewSnapshot_PreservesLocal()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["LocalJob"] = ("* * * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["LocalJob"] = new TickerFunctionDescriptor("LocalJob")
        });
        TickerFunctionProvider.Build();

        var (fns, descs) = RemotePair("RemoteJob@node");
        TickerFunctionProvider.MergeRemoteSnapshot(fns, descs, IsRemoteKey);

        var before = TickerFunctionProvider.Snapshot;
        Assert.True(before.Functions.ContainsKey("RemoteJob@node"));
        Assert.True(before.Descriptors.ContainsKey("RemoteJob@node"));

        var removed = TickerFunctionProvider.UnregisterRemoteFunction("RemoteJob@node");
        Assert.True(removed);

        var after = TickerFunctionProvider.Snapshot;
        // One new, coherent snapshot: function AND descriptor gone together.
        Assert.NotSame(before, after);
        Assert.False(after.Functions.ContainsKey("RemoteJob@node"));
        Assert.False(after.Descriptors.ContainsKey("RemoteJob@node"));
        Assert.False(TickerFunctionProvider.TickerFunctions.ContainsKey("RemoteJob@node"));
        // Local slice preserved.
        Assert.True(after.Functions.ContainsKey("LocalJob"));
        Assert.True(after.Descriptors.ContainsKey("LocalJob"));

        // Absent key → false, no new snapshot.
        var again = TickerFunctionProvider.Snapshot;
        Assert.False(TickerFunctionProvider.UnregisterRemoteFunction("RemoteJob@node"));
        Assert.Same(again, TickerFunctionProvider.Snapshot);
    }

    // Reproduces exactly what RemoteFunctionsSyncService does: build canonical descriptors from Hub
    // function metadata (type name + priority + cron) and publish ONE snapshot via MergeRemoteSnapshot.
    // (The real service lives in TickerQ.RemoteExecutor, which cannot build on ARM64 due to the
    // grpc.tools x64-only protoc; this exercises the identical provider protocol it now calls.)
    [Fact]
    public void RemoteSyncShapedMerge_PublishesOneCoherentSnapshot_FunctionsAndDescriptorsAgree()
    {
        // Hub response for one node with two active functions.
        var remoteFunctions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>();
        var remoteDescriptors = new Dictionary<string, TickerFunctionDescriptor>();
        foreach (var (bare, node, type, priority, cron) in new[]
                 {
                     ("Orders", "n1", "Orders.Req", TickerTaskPriority.High, "0 0 * * *"),
                     ("Emails", "n1", (string)null, TickerTaskPriority.Normal, "* * * * *")
                 })
        {
            var qualified = $"{bare}@{node}";
            remoteFunctions[qualified] = (cron, priority, NoOpDelegate, 0);
            var request = string.IsNullOrWhiteSpace(type) ? null : new TickerRequestContract(type);
            remoteDescriptors[qualified] = new TickerFunctionDescriptor(qualified, priority, cron, request: request);
        }

        TickerFunctionProvider.MergeRemoteSnapshot(remoteFunctions, remoteDescriptors, IsRemoteKey);

        var snap = TickerFunctionProvider.Snapshot;
        Assert.Equal(2, snap.Functions.Count);
        Assert.Equal(2, snap.Descriptors.Count);
        // Same keys in both tables — no split publication.
        Assert.True(snap.Functions.ContainsKey("Orders@n1") && snap.Descriptors.ContainsKey("Orders@n1"));
        Assert.Equal("Orders.Req", snap.Descriptors["Orders@n1"].Request!.TypeName);
        Assert.Equal(TickerTaskPriority.High, snap.Descriptors["Orders@n1"].Priority);
        Assert.Equal("0 0 * * *", snap.Descriptors["Orders@n1"].CronExpression);
        // Request-less remote function → Request == null descriptor.
        Assert.Null(snap.Descriptors["Emails@n1"].Request);
    }

    private static void ClearPendingDescriptors()
    {
        var field = typeof(TickerFunctionProvider).GetField("_pendingDescriptors",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        ((System.Collections.IList)field.GetValue(null)!).Clear();
    }
}
