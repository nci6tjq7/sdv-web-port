using System;
using System.Reflection;

namespace StardewValley
{
    /// <summary>
    /// Mock SDV helper. Real SDV has a StardewValley.Program class with a Main
    /// method, but for Phase 2.5 we don't need a Main — SdvLoad PoC invokes
    /// Game1 directly via reflection. This stub keeps the namespace alive
    /// and provides a few sub-namespaces that real SDV has, so the assembly
    /// looks SDV-like for testing.
    /// </summary>
    public static class Program
    {
        // Intentionally no Main method. SdvLoad PoC will:
        //   1. Load this assembly via AssemblyLoadContext
        //   2. Find StardewValley.Game1 via reflection
        //   3. Instantiate it via Activator.CreateInstance
        //   4. Call Run() via reflection
        // This mirrors how a real mod loader or test harness would invoke SDV.
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
