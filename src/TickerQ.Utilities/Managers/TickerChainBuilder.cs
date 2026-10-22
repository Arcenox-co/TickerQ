using System;
using System.Collections.Generic;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers;

/// <summary>Options shared by time-ticker and recurring-chain steps.</summary>
public sealed class TickerChainStepOptions
{
    /// <summary>Optional text shown for this step in the dashboard.</summary>
    public string Description { get; set; }

    /// <summary>Number of retries after the first failed attempt.</summary>
    public int Retries { get; set; }

    /// <summary>Retry delays in seconds.</summary>
    public int[] RetryIntervals { get; set; }

    /// <summary>Per-attempt timeout in seconds. Null inherits the scheduler default.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Action taken when the node executing this step disappears.</summary>
    public StaleAction OnStale { get; set; }

    /// <summary>Condition evaluated against the direct parent. Ignored for the root step.</summary>
    public RunCondition RunCondition { get; set; } = RunCondition.OnSuccess;

    internal void Validate()
    {
        if (Retries < 0)
            throw new ArgumentOutOfRangeException(nameof(Retries));
        if (!Enum.IsDefined(typeof(StaleAction), OnStale))
            throw new ArgumentOutOfRangeException(nameof(OnStale));
        if (!Enum.IsDefined(typeof(RunCondition), RunCondition))
            throw new ArgumentOutOfRangeException(nameof(RunCondition));
        if (RetryIntervals is null) return;
        foreach (var interval in RetryIntervals)
            if (interval < 0)
                throw new ArgumentOutOfRangeException(nameof(RetryIntervals), "Retry intervals cannot be negative.");
    }
}

/// <summary>
/// A step in a chain. <see cref="Then{TFunction}(Action{TickerChainStepOptions})"/> advances one
/// linear branch; <see cref="ThenMany"/> creates a durable parallel group after this step.
/// </summary>
public sealed class TickerChainStepBuilder
{
    private readonly TickerChainNodeDefinition _node;

    internal TickerChainStepBuilder(TickerChainNodeDefinition node) => _node = node;

