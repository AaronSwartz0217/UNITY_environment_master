using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace EnvironmentMaster
{
    [Serializable]
    public sealed class EnvironmentSample
    {
        public string utc, scene, camera, source;
        public float fps = -1, frameMs = -1;
        public long drawCalls = -1, batches = -1, setPass = -1, triangles = -1;
        public long allocatedBytes, reservedBytes;
        public ulong textureCurrentBytes, textureTargetBytes, textureDesiredBytes, textureFullBytes, nonStreamingBytes;
        public ulong streamingTextures, pendingTextures, loadingTextures;
        public int activeRenderers, frustumCandidates, outsideFrustum;
        public bool streamingEnabled;
        public float streamingBudgetMB;
    }

    public sealed class EnvironmentSampler : IDisposable
    {
        private ProfilerRecorder draw, batches, passes, triangles;
        private double previousTime = -1;
        private int previousFrame;
        private bool previouslyPlaying;

        public EnvironmentSampler()
        {
            draw = Start("Draw Calls Count"); batches = Start("Batches Count");
            passes = Start("SetPass Calls Count"); triangles = Start("Triangles Count");
        }
        private static ProfilerRecorder Start(string name)
        {
            try { return ProfilerRecorder.StartNew(ProfilerCategory.Render, name, 1); }
            catch { return default; }
        }
        private static long Value(ProfilerRecorder recorder) => recorder.Valid && recorder.Count > 0 ? recorder.LastValue : -1;

        public EnvironmentSample Sample(Camera camera, IReadOnlyList<Renderer> renderers, string scene, bool playing)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            var sample = new EnvironmentSample
            {
                utc = DateTime.UtcNow.ToString("O"), scene = scene,
                camera = camera ? camera.name : "None", source = playing ? "PlayMode/Player" : "Editor",
                allocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), reservedBytes = Profiler.GetTotalReservedMemoryLong(),
                textureCurrentBytes = Texture.currentTextureMemory, textureTargetBytes = Texture.targetTextureMemory,
                textureDesiredBytes = Texture.desiredTextureMemory, textureFullBytes = Texture.totalTextureMemory,
                nonStreamingBytes = Texture.nonStreamingTextureMemory, streamingTextures = Texture.streamingTextureCount,
                pendingTextures = Texture.streamingTexturePendingLoadCount, loadingTextures = Texture.streamingTextureLoadingCount,
                streamingEnabled = QualitySettings.streamingMipmapsActive, streamingBudgetMB = QualitySettings.streamingMipmapsMemoryBudget,
                drawCalls = Value(draw), batches = Value(batches), setPass = Value(passes), triangles = Value(triangles)
            };
            int frames = Time.frameCount - previousFrame;
            if (playing && previouslyPlaying && previousTime >= 0 && now > previousTime && frames > 0)
            {
                sample.fps = (float)(frames / (now - previousTime));
                sample.frameMs = 1000f / sample.fps;
            }
            previousTime = now; previousFrame = Time.frameCount;
            previouslyPlaying = playing;
            var planes = camera ? GeometryUtility.CalculateFrustumPlanes(camera) : null;
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                sample.activeRenderers++;
                if (camera && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0 && GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) sample.frustumCandidates++;
            }
            sample.outsideFrustum = camera ? sample.activeRenderers - sample.frustumCandidates : -1;
            return sample;
        }

        public void Dispose() { draw.Dispose(); batches.Dispose(); passes.Dispose(); triangles.Dispose(); }
    }

    public static class EnvironmentLog
    {
        public static void Append(string path, EnvironmentSample sample)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, JsonUtility.ToJson(sample) + Environment.NewLine);
        }
    }
}
