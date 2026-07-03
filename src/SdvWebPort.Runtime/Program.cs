using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using SdvWebPort.Runtime.Vfs;

namespace SdvWebPort.Runtime;

public static partial class Program
{
    private static IVirtualFileSystem? _vfs;

    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("[SdvWebPort] Runtime initialized (Phase 1a)");
        Console.WriteLine($"[SdvWebPort] .NET version: {Environment.Version}");
        try
        {
            var caps = VfsJsInterop.VfsGetCapabilities();
            Console.WriteLine($"[SdvWebPort] VFS capabilities: {caps}");
            if (caps.Contains("\"fsa\":true"))
            { VfsUiInterop.ShowElement("fsa-ui"); VfsUiInterop.HideElement("opfs-ui"); }
            else if (caps.Contains("\"opfs\":true"))
            { VfsUiInterop.HideElement("fsa-ui"); VfsUiInterop.ShowElement("opfs-ui"); }
            else { VfsUiInterop.ShowElement("unsupported-ui"); }
        }
        catch (Exception ex)
        { Console.WriteLine($"[SdvWebPort] VFS detection failed: {ex.Message}"); }
        await Task.Delay(-1);
        return 0;
    }

    [JSExport]
    public static async Task<bool> InitializeVfsFromDirectory()
    {
        bool picked = VfsJsInterop.VfsPickDirectory();
        if (!picked) return false;
        _vfs = new FileSystemAccessApiVfs();
        VfsUiInterop.SetStatus("GOG directory loaded (FSA)");
        return true;
    }

    [JSExport]
    public static async Task<bool> InitializeVfsFromOpfs()
    {
        _vfs = new OpfsVfs();
        VfsUiInterop.SetStatus("Files uploaded to OPFS");
        return true;
    }
}

internal static partial class VfsJsInterop
{
    [JSImport("globalThis.vfsGetCapabilities")]
    internal static partial string VfsGetCapabilities();
    [JSImport("globalThis.vfsPickDirectory")]
    internal static partial bool VfsPickDirectory();
}

internal static partial class VfsUiInterop
{
    [JSImport("globalThis.showElement")]
    internal static partial void ShowElement(string id);
    [JSImport("globalThis.hideElement")]
    internal static partial void HideElement(string id);
    [JSImport("globalThis.setStatJs")]
    internal static partial void SetStatus(string message);
}
