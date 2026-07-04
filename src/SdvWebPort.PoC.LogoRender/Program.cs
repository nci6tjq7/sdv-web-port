using System;
using System.IO;
using System.Reflection;
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

    protected override void Initialize()
    {
        _graphics = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 800, PreferredBackBufferHeight = 600 };
        base.Initialize();
        _graphics.ApplyChanges();
        Console.WriteLine($"[LogoRender] GraphicsDevice: {(GraphicsDevice != null ? "OK" : "NULL")}");
    }

    private (int w, int h, byte[] rgba) LoadXnbTexture(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        byte[] xnbBytes;
        using (var s = asm.GetManifestResourceStream(resourceName))
        { using var ms = new MemoryStream(); s!.CopyTo(ms); xnbBytes = ms.ToArray(); }
        int decompSize = BitConverter.ToInt32(xnbBytes, 10);
        byte[] compData = xnbBytes[14..];
        var contentAsm = typeof(ContentManager).Assembly;
        var lzxType = contentAsm.GetType("Microsoft.Xna.Framework.Content.LzxDecoderStream")!;
        using var cs = new MemoryStream(compData);
        var lzx = Activator.CreateInstance(lzxType, cs, decompSize, compData.Length)!;
        using var ds = new MemoryStream();
        byte[] buf = new byte[8192];
        while (true) { int r = ((Stream)lzx).Read(buf, 0, buf.Length); if (r <= 0) break; ds.Write(buf, 0, r); }
        ds.Position = 0;
        using var reader = new XnbReader(ds);
        int trCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < trCount; i++) { reader.ReadXnbString(); reader.ReadInt32(); }
        reader.Read7BitEncodedInt(); reader.Read7BitEncodedInt();
        var tex = XnbTextureReader.Read(reader);
        var rgba = XnbTextureReader.NormalizeToRgba(tex);
        Console.WriteLine($"[LogoRender] {resourceName}: {tex.Width}x{tex.Height}, {rgba.Length} bytes");
        return (tex.Width, tex.Height, rgba);
    }

    public void RenderTitleScreen()
    {
        if (GraphicsDevice == null) return;
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        Console.WriteLine("[LogoRender] Loading 3 SDV title screen assets...");
        var logo = LoadXnbTexture("SdvWebPort.PoC.LogoRender.Content.logo.xnb");
        var yellow = LoadXnbTexture("SdvWebPort.PoC.LogoRender.Content.yellowLettersLogo.xnb");
        var buttons = LoadXnbTexture("SdvWebPort.PoC.LogoRender.Content.TitleButtons.xnb");
        var logoTex = new Texture2D(GraphicsDevice, logo.w, logo.h); logoTex.SetData(logo.rgba);
        var yellowTex = new Texture2D(GraphicsDevice, yellow.w, yellow.h); yellowTex.SetData(yellow.rgba);
        var buttonsTex = new Texture2D(GraphicsDevice, buttons.w, buttons.h); buttonsTex.SetData(buttons.rgba);
        Console.WriteLine($"[LogoRender] 3 Texture2Ds created ✓");
        GraphicsDevice.Clear(new Color(30, 60, 120));
        _spriteBatch.Begin();
        _spriteBatch.Draw(logoTex, new Vector2(200, 50), Color.White);
        _spriteBatch.Draw(yellowTex, new Vector2(250, 280), Color.White);
        _spriteBatch.Draw(buttonsTex, new Vector2(150, 400), Color.White);
        _spriteBatch.End();
        Console.WriteLine("[LogoRender] Title screen drawn ✓");
        Console.WriteLine("[LogoRender] === TITLE SCREEN RENDER PASS ===");
    }
}

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("[LogoRender] Starting title screen render");
        try
        {
            GameFactory.RegisterGameFactory(new ConcreteGameFactory());
            InputFactory.RegisterInputFactory(new ConcreteInputFactory());
            var game = new LogoGame();
            game.Run();
            game.RenderTitleScreen();
        }
        catch (Exception ex) { Console.WriteLine($"[LogoRender] FATAL: {ex.GetType().Name}: {ex.Message}"); Console.WriteLine(ex.StackTrace); }
    }
}
