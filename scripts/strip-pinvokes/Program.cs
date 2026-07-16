using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: strip-pinvokes <input.dll> <output.dll>");
            return 1;
        }

        var inputPath = args[0];
        var outputPath = args[1];

        Console.WriteLine($"[+] Reading: {inputPath}");
        var asm = AssemblyDefinition.ReadAssembly(inputPath);

        int pinvokeMethodsRemoved = 0;
        int monoPInvokeCallbackAttrsRemoved = 0;
        int dllImportAttrsRemoved = 0;

        foreach (var module in asm.Modules)
        {
            foreach (var type in module.Types)
            {
                StripAttributesFromType(type, ref pinvokeMethodsRemoved, ref monoPInvokeCallbackAttrsRemoved, ref dllImportAttrsRemoved);
            }
        }

        Console.WriteLine($"[+] P/Invoke methods removed: {pinvokeMethodsRemoved}");
        Console.WriteLine($"[+] MonoPInvokeCallback attributes removed: {monoPInvokeCallbackAttrsRemoved}");
        Console.WriteLine($"[+] DllImport attributes removed: {dllImportAttrsRemoved}");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        asm.Write(outputPath);
        Console.WriteLine($"[+] Written: {outputPath}");
        return 0;
    }

    static void StripAttributesFromType(TypeDefinition type,
        ref int pinvokeMethodsRemoved, ref int monoPInvokeCallbackAttrsRemoved, ref int dllImportAttrsRemoved)
    {
        foreach (var nested in type.NestedTypes)
        {
            StripAttributesFromType(nested, ref pinvokeMethodsRemoved, ref monoPInvokeCallbackAttrsRemoved, ref dllImportAttrsRemoved);
        }

        for (int i = type.Methods.Count - 1; i >= 0; i--)
        {
            var method = type.Methods[i];

            bool hasMonoPInvokeCallback = false;
            for (int j = method.CustomAttributes.Count - 1; j >= 0; j--)
            {
                var attr = method.CustomAttributes[j];
                if (attr.AttributeType.Name == "MonoPInvokeCallbackAttribute")
                {
                    method.CustomAttributes.RemoveAt(j);
                    hasMonoPInvokeCallback = true;
                    monoPInvokeCallbackAttrsRemoved++;
                    Console.WriteLine($"  [-] Removed MonoPInvokeCallback from {type.FullName}.{method.Name}");
                }
            }

            if (method.PInvokeInfo != null)
            {
                Console.WriteLine($"  [-] Removing P/Invoke method {type.FullName}.{method.Name} (was importing {method.PInvokeInfo.Module.Name})");
                method.PInvokeInfo = null;
                dllImportAttrsRemoved++;
                method.ImplAttributes = MethodImplAttributes.IL;
                method.Attributes = MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig;
                var il = method.Body.GetILProcessor();
                il.Clear();
                il.Emit(Mono.Cecil.Cil.OpCodes.Nop);
                il.Emit(Mono.Cecil.Cil.OpCodes.Newobj,
                    method.Module.ImportReference(typeof(NotImplementedException).GetConstructor(new[] { typeof(string) })));
                il.Emit(Mono.Cecil.Cil.OpCodes.Throw);
                method.Body.InitLocals = true;
                pinvokeMethodsRemoved++;
            }
            else if (hasMonoPInvokeCallback)
            {
                Console.WriteLine($"  [-] Stubbing reverse P/Invoke {type.FullName}.{method.Name}");
                method.ImplAttributes = MethodImplAttributes.IL;
                var il = method.Body.GetILProcessor();
                il.Clear();
                il.Emit(Mono.Cecil.Cil.OpCodes.Nop);
                il.Emit(Mono.Cecil.Cil.OpCodes.Newobj,
                    method.Module.ImportReference(typeof(NotImplementedException).GetConstructor(new[] { typeof(string) })));
                il.Emit(Mono.Cecil.Cil.OpCodes.Throw);
                method.Body.InitLocals = true;
            }
        }
    }
}
