using System;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Executable bounds shared by defined-Cron validation and the Core execution loop.
    /// Timer-backed operations use the runtime's maximum finite timer duration; timeout values
    /// reserve the default grace period because Core delays for timeout plus grace.
    /// </summary>
    public static class DefinedCronExecutionLimits
    {
        // Core reports attempt + 1 and increments attempt after the final loop body.
        public const int MaxRetries = int.MaxValue - 1;

        // Task.Delay and CancellationTokenSource.CancelAfter accept at most 0xfffffffe milliseconds.
        public const long MaxTimerDelayMilliseconds = 0xfffffffeL;
        public const int MaxRetryIntervalSeconds = (int)(MaxTimerDelayMilliseconds / 1000);
        public const int DefaultTimeoutGraceSeconds = 5;
        public const int MaxTimeoutSeconds = MaxRetryIntervalSeconds - DefaultTimeoutGraceSeconds;

        public static TimeSpan MaxTimerDelay => TimeSpan.FromMilliseconds(MaxTimerDelayMilliseconds);
        public static TimeSpan MaxTimeout => TimeSpan.FromSeconds(MaxTimeoutSeconds);
    }
}
