using SdvWebPort.Vfs;

namespace SdvWebPort.Vfs.Tests;

/// <summary>
/// Runs the shared contract tests against InMemoryVfs (the Phase 0 reference impl).
/// </summary>
public class InMemoryVfsContractTests : VfsContractTests
{
    protected override IVirtualFileSystem CreateVfs() => new InMemoryVfs();
}
