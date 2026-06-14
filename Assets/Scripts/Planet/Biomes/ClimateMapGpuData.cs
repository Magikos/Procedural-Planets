using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public sealed class ClimateMapGpuData : IDisposable
{
    const int FaceCount = 6;

    static readonly int ClimateMapId = Shader.PropertyToID(ShaderGlobalIds.ClimateMap);
    static readonly int ClimateMapResolutionId = Shader.PropertyToID(ShaderGlobalIds.ClimateMapResolution);
    static readonly int ClimateTemperatureRangeId =
        Shader.PropertyToID(ShaderGlobalIds.ClimateTemperatureRangeCelsius);

    Texture2DArray _texture;

    ClimateMapGpuData(Texture2DArray texture)
    {
        _texture = texture;
    }

    public Texture2DArray Texture => _texture;
    public int Resolution => _texture != null ? _texture.width : 0;

    public static async Awaitable<ClimateMapGpuData> BuildAsync(
        IClimateProvider climateProvider,
        IReadOnlyList<IFaceMeshSampler> faceSamplers,
        int resolution,
        float minimumTemperatureCelsius,
        float maximumTemperatureCelsius,
        ILogger logger,
        IProgressHandle progress,
        CancellationToken ct)
    {
        if (climateProvider == null)
            throw new ArgumentNullException(nameof(climateProvider));
        if (faceSamplers == null || faceSamplers.Count < FaceCount)
            throw new ArgumentException(
                "Six generated face samplers are required.",
                nameof(faceSamplers));

        resolution = Mathf.Clamp(resolution, 32, 512);
        var stopwatch = Stopwatch.StartNew();
        progress?.Report(0f, "Calculating climate map...");
        var samplerSnapshot = new IFaceMeshSampler[FaceCount];
        for (int face = 0; face < FaceCount; face++)
            samplerSnapshot[face] = faceSamplers[face];

        float computeProgress = 0f;
        Awaitable<Color[][]> computeTask = ComputeFacePixelsAsync(
            climateProvider,
            samplerSnapshot,
            resolution,
            ct,
            value => Volatile.Write(ref computeProgress, value));
        var computeAwaiter = computeTask.GetAwaiter();
        float reportedComputeProgress = 0f;
        while (!computeAwaiter.IsCompleted)
        {
            reportedComputeProgress = Mathf.Max(
                reportedComputeProgress,
                Volatile.Read(ref computeProgress));
            progress?.Report(
                reportedComputeProgress * 0.75f,
                "Calculating climate map...");
            await Awaitable.NextFrameAsync();
        }
        Color[][] facePixels = computeAwaiter.GetResult();
        ct.ThrowIfCancellationRequested();

        TextureFormat format = SystemInfo.SupportsTextureFormat(TextureFormat.RGHalf)
            ? TextureFormat.RGHalf
            : TextureFormat.RGBAHalf;
        var texture = new Texture2DArray(
            resolution,
            resolution,
            FaceCount,
            format,
            true,
            true)
        {
            name = "Planet Climate Map",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 0,
            hideFlags = HideFlags.DontSave,
        };

        try
        {
            for (int face = 0; face < FaceCount; face++)
            {
                texture.SetPixels(facePixels[face], face, 0);
                progress?.Report(
                    0.75f + 0.15f * ((face + 1f) / FaceCount),
                    $"Uploading climate face {face + 1}/{FaceCount}...");
                await Awaitable.NextFrameAsync(ct);
            }
            texture.Apply(true, true);
        }
        catch
        {
            UnityEngine.Object.Destroy(texture);
            throw;
        }

        Shader.SetGlobalTexture(ClimateMapId, texture);
        Shader.SetGlobalFloat(ClimateMapResolutionId, resolution);
        Shader.SetGlobalVector(
            ClimateTemperatureRangeId,
            new Vector4(
                minimumTemperatureCelsius,
                maximumTemperatureCelsius,
                maximumTemperatureCelsius - minimumTemperatureCelsius,
                0f));

        stopwatch.Stop();
        int bytesPerPixel = format == TextureFormat.RGHalf ? 4 : 8;
        long baseBytes = (long)resolution * resolution * FaceCount * bytesPerPixel;
        long approximateBytesWithMips = baseBytes * 4L / 3L;
        logger?.Log(
            LogLevel.Debug,
            "Climate",
            $"Built GPU climate map: {resolution}x{resolution}x6 {format}, " +
            $"~{approximateBytesWithMips / (1024f * 1024f):F2} MiB, " +
            $"{stopwatch.ElapsedMilliseconds} ms");

        progress?.Report(1f, "Climate map ready.");
        return new ClimateMapGpuData(texture);
    }

    static async Awaitable<Color[][]> ComputeFacePixelsAsync(
        IClimateProvider climateProvider,
        IFaceMeshSampler[] faceSamplers,
        int resolution,
        CancellationToken ct,
        Action<float> onProgress)
    {
        await Awaitable.BackgroundThreadAsync();
        var facePixels = new Color[FaceCount][];
        int completedRows = 0;
        int totalRows = FaceCount * resolution;
        var options = new ParallelOptions { CancellationToken = ct };

        Parallel.For(0, FaceCount, options, face =>
        {
            IFaceMeshSampler sampler = faceSamplers[face];
            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                float v = EdgeSnappedUv(y, resolution);
                int row = y * resolution;
                for (int x = 0; x < resolution; x++)
                {
                    float u = EdgeSnappedUv(x, resolution);
                    Vector3 direction = CoordinateConverter.CubeFaceToUnitSphere(
                        face,
                        new Vector2(u, v));
                    float elevation = SampleElevation(sampler, u, v);
                    ClimateSample climate = climateProvider.Evaluate(direction, elevation);
                    pixels[row + x] = new Color(
                        climate.Temperature01,
                        climate.Moisture01,
                        0f,
                        1f);
                }

                int rows = Interlocked.Increment(ref completedRows);
                if ((rows & 7) == 0 || rows == totalRows)
                    onProgress?.Invoke((float)rows / totalRows);
            }

            facePixels[face] = pixels;
        });

        ct.ThrowIfCancellationRequested();
        await Awaitable.MainThreadAsync();
        return facePixels;
    }

    public void Dispose()
    {
        if (_texture == null)
            return;

        if (Shader.GetGlobalTexture(ClimateMapId) == _texture)
        {
            Shader.SetGlobalTexture(ClimateMapId, (Texture)null);
            Shader.SetGlobalFloat(ClimateMapResolutionId, 0f);
        }

        UnityEngine.Object.Destroy(_texture);
        _texture = null;
    }

    static float SampleElevation(IFaceMeshSampler sampler, float u, float v)
    {
        if (sampler?.Elevations == null || sampler.Resolution < 2)
            return 0f;

        int resolution = sampler.Resolution;
        float x = Mathf.Clamp01(u) * (resolution - 1);
        float y = Mathf.Clamp01(v) * (resolution - 1);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, resolution - 1);
        int y1 = Mathf.Min(y0 + 1, resolution - 1);
        float tx = x - x0;
        float ty = y - y0;
        float a = Mathf.Lerp(
            sampler.Elevations[y0 * resolution + x0],
            sampler.Elevations[y0 * resolution + x1],
            tx);
        float b = Mathf.Lerp(
            sampler.Elevations[y1 * resolution + x0],
            sampler.Elevations[y1 * resolution + x1],
            tx);
        return Mathf.Lerp(a, b, ty);
    }

    static float EdgeSnappedUv(int index, int resolution)
    {
        if (index <= 0) return 0f;
        if (index >= resolution - 1) return 1f;
        return (index + 0.5f) / resolution;
    }
}
