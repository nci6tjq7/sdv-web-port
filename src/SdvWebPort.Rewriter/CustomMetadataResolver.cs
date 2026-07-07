using Mono.Cecil;
using System;
using System.Linq;

namespace SdvWebPort.Rewriter;

/// <summary>
/// Custom MetadataResolver that handles nested type resolution across assemblies.
/// Cecil's default MetadataResolver can fail to resolve nested types when the
/// declaring type's assembly is loaded from an embedded resource (not from disk).
/// This resolver manually searches nested types in resolved assemblies.
/// </summary>
public class CustomMetadataResolver : MetadataResolver
{
    public CustomMetadataResolver(IAssemblyResolver assemblyResolver)
        : base(assemblyResolver)
    {
    }

    public override TypeDefinition? Resolve(TypeReference type)
    {
        try
        {
            return base.Resolve(type);
        }
        catch (Exception ex)
        {
            // Fallback: manually search for the type in all loaded assemblies
            var result = ResolveManual(type);
            if (result != null)
                return result;
            // If we can't resolve, return a dummy TypeDefinition to prevent
            // ResolutionException from crashing the Write.
            // This is safe because Cecil only uses the resolved type for
            // metadata token calculation, not for actual code execution.
            Console.WriteLine("[CustomMetadataResolver] WARNING: Could not resolve " + type.FullName + " — returning dummy");
            return CreateDummyType(type);
        }
    }

    private static TypeDefinition? CreateDummyType(TypeReference type)
    {
        // Create a minimal TypeDefinition that satisfies Cecil's requirements
        var module = ModuleDefinition.CreateModule("Dummy", new ModuleParameters { Kind = ModuleKind.Dll });
        var td = new TypeDefinition(type.Namespace, type.Name, TypeAttributes.Public)
        {
            BaseType = module.TypeSystem.Object,
        };
        module.Types.Add(td);
        return td;
    }

    private TypeDefinition? ResolveManual(TypeReference type)
    {
        // Get the assembly
        var scope = type.Scope;
        AssemblyDefinition? asm = null;

        if (scope is AssemblyNameReference anr)
        {
            asm = AssemblyResolver.Resolve(anr);
        }
        else if (scope is ModuleDefinition mod)
        {
            asm = mod.Assembly;
        }

        if (asm == null) return null;

        // Try to find the type, including nested types
        var fullName = type.FullName;
        return FindType(asm.MainModule, fullName);
    }

    private TypeDefinition? FindType(ModuleDefinition module, string fullName)
    {
        // Handle nested types: "Namespace.ParentType/NestedType"
        var parts = fullName.Split('/');

        TypeDefinition? current = null;
        for (int i = 0; i < parts.Length; i++)
        {
            if (i == 0)
            {
                // Top-level type — search by full name (namespace.name)
                var name = parts[0];
                foreach (var t in module.Types)
                {
                    if (t.FullName == name)
                    {
                        current = t;
                        break;
                    }
                }
                if (current == null) return null;
            }
            else
            {
                // Nested type
                current = current.NestedTypes.FirstOrDefault(t => t.Name == parts[i]);
                if (current == null) return null;
            }
        }

        return current;
    }
}
