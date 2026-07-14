// Force the linker to include these MojoShader symbols from FNA3D.a
// Without this, the linker strips them because no C code references them
// (only C# P/Invokes them at runtime).

#include <stdint.h>

extern uint32_t MOJOSHADER_sdlGetShaderFormats(void);
extern void* MOJOSHADER_sdlCompileShader(const void*, int, const void*, int, const void*, int, const void*, int, int);
extern const char* MOJOSHADER_sdlGetShaderProfile(void);
extern void MOJOSHADER_sdlDestroyShader(void*);
extern void MOJOSHADER_sdlBindShaders(void*);
extern int MOJOSHADER_sdlGetBoundShaderProgram(void*, int);
extern void MOJOSHADER_sdlBindProgram(void*, int, void*);
extern void MOJOSHADER_sdlMapUniformBuffer(void*, int, int, int, unsigned int, unsigned int);
extern void MOJOSHADER_sdlUnmapUniformBuffer(void*);
extern void* MOJOSHADER_sdlGetUniformData(void*, int);
extern void* MOJOSHADER_sdlGetVertexData(void*, int);
extern void* MOJOSHADER_sdlGetPixelData(void*, int);
extern void MOJOSHADER_sdlDestroyEffect(void*);
extern void* MOJOSHADER_sdlCreateEffect(const void*, int, const void*, int, const void*, int, void*);
extern void* MOJOSHADER_sdlCreateEffectRaw(const void*, int, const void*, int, const void*, int, void*);
extern void MOJOSHADER_sdlEffectBeginPass(void*, int);
extern void MOJOSHADER_sdlEffectEndPass(void*);
extern void MOJOSHADER_sdlEffectCommitChanges(void*);
extern void MOJOSHADER_sdlEffectEnd(void*);
extern int MOJOSHADER_sdlEffectSetTechnique(void*, const void*);
extern const void* MOJOSHADER_sdlEffectGetCurrentTechnique(void*);
extern const void* MOJOSHADER_sdlEffectGetTechnique(void*, const char*);
extern const char* MOJOSHADER_sdlEffectGetTechniqueName(void*, int);
extern int MOJOSHADER_sdlEffectGetNumTechniques(void*);
extern int MOJOSHADER_sdlEffectGetNumPasses(void*, const void*);
extern const char* MOJOSHADER_sdlEffectGetPassName(void*, int);

// Volatile pointer table to prevent dead-code elimination
volatile void* mojoshader_exports[] = {
    (void*)MOJOSHADER_sdlGetShaderFormats,
    (void*)MOJOSHADER_sdlCompileShader,
    (void*)MOJOSHADER_sdlGetShaderProfile,
    (void*)MOJOSHADER_sdlDestroyShader,
    (void*)MOJOSHADER_sdlBindShaders,
    (void*)MOJOSHADER_sdlGetBoundShaderProgram,
    (void*)MOJOSHADER_sdlBindProgram,
    (void*)MOJOSHADER_sdlMapUniformBuffer,
    (void*)MOJOSHADER_sdlUnmapUniformBuffer,
    (void*)MOJOSHADER_sdlGetUniformData,
    (void*)MOJOSHADER_sdlGetVertexData,
    (void*)MOJOSHADER_sdlGetPixelData,
    (void*)MOJOSHADER_sdlDestroyEffect,
    (void*)MOJOSHADER_sdlCreateEffect,
    (void*)MOJOSHADER_sdlCreateEffectRaw,
    (void*)MOJOSHADER_sdlEffectBeginPass,
    (void*)MOJOSHADER_sdlEffectEndPass,
    (void*)MOJOSHADER_sdlEffectCommitChanges,
    (void*)MOJOSHADER_sdlEffectEnd,
    (void*)MOJOSHADER_sdlEffectSetTechnique,
    (void*)MOJOSHADER_sdlEffectGetCurrentTechnique,
    (void*)MOJOSHADER_sdlEffectGetTechnique,
    (void*)MOJOSHADER_sdlEffectGetTechniqueName,
    (void*)MOJOSHADER_sdlEffectGetNumTechniques,
    (void*)MOJOSHADER_sdlEffectGetNumPasses,
    (void*)MOJOSHADER_sdlEffectGetPassName,
};
