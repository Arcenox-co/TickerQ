using TickerQ.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Sample.Dashboard.ReflectionFree;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerQ(options =>
{
    options.WithJsonContext(TickerQRequestJsonContext.Default);
    options.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard());
});

// Style 1: Inline group with callback
builder.Services.MapTickerGroup("Background", group =>
{
    group.MapTicker<CleanupJob>()
        .WithCron("0 0 * * * *")
        .WithMaxConcurrency(1);
});

// Style 2: Variable group with shared defaults
var orderJobs = builder.Services.MapTickerGroup("Orders")
    .WithMaxConcurrency(5);

orderJobs.MapTicker<ProcessOrderJob, OrderRequest>();

// No group — lambda-based
builder.Services.MapTicker("InlinePing", (ctx, ct) =>
{
    Console.WriteLine($"[{DateTime.UtcNow}] Ping! Id={ctx.Id}");
    return Task.CompletedTask;
});

builder.Services.MapTickerGroup("gr",gr =>
{
    gr.WithMaxConcurrency(2);
});

var app = builder.Build();

app.UseTickerQ();

if (Environment.GetEnvironmentVariable("TICKERQ_AOT_METADATA_PROBE") == "1")
{
    TickerFunctionProvider.Build();
    var typeInfo = TickerFunctionProvider.GetRequestTypeInfo<OrderRequest>("ReflectionFree_TimeJob");
    var payload = TickerHelper.CreateTickerRequest(
        new OrderRequest("aot-probe", 42.5m, new OrderCustomer("buyer@example.com", "AOT Buyer")),
        typeInfo);
    var roundTrip = TickerHelper.ReadTickerRequest(payload, typeInfo);
    if (roundTrip is not
        {
            OrderId: "aot-probe",
            Amount: 42.5m,
            Customer.Email: "buyer@example.com"
        })
        throw new InvalidOperationException("Native AOT request metadata round-trip failed.");

    var validation = TickerRequestPayloadValidator.Validate("ReflectionFree_TimeJob", payload);
    if (!validation.IsValid || validation.Value is not OrderRequest { OrderId: "aot-probe", Amount: 42.5m })
        throw new InvalidOperationException("Native AOT authoritative request validation failed.");

    var schemaInvalidPayload = TickerHelper.CreateTickerRequest(new OrderRequest(null!, 42.5m), typeInfo);
    var schemaInvalidValidation = TickerRequestPayloadValidator.Validate(
        "ReflectionFree_TimeJob", schemaInvalidPayload);
    if (schemaInvalidValidation.IsValid)
        throw new InvalidOperationException("Native AOT schema validator accepted an invalid request payload.");

    var includedTypeInfo = TickerFunctionProvider.GetRequestTypeInfo<IncludedAccessorRequest>(
        "ReflectionFree_IncludedAccessorJob");
    var includedRequest = new IncludedAccessorRequest
    {
        InternalValue = "included-aot-probe",
        ProtectedInternalValue = "included-aot-probe"
    };
    var includedPayload = TickerHelper.CreateTickerRequest(includedRequest, includedTypeInfo);
    var includedRoundTrip = TickerHelper.ReadTickerRequest(includedPayload, includedTypeInfo);
    if (includedRoundTrip is null || !includedRoundTrip.Matches("included-aot-probe"))
        throw new InvalidOperationException("Native AOT [JsonInclude] accessor round-trip failed.");

    Console.WriteLine("TickerQ Native AOT request metadata probe passed.");
    return;
}

app.Run();
