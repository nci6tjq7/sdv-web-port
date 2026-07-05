using Mono.Cecil;
using Mono.Cecil.Cil;
using SdvWebPort.Rewriter;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SdvWebPort.Rewriter.Tests;

public class SdvFileSystemRewriterTests
{
    /// <summary>
    /// Test that the rewriter correctly redirects File.OpenRead calls
    /// to SdvFileShim.OpenRead calls in a test assembly.
    /// </summary>
    [Fact]
    public void Rewrite_FileOpenRead_RedirectsToSdvFileShim()
    {
        // Arrange: load this test assembly's own bytes (it has a method that calls File.OpenRead)
        var testAsmPath = Assembly.GetExecutingAssembly().Location;
        var testAsmBytes = File.ReadAllBytes(testAsmPath);

        // Act: run the rewriter
        var rewrittenBytes = SdvFileSystemRewriter.Rewrite(testAsmBytes);

        // Assert: load the rewritten assembly with Cecil and check the method that calls File.OpenRead
        using var ms = new MemoryStream(rewrittenBytes);
        using var asmDef = AssemblyDefinition.ReadAssembly(ms);
        var module = asmDef.MainModule;

        // Find the TestTarget.OpenReadFile method
        var targetType = module.GetType("SdvWebPort.Rewriter.Tests.TestTarget");
        Assert.NotNull(targetType);
        var openReadMethod = targetType.Methods.FirstOrDefault(m => m.Name == "OpenReadFile");
        Assert.NotNull(openReadMethod);

        // Check that the method body has a call to SdvFileShim.OpenRead
        var calls = openReadMethod.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .Where(m => m != null)
            .ToList();

        Assert.Contains(calls, c => c!.DeclaringType?.FullName == "SdvWebPort.Vfs.SdvFileShim" && c.Name == "OpenRead");
        Assert.DoesNotContain(calls, c => c!.DeclaringType?.FullName == "System.IO.File" && c.Name == "OpenRead");
    }

    [Fact]
    public void Rewrite_FileExists_RedirectsToSdvFileShim()
    {
        var testAsmPath = Assembly.GetExecutingAssembly().Location;
        var testAsmBytes = File.ReadAllBytes(testAsmPath);
        var rewrittenBytes = SdvFileSystemRewriter.Rewrite(testAsmBytes);

        using var ms = new MemoryStream(rewrittenBytes);
        using var asmDef = AssemblyDefinition.ReadAssembly(ms);
        var module = asmDef.MainModule;
        var targetType = module.GetType("SdvWebPort.Rewriter.Tests.TestTarget");
        var existsMethod = targetType.Methods.FirstOrDefault(m => m.Name == "CheckFileExists");
        Assert.NotNull(existsMethod);

        var calls = existsMethod.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .Where(m => m != null)
            .ToList();

        Assert.Contains(calls, c => c!.DeclaringType?.FullName == "SdvWebPort.Vfs.SdvFileShim" && c.Name == "Exists");
    }

    [Fact]
    public void Rewrite_DirectoryGetFiles_RedirectsToSdvFileShim()
    {
        var testAsmPath = Assembly.GetExecutingAssembly().Location;
        var testAsmBytes = File.ReadAllBytes(testAsmPath);
        var rewrittenBytes = SdvFileSystemRewriter.Rewrite(testAsmBytes);

        using var ms = new MemoryStream(rewrittenBytes);
        using var asmDef = AssemblyDefinition.ReadAssembly(ms);
        var module = asmDef.MainModule;
        var targetType = module.GetType("SdvWebPort.Rewriter.Tests.TestTarget");
        var getFilesMethod = targetType.Methods.FirstOrDefault(m => m.Name == "ListFiles");
        Assert.NotNull(getFilesMethod);

        var calls = getFilesMethod.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .Where(m => m != null)
            .ToList();

        Assert.Contains(calls, c => c!.DeclaringType?.FullName == "SdvWebPort.Vfs.SdvFileShim" && c.Name == "GetFiles");
    }

    [Fact]
    public void Rewrite_FileReadAllBytes_RedirectsToSdvFileShim()
    {
        var testAsmPath = Assembly.GetExecutingAssembly().Location;
        var testAsmBytes = File.ReadAllBytes(testAsmPath);
        var rewrittenBytes = SdvFileSystemRewriter.Rewrite(testAsmBytes);

        using var ms = new MemoryStream(rewrittenBytes);
        using var asmDef = AssemblyDefinition.ReadAssembly(ms);
        var module = asmDef.MainModule;
        var targetType = module.GetType("SdvWebPort.Rewriter.Tests.TestTarget");
        var readBytesMethod = targetType.Methods.FirstOrDefault(m => m.Name == "ReadBytes");
        Assert.NotNull(readBytesMethod);

        var calls = readBytesMethod.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .Where(m => m != null)
            .ToList();

        Assert.Contains(calls, c => c!.DeclaringType?.FullName == "SdvWebPort.Vfs.SdvFileShim" && c.Name == "ReadAllBytes");
    }

