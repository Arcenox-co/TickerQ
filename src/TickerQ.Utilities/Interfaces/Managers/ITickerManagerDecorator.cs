namespace TickerQ.Utilities.Interfaces.Managers
{
    /// <summary>
    /// Implemented by a ticker manager that wraps another one, such as the EF Core staging managers
    /// registered by UseApplicationDbContext. Startup seeding unwraps it to reach the built-in manager.
    /// </summary>
    internal interface ITickerManagerDecorator
    {
        object InnerManager { get; }
    }
}
