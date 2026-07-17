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
        // Read with ReadWrite mode so we can write back
        var fnaAsm = AssemblyDefinition.ReadAssembly(fnaPath, new ReaderParameters { ReadWrite = true });

        Console.WriteLine($"[+] Reading HttpTitleContainer: {shimPath}");
        var shimAsm = AssemblyDefinition.ReadAssembly(shimPath, new ReaderParameters { AssemblyResolver = new DefaultAssemblyResolver() });

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

        fnaAsm.Write(fnaPath);
        Console.WriteLine($"[+] Written: {fnaPath}");
        return 0;
    }
}
