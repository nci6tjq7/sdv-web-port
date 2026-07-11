// Inspect TitleMenu fields, methods, and setUpIcons IL
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

class Inspect
{
    static void Main(string[] args)
    {
        var asmPath = args[0];
        var asm = AssemblyDefinition.ReadAssembly(asmPath);
        var tm = asm.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Menus.TitleMenu");
        if (tm == null) { Console.WriteLine("TitleMenu not found"); return; }
        
        Console.WriteLine("=== TitleMenu Fields ===");
        foreach (var f in tm.Fields.OrderBy(f => f.Name))
            Console.WriteLine($"  {f.FieldType.Name} {f.Name}");
        
        Console.WriteLine("\n=== TitleMenu Methods (key ones) ===");
        foreach (var m in tm.Methods.Where(m => 
            m.Name == "draw" || m.Name == "setUpIcons" || m.Name == ".ctor" ||
            m.Name == "update" || m.Name == "performHoverAction" || m.Name == "receiveLeftClick"))
        {
            Console.WriteLine($"  {m.Name}({m.Parameters.Count} params) - {m.Body?.Instructions.Count ?? 0} instrs");
        }
        
        // Look at setUpIcons IL to find what buttons are created
        var setUpIcons = tm.Methods.FirstOrDefault(m => m.Name == "setUpIcons");
        if (setUpIcons != null)
        {
            Console.WriteLine("\n=== setUpIcons IL (first 100 instrs) ===");
            int i = 0;
            foreach (var ins in setUpIcons.Body.Instructions)
            {
                if (i++ > 100) break;
                Console.WriteLine($"  {i-1,3}: {ins}");
            }
        }
    }
}
