using NSubstitute;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

internal static class ActivationEpochTestDouble
{
    internal static void Configure(IInternalTickerManager manager, long epoch = 1)
    {
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.SupportsAuthoritativeCronReconciliation.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), epoch,
                Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = epoch, Phase = ActivationEpochPhase.Activating });
        manager.AdvanceReconciliationCheckpointAsync(Arg.Any<ReconciliationActivationScope>(), epoch,
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ActivationEpochState
            {
                Epoch = epoch,
                Phase = ActivationEpochPhase.Activating,
                Checkpoint = call.ArgAt<string>(2)
            });
        manager.CommitReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), epoch,
                Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = epoch, Phase = ActivationEpochPhase.Activated });
        manager.GetReconciliationActivationStateAsync(Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState());
        manager.BeginReconciliationActivationEpochAsync(epoch, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = epoch, Phase = ActivationEpochPhase.Activating });
        manager.AdvanceReconciliationCheckpointAsync(epoch, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ActivationEpochState
            {
                Epoch = epoch,
                Phase = ActivationEpochPhase.Activating,
                Checkpoint = call.Arg<string>()
            });
        manager.CommitReconciliationActivationEpochAsync(epoch, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = epoch, Phase = ActivationEpochPhase.Activated });
    }
}