    [Fact]
    public void Rewrite_FileReadAllText_RedirectsToSdvFileShim()
    {
        var testAsmPath = Assembly.GetExecutingAssembly().Location;
        var testAsmBytes = File.ReadAllBytes(testAsmPath);
        var rewrittenBytes = SdvFileSystemRewriter.Rewrite(testAsmBytes);

        using var ms = new MemoryStream(rewrittenBytes);
        using var asmDef = AssemblyDefinition.ReadAssembly(ms);
        var module = asmDef.MainModule;
        var targetType = module.GetType("SdvWebPort.Rewriter.Tests.TestTarget");
        var readTextMethod = targetType.Methods.FirstOrDefault(m => m.Name == "ReadText");
        Assert.NotNull(readTextMethod);

        var calls = readTextMethod.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .Where(m => m != null)
            .ToList();

        Assert.Contains(calls, c => c!.DeclaringType?.FullName == "SdvWebPort.Vfs.SdvFileShim" && c.Name == "ReadAllText");
    }

    [Fact]
    public void Rewrite_DirectoryGetFilesWithPattern_RedirectsToSdvFileShim()
    {
        var testAsmPath = Assembly.GetExecutingAssembly().Location;
        var testAsmBytes = File.ReadAllBytes(testAsmPath);
        var rewrittenBytes = SdvFileSystemRewriter.Rewrite(testAsmBytes);

        using var ms = new MemoryStream(rewrittenBytes);
        using var asmDef = AssemblyDefinition.ReadAssembly(ms);
        var module = asmDef.MainModule;
        var targetType = module.GetType("SdvWebPort.Rewriter.Tests.TestTarget");
        var getFilesWithPatternMethod = targetType.Methods.FirstOrDefault(m => m.Name == "ListFilesWithPattern");
        Assert.NotNull(getFilesWithPatternMethod);

        var calls = getFilesWithPatternMethod.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .Where(m => m != null)
            .ToList();

        Assert.Contains(calls, c => c!.DeclaringType?.FullName == "SdvWebPort.Vfs.SdvFileShim" && c.Name == "GetFiles");
    }

    [Fact]
    public void Rewrite_DirectoryExists_RedirectsToSdvFileShimDirectoryExists()
    {
        var testAsmPath = Assembly.GetExecutingAssembly().Location;
        var testAsmBytes = File.ReadAllBytes(testAsmPath);
        var rewrittenBytes = SdvFileSystemRewriter.Rewrite(testAsmBytes);

        using var ms = new MemoryStream(rewrittenBytes);
        using var asmDef = AssemblyDefinition.ReadAssembly(ms);
        var module = asmDef.MainModule;
        var targetType = module.GetType("SdvWebPort.Rewriter.Tests.TestTarget");
        var dirExistsMethod = targetType.Methods.FirstOrDefault(m => m.Name == "CheckDirExists");
        Assert.NotNull(dirExistsMethod);

        var calls = dirExistsMethod.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .Where(m => m != null)
            .ToList();

        // Note: Directory.Exists → SdvFileShim.DirectoryExists (renamed)
        Assert.Contains(calls, c => c!.DeclaringType?.FullName == "SdvWebPort.Vfs.SdvFileShim" && c.Name == "DirectoryExists");
        Assert.DoesNotContain(calls, c => c!.DeclaringType?.FullName == "System.IO.Directory" && c.Name == "Exists");
    }
}

/// <summary>
/// Test target with methods that call System.IO.File/Directory.
/// The rewriter should redirect these calls to SdvFileShim.
/// </summary>
public static class TestTarget
{
    public static Stream OpenReadFile(string path)
    {
        return File.OpenRead(path);
    }

    public static bool CheckFileExists(string path)
    {
        return File.Exists(path);
    }

    public static string[] ListFiles(string dir)
    {
        return Directory.GetFiles(dir);
    }

    public static byte[] ReadBytes(string path)
    {
        return File.ReadAllBytes(path);
    }

    public static string ReadText(string path)
    {
        return File.ReadAllText(path);
    }

    public static string[] ListFilesWithPattern(string dir, string pattern)
    {
        return Directory.GetFiles(dir, pattern);
    }

    public static bool CheckDirExists(string dir)
    {
        return Directory.Exists(dir);
    }
}
