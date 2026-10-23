using System;
using System.IO;
using UnityEngine;

namespace EnvironmentMaster
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Environment Master/Runtime Monitor")]
    public sealed class EnvironmentRuntimeMonitor : MonoBehaviour
    {
        public Camera targetCamera;
        [Min(0.25f)] public float sampleInterval = 1;
        public bool saveJsonLines = true;
        public EnvironmentSample latest;
        public string logPath;
        private EnvironmentSampler sampler;
        private Renderer[] renderers = Array.Empty<Renderer>();
        private double nextSample, nextRefresh;
        private void OnEnable()
        {
            sampler = new EnvironmentSampler();
            logPath = Path.Combine(Application.persistentDataPath, "EnvironmentMaster", "runtime-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + GetInstanceID() + ".jsonl");
            nextSample = nextRefresh = 0;
        }
        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextSample) return;
            nextSample = now + Mathf.Max(0.25f, sampleInterval);
            if (now >= nextRefresh)
            {
                renderers = FindObjectsOfType<Renderer>(); nextRefresh = now + 5;
            }
            latest = sampler.Sample(targetCamera ? targetCamera : Camera.main, renderers, gameObject.scene.path, true);
            if (!saveJsonLines) return;
            try { EnvironmentLog.Append(logPath, latest); }
            catch (Exception ex) { saveJsonLines = false; Debug.LogWarning("Environment Master 停止写日志：" + ex.Message, this); }
        }
        private void OnDisable() { sampler?.Dispose(); sampler = null; }
    }
}
