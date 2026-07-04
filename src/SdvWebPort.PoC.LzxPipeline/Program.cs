using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SdvWebPort.Vfs.Content;

namespace SdvWebPort.PoC.LzxPipeline;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("[LzxPipeline] Starting full LZX pipeline test");
        try
        {
            // Step 1: Load real SDV logo.xnb
            var asm = Assembly.GetExecutingAssembly();
            byte[] xnbBytes;
            using (var s = asm.GetManifestResourceStream("SdvWebPort.PoC.LzxPipeline.Content.logo.xnb"))
            {
                if (s == null) throw new FileNotFoundException("logo.xnb resource not found");
                using var ms = new MemoryStream(); s.CopyTo(ms); xnbBytes = ms.ToArray();
            }
            Console.WriteLine($"[LzxPipeline] Step 1: {xnbBytes.Length} bytes loaded ✓");

            // Step 2: Parse header
            byte flag = xnbBytes[5];
            bool isLzx = (flag & 0x80) != 0;
            Console.WriteLine($"[LzxPipeline] Step 2: Flag=0x{flag:X2} LZX={isLzx} ✓");

            // Step 3: LZX decompress
            int decompSize = BitConverter.ToInt32(xnbBytes, 10);
            byte[] compData = xnbBytes[14..];
            Console.WriteLine($"[LzxPipeline] Step 3: DecompSize={decompSize}, CompData={compData.Length}");

            // Force KNI Content assembly to load by actually using a type
            var contentType = typeof(Microsoft.Xna.Framework.Content.ContentManager);
            Console.WriteLine($"[LzxPipeline] Step 3: ContentManager type: {contentType.AssemblyQualifiedName}");
            var contentAsm = contentType.Assembly;
            Console.WriteLine($"[LzxPipeline] Step 3: Assembly loaded: {contentAsm.GetName().Name} ✓");

            var lzxType = contentAsm.GetType("Microsoft.Xna.Framework.Content.LzxDecoderStream");
            Console.WriteLine($"[LzxPipeline] Step 3: LzxDecoderStream: {lzxType?.FullName ?? "NOT FOUND"}");

            if (lzxType == null) throw new InvalidOperationException("LzxDecoderStream not found");

            using var cs = new MemoryStream(compData);
            var lzx = Activator.CreateInstance(lzxType, cs, decompSize, compData.Length)!;
            using var ds = new MemoryStream();
            byte[] buf = new byte[8192];
            int total = 0;
            while (true) { int r = ((Stream)lzx).Read(buf, 0, buf.Length); if (r <= 0) break; ds.Write(buf, 0, r); total += r; }
            Console.WriteLine($"[LzxPipeline] Step 3: Decompressed {total} bytes ✓");

            // Step 4: Parse XNB content
            ds.Position = 0;
            using var reader = new XnbReader(ds);
            int trCount = reader.Read7BitEncodedInt();
            Console.WriteLine($"[LzxPipeline] Step 4: TypeReaders={trCount}");
            for (int i = 0; i < trCount; i++)
                Console.WriteLine($"  [{i}] {reader.ReadXnbString()} v{reader.ReadInt32()}");
            reader.Read7BitEncodedInt();
            reader.Read7BitEncodedInt();

            // Step 5: Read texture
            var tex = XnbTextureReader.Read(reader);
            Console.WriteLine($"[LzxPipeline] Step 5: {tex.Width}x{tex.Height} {tex.Format} {tex.PixelData.Length} bytes ✓");

            var rgba = XnbTextureReader.NormalizeToRgba(tex);
            Console.WriteLine($"[LzxPipeline] Step 6: RGBA {rgba.Length} bytes ✓");
            Console.WriteLine($"[LzxPipeline] First 16: {BitConverter.ToString(rgba, 0, 16)}");
            Console.WriteLine("[LzxPipeline] === FULL PIPELINE PASS ===");
            Console.WriteLine($"[LzxPipeline] SDV logo: {tex.Width}x{tex.Height}, {rgba.Length} bytes RGBA");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LzxPipeline] FATAL: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
