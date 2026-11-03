using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TickerQ;

/// <summary>
/// An execution's DI scope, created on the first resolution. A function that resolves nothing
/// — a static method, or one whose dependencies are all singletons resolved elsewhere — pays for
/// this object instead of a scope and its resolved-services dictionary. Everything a resolution
/// returns, including <see cref="IServiceProvider"/> and <see cref="IServiceScopeFactory"/>
/// themselves, comes from the real scope.
/// </summary>
internal sealed class LazyServiceScope : IServiceScope, IServiceProvider, ISupportRequiredService,
    IKeyedServiceProvider, IAsyncDisposable
{
    // One reference publishes the scope, so no reader can observe it half-written. Creation and
    // disposal race only through compare-and-exchange on it: a scope created after disposal took
    // the slot is disposed by its creator, and one published first is disposed by the disposer.
    private sealed class Created(AsyncServiceScope scope)
    {
        internal readonly AsyncServiceScope Scope = scope;
    }

    private static readonly Created Disposed = new(default);
    private readonly IServiceScopeFactory _factory;
    private Created _created;

    internal LazyServiceScope(IServiceScopeFactory factory) => _factory = factory;

    public IServiceProvider ServiceProvider => this;

    private IServiceProvider Scoped
    {
        get
        {
            var current = Volatile.Read(ref _created);
            if (current == null)
            {
                var candidate = new Created(_factory.CreateAsyncScope());
                current = Interlocked.CompareExchange(ref _created, candidate, null);
                if (current == null) return candidate.Scope.ServiceProvider;
                candidate.Scope.Dispose(); // Lost to a concurrent first resolution or to disposal.
            }
            ObjectDisposedException.ThrowIf(ReferenceEquals(current, Disposed), this);
            return current.Scope.ServiceProvider;
        }
    }

    public object GetService(Type serviceType) => Scoped.GetService(serviceType);

    public object GetRequiredService(Type serviceType) => Scoped.GetRequiredService(serviceType);

    public object GetKeyedService(Type serviceType, object serviceKey)
        => Keyed.GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object serviceKey)
        => Keyed.GetRequiredKeyedService(serviceType, serviceKey);

    // The same failure a container without keyed services reports through the framework extensions.
    private IKeyedServiceProvider Keyed => Scoped as IKeyedServiceProvider
        ?? throw new InvalidOperationException("This service provider doesn't support keyed services.");

    public void Dispose()
    {
        if (TakeForDisposal() is { } created) created.Scope.Dispose();
    }

    public ValueTask DisposeAsync()
        => TakeForDisposal() is { } created ? created.Scope.DisposeAsync() : default;

    private Created TakeForDisposal()
    {
        var taken = Interlocked.Exchange(ref _created, Disposed);
        return taken == null || ReferenceEquals(taken, Disposed) ? null : taken;
    }
}
