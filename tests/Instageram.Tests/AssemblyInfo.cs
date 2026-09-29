using Xunit;

// The application exposes static services (PortablePaths, DatabaseService,
// AppSettings, Localization) that share one database and one settings file.
// Running test classes in parallel would make them interfere, so it is off.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
