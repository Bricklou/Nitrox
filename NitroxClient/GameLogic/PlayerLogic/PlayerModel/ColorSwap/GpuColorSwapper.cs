using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NitroxClient.GameLogic.PlayerLogic.PlayerModel.ColorSwap;

/// <summary>
///     Recolors textures on the GPU with the ColorSwap shader. The source textures are never modified. Results are cached per
///     (source texture, filters, player color) so equipment of players sharing a color shares the same texture.
/// </summary>
public static class GpuColorSwapper
{
    private static readonly int ACCUM_TEX_ID = Shader.PropertyToID("_AccumTex");
    private static readonly int BLOCK_ID = Shader.PropertyToID("_Block");

    private static readonly Dictionary<string, RenderTexture> cache = [];
    private static readonly Dictionary<RenderTexture, Texture> originals = [];
    private static Material material;
    private static bool warnedMissingShader;

    public static void Initialize(Shader shader)
    {
        if (!shader)
        {
            return;
        }
        if (material)
        {
            UnityEngine.Object.Destroy(material);
        }
        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    /// <summary>
    ///     Frees all cached textures. Call when leaving the world.
    /// </summary>
    public static void ReleaseAll()
    {
        foreach (RenderTexture renderTexture in cache.Values)
        {
            if (renderTexture)
            {
                renderTexture.Release();
                UnityEngine.Object.Destroy(renderTexture);
            }
        }
        cache.Clear();
        originals.Clear();
    }

    /// <summary>
    ///     Returns <paramref name="texture" /> as the game originally had it, even if it is one of our recolored results.
    /// </summary>
    public static Texture GetOriginal(Texture texture)
    {
        return texture is RenderTexture renderTexture && originals.TryGetValue(renderTexture, out Texture original) ? original : texture;
    }

    /// <summary>
    ///     Applies the filters in order. Every filter reads the original pixels and only writes inside its block, so overlapping
    ///     blocks behave like writing separate pixel copies back in order.
    /// </summary>
    /// <returns>The recolored texture, or the unmodified source when the shader isn't available.</returns>
    public static Texture Swap(Texture source, params (HsvSwapper filter, TextureBlock block)[] filters)
    {
        source = GetOriginal(source);
        if (!material)
        {
            if (!warnedMissingShader)
            {
                warnedMissingShader = true;
                Log.Error("ColorSwap shader isn't loaded, player colors won't be applied.");
            }
            return source;
        }

        string key = BuildCacheKey(source, filters);
        if (cache.TryGetValue(key, out RenderTexture cached) && cached)
        {
            return cached;
        }

        RenderTexture result = CreateRenderTexture(source);

        RenderTextureDescriptor descriptor = result.descriptor;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        RenderTexture accumulated = RenderTexture.GetTemporary(descriptor);
        RenderTexture target = RenderTexture.GetTemporary(descriptor);

        Graphics.Blit(source, accumulated);
        foreach ((HsvSwapper filter, TextureBlock block) in filters)
        {
            filter.ConfigureMaterial(material);
            material.SetVector(BLOCK_ID, ToNormalizedBlock(block, source));
            material.SetTexture(ACCUM_TEX_ID, accumulated);
            Graphics.Blit(source, target, material);
            (accumulated, target) = (target, accumulated);
        }

        Graphics.Blit(accumulated, result);
        RenderTexture.ReleaseTemporary(accumulated);
        RenderTexture.ReleaseTemporary(target);

        cache[key] = result;
        originals[result] = source;
        return result;
    }

    private static RenderTexture CreateRenderTexture(Texture source)
    {
        // Default follows the project's color space: sRGB in Linear (decoded when sampled, encoded when written) and plain bytes in Gamma.
        // Forcing sRGB would make Gamma projects decode the texture without it ever being encoded, which darkens it.
        RenderTexture renderTexture = new(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
        {
            useMipMap = true,
            autoGenerateMips = true,
            filterMode = source.filterMode,
            anisoLevel = source.anisoLevel,
            wrapMode = source.wrapMode,
            hideFlags = HideFlags.HideAndDontSave
        };
        renderTexture.Create();
        return renderTexture;
    }

    private static Vector4 ToNormalizedBlock(TextureBlock block, Texture source)
    {
        if (block == null)
        {
            return new Vector4(0, 0, 1, 1);
        }
        return new Vector4(
            (float)block.X / source.width,
            (float)block.Y / source.height,
            (float)block.BlockWidth / source.width,
            (float)block.BlockHeight / source.height);
    }

    private static string BuildCacheKey(Texture source, (HsvSwapper filter, TextureBlock block)[] filters)
    {
        StringBuilder builder = new();
        builder.Append(source.GetInstanceID());
        foreach ((HsvSwapper filter, TextureBlock block) in filters)
        {
            builder.Append('#').Append(filter.CacheKey);
            if (block != null)
            {
                builder.Append(FormattableString.Invariant($"@{block.X},{block.Y},{block.BlockWidth},{block.BlockHeight}"));
            }
        }
        return builder.ToString();
    }
}
