using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.FnaRuntime;

public static partial class Program
{
    private static int Main(string[] args)
    {
        AppContext.SetSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", false);
        Console.WriteLine("[SdvWebPort.FnaRuntime] Set IsDynamicCodeSupported=false");

        Console.WriteLine("[SdvWebPort.FnaRuntime] Starting Stardew Valley...");
        Console.WriteLine($"[SdvWebPort.FnaRuntime] .NET version: {Environment.Version}");
        Console.WriteLine("[SdvWebPort.FnaRuntime] Build: ReadAsset-direct + B+logs + D+preload (fb451b4)");

        try
        {
            Microsoft.Xna.Framework.HttpTitleContainer.SetBaseUrl("/deps/");
            OnReady();
            Console.WriteLine("[SdvWebPort.FnaRuntime] JS notified");

            Console.WriteLine("[SdvWebPort.FnaRuntime] Booting StardewValley.Program.Main...");
            StardewValley.Program.Main(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SdvWebPort.FnaRuntime] FATAL: {ex}");
            OnError(ex.ToString());
        }

        Console.WriteLine("[SdvWebPort.FnaRuntime] Main returned");
        return 0;
    }

    // A2: Cached asset loader — called by patched LocalizedContentManager.LoadImpl.
    // Checks loadedAssets dictionary first; if miss, calls ReadAsset and caches result.
    public static T LoadWithCache<T>(object contentManager, string assetName)
    {
        try
        {
            var baseType = contentManager.GetType();
            while (baseType != null && baseType.Name != "ContentManager")
                baseType = baseType.BaseType;

            if (baseType != null)
            {
                var loadedAssetsField = baseType.GetField("loadedAssets",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (loadedAssetsField != null)
                {
                    var loadedAssets = loadedAssetsField.GetValue(contentManager) as IDictionary;
                    if (loadedAssets != null)
                    {
                        string key = assetName.Replace('\\', '/');
                        if (loadedAssets.Contains(key))
                            return (T)loadedAssets[key];
                    }
                }
            }

            T result = CallReadAsset<T>(contentManager, assetName);

            if (baseType != null)
            {
                var loadedAssetsField = baseType.GetField("loadedAssets",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (loadedAssetsField != null)
                {
                    var loadedAssets = loadedAssetsField.GetValue(contentManager) as IDictionary;
                    if (loadedAssets != null)
                        loadedAssets[assetName.Replace('\\', '/')] = result;
                }
            }

            return result;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[LoadWithCache] Error: {e}");
            throw;
        }
    }

    private static T CallReadAsset<T>(object contentManager, string assetName)
    {
        var baseType = contentManager.GetType();
        while (baseType != null && baseType.Name != "ContentManager")
            baseType = baseType.BaseType;

        if (baseType == null)
            throw new InvalidOperationException("ContentManager base type not found");

        var readAssetMethod = baseType.GetMethod("ReadAsset",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (readAssetMethod == null)
            throw new InvalidOperationException("ReadAsset method not found");

        var genericMethod = readAssetMethod.MakeGenericMethod(typeof(T));
        return (T)genericMethod.Invoke(contentManager, new object[] { assetName, null });
    }

    [JSImport("globalThis.SDV.onReady")]
    public static partial void OnReady();

    [JSImport("globalThis.SDV.error")]
    public static partial void OnError(string msg);
}
