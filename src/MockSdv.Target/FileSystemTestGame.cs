using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;

namespace StardewValley
{
    /// <summary>
    /// Test Game that calls File.OpenRead in LoadContent — simulates real SDV's
    /// content loading pattern. Used to verify the Cecil rewriter redirects
    /// File.OpenRead calls to SdvFileShim.OpenRead (which routes to VFS).
    ///
    /// The rewriter should transform:
    ///   File.OpenRead("Content/test.txt")
    /// into:
    ///   SdvFileShim.OpenRead("Content/test.txt")
    /// which routes to the IVirtualFileSystem.
    /// </summary>
    public class FileSystemTestGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private string _loadedText = "(not loaded)";
        private int _frameCount;

        public FileSystemTestGame()
        {
            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 800,
                PreferredBackBufferHeight = 600,
            };
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
            Console.WriteLine("[FileSystemTestGame] Constructor complete");
        }

        protected override void Initialize()
        {
            Console.WriteLine("[FileSystemTestGame] Initialize");
            base.Initialize();
            Console.WriteLine($"[FileSystemTestGame] GraphicsDevice present: {GraphicsDevice != null}");
        }

        protected override void LoadContent()
        {
            Console.WriteLine("[FileSystemTestGame] LoadContent — calling File.OpenRead (will be rewritten to SdvFileShim.OpenRead)");
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _whitePixel = new Texture2D(GraphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // This File.OpenRead call will be rewritten by the Cecil rewriter
            // to SdvFileShim.OpenRead, which routes to IVirtualFileSystem.
            try
            {
                using var stream = File.OpenRead("Content/test.txt");
                using var reader = new StreamReader(stream);
                _loadedText = reader.ReadToEnd();
                Console.WriteLine($"[FileSystemTestGame] Loaded text: {_loadedText}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileSystemTestGame] File.OpenRead failed: {ex.Message}");
                _loadedText = "(load failed)";
            }
            Console.WriteLine("[FileSystemTestGame] LoadContent complete");
        }

        protected override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);
            _spriteBatch.Begin();
            _spriteBatch.Draw(_whitePixel, new Rectangle(100, 100, 50, 50), Color.Red);
            _spriteBatch.End();

            _frameCount++;
            if (_frameCount % 60 == 0)
            {
                Console.WriteLine($"[FileSystemTestGame] Frame {_frameCount}, loadedText='{_loadedText}'");
            }
            base.Draw(gameTime);
        }
    }
}
