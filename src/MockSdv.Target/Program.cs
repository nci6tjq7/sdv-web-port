using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Reflection;
using System.Linq;

namespace StardewValley
{
    /// <summary>
    /// Mock SDV entry point — simulates the shape of real StardewValley.Program.
    /// Uses MonoGame.Framework types (Vector2, Color) so we exercise the facade
    /// → KNI type forwarding at runtime.
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("[MockSdv] Program.Main entered");
            Console.WriteLine($"[MockSdv] Assembly: {typeof(Program).Assembly.FullName}");
            Console.WriteLine($"[MockSdv] .NET version: {Environment.Version}");

            // Exercise the MonoGame.Framework reference: create a Vector2.
            // This forces the runtime to resolve Microsoft.Xna.Framework.Vector2
            // via the facade → KNI type forwarder.
            var v = new Vector2(3f, 4f);
            Console.WriteLine($"[MockSdv] Vector2({v.X}, {v.Y}) — Length: {v.Length()}");

            // Exercise another type: Color
            var c = new Color(255, 128, 0);
            Console.WriteLine($"[MockSdv] Color: R={c.R} G={c.G} B={c.B} A={c.A}");

            // Inspect Game1's base type chain — should resolve through the facade
            // to KNI's Microsoft.Xna.Framework.Game class.
            Console.WriteLine($"[MockSdv] Game1 base type: {typeof(Game1).BaseType?.FullName}");
            Console.WriteLine($"[MockSdv] Game1 base asm: {typeof(Game1).BaseType?.Assembly.GetName().Name} v{typeof(Game1).BaseType?.Assembly.GetName().Version}");

            // List 5 sample types from this assembly (simulating SDV type enumeration)
            var types = typeof(Program).Assembly.GetTypes();
            Console.WriteLine($"[MockSdv] Total types in assembly: {types.Length}");
            foreach (var t in types.Take(5))
            {
                Console.WriteLine($"  - {t.FullName}");
            }

            Console.WriteLine("[MockSdv] Main complete — facade → KNI resolution verified");
        }
    }

    /// <summary>
    /// Mock Game1 — extends MonoGame's Game class, simulating real StardewValley.Game1.
    /// </summary>
    public class Game1 : Game
    {
        public Game1() : base()
        {
            Console.WriteLine("[MockSdv] Game1 constructed");
        }
    }
}

namespace StardewValley.GameLocation
{
    public class GameLocation
    {
        public string Name { get; set; } = "Farm";
    }
}

namespace StardewValley.Farmer
{
    public class Farmer
    {
        public string Name { get; set; } = "Player";
        public int Gold { get; set; } = 500;
    }
}
