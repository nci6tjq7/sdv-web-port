// Force the .NET WASM SDK to export MojoShader symbols by declaring
// DllImport for them. The SDK scans for DllImport attributes and
// automatically exports the referenced native symbols from WASM.

using System.Runtime.InteropServices;
using System.Security;

namespace SdvWebPort.FnaRuntime;

internal static class MojoShaderPInvokes
{
    private const string lib = "FNA3D";

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlGetShaderFormats")]
    public static extern uint GetShaderFormats();

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlCompileShader")]
    public static extern IntPtr CompileShader(IntPtr vp, int vlen, IntPtr pp, int plen, IntPtr vp2, int vl2, IntPtr pp2, int pl2, int fmt);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlGetShaderProfile")]
    public static extern IntPtr GetShaderProfile();

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlDestroyShader")]
    public static extern void DestroyShader(IntPtr shader);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlBindShaders")]
    public static extern void BindShaders(IntPtr shaders);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlGetBoundShaderProgram")]
    public static extern int GetBoundShaderProgram(IntPtr shaders, int technique);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlBindProgram")]
    public static extern void BindProgram(IntPtr shaders, int technique, IntPtr program);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlMapUniformBuffer")]
    public static extern void MapUniformBuffer(IntPtr shaders, int technique, int pass, int uniform, uint offset, uint size);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlUnmapUniformBuffer")]
    public static extern void UnmapUniformBuffer(IntPtr shaders);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlGetUniformData")]
    public static extern IntPtr GetUniformData(IntPtr shaders, int uniform);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlGetVertexData")]
    public static extern IntPtr GetVertexData(IntPtr shaders, int attribute);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlGetPixelData")]
    public static extern IntPtr GetPixelData(IntPtr shaders, int attribute);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlDestroyEffect")]
    public static extern void DestroyEffect(IntPtr effect);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlCreateEffect")]
    public static extern IntPtr CreateEffect(IntPtr effect, int effectSize, IntPtr vp, int vlen, IntPtr pp, int plen, IntPtr data);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlCreateEffectRaw")]
    public static extern IntPtr CreateEffectRaw(IntPtr effect, int effectSize, IntPtr vp, int vlen, IntPtr pp, int plen, IntPtr data);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectBeginPass")]
    public static extern void EffectBeginPass(IntPtr effect, int pass);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectEndPass")]
    public static extern void EffectEndPass(IntPtr effect);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectCommitChanges")]
    public static extern void EffectCommitChanges(IntPtr effect);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectEnd")]
    public static extern void EffectEnd(IntPtr effect);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectSetTechnique")]
    public static extern int EffectSetTechnique(IntPtr effect, IntPtr technique);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectGetCurrentTechnique")]
    public static extern IntPtr EffectGetCurrentTechnique(IntPtr effect);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectGetTechnique")]
    public static extern IntPtr EffectGetTechnique(IntPtr effect, string name);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectGetTechniqueName")]
    public static extern IntPtr EffectGetTechniqueName(IntPtr effect, int technique);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectGetNumTechniques")]
    public static extern int EffectGetNumTechniques(IntPtr effect);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectGetNumPasses")]
    public static extern int EffectGetNumPasses(IntPtr effect, IntPtr technique);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlEffectGetPassName")]
    public static extern IntPtr EffectGetPassName(IntPtr effect, int pass);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlGetAttribMappings")]
    public static extern void GetAttribMappings(IntPtr shaders, out IntPtr attribs, out int count);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlGetUniformMappings")]
    public static extern void GetUniformMappings(IntPtr shaders, out IntPtr uniforms, out int count);

    [DllImport(lib, EntryPoint = "MOJOSHADER_sdlShaderBinaryToSPIRV")]
    public static extern IntPtr ShaderBinaryToSPIRV(IntPtr vp, int vlen, IntPtr pp, int plen, out int outSize);
}
