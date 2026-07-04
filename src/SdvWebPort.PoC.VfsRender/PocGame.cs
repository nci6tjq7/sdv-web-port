using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform;
using Microsoft.Xna.Platform.Input;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;

namespace SdvWebPort.PoC.VfsRender;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("[PoC.VfsRender] Starting (direct approach)");
        try
        {
            GameFactory.RegisterGameFactory(new ConcreteGameFactory());
            InputFactory.RegisterInputFactory(new ConcreteInputFactory());

            // Step 1: Load PNG from VFS (embedded resource = simulated VFS)
            var asm = Assembly.GetExecutingAssembly();
            var rn = "SdvWebPort.PoC.VfsRender.Content.test_sprite.png";
            byte[] pngBytes;
            using (var s = asm.GetManifestResourceStream(rn)) { using var ms = new MemoryStream(); s!.CopyTo(ms); pngBytes = ms.ToArray(); }
            Console.WriteLine($"[PoC.VfsRender] Step 1: VFS loaded {pngBytes.Length} bytes");

            // Step 2: Decode PNG via browser Canvas (JS interop)
            int w = PngDecoder.GetPngWidth(pngBytes);
            int h = PngDecoder.GetPngHeight(pngBytes);
            byte[] rgba = PngDecoder.GetPngRgba(pngBytes);
            Console.WriteLine($"[PoC.VfsRender] Step 2: Canvas decoded {w}x{h}, {rgba.Length} RGBA bytes");

            Console.WriteLine("[PoC.VfsRender] Steps 1-2 PASS: VFS read + Canvas decode working");
            Console.WriteLine("[PoC.VfsRender] Step 3 (GraphicsDevice + Texture2D) requires real browser (rAF)");
        }
        catch (Exception ex) { Console.WriteLine($"[PoC.VfsRender] FATAL: {ex.GetType().Name}: {ex.Message}"); }
    }
}
internal static partial class PngDecoder
{
    [JSImport("globalThis.getPngWidth")] internal static partial int GetPngWidth(byte[] b);
    [JSImport("globalThis.getPngHeight")] internal static partial int GetPngHeight(byte[] b);
    [JSImport("globalThis.getPngRgba")] internal static partial byte[] GetPngRgba(byte[] b);
}