    public TickerChainStepBuilder Then<TFunction>(Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction
        => AddChild(() => TickerFunctionProvider.GetFunctionName<TFunction>(), null, configure);

    public TickerChainStepBuilder Then<TFunction, TRequest>(
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction<TRequest>
        => AddChild(
            () => TickerFunctionProvider.GetFunctionName<TFunction>(),
            function => TickerHelper.CreateTickerRequest(request, function),
            configure);

    public TickerChainStepBuilder Then(
        TickerFunctionRef function,
        Action<TickerChainStepOptions> configure = null)
        => AddChild(FixedFunction(function.Name), null, configure);

    public TickerChainStepBuilder Then<TRequest>(
        TickerFunctionRef<TRequest> function,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddChild(FixedFunction(function.Name), name => TickerHelper.CreateTickerRequest(request, name), configure);

    public TickerChainStepBuilder Then(
        string functionName,
        Action<TickerChainStepOptions> configure = null)
        => AddChild(FixedFunction(functionName), null, configure);

    public TickerChainStepBuilder Then<TRequest>(
        string functionName,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddChild(FixedFunction(functionName), name => TickerHelper.CreateTickerRequest(request, name), configure);

    /// <summary>
    /// Creates a durable parallel group after this step. Each branch can continue independently
    /// with <c>Then</c> or another <c>ThenMany</c>. Use the returned group builder to add a join
    /// continuation after the branch subtrees have reached their terminal states.
    /// </summary>
    public TickerParallelGroupBuilder ThenMany(Action<TickerChainBranchesBuilder> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var group = TickerChainNodeDefinition.CreateParallelGroup();
        _node.Children.Add(group);
        var branches = new TickerChainBranchesBuilder(group);
        configure(branches);
        if (branches.AddedCount == 0)
            throw new InvalidOperationException("ThenMany must add at least one child step.");
        return new TickerParallelGroupBuilder(group);
    }

    internal TickerChainStepBuilder AddChild(
        Func<string> function,
        Func<string, byte[]> request,
        Action<TickerChainStepOptions> configure)
    {
        var child = TickerChainNodeDefinition.Create(function, request, configure);
        _node.Children.Add(child);
        return new TickerChainStepBuilder(child);
    }

    internal static Func<string> FixedFunction(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            throw new ArgumentException("A chain step function name is required.", nameof(functionName));
        var normalized = functionName.Trim();
        return () => normalized;
    }
}

/// <summary>
/// Adds continuations to a parallel group. Every continuation waits until all branch subtrees
/// reach a terminal status; its method name determines which aggregate outcome releases it.
/// </summary>
public sealed class TickerParallelGroupBuilder
{
    private readonly TickerChainNodeDefinition _group;

    internal TickerParallelGroupBuilder(TickerChainNodeDefinition group) => _group = group;

    public TickerChainStepBuilder ThenAfterAll<TFunction>(Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction
        => AddContinuation(() => TickerFunctionProvider.GetFunctionName<TFunction>(), null,
            RunCondition.OnSuccess, configure);

    public TickerChainStepBuilder ThenAfterAll<TFunction, TRequest>(
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction<TRequest>
        => AddContinuation(
            () => TickerFunctionProvider.GetFunctionName<TFunction>(),
            function => TickerHelper.CreateTickerRequest(request, function),
            RunCondition.OnSuccess,
            configure);

    public TickerChainStepBuilder ThenAfterAnyFailed<TFunction>(Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction
        => AddContinuation(() => TickerFunctionProvider.GetFunctionName<TFunction>(), null,
            RunCondition.OnFailure, configure);

    public TickerChainStepBuilder ThenAfterAnyFailed<TFunction, TRequest>(
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction<TRequest>
        => AddContinuation(
            () => TickerFunctionProvider.GetFunctionName<TFunction>(),
            function => TickerHelper.CreateTickerRequest(request, function),
            RunCondition.OnFailure,
            configure);

    public TickerChainStepBuilder ThenAfterAnyFailedOrCancelled<TFunction>(
        Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction
        => AddContinuation(() => TickerFunctionProvider.GetFunctionName<TFunction>(), null,
            RunCondition.OnFailureOrCancelled, configure);

    public TickerChainStepBuilder ThenAfterAnyFailedOrCancelled<TFunction, TRequest>(
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction<TRequest>
        => AddContinuation(
            () => TickerFunctionProvider.GetFunctionName<TFunction>(),
            function => TickerHelper.CreateTickerRequest(request, function),
            RunCondition.OnFailureOrCancelled,
            configure);

    public TickerChainStepBuilder ThenAfterAllCompleted<TFunction>(
        Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction
        => AddContinuation(() => TickerFunctionProvider.GetFunctionName<TFunction>(), null,
            RunCondition.OnAnyTerminalStatus, configure);

    public TickerChainStepBuilder ThenAfterAllCompleted<TFunction, TRequest>(
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction<TRequest>
        => AddContinuation(
            () => TickerFunctionProvider.GetFunctionName<TFunction>(),
            function => TickerHelper.CreateTickerRequest(request, function),
            RunCondition.OnAnyTerminalStatus,
            configure);

    public TickerChainStepBuilder ThenAfterAll(
        TickerFunctionRef function,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(TickerChainStepBuilder.FixedFunction(function.Name), null,
            RunCondition.OnSuccess, configure);

    public TickerChainStepBuilder ThenAfterAll<TRequest>(
        TickerFunctionRef<TRequest> function,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(
            TickerChainStepBuilder.FixedFunction(function.Name),
            name => TickerHelper.CreateTickerRequest(request, name),
            RunCondition.OnSuccess,
            configure);

    public TickerChainStepBuilder ThenAfterAnyFailed(
        TickerFunctionRef function,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(TickerChainStepBuilder.FixedFunction(function.Name), null,
            RunCondition.OnFailure, configure);

    public TickerChainStepBuilder ThenAfterAnyFailed<TRequest>(
        TickerFunctionRef<TRequest> function,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(
            TickerChainStepBuilder.FixedFunction(function.Name),
            name => TickerHelper.CreateTickerRequest(request, name),
            RunCondition.OnFailure,
            configure);

    public TickerChainStepBuilder ThenAfterAnyFailedOrCancelled(
        TickerFunctionRef function,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(TickerChainStepBuilder.FixedFunction(function.Name), null,
            RunCondition.OnFailureOrCancelled, configure);

    public TickerChainStepBuilder ThenAfterAnyFailedOrCancelled<TRequest>(
        TickerFunctionRef<TRequest> function,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(
            TickerChainStepBuilder.FixedFunction(function.Name),
            name => TickerHelper.CreateTickerRequest(request, name),
            RunCondition.OnFailureOrCancelled,
            configure);

    public TickerChainStepBuilder ThenAfterAllCompleted(
        TickerFunctionRef function,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(TickerChainStepBuilder.FixedFunction(function.Name), null,
            RunCondition.OnAnyTerminalStatus, configure);

    public TickerChainStepBuilder ThenAfterAllCompleted<TRequest>(
        TickerFunctionRef<TRequest> function,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(
            TickerChainStepBuilder.FixedFunction(function.Name),
            name => TickerHelper.CreateTickerRequest(request, name),
            RunCondition.OnAnyTerminalStatus,
            configure);

    public TickerChainStepBuilder ThenAfterAll(
        string functionName,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(TickerChainStepBuilder.FixedFunction(functionName), null,
            RunCondition.OnSuccess, configure);

    public TickerChainStepBuilder ThenAfterAll<TRequest>(
        string functionName,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(
            TickerChainStepBuilder.FixedFunction(functionName),
            name => TickerHelper.CreateTickerRequest(request, name),
            RunCondition.OnSuccess,
            configure);

    public TickerChainStepBuilder ThenAfterAnyFailed(
        string functionName,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(TickerChainStepBuilder.FixedFunction(functionName), null,
            RunCondition.OnFailure, configure);

    public TickerChainStepBuilder ThenAfterAnyFailed<TRequest>(
        string functionName,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(
            TickerChainStepBuilder.FixedFunction(functionName),
            name => TickerHelper.CreateTickerRequest(request, name),
            RunCondition.OnFailure,
            configure);

    public TickerChainStepBuilder ThenAfterAnyFailedOrCancelled(
        string functionName,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(TickerChainStepBuilder.FixedFunction(functionName), null,
            RunCondition.OnFailureOrCancelled, configure);

    public TickerChainStepBuilder ThenAfterAnyFailedOrCancelled<TRequest>(
        string functionName,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(
            TickerChainStepBuilder.FixedFunction(functionName),
            name => TickerHelper.CreateTickerRequest(request, name),
            RunCondition.OnFailureOrCancelled,
            configure);

    public TickerChainStepBuilder ThenAfterAllCompleted(
        string functionName,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(TickerChainStepBuilder.FixedFunction(functionName), null,
            RunCondition.OnAnyTerminalStatus, configure);

    public TickerChainStepBuilder ThenAfterAllCompleted<TRequest>(
        string functionName,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddContinuation(
            TickerChainStepBuilder.FixedFunction(functionName),
            name => TickerHelper.CreateTickerRequest(request, name),
            RunCondition.OnAnyTerminalStatus,
            configure);

    private TickerChainStepBuilder AddContinuation(
        Func<string> function,
        Func<string, byte[]> request,
        RunCondition condition,
        Action<TickerChainStepOptions> configure)
    {
        var child = TickerChainNodeDefinition.Create(function, request, configure, condition);
        _group.Children.Add(child);
        return new TickerChainStepBuilder(child);
    }
}

/// <summary>Builds the sibling branches supplied to <c>ThenMany</c>.</summary>
public sealed class TickerChainBranchesBuilder
{
    private readonly TickerChainNodeDefinition _parent;

    internal TickerChainBranchesBuilder(TickerChainNodeDefinition parent) => _parent = parent;
    internal int AddedCount { get; private set; }

    public TickerChainStepBuilder Add<TFunction>(Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction
        => AddCore(() => TickerFunctionProvider.GetFunctionName<TFunction>(), null, configure);

    public TickerChainStepBuilder Add<TFunction, TRequest>(
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction<TRequest>
        => AddCore(
            () => TickerFunctionProvider.GetFunctionName<TFunction>(),
            function => TickerHelper.CreateTickerRequest(request, function),
            configure);

    public TickerChainStepBuilder Add(
        TickerFunctionRef function,
        Action<TickerChainStepOptions> configure = null)
        => AddCore(TickerChainStepBuilder.FixedFunction(function.Name), null, configure);

    public TickerChainStepBuilder Add<TRequest>(
        TickerFunctionRef<TRequest> function,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddCore(
            TickerChainStepBuilder.FixedFunction(function.Name),
            name => TickerHelper.CreateTickerRequest(request, name),
            configure);

    public TickerChainStepBuilder Add(
        string functionName,
        Action<TickerChainStepOptions> configure = null)
        => AddCore(TickerChainStepBuilder.FixedFunction(functionName), null, configure);

    public TickerChainStepBuilder Add<TRequest>(
        string functionName,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => AddCore(
            TickerChainStepBuilder.FixedFunction(functionName),
            name => TickerHelper.CreateTickerRequest(request, name),
            configure);

    private TickerChainStepBuilder AddCore(
        Func<string> function,
        Func<string, byte[]> request,
        Action<TickerChainStepOptions> configure)
    {
        var child = TickerChainNodeDefinition.CreateParallelBranch(function, request, configure);
        _parent.Children.Add(child);
        AddedCount++;
        return new TickerChainStepBuilder(child);
    }
}

/// <summary>Builds a durable time-ticker graph for <c>ITimeTickerManager.AddChainAsync</c>.</summary>
public sealed class TimeTickerChainBuilder<TTimeTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>
{
    private readonly Func<TTimeTicker> _entityFactory;
    private TickerChainNodeDefinition _root;
    private string _description;
    private DateTime? _executionTime;
    private ChainDeleteBehavior _deleteBehavior = ChainDeleteBehavior.Cascade;

    internal TimeTickerChainBuilder(Func<TTimeTicker> entityFactory)
        => _entityFactory = entityFactory ?? throw new ArgumentNullException(nameof(entityFactory));

    public TimeTickerChainBuilder<TTimeTicker> WithDescription(string description)
    {
        _description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return this;
    }

    public TimeTickerChainBuilder<TTimeTicker> RunAt(DateTime executionTime)
    {
        _executionTime = executionTime;
        return this;
    }

    public TimeTickerChainBuilder<TTimeTicker> WithDeleteBehavior(ChainDeleteBehavior behavior)
    {
        if (!Enum.IsDefined(typeof(ChainDeleteBehavior), behavior))
            throw new ArgumentOutOfRangeException(nameof(behavior));
        _deleteBehavior = behavior;
        return this;
    }

    public TickerChainStepBuilder StartWith<TFunction>(Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction
        => Start(() => TickerFunctionProvider.GetFunctionName<TFunction>(), null, configure);

    public TickerChainStepBuilder StartWith<TFunction, TRequest>(
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction<TRequest>
        => Start(
            () => TickerFunctionProvider.GetFunctionName<TFunction>(),
            function => TickerHelper.CreateTickerRequest(request, function),
            configure);

    public TickerChainStepBuilder StartWith(
        TickerFunctionRef function,
        Action<TickerChainStepOptions> configure = null)
        => Start(TickerChainStepBuilder.FixedFunction(function.Name), null, configure);

    public TickerChainStepBuilder StartWith<TRequest>(
        TickerFunctionRef<TRequest> function,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => Start(
            TickerChainStepBuilder.FixedFunction(function.Name),
            name => TickerHelper.CreateTickerRequest(request, name),
            configure);

    public TickerChainStepBuilder StartWith(
        string functionName,
        Action<TickerChainStepOptions> configure = null)
        => Start(TickerChainStepBuilder.FixedFunction(functionName), null, configure);

    public TickerChainStepBuilder StartWith<TRequest>(
        string functionName,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => Start(
            TickerChainStepBuilder.FixedFunction(functionName),
            name => TickerHelper.CreateTickerRequest(request, name),
            configure);

    private TickerChainStepBuilder Start(
        Func<string> function,
        Func<string, byte[]> request,
        Action<TickerChainStepOptions> configure)
    {
        if (_root != null)
            throw new InvalidOperationException("A chain can contain only one StartWith step.");
        _root = TickerChainNodeDefinition.Create(function, request, configure);
        return new TickerChainStepBuilder(_root);
    }

    internal TTimeTicker Build()
    {
        if (_root is null)
            throw new InvalidOperationException("A chain must start with StartWith.");
        var root = MaterializeNode(_root, null, true);
        var pending = new Stack<(TickerChainNodeDefinition Definition, TTimeTicker Entity)>();
        pending.Push((_root, root));
        while (pending.TryPop(out var current))
        {
            if (current.Definition.Children.Count == 0) continue;
            var created = new (TickerChainNodeDefinition Definition, TTimeTicker Entity)[current.Definition.Children.Count];
            for (var index = 0; index < current.Definition.Children.Count; index++)
            {
                var childDefinition = current.Definition.Children[index];
                var child = MaterializeNode(childDefinition, current.Entity, false);
                current.Entity.Children.Add(child);
                created[index] = (childDefinition, child);
            }
            for (var index = created.Length - 1; index >= 0; index--)
                pending.Push(created[index]);
        }
        return root;
    }

    private TTimeTicker MaterializeNode(TickerChainNodeDefinition definition, TTimeTicker parent, bool isRoot)
    {
        var function = definition.ResolveFunction();
        var entity = _entityFactory();
        entity.Id = Guid.NewGuid();
        entity.Function = function;
        entity.Description = isRoot ? _description ?? definition.Description : definition.Description;
        entity.ExecutionTime = isRoot ? _executionTime : null;
        entity.Request = definition.CreateRequest(function);
        entity.Retries = definition.Retries;
        entity.RetryIntervals = definition.CopyRetryIntervals();
        entity.TimeoutSeconds = definition.TimeoutSeconds;
        entity.OnStale = definition.OnStale;
        entity.RunCondition = isRoot ? null : definition.RunCondition;
        entity.ParentId = parent?.Id;
        entity.DeleteBehavior = isRoot ? _deleteBehavior : ChainDeleteBehavior.Cascade;

        return entity;
    }
}

/// <summary>Builds a recurring chain template for <c>ICronTickerManager.AddChainAsync</c>.</summary>
public sealed class CronTickerChainBuilder<TCronTicker>
    where TCronTicker : CronTickerEntity
{
    private readonly Func<TCronTicker> _entityFactory;
    private TickerChainNodeDefinition _root;
    private string _expression;
    private string _description;
    private bool _skipIfAlreadyRunning;
    private bool _isEnabled = true;
    private ChainDeleteBehavior _deleteBehavior = ChainDeleteBehavior.Restrict;

    internal CronTickerChainBuilder(Func<TCronTicker> entityFactory)
        => _entityFactory = entityFactory ?? throw new ArgumentNullException(nameof(entityFactory));

    public CronTickerChainBuilder<TCronTicker> WithExpression(string expression)
    {
        _expression = string.IsNullOrWhiteSpace(expression) ? null : expression.Trim();
        return this;
    }

    public CronTickerChainBuilder<TCronTicker> WithDescription(string description)
    {
        _description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return this;
    }

    public CronTickerChainBuilder<TCronTicker> SkipIfAlreadyRunning(bool enabled = true)
    {
        _skipIfAlreadyRunning = enabled;
        return this;
    }

    public CronTickerChainBuilder<TCronTicker> Enabled(bool enabled = true)
    {
        _isEnabled = enabled;
        return this;
    }

    public CronTickerChainBuilder<TCronTicker> WithDeleteBehavior(ChainDeleteBehavior behavior)
    {
        if (!Enum.IsDefined(typeof(ChainDeleteBehavior), behavior))
            throw new ArgumentOutOfRangeException(nameof(behavior));
        _deleteBehavior = behavior;
        return this;
    }

    public TickerChainStepBuilder StartWith<TFunction>(Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction
        => Start(() => TickerFunctionProvider.GetFunctionName<TFunction>(), null, configure);

    public TickerChainStepBuilder StartWith<TFunction, TRequest>(
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        where TFunction : class, ITickerFunction<TRequest>
        => Start(
            () => TickerFunctionProvider.GetFunctionName<TFunction>(),
            function => TickerHelper.CreateTickerRequest(request, function),
            configure);

    public TickerChainStepBuilder StartWith(
        TickerFunctionRef function,
        Action<TickerChainStepOptions> configure = null)
        => Start(TickerChainStepBuilder.FixedFunction(function.Name), null, configure);

    public TickerChainStepBuilder StartWith<TRequest>(
        TickerFunctionRef<TRequest> function,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => Start(
            TickerChainStepBuilder.FixedFunction(function.Name),
            name => TickerHelper.CreateTickerRequest(request, name),
            configure);

    public TickerChainStepBuilder StartWith(
        string functionName,
        Action<TickerChainStepOptions> configure = null)
        => Start(TickerChainStepBuilder.FixedFunction(functionName), null, configure);

    public TickerChainStepBuilder StartWith<TRequest>(
        string functionName,
        TRequest request,
        Action<TickerChainStepOptions> configure = null)
        => Start(
            TickerChainStepBuilder.FixedFunction(functionName),
            name => TickerHelper.CreateTickerRequest(request, name),
            configure);

    private TickerChainStepBuilder Start(
        Func<string> function,
        Func<string, byte[]> request,
        Action<TickerChainStepOptions> configure)
    {
        if (_root != null)
            throw new InvalidOperationException("A chain can contain only one StartWith step.");
        _root = TickerChainNodeDefinition.Create(function, request, configure);
        return new TickerChainStepBuilder(_root);
    }

    internal TCronTicker Build()
    {
        if (_root is null)
            throw new InvalidOperationException("A chain must start with StartWith.");
        if (string.IsNullOrWhiteSpace(_expression))
            throw new InvalidOperationException("A recurring chain requires WithExpression.");

        var rootFunction = _root.ResolveFunction();
        var entity = _entityFactory();
        entity.Id = Guid.NewGuid();
        entity.Function = rootFunction;
        entity.Expression = _expression;
        entity.Description = _description ?? _root.Description;
        entity.Request = _root.CreateRequest(rootFunction);
        entity.Retries = _root.Retries;
        entity.RetryIntervals = _root.CopyRetryIntervals();
        entity.TimeoutSeconds = _root.TimeoutSeconds;
        entity.OnStale = _root.OnStale;
        entity.SkipIfAlreadyRunning = _skipIfAlreadyRunning;
        entity.IsEnabled = _isEnabled;
        entity.Chain = new CronChainTemplate { DeleteBehavior = _deleteBehavior };
        foreach (var child in _root.Children)
            entity.Chain.Children.Add(Materialize(child));
        return entity;
    }

    private static CronChainNode Materialize(TickerChainNodeDefinition definition)
    {
        var function = definition.ResolveFunction();
        var node = new CronChainNode
        {
            Function = function,
            Description = definition.Description,
            Request = definition.CreateRequest(function),
            Retries = definition.Retries,
            RetryIntervals = definition.CopyRetryIntervals(),
            TimeoutSeconds = definition.TimeoutSeconds,
            OnStale = definition.OnStale,
            RunCondition = definition.RunCondition
        };
        foreach (var child in definition.Children)
            node.Children.Add(Materialize(child));
        return node;
    }
}

internal sealed class TickerChainNodeDefinition
{
    private readonly Func<string> _function;
    private readonly Func<string, byte[]> _request;
    private readonly int[] _retryIntervals;

    private TickerChainNodeDefinition(
        Func<string> function,
        Func<string, byte[]> request,
        TickerChainStepOptions options)
    {
        _function = function;
        _request = request;
        Description = string.IsNullOrWhiteSpace(options.Description) ? null : options.Description.Trim();
        Retries = options.Retries;
        _retryIntervals = options.RetryIntervals is null ? null : (int[])options.RetryIntervals.Clone();
        TimeoutSeconds = options.TimeoutSeconds;
        OnStale = options.OnStale;
        RunCondition = options.RunCondition;
    }

    internal string Description { get; }
    internal int Retries { get; }
    internal int? TimeoutSeconds { get; }
    internal StaleAction OnStale { get; }
    internal RunCondition RunCondition { get; }
    internal List<TickerChainNodeDefinition> Children { get; } = new();

    internal static TickerChainNodeDefinition Create(
        Func<string> function,
        Func<string, byte[]> request,
        Action<TickerChainStepOptions> configure,
        RunCondition? runCondition = null)
    {
        var options = new TickerChainStepOptions();
        configure?.Invoke(options);
        if (runCondition.HasValue)
            options.RunCondition = runCondition.Value;
        options.Validate();
        return new TickerChainNodeDefinition(function, request, options);
    }

    internal static TickerChainNodeDefinition CreateParallelBranch(
        Func<string> function,
        Func<string, byte[]> request,
        Action<TickerChainStepOptions> configure)
    {
        var options = new TickerChainStepOptions();
        configure?.Invoke(options);
        options.RunCondition = TickerChainInfrastructure.ToParallelBranchCondition(options.RunCondition);
        options.Validate();
        return new TickerChainNodeDefinition(function, request, options);
    }

    internal static TickerChainNodeDefinition CreateParallelGroup()
        => Create(
            () => TickerChainInfrastructure.ParallelGroupFunction,
            null,
            options =>
            {
                options.Description = TickerChainInfrastructure.ParallelGroupDescription;
                // The coordinator observes every upstream terminal outcome. Individual
                // grouped jobs decide which of those outcomes releases them.
                options.RunCondition = RunCondition.OnAnyTerminalStatus;
            });

    internal string ResolveFunction()
    {
        var function = _function?.Invoke();
        if (string.IsNullOrWhiteSpace(function))
            throw new InvalidOperationException("A chain step resolved to an empty function name.");
        return function.Trim();
    }

    internal byte[] CreateRequest(string function) => _request?.Invoke(function);
    internal int[] CopyRetryIntervals() => _retryIntervals is null ? null : (int[])_retryIntervals.Clone();
}
