using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace StardewValley
{
    /// <summary>
    /// Mock SDV Game1 — simulates the shape of real StardewValley.Game1.
    /// Extends MonoGame's Game class. Initializes GraphicsDeviceManager,
    /// creates a SpriteBatch, and renders a colored background + a moving
    /// rectangle so we can visually verify the render pipeline works.
    /// </summary>
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private Vector2 _boxPosition;
        private Vector2 _boxVelocity;
        private int _frameCount;

        public Game1()
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
            Console.WriteLine("[MockSdv.Game1] Constructor complete");
        }

        protected override void Initialize()
        {
            Console.WriteLine("[MockSdv.Game1] Initialize — GraphicsDevice should be set after this");
            base.Initialize();
            Console.WriteLine($"[MockSdv.Game1] GraphicsDevice present: {GraphicsDevice != null}");
            if (GraphicsDevice != null)
            {
                Console.WriteLine($"[MockSdv.Game1] Viewport: {GraphicsDevice.Viewport.Width}x{GraphicsDevice.Viewport.Height}");
            }
        }

        protected override void LoadContent()
        {
            Console.WriteLine("[MockSdv.Game1] LoadContent — creating SpriteBatch + white pixel texture");
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _whitePixel = new Texture2D(GraphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });
            Console.WriteLine("[MockSdv.Game1] LoadContent complete");
        }

        protected override void Update(GameTime gameTime)
        {
            // Bounce a 50x50 box around the 800x600 viewport
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
            // CornflowerBlue background (same as PoC.Render — easy to verify visually)
            GraphicsDevice.Clear(Color.CornflowerBlue);

            _spriteBatch.Begin();
            // Draw a 50x50 red box at _boxPosition
            _spriteBatch.Draw(_whitePixel, new Rectangle((int)_boxPosition.X, (int)_boxPosition.Y, 50, 50), Color.Red);
            _spriteBatch.End();

            _frameCount++;
            if (_frameCount % 30 == 0)
            {
                Console.WriteLine($"[MockSdv.Game1] Frame {_frameCount} drawn");
            }
            base.Draw(gameTime);
        }

        public int FrameCount => _frameCount;
    }
}
