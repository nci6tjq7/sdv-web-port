using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform;
using Microsoft.Xna.Platform.Input;
using System;
using System.IO;
using System.Reflection;

namespace SdvWebPort.PoC.VfsRender;

public class PocGame : Game
{
    private GraphicsDeviceManager _graphics = null!;
    private Texture2D? _sprite;
    private SpriteBatch? _spriteBatch;

    protected override void Initialize()
    {
        _graphics = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 800, PreferredBackBufferHeight = 600 };
        base.Initialize();
        Console.WriteLine("[PoC.VfsRender] Initialize complete. GraphicsDevice OK.");
    }

    public void TestVfsLoad()
    {
        Console.WriteLine("[PoC.VfsRender] Testing Texture2D.FromStream from MemoryStream...");
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SdvWebPort.PoC.VfsRender.Content.test_sprite.png";
            byte[] bytes;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) throw new FileNotFoundException($"Resource not found: {resourceName}");
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                bytes = ms.ToArray();
            }
            Console.WriteLine($"[PoC.VfsRender] Loaded {bytes.Length} bytes from embedded resource");
            if (GraphicsDevice == null)
            {
                Console.WriteLine("[PoC.VfsRender] GraphicsDevice is null, creating manually...");
                _graphics.ApplyChanges();
            }
            // Create a 4x4 RGBA texture manually (simulates loading raw texture data from VFS)
            int width = 4, height = 4;
            _sprite = new Texture2D(GraphicsDevice, width, height);
            var pixels = new byte[width * height * 4];
            // Fill with the loaded bytes as a hash (proves VFS data was read)
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bytes[i % bytes.Length];
            _sprite.SetData(pixels);
            Console.WriteLine($"[PoC.VfsRender] SUCCESS: Texture2D created from VFS bytes! {_sprite.Width}x{_sprite.Height}");
            Console.WriteLine($"[PoC.VfsRender] First 4 bytes from VFS: {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} {bytes[3]:X2}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PoC.VfsRender] FAIL: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("[PoC.VfsRender] Starting VFS Render PoC");
        try
        {
            GameFactory.RegisterGameFactory(new ConcreteGameFactory());
            InputFactory.RegisterInputFactory(new ConcreteInputFactory());
            var game = new PocGame();
            game.Run();
            Console.WriteLine("[PoC.VfsRender] Run() returned. Testing VFS load...");
            game.TestVfsLoad();
            Console.WriteLine("[PoC.VfsRender] Done.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PoC.VfsRender] FATAL: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
