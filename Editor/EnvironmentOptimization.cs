using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentMaster.Editor
{
    public static class EnvironmentOptimization
    {
        public static string LastReport { get; private set; } = "尚未执行优化操作。";
        private static string Root => Path.GetFullPath("Library/EnvironmentMaster");
        public static Renderer[] SceneRenderers() => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();
        public static GameObject[] SelectedObjects() => Selection.gameObjects.Where(g => g.scene == SceneManager.GetActiveScene())
            .SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).Distinct().ToArray();

        public static string[] SceneTexturePaths()
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects().Cast<UnityEngine.Object>().ToList();
            if (RenderSettings.skybox) roots.Add(RenderSettings.skybox);
            return EditorUtility.CollectDependencies(roots.ToArray()).Select(AssetDatabase.GetAssetPath)
                .Where(p => !string.IsNullOrEmpty(p)).Distinct().Where(p => EligibleTexture(p)).OrderBy(p => p).ToArray();
        }
        private static bool EligibleTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            return path.StartsWith("Assets/", StringComparison.Ordinal) && importer && importer.textureShape == TextureImporterShape.Texture2D &&
                (importer.textureType == TextureImporterType.Default || importer.textureType == TextureImporterType.NormalMap);
        }
        public static string TextureAudit()
        {
            var paths = SceneTexturePaths();
            var imports = paths.Select(p => (TextureImporter)AssetImporter.GetAtPath(p)).ToArray();
            return $"当前场景可处理纹理：{paths.Length}；已开 Mipmap：{imports.Count(i => i.mipmapEnabled)}；" +
                $"已开 Streaming：{imports.Count(i => i.streamingMipmaps)}；Read/Write：{imports.Count(i => i.isReadable)}。\n" +
                $"当前质量级别：{QualitySettings.names[QualitySettings.GetQualityLevel()]}；Streaming={QualitySettings.streamingMipmapsActive}；预算={QualitySettings.streamingMipmapsMemoryBudget:F0} MB。\n" +
                "排除 Sprite、GUI、Lightmap、Cubemap 和只读包资源。模型内嵌纹理不在此范围。";
        }

        [Serializable] private sealed class ImportBackup { public string path, guid, backup; }
        [Serializable] private sealed class TextureBackup
        {
            public int qualityLevel;
            public bool qualityChanged, active, allCameras;
            public float budget;
            public List<ImportBackup> textures = new List<ImportBackup>();
        }
        private static void Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || Lightmapping.isRunning || StaticOcclusionCulling.isRunning)
                throw new InvalidOperationException("请在编辑模式且烘焙、编译结束后操作。");
            if (!SceneManager.GetActiveScene().IsValid() || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("请先打开普通场景，退出 Prefab 模式。");
        }

        public static void ConfigureTextures(bool enableQuality, float budget, bool streaming, bool mipmaps, bool disableReadWrite, int maxSize)
        {
            Guard();
            if (budget < 32 || budget > 8192) throw new ArgumentOutOfRangeException(nameof(budget), "预算范围 32–8192 MB。");
            var paths = SceneTexturePaths();
            string folder = Path.Combine(Root, "Backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            var backup = new TextureBackup
            {
                qualityLevel = QualitySettings.GetQualityLevel(), active = QualitySettings.streamingMipmapsActive,
                allCameras = QualitySettings.streamingMipmapsAddAllCameras, budget = QualitySettings.streamingMipmapsMemoryBudget
            };
            string manifest = Path.Combine(folder, "textures.json");
            string latest = Path.Combine(Root, "last-texture-backup.txt");
            File.WriteAllText(latest, manifest);
            var report = new StringBuilder();
            try
            {
                if (enableQuality)
                {
                    backup.qualityChanged = true;
                    File.WriteAllText(manifest, JsonUtility.ToJson(backup, true));
                    SetStreamingQuality(backup.qualityLevel, true, true, budget);
                    report.AppendLine("已启用当前质量级别的 Streaming，包含所有相机；预算 " + budget + " MB。");
                }
                for (int index = 0; index < paths.Length; index++)
                {
                    string path = paths[index];
                    if (EditorUtility.DisplayCancelableProgressBar("纹理优化", path, (float)index / Math.Max(paths.Length, 1)))
                    { report.AppendLine("已取消后续处理，已完成项保留，可恢复最近操作。"); break; }
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    bool needsMip = mipmaps && !importer.mipmapEnabled;
                    bool needsStreaming = streaming && (mipmaps || importer.mipmapEnabled) && !importer.streamingMipmaps;
                    bool needsRead = disableReadWrite && importer.isReadable;
                    bool needsSize = maxSize > 0 && importer.maxTextureSize > maxSize;
                    if (!needsMip && !needsStreaming && !needsRead && !needsSize) continue;
                    string meta = Path.Combine(folder, "importer-" + index + ".meta");
                    File.Copy(path + ".meta", meta);
                    backup.textures.Add(new ImportBackup { path = path, guid = AssetDatabase.AssetPathToGUID(path), backup = meta });
                    File.WriteAllText(manifest, JsonUtility.ToJson(backup, true));
                    if (needsMip) importer.mipmapEnabled = true;
                    if (needsStreaming) importer.streamingMipmaps = true;
                    if (needsRead) importer.isReadable = false;
                    if (needsSize) importer.maxTextureSize = maxSize;
                    importer.SaveAndReimport();
                    report.AppendLine("已更新：" + path);
                }
                report.AppendLine("修改纹理数：" + backup.textures.Count + "；备份：" + folder);
            }
            finally
            {
                File.WriteAllText(manifest, JsonUtility.ToJson(backup, true));
                EditorUtility.ClearProgressBar(); LastReport = report.ToString();
            }
        }

        private static void SetStreamingQuality(int level, bool active, bool allCameras, float budget)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").First();
            var serialized = new SerializedObject(asset);
            var quality = serialized.FindProperty("m_QualitySettings").GetArrayElementAtIndex(level);
            quality.FindPropertyRelative("streamingMipmapsActive").boolValue = active;
            quality.FindPropertyRelative("streamingMipmapsAddAllCameras").boolValue = allCameras;
            quality.FindPropertyRelative("streamingMipmapsMemoryBudget").floatValue = budget;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (QualitySettings.GetQualityLevel() == level)
            {
                QualitySettings.streamingMipmapsActive = active;
                QualitySettings.streamingMipmapsAddAllCameras = allCameras;
                QualitySettings.streamingMipmapsMemoryBudget = budget;
            }
            // ProjectSettings assets are not ordinary AssetDatabase assets; explicitly persist
            // this one settings object so the selected quality profile survives an Editor restart.
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new[] { asset }, "ProjectSettings/QualitySettings.asset", true);
        }
        public static void RestoreTextures()
        {
            Guard();
            string pointer = Path.Combine(Root, "last-texture-backup.txt");
            if (!File.Exists(pointer)) throw new InvalidOperationException("尚无纹理操作备份。");
            var backup = JsonUtility.FromJson<TextureBackup>(File.ReadAllText(File.ReadAllText(pointer)));
            foreach (var entry in backup.textures)
            {
                string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (string.IsNullOrEmpty(path)) throw new IOException("无法定位原资源：" + entry.path);
                File.Copy(entry.backup, path + ".meta", true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            if (backup.qualityChanged) SetStreamingQuality(backup.qualityLevel, backup.active, backup.allCameras, backup.budget);
            LastReport = "已恢复最近一次纹理操作的导入设置与对应质量级别 Streaming 设置。";
        }

        public static void ApplyCamera(Camera camera, int layer, float distance, bool spherical, bool occlusion)
        {
            Guard();
            if (!camera || camera.gameObject.scene != SceneManager.GetActiveScene()) throw new InvalidOperationException("请选择当前场景的相机。");
            if (distance < 0 || float.IsNaN(distance) || float.IsInfinity(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
            Undo.RecordObject(camera, "Environment Master 相机剔除");
            var distances = camera.layerCullDistances; distances[layer] = distance;
            camera.layerCullDistances = distances; camera.layerCullSpherical = spherical; camera.useOcclusionCulling = occlusion;
            if (PrefabUtility.IsPartOfPrefabInstance(camera)) PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
            LastReport = $"相机 {camera.name}：层 {layer} 距离 {distance}（0 使用相机远裁剪面）；其他层距离保持不变。场景尚未保存，可 Undo。";
        }
        public static void MarkOcclusion(bool occluder)
        {
            Guard();
            var objects = SelectedObjects().Where(g => g.GetComponent<MeshRenderer>()).ToArray();
            foreach (var go in objects)
            {
                Undo.RecordObject(go, "Environment Master 遮挡标记");
                var flags = GameObjectUtility.GetStaticEditorFlags(go) | StaticEditorFlags.OccludeeStatic;
                if (occluder) flags |= StaticEditorFlags.OccluderStatic;
                GameObjectUtility.SetStaticEditorFlags(go, flags);
                if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            LastReport = "已标记 " + objects.Length + " 个选中 MeshRenderer 为静态被遮挡物" + (occluder ? "及遮挡体" : "") + "。请保存后烘焙遮挡数据。";
        }
        public static void CreateOcclusionArea()
        {
            Guard();
            var renderers = SceneRenderers().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("场景中没有可用 Renderer。");
            var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var go = new GameObject("EnvironmentMaster_OcclusionArea"); Undo.RegisterCreatedObjectUndo(go, "创建遮挡区域");
            go.transform.position = bounds.center;
            var area = go.AddComponent<OcclusionArea>(); area.size = bounds.size + Vector3.one * 10;
            Selection.activeGameObject = go;
            LastReport = "已创建包围场景 Renderer 的 OcclusionArea，请按玩家可到达区域调整范围。";
        }
        public static void BakeOcclusion()
        {
            Guard();
            if (SceneManager.sceneCount != 1) throw new InvalidOperationException("遮挡烘焙请只打开目标场景，避免修改其他场景数据。");
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) return;
            if (!StaticOcclusionCulling.Compute()) throw new InvalidOperationException("未能启动遮挡烘焙，请检查 Console。");
            LastReport = "遮挡烘焙已启动；监测栏会显示运行状态，完成后请保存场景。";
        }
        public static string RenderingAudit()
        {
            var renderers = SceneRenderers();
            var groups = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LODGroup>(true)).ToArray();
            var meshes = renderers.Select(r => r.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh).Select(f => f.sharedMesh).ToArray();
            long triangleCount = meshes.Sum(m => Enumerable.Range(0, m.subMeshCount).Where(i => m.GetTopology(i) == MeshTopology.Triangles).Sum(i => (long)m.GetIndexCount(i) / 3));
            return $"Renderer：{renderers.Length}；LODGroup：{groups.Length}；MeshFilter 三角形总量：{triangleCount}（含实例，非实际绘制三角形）。\n" +
                $"多材质 Renderer：{renderers.Count(r => r.sharedMaterials.Length > 1)}；阴影投射 Renderer：{renderers.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)}。\n" +
                "LOD 不会自动生成低模；Instancing 是否生效仍取决于 Shader、材质、网格及渲染管线。";
        }
        public static void CreateLODFromChildren(GameObject root)
        {
            Guard();
            if (!root || root.scene != SceneManager.GetActiveScene() || root.GetComponent<LODGroup>()) throw new InvalidOperationException("请选择当前场景中尚无 LODGroup 的根物体。");
            var levels = new List<LOD>();
            float[] thresholds = { 0.6f, 0.25f, 0.03f };
            for (int i = 0; i < 3; i++)
            {
                var child = root.transform.Find("LOD" + i);
                if (!child) break;
                if (child.GetComponentsInChildren<LODGroup>(true).Length > 0) throw new InvalidOperationException("子层级中已有 LODGroup，请避免嵌套。");
                var renderers = child.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException(child.name + " 下没有 Renderer。");
                levels.Add(new LOD(thresholds[i], renderers));
            }
            if (levels.Count < 2) throw new InvalidOperationException("请先提供至少 LOD0、LOD1 两个直接子物体和对应模型。");
            var group = Undo.AddComponent<LODGroup>(root); group.SetLODs(levels.ToArray()); group.RecalculateBounds();
            LastReport = "已创建 " + levels.Count + " 级 LODGroup，最后一级以下剔除。请检查屏幕占比阈值和模型对应关系。";
        }
        public static void EnableSelectedInstancing()
        {
            Guard();
            var materials = SelectedObjects().SelectMany(g => g.GetComponents<Renderer>()).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
            foreach (var material in materials)
            {
                if (!AssetDatabase.GetAssetPath(material).StartsWith("Assets/", StringComparison.Ordinal)) continue;
                Undo.RecordObject(material, "开启材质 Instancing"); material.enableInstancing = true; EditorUtility.SetDirty(material);
            }
            LastReport = "已为选中对象的可写材质开启 Instancing 标记。共享材质会影响其他使用者；可 Undo，实际合批请查看 Frame Debugger。";
        }
    }
}
