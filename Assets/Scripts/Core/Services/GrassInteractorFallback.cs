using UnityEngine;

// FoliageLit and the grass shaders read the global _GrassInteractors StructuredBuffer, which
// GrassInteractorRegistry binds on the planet. Off the planet nothing binds it, and an UNBOUND
// StructuredBuffer makes the driver silently drop every draw that declares it — the foliage does not render
// at all, with no error. Any showcase scene or offscreen measuring rig that draws foliage without a planet
// binds this 1-element dummy instead; count 0 means the shader loop never reads it.
public static class GrassInteractorFallback
{
    static readonly int _interactorsId = Shader.PropertyToID(ShaderGlobalIds.GrassInteractors);
    static readonly int _interactorCountId = Shader.PropertyToID(ShaderGlobalIds.GrassInteractorCount);

    // The caller owns the buffer and releases it: the globals are cleared by a domain reload, so the binding
    // has to be re-made from whatever lifetime the caller already has (OnEnable, or a rig's constructor).
    public static void Bind(ref ComputeBuffer buffer)
    {
        buffer ??= new ComputeBuffer(1, sizeof(float) * 8, ComputeBufferType.Structured);
        Shader.SetGlobalBuffer(_interactorsId, buffer);
        Shader.SetGlobalInt(_interactorCountId, 0);
    }
}
