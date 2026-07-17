using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: patch-fna-titlecontainer <FNA.dll> <HttpTitleContainer.dll>");
            Console.WriteLine("  Patches FNA.dll's TitleContainer.OpenStream to call");
            Console.WriteLine("  HttpTitleContainer.OpenStream instead of File.OpenRead.");
            return 1;
        }

        var fnaPath = args[0];
        var shimPath = args[1];

        Console.WriteLine($"[+] Reading FNA: {fnaPath}");
        // Read into memory (not ReadWrite mode — that has issues with embedded resources)
        var fnaBytes = File.ReadAllBytes(fnaPath);
        var fnaAsm = AssemblyDefinition.ReadAssembly(new MemoryStream(fnaBytes));

        Console.WriteLine($"[+] Reading HttpTitleContainer: {shimPath}");
        var shimBytes = File.ReadAllBytes(shimPath);
        var shimAsm = AssemblyDefinition.ReadAssembly(new MemoryStream(shimBytes));

        var shimType = shimAsm.MainModule.Types.FirstOrDefault(t => t.FullName == "Microsoft.Xna.Framework.HttpTitleContainer");
        if (shimType == null)
        {
            Console.WriteLine("[!] HttpTitleContainer type not found!");
            return 1;
        }
        var shimMethod = shimType.Methods.FirstOrDefault(m => m.Name == "OpenStream");
        if (shimMethod == null)
        {
            Console.WriteLine("[!] HttpTitleContainer.OpenStream method not found!");
            return 1;
        }
        Console.WriteLine($"[+] Found HttpTitleContainer.OpenStream");

        var titleContainerType = fnaAsm.MainModule.Types.FirstOrDefault(t => t.FullName == "Microsoft.Xna.Framework.TitleContainer");
        if (titleContainerType == null)
        {
            Console.WriteLine("[!] TitleContainer type not found in FNA!");
            return 1;
        }
        var openStreamMethod = titleContainerType.Methods.FirstOrDefault(m => m.Name == "OpenStream");
        if (openStreamMethod == null)
        {
            Console.WriteLine("[!] TitleContainer.OpenStream method not found in FNA!");
            return 1;
        }
        Console.WriteLine($"[+] Found TitleContainer.OpenStream");

        var importedShimMethod = fnaAsm.MainModule.ImportReference(shimMethod);

        var instrs = openStreamMethod.Body.Instructions;
        instrs.Clear();
        openStreamMethod.Body.ExceptionHandlers.Clear();

        instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        instrs.Add(Instruction.Create(OpCodes.Call, importedShimMethod));
        instrs.Add(Instruction.Create(OpCodes.Ret));

        openStreamMethod.Body.InitLocals = true;

        Console.WriteLine($"[+] Patched TitleContainer.OpenStream -> HttpTitleContainer.OpenStream");

        // Also patch ContentManager.CheckRawExtensions to skip File.Exists checks.
        // In WASM, File.Exists always returns false for HTTP-served files.
        // We replace 'call File.Exists' with 'ldc.i4.1' (true) so the code always
        // proceeds to TitleContainer.OpenStream.
        var contentManagerType = fnaAsm.MainModule.Types.FirstOrDefault(t => t.FullName == "Microsoft.Xna.Framework.Content.ContentManager");
        if (contentManagerType != null)
        {
            var checkRawMethod = contentManagerType.Methods.FirstOrDefault(m => m.Name == "CheckRawExtensions");
            if (checkRawMethod != null)
            {
                var fileExistsMethod = fnaAsm.MainModule.ImportReference(typeof(File).GetMethod("Exists", new[] { typeof(string) }));
                int patched = 0;
                foreach (var instr in checkRawMethod.Body.Instructions)
                {
                    if (instr.OpCode == OpCodes.Call && instr.Operand is MethodReference mr && mr.Name == "Exists")
                    {
                        instr.OpCode = OpCodes.Ldc_I4_1; // push true
                        instr.Operand = null;
                        patched++;
                    }
                }
                if (patched > 0)
                {
                    Console.WriteLine($"[+] Patched CheckRawExtensions: {patched} File.Exists → true");
                }
            }
        }

        // Also add a Location property to TitleContainer (SDV expects it from MonoGame)
        // TitleContainer.Location returns "Content" via HttpTitleContainer.Location
        var shimLocationProperty = shimType.Properties.FirstOrDefault(p => p.Name == "Location");
        if (shimLocationProperty != null && shimLocationProperty.GetMethod != null)
        {
            var importedLocationGetter = fnaAsm.MainModule.ImportReference(shimLocationProperty.GetMethod);

            // Check if TitleContainer already has a Location property
            var existingLocationProp = titleContainerType.Properties.FirstOrDefault(p => p.Name == "Location");
            if (existingLocationProp == null)
            {
                // Create a new property
                var stringType = fnaAsm.MainModule.ImportReference(typeof(string));
                var locationProperty = new PropertyDefinition("Location", PropertyAttributes.None, stringType);

                // Create the getter method
                var getter = new MethodDefinition("get_Location",
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                    stringType);
                getter.Body = new MethodBody(getter);
                getter.Body.Instructions.Add(Instruction.Create(OpCodes.Call, importedLocationGetter));
                getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                getter.Body.InitLocals = true;

                locationProperty.GetMethod = getter;
                titleContainerType.Methods.Add(getter);
                titleContainerType.Properties.Add(locationProperty);

                Console.WriteLine($"[+] Added TitleContainer.Location property -> HttpTitleContainer.Location");
            }
            else
            {
                Console.WriteLine($"[SKIP] TitleContainer.Location already exists");
            }
        }

        // Write to a temp file first, then replace the original
        var tempPath = fnaPath + ".tmp";
        using (var fs = File.Create(tempPath))
        {
            fnaAsm.Write(fs);
        }
        fnaAsm.Dispose();
        File.Copy(tempPath, fnaPath, overwrite: true);
        File.Delete(tempPath);
        Console.WriteLine($"[+] Written: {fnaPath}");
        return 0;
    }
}
