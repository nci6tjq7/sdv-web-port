using System.Runtime.InteropServices.JavaScript;

namespace SdvWebPort.Runtime.Content;

/// <summary>
/// JS interop for browser Canvas API image decoding.
/// Pre-decode-and-cache strategy:
/// 1. C# calls PreDecodeImage(key, bytes) for each image
/// 2. C# calls AwaitAllDecoded() to wait
/// 3. C# calls GetCachedWidth/Height/Rgba(key) to read decoded data
/// </summary>
internal static partial class ContentJsInterop
{
    [JSImport("globalThis.contentPreDecode")]
    internal static partial void PreDecodeImage(string cacheKey, byte[] imageBytes);

    [JSImport("globalThis.contentAwaitDecoded")]
    internal static partial void AwaitAllDecoded();

    [JSImport("globalThis.contentGetWidth")]
    internal static partial int GetCachedWidth(string cacheKey);

    [JSImport("globalThis.contentGetHeight")]
    internal static partial int GetCachedHeight(string cacheKey);

    [JSImport("globalThis.contentGetRgba")]
    internal static partial byte[] GetCachedRgba(string cacheKey);
}
