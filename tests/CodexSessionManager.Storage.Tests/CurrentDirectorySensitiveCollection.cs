#pragma warning disable S3990 // Codacy false positive: the containing assembly declares CLSCompliant(true).
using Xunit;

namespace CodexSessionManager.Storage.Tests;

[CollectionDefinition("CurrentDirectorySensitive", DisableParallelization = true)]
public sealed class CurrentDirectorySensitiveCollection;
