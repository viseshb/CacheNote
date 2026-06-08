// UI tests each launch the real (single-instance-unaware until M2) app and share
// the same database, so they must run sequentially, not in parallel.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
