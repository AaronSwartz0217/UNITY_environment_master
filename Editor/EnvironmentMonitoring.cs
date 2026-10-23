using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentMaster.Editor
{
    [InitializeOnLoad]
    public static class EnvironmentMonitoring
    {
        private static string Key => "EnvironmentMaster." + Application.dataPath;
        private static EnvironmentSampler sampler;
        private static Renderer[] renderers = Array.Empty<Renderer>();
        private static double nextSample, nextRefresh;
        private static readonly Queue<EnvironmentSample> samples = new Queue<EnvironmentSample>();
        public static EnvironmentSample Latest { get; private set; }
        public static Camera TargetCamera;
        public static string Error { get; private set; }
        public static string LogPath { get; private set; }
        public static bool Enabled { get => EditorPrefs.GetBool(Key + ".enabled", true); set => EditorPrefs.SetBool(Key + ".enabled", value); }
        public static bool Record { get => EditorPrefs.GetBool(Key + ".record", true); set => EditorPrefs.SetBool(Key + ".record", value); }
        public static float Interval { get => EditorPrefs.GetFloat(Key + ".interval", 1); set => EditorPrefs.SetFloat(Key + ".interval", Mathf.Clamp(value, 0.25f, 30)); }
        public static int SampleCount => samples.Count;
        public static EnvironmentSample Baseline
        {
            get { string value = SessionState.GetString(Key + ".baseline", ""); return string.IsNullOrEmpty(value) ? null : JsonUtility.FromJson<EnvironmentSample>(value); }
        }
        static EnvironmentMonitoring()
        {
            EditorApplication.update += Update;
            EditorApplication.hierarchyChanged += () => nextRefresh = 0;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
            LogPath = Path.GetFullPath("Library/EnvironmentMaster/Monitoring/editor-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl");
        }
        private static void Dispose() { sampler?.Dispose(); sampler = null; }
        private static void Update()
        {
            if (!Enabled || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextSample) return;
            nextSample = EditorApplication.timeSinceStartup + Interval;
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded) return;
                if (EditorApplication.timeSinceStartup >= nextRefresh)
                {
                    renderers = EnvironmentOptimization.SceneRenderers(); nextRefresh = EditorApplication.timeSinceStartup + 5;
                }
                var camera = TargetCamera && TargetCamera.gameObject.scene == scene ? TargetCamera :
                    scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).FirstOrDefault(c => c.enabled && c.CompareTag("MainCamera"));
                if (!camera) camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).FirstOrDefault(c => c.enabled);
                if (sampler == null) sampler = new EnvironmentSampler();
                Latest = sampler.Sample(camera, renderers, scene.path, EditorApplication.isPlaying);
                samples.Enqueue(Latest); while (samples.Count > 600) samples.Dequeue();
                Error = null;
                if (Record)
                {
                    try { EnvironmentLog.Append(LogPath, Latest); }
                    catch (Exception ex) { Record = false; Error = "日志写入失败，采样继续：" + ex.Message; }
                }
            }
            catch (Exception ex) { Error = ex.Message; }
        }
        public static void SaveBaseline()
        {
            if (Latest != null) SessionState.SetString(Key + ".baseline", JsonUtility.ToJson(Latest));
        }
        private static string MB(ulong bytes) => (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";
        private static string Counter(long value) => value < 0 ? "N/A" : value.ToString("N0");
        public static string Summary()
        {
            var s = Latest;
            if (s == null) return "等待采样…";
            var sb = new StringBuilder();
            sb.AppendLine("采样 UTC：" + s.utc + " | " + s.source + " | Camera：" + s.camera);
            sb.AppendLine("场景：" + s.scene);
            sb.AppendLine(s.fps < 0 ? "运行帧率：N/A（需进入 Play 且至少采样两次）" : $"运行帧率：{s.fps:F1} FPS / {s.frameMs:F2} ms（采样区间平均，非 GPU 时间）");
            sb.AppendLine($"进程渲染计数：Draw Calls {Counter(s.drawCalls)} / Batches {Counter(s.batches)} / SetPass {Counter(s.setPass)} / Triangles {Counter(s.triangles)}");
            sb.AppendLine($"活动 Renderer：{s.activeRenderers}；视锥与层掩码内候选：{(s.camera == "None" ? "N/A" : s.frustumCandidates.ToString())}；其余：{Counter(s.outsideFrustum)}");
            sb.AppendLine("候选数不是最终绘制数，不包含遮挡、LOD、距离剔除或多相机合并结果。");
            sb.AppendLine("纹理当前 / 目标 / 理论全分辨率：" + MB(s.textureCurrentBytes) + " / " + MB(s.textureTargetBytes) + " / " + MB(s.textureFullBytes));
            sb.AppendLine($"Streaming={s.streamingEnabled}；预算 {s.streamingBudgetMB:F0} MB；流送纹理 {s.streamingTextures}；等待 {s.pendingTextures}；加载 {s.loadingTextures}");
            sb.AppendLine("非流送纹理：" + MB(s.nonStreamingBytes) + "；Unity 已分配内存：" + MB((ulong)Math.Max(0, s.allocatedBytes)));
            sb.AppendLine($"光照烘焙：{Lightmapping.isRunning}；遮挡烘焙：{StaticOcclusionCulling.isRunning}；历史缓存 {SampleCount}/600");
            var baseline = Baseline;
            if (baseline != null)
            {
                sb.AppendLine("基准：" + baseline.utc + "（" + baseline.source + "，" + baseline.camera + "）");
                if (baseline.scene == s.scene && baseline.source == s.source && baseline.camera == s.camera)
                    sb.AppendLine($"相对基准纹理变化：{((double)s.textureCurrentBytes - baseline.textureCurrentBytes) / 1048576:F1} MB；候选变化：{s.frustumCandidates - baseline.frustumCandidates}");
                else sb.AppendLine("基准场景、采样模式或相机不同，暂不比较。请固定视角、分辨率与质量档位再采样。");
            }
            return sb.ToString();
        }
        public static void ExportCSV(string path)
        {
            var sb = new StringBuilder("utc,scene,camera,source,fps,frameMs,drawCalls,batches,setPass,triangles,allocatedBytes,textureCurrentBytes,textureTargetBytes,streamingTextures,pendingTextures,loadingTextures,activeRenderers,frustumCandidates\n");
            IEnumerable<EnvironmentSample> source = File.Exists(LogPath)
                ? File.ReadLines(LogPath).Where(line => !string.IsNullOrWhiteSpace(line)).Select(JsonUtility.FromJson<EnvironmentSample>) : samples;
            foreach (var s in source)
            {
                string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
                sb.AppendLine(string.Join(",", new[] { Q(s.utc), Q(s.scene), Q(s.camera), Q(s.source), s.fps.ToString(CultureInfo.InvariantCulture), s.frameMs.ToString(CultureInfo.InvariantCulture),
                    s.drawCalls.ToString(), s.batches.ToString(), s.setPass.ToString(), s.triangles.ToString(), s.allocatedBytes.ToString(), s.textureCurrentBytes.ToString(), s.textureTargetBytes.ToString(),
                    s.streamingTextures.ToString(), s.pendingTextures.ToString(), s.loadingTextures.ToString(), s.activeRenderers.ToString(), s.frustumCandidates.ToString() }));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
