using Xunit;

// xunit runs test collections in parallel threads within a single test host process by
// default. On GitHub Actions' Windows runners this test host has crashed outright (not
// just failed) partway through a run - see CLAUDE.md for the two prior rounds of
// investigation (excluding Category=RequiresDesktop tests, then pinning windows-2022)
// that ruled out real Shell/Recycle Bin COM automation and runner-image instability as
// the sole causes without fully fixing it. Forcing sequential execution removes thread-pool
// contention as a variable - the whole suite is small enough (well under a second locally)
// that running it single-threaded costs nothing measurable.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
