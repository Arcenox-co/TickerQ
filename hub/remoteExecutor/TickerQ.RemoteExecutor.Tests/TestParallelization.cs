// The RemoteExecutor tests exercise process-wide static registries
// (TickerFunctionProvider / RemoteFunctionRegistry). Run them serially so classes
// don't clobber each other's published snapshot.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
