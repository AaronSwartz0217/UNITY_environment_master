using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OneClickSceneBake
{
    public enum PreparedLightMode { Mixed, Baked }

    [Serializable]
    public sealed class BakePreparationOptions
    {
        public bool configureLighting = true;
        public bool generateUV = true;
        public bool convertRealtimeLights = true;
        public bool markSelectedObjects;
        public PreparedLightMode lightMode = PreparedLightMode.Mixed;
    }

    public static class BakePreparation
    {
        public static bool IsPreparing { get; private set; }
        public static string LastReport { get; private set; } = "尚未执行自动准备。";

        private static bool IsContributor(MeshRenderer renderer) => renderer.enabled && renderer.gameObject.activeInHierarchy &&
            (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.ContributeGI) != 0;

        private static MeshRenderer[] Renderers(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).ToArray();

        private static bool MissingUV(Mesh mesh) => mesh && !mesh.HasVertexAttribute(VertexAttribute.TexCoord1);

        private static GameObject[] SelectedEnvironment(Scene scene) => Selection.gameObjects
            .Where(go => go.scene == scene).SelectMany(go => go.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).Where(go => go.GetComponent<MeshRenderer>() || go.GetComponent<Terrain>()).Distinct().ToArray();

        public static string Preview(BakePreparationOptions options)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return "没有有效的活动场景。";
            var selected = options.markSelectedObjects ? new HashSet<GameObject>(SelectedEnvironment(scene)) : new HashSet<GameObject>();
            var missing = Renderers(scene).Where(r => IsContributor(r) || (selected.Contains(r.gameObject) && r.enabled && r.gameObject.activeInHierarchy))
                .Select(r => r.GetComponent<MeshFilter>()).Where(f => f && MissingUV(f.sharedMesh)).ToArray();
            var models = missing.Select(f => AssetDatabase.GetAssetPath(f.sharedMesh))
                .Where(p => AssetImporter.GetAtPath(p) is ModelImporter).Distinct().ToArray();
            int lights = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true))
                .Count(l => l.enabled && l.gameObject.activeInHierarchy && l.lightmapBakeType == LightmapBakeType.Realtime);
            return $"预计处理：{(options.generateUV ? missing.Length : 0)} 个缺失 UV2 的网格实例（{(options.generateUV ? models.Length : 0)} 个模型文件）；" +
                $"{(options.convertRealtimeLights ? lights : 0)} 个实时光源 → {options.lightMode}；{selected.Count} 个选中环境物体。\n" +
                (options.configureLighting ? "配置手动烘焙 / Baked GI；保留分辨率、采样和后端。\n" : "保留现有 Lighting Settings。\n") +
                "只处理当前活动场景中启用的 GI 网格。模型重新导入会影响共用该模型的其他场景；已有 UV2 不会被重写。";
        }

        public static bool Run(BakePreparationOptions options)
        {
            var report = new StringBuilder();
            string backup = null;
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (IsPreparing || Lightmapping.isRunning || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            { LastReport = "请等待当前烘焙 / 编译完成，并在编辑模式运行。"; return false; }
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || PrefabStageUtility.GetCurrentPrefabStage() != null)
            { LastReport = "请先打开普通场景并退出 Prefab 编辑模式。"; return false; }
            if (string.IsNullOrEmpty(scene.path) || scene.isDirty)
                if (!EditorSceneManager.SaveScene(scene)) { LastReport = "取消保存，未修改或烘焙。"; return false; }
            IsPreparing = true;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("一键烘焙准备");
            try
            {
                backup = Path.GetFullPath("Library/OneClickSceneBakeBackups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                Directory.CreateDirectory(backup);
                File.Copy(scene.path, Path.Combine(backup, "Scene.unity"));
                report.AppendLine("场景原路径：" + scene.path);
                report.AppendLine("准备前备份：" + backup);
                string generated = Path.GetDirectoryName(scene.path).Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(scene.path) + "_BakeGenerated";
                if (options.configureLighting)
                {
                    Lightmapping.TryGetLightingSettings(out var current);
                    if (!current || current.autoGenerate || !current.bakedGI)
                    {
                        EnsureFolder(generated);
                        var settings = UnityEngine.Object.Instantiate(current ? current : Lightmapping.lightingSettingsDefaults);
                        settings.name = scene.name + "_ManualBake";
                        settings.autoGenerate = false;
                        settings.bakedGI = true;
                        string path = AssetDatabase.GenerateUniqueAssetPath(generated + "/LightingSettings.lighting");
                        AssetDatabase.CreateAsset(settings, path);
                        Lightmapping.lightingSettings = settings;
                        report.AppendLine("创建场景专用 Lighting Settings：" + path + "（保留原参数）");
                    }
                }
                if (options.markSelectedObjects)
                {
                    var selected = SelectedEnvironment(scene);
                    foreach (var go in selected)
                    {
                        Undo.RecordObject(go, "标记 Contribute GI");
                        GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) | StaticEditorFlags.ContributeGI);
                        if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.RecordPrefabInstancePropertyModifications(go);
                    }
                    report.AppendLine("标记选中环境物体：" + selected.Length);
                }
                if (options.convertRealtimeLights)
                {
                    int count = 0;
                    foreach (var light in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)))
                    {
                        if (!light.enabled || !light.gameObject.activeInHierarchy || light.lightmapBakeType != LightmapBakeType.Realtime) continue;
                        Undo.RecordObject(light, "设置烘焙光源模式");
                        light.lightmapBakeType = options.lightMode == PreparedLightMode.Baked ? LightmapBakeType.Baked : LightmapBakeType.Mixed;
                        if (PrefabUtility.IsPartOfPrefabInstance(light)) PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                        count++;
                    }
                    report.AppendLine("实时光源转换：" + count + " → " + options.lightMode);
                }
                if (options.generateUV) GenerateUV(scene, generated, backup, report);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("准备后的场景未能保存。");
                report.AppendLine("自动准备完成；场景已保存。");
                LastReport = report.ToString();
                File.WriteAllText(Path.Combine(backup, "PreparationReport.txt"), LastReport);
                Debug.Log("[当前场景烘焙] " + LastReport);
                return true;
            }
            catch (Exception ex)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                report.AppendLine("准备未完成，未启动烘焙：" + ex.Message);
                report.AppendLine("部分修改可能已完成；场景对象可撤销，模型导入设置请使用备份恢复。");
                LastReport = report.ToString();
                if (backup != null) File.WriteAllText(Path.Combine(backup, "PreparationReport.txt"), LastReport);
                Debug.LogWarning("[当前场景烘焙] " + LastReport);
                return false;
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
                EditorUtility.ClearProgressBar();
                IsPreparing = false;
            }
        }

        private static void GenerateUV(Scene scene, string generated, string backup, StringBuilder report)
        {
            var importPaths = new HashSet<string>();
            foreach (var renderer in Renderers(scene).Where(IsContributor))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !MissingUV(filter.sharedMesh)) continue;
                string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
                if (AssetImporter.GetAtPath(path) is ModelImporter) importPaths.Add(path);
            }
            int index = 0;
            foreach (string path in importPaths)
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new InvalidOperationException("无法修改只读包内模型，请先复制到 Assets：" + path);
                if (EditorUtility.DisplayCancelableProgressBar("生成光照 UV", path, (float)index / Math.Max(1, importPaths.Count))) throw new OperationCanceledException("用户取消 UV 准备。");
                string metaBackup = Path.Combine(backup, path.Replace('/', Path.DirectorySeparatorChar) + ".meta");
                Directory.CreateDirectory(Path.GetDirectoryName(metaBackup));
                File.Copy(path + ".meta", metaBackup);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.generateSecondaryUV = true;
                importer.SaveAndReimport();
                report.AppendLine("模型生成光照 UV：" + path);
                index++;
            }
            // Reacquire components after model reimport; imported meshes may have been replaced.
            var copies = new Dictionary<Mesh, Mesh>();
            foreach (var renderer in Renderers(scene).Where(IsContributor))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !MissingUV(filter.sharedMesh)) continue;
                var original = filter.sharedMesh;
                string path = AssetDatabase.GetAssetPath(original);
                if (AssetImporter.GetAtPath(path) is ModelImporter)
                    throw new InvalidOperationException("重新导入后仍缺少 UV2：" + path + " / " + original.name);
                if (!copies.TryGetValue(original, out var copy))
                {
                    if (EditorUtility.DisplayCancelableProgressBar("生成独立网格光照 UV", original.name, 0.9f)) throw new OperationCanceledException("用户取消 UV 准备。");
                    copy = UnityEngine.Object.Instantiate(original);
                    copy.name = original.name + "_LightmapUV";
                    try
                    {
                        if (!Unwrapping.GenerateSecondaryUVSet(copy) || MissingUV(copy))
                            throw new InvalidOperationException("无法为网格生成 UV2：" + original.name);
                    }
                    catch { UnityEngine.Object.DestroyImmediate(copy); throw; }
                    EnsureFolder(generated);
                    string safeName = string.Concat(copy.name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
                    string copyPath = AssetDatabase.GenerateUniqueAssetPath(generated + "/" + safeName + ".asset");
                    AssetDatabase.CreateAsset(copy, copyPath);
                    copies.Add(original, copy);
                    report.AppendLine("独立网格副本（保留源网格）：" + copyPath);
                }
                Undo.RecordObject(filter, "使用光照 UV 网格");
                filter.sharedMesh = copy;
                if (PrefabUtility.IsPartOfPrefabInstance(filter)) PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            }
            int missing = Renderers(scene).Where(IsContributor).Count(r => r.GetComponent<MeshFilter>() && MissingUV(r.GetComponent<MeshFilter>().sharedMesh));
            if (missing > 0) throw new InvalidOperationException("仍有 " + missing + " 个 GI 网格缺少 UV2。");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
