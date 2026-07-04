using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform;
using Microsoft.Xna.Platform.Input;
using SdvWebPort.Vfs.Content;

namespace SdvWebPort.PoC.LogoRender;

public class LogoGame : Game
{
    private GraphicsDeviceManager _graphics = null!;
    private SpriteBatch? _spriteBatch;
    private Texture2D? _logoTexture;

    protected override void Initialize()
    {
        _graphics = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 800, PreferredBackBufferHeight = 600 };
        base.Initialize();
        Console.WriteLine("[LogoRender] Initialize complete.");
        _graphics.ApplyChanges();
        Console.WriteLine($"[LogoRender] GraphicsDevice: {(GraphicsDevice != null ? "OK" : "NULL")}");
    }

    public void LoadLogo()
    {
        if (GraphicsDevice == null) { Console.WriteLine("[LogoRender] No GraphicsDevice"); return; }
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        var asm = Assembly.GetExecutingAssembly();
        byte[] xnbBytes;
        using (var s = asm.GetManifestResourceStream("SdvWebPort.PoC.LogoRender.Content.logo.xnb"))
        { using var ms = new MemoryStream(); s!.CopyTo(ms); xnbBytes = ms.ToArray(); }
        Console.WriteLine($"[LogoRender] logo.xnb: {xnbBytes.Length} bytes");

        int decompSize = BitConverter.ToInt32(xnbBytes, 10);
        byte[] compData = xnbBytes[14..];
        var contentAsm = typeof(ContentManager).Assembly;
        var lzxType = contentAsm.GetType("Microsoft.Xna.Framework.Content.LzxDecoderStream")!;
        using var cs = new MemoryStream(compData);
        var lzx = Activator.CreateInstance(lzxType, cs, decompSize, compData.Length)!;
        using var ds = new MemoryStream();
        byte[] buf = new byte[8192];
        while (true) { int r = ((Stream)lzx).Read(buf, 0, buf.Length); if (r <= 0) break; ds.Write(buf, 0, r); }
        Console.WriteLine($"[LogoRender] LZX decompressed: {ds.Length} bytes");

        ds.Position = 0;
        using var reader = new XnbReader(ds);
        int trCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < trCount; i++) { reader.ReadXnbString(); reader.ReadInt32(); }
        reader.Read7BitEncodedInt(); reader.Read7BitEncodedInt();

        var tex = XnbTextureReader.Read(reader);
        var rgba = XnbTextureReader.NormalizeToRgba(tex);
        Console.WriteLine($"[LogoRender] Texture: {tex.Width}x{tex.Height}, {rgba.Length} bytes");

        _logoTexture = new Texture2D(GraphicsDevice, tex.Width, tex.Height);
        _logoTexture.SetData(rgba);
        Console.WriteLine($"[LogoRender] Texture2D created ✓");

        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin();
        _spriteBatch.Draw(_logoTexture, new Vector2(200, 80), Color.White);
        _spriteBatch.End();
        Console.WriteLine("[LogoRender] Draw complete ✓");
        Console.WriteLine("[LogoRender] === RENDER PASS ===");
    }
}

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("[LogoRender] Starting");
        try
        {
            GameFactory.RegisterGameFactory(new ConcreteGameFactory());
            InputFactory.RegisterInputFactory(new ConcreteInputFactory());
            var game = new LogoGame();
            game.Run();
            game.LoadLogo();
        }
        catch (Exception ex) { Console.WriteLine($"[LogoRender] FATAL: {ex.GetType().Name}: {ex.Message}"); Console.WriteLine(ex.StackTrace); }
    }
}
