using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform;
using System;

namespace SdvWebPort.PoC.Render;

/// <summary>
/// Minimal KNI Game subclass that validates WebGL2 backend can:
/// 1. Initialize GraphicsDevice
/// 2. Create SpriteBatch
/// 3. Load a PNG texture
/// 4. Render frames in a loop
/// 5. Log FPS to console (auto-proxied to browser console.log by Uno.Wasm.Bootstrap)
/// </summary>
public class PocGame : Game
{
    private GraphicsDeviceManager _graphics = null!;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _sprite = null!;
    private Vector2 _position = new(100, 100);
    private Vector2 _velocity = new(60f, 40f);
    private int _frameCount;
    private TimeSpan _lastFpsUpdate;
    private int _currentFps;

    protected override void Initialize()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 800,
            PreferredBackBufferHeight = 600,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        base.Initialize();
        Console.WriteLine("[PoC.Render] Initialize complete. GraphicsDevice OK.");
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // Load sprite (PNG via stream — avoids KNI content pipeline complexity for PoC)
        try
        {
            using var stream = System.IO.File.OpenRead("Content/test_sprite.png");
            _sprite = Texture2D.FromStream(GraphicsDevice, stream);
            Console.WriteLine($"[PoC.Render] Sprite loaded: {_sprite.Width}x{_sprite.Height}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PoC.Render] Sprite load failed: {ex.Message}. Drawing without sprite.");
            // Fallback: 1x1 white texture so Draw() still works
            _sprite = new Texture2D(GraphicsDevice, 1, 1);
            _sprite.SetData(new[] { Color.White });
        }
    }

    protected override void Update(GameTime gameTime)
    {
        // Bounce sprite around the screen
        _position += _velocity * (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_position.X < 0 || _position.X + _sprite.Width > 800)
            _velocity.X *= -1;
        if (_position.Y < 0 || _position.Y + _sprite.Height > 600)
            _velocity.Y *= -1;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        _spriteBatch.Begin();
        _spriteBatch.Draw(_sprite, _position, Color.White);
        _spriteBatch.End();

        // FPS tracking — log to console (Uno proxies to console.log)
        _frameCount++;
        if (gameTime.TotalGameTime - _lastFpsUpdate > TimeSpan.FromSeconds(1))
        {
            _currentFps = _frameCount;
            _frameCount = 0;
            _lastFpsUpdate = gameTime.TotalGameTime;
            Console.WriteLine($"[PoC.Render] FPS: {_currentFps}");
        }

        base.Draw(gameTime);
    }
}

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("[PoC.Render] Starting KNI WebGL PoC");
        try
        {
            // KNI requires platform-specific GameFactory registration before instantiating Game.
            // The Blazor.GL platform package provides ConcreteGameFactory.
            Console.WriteLine("[PoC.Render] Registering ConcreteGameFactory...");
            GameFactory.RegisterGameFactory(new ConcreteGameFactory());
            Console.WriteLine("[PoC.Render] GameFactory registered.");

            using var game = new PocGame();
            game.Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PoC.Render] FATAL: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[PoC.Render] Stack: {ex.StackTrace}");
            throw;
        }
    }
}
