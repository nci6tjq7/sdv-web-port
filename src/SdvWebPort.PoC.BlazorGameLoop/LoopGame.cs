using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace SdvWebPort.PoC.BlazorGameLoop;

/// <summary>
/// Minimal Game subclass that renders a CornflowerBlue background + a bouncing
/// red box. Used to prove KNI's Blazor.GL game loop works on net8.0
/// BlazorWebAssembly SDK (the SDK KNI is designed for).
///
/// This mirrors the MockSdv.Game1 shape — if this renders, we can later swap
/// in the real SDV Game1 via the facade→KNI pipeline.
/// </summary>
public class LoopGame : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private Texture2D _whitePixel;
    private Vector2 _boxPosition;
    private Vector2 _boxVelocity;
    private int _frameCount;

    public LoopGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 800,
            PreferredBackBufferHeight = 600,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        _boxPosition = new Vector2(100, 100);
        _boxVelocity = new Vector2(120f, 80f);
        Console.WriteLine("[LoopGame] Constructor complete");
    }

    protected override void Initialize()
    {
        Console.WriteLine("[LoopGame] Initialize");
        base.Initialize();
        Console.WriteLine($"[LoopGame] GraphicsDevice present: {GraphicsDevice != null}");
        if (GraphicsDevice != null)
        {
            Console.WriteLine($"[LoopGame] Viewport: {GraphicsDevice.Viewport.Width}x{GraphicsDevice.Viewport.Height}");
        }
    }

    protected override void LoadContent()
    {
        Console.WriteLine("[LoopGame] LoadContent");
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _whitePixel = new Texture2D(GraphicsDevice, 1, 1);
        _whitePixel.SetData(new[] { Color.White });
        Console.WriteLine("[LoopGame] LoadContent complete");
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _boxPosition += _boxVelocity * dt;
        if (_boxPosition.X < 0 || _boxPosition.X + 50 > 800)
            _boxVelocity.X *= -1;
        if (_boxPosition.Y < 0 || _boxPosition.Y + 50 > 600)
            _boxVelocity.Y *= -1;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        _spriteBatch.Begin();
        _spriteBatch.Draw(_whitePixel, new Rectangle((int)_boxPosition.X, (int)_boxPosition.Y, 50, 50), Color.Red);
        _spriteBatch.End();

        _frameCount++;
        if (_frameCount % 30 == 0)
        {
            Console.WriteLine($"[LoopGame] Frame {_frameCount} drawn");
        }
        base.Draw(gameTime);
    }

    public int FrameCount => _frameCount;
}
