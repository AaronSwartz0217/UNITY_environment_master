using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace EnvironmentMaster.Editor
{
    public static class EnvironmentPanels
    {
        private static ScrollView Page(string title, string help)
        {
            var page = new ScrollView(); page.style.flexGrow = 1;
            page.style.paddingLeft = page.style.paddingRight = 12;
            page.style.paddingTop = page.style.paddingBottom = 10;
            var label = new Label(title); label.style.fontSize = 17; label.style.unityFontStyleAndWeight = FontStyle.Bold;
            page.Add(label); page.Add(new HelpBox(help, HelpBoxMessageType.Info));
            return page;
        }
        private static Label Text(VisualElement parent, string text = "")
        {
            var label = new Label(text); label.style.whiteSpace = WhiteSpace.Normal; label.style.marginBottom = 8; parent.Add(label); return label;
        }
        private static void ActionButton(VisualElement parent, string title, Action action, Label status)
        {
            parent.Add(new Button(() =>
            {
                try { action(); status.text = EnvironmentOptimization.LastReport; }
                catch (Exception ex) { status.text = "操作未完成：" + ex.Message; Debug.LogWarning("[Environment Master] " + ex.Message); }
            }) { text = title });
        }
        public static VisualElement Textures()
        {
            var page = Page("Mipmap Streaming / 纹理内存", "Mipmap Streaming 根据相机需求加载纹理层级，不是卸载场景物体。预算包含非流送纹理，并不是显存硬上限。修改导入器会影响共用纹理的其他场景。优化前保存导入器备份。");
            var audit = Text(page, EnvironmentOptimization.TextureAudit());
            page.Add(new Button(() => audit.text = EnvironmentOptimization.TextureAudit()) { text = "重新扫描当前场景纹理" });
            var quality = new Toggle("启用当前质量级别的 Texture Streaming") { value = true }; page.Add(quality);
            var budget = new FloatField("纹理预算 MB（32–8192）") { value = 512 }; page.Add(budget);
            var mipmaps = new Toggle("为缺失 Mipmap 的普通 / 法线纹理生成 Mipmap") { value = true }; page.Add(mipmaps);
            var streaming = new Toggle("为可用纹理开启 Streaming Mipmaps") { value = true }; page.Add(streaming);
            var readable = new Toggle("关闭纹理 Read/Write（先确认没有 CPU 读写代码）"); page.Add(readable);
            var clamp = new Toggle("限制默认 Max Size（平台覆盖设置保持原样）"); page.Add(clamp);
            var max = new IntegerField("最大尺寸（2 的幂）") { value = 2048 }; page.Add(max);
            var status = Text(page);
            ActionButton(page, "备份并应用以上纹理设置", () =>
            {
                if (clamp.value && (max.value < 32 || max.value > 16384 || (max.value & (max.value - 1)) != 0)) throw new ArgumentException("Max Size 应为 32–16384 内 2 的幂。");
                EnvironmentOptimization.ConfigureTextures(quality.value, budget.value, streaming.value, mipmaps.value, readable.value, clamp.value ? max.value : 0);
                audit.text = EnvironmentOptimization.TextureAudit();
            }, status);
            ActionButton(page, "恢复最近一次纹理操作（覆盖该批导入器的后续修改）", () => { EnvironmentOptimization.RestoreTextures(); audit.text = EnvironmentOptimization.TextureAudit(); }, status);
            page.Add(new HelpBox("仅改变当前质量档位；切换到其他档位或构建平台后需要重新确认。天空盒/Cubemap、UI、烘焙 Lightmap 不批量修改。请在 Play 和目标设备验证清晰度与流送情况。", HelpBoxMessageType.Warning));
            return page;
        }
        public static VisualElement Visibility()
        {
            var page = Page("视锥 / 距离 / 遮挡剔除", "Unity 自动剔除相机视锥之外的渲染。这里不禁用 GameObject / Renderer，避免破坏脚本、其他相机或阴影。视野外物体仍可能用于投射阴影；剔除渲染也不会停止其脚本和物理。");
            var camera = new ObjectField("目标相机") { objectType = typeof(Camera), allowSceneObjects = true, value = Camera.main }; page.Add(camera);
            var layer = new LayerField("要设置距离的 Layer") { value = 0 }; page.Add(layer);
            var distance = new FloatField("该层绘制距离（0 = 远裁剪面）") { value = 100 }; page.Add(distance);
            var spherical = new Toggle("球形距离剔除（作用于此相机所有距离层）") { value = true }; page.Add(spherical);
            var occlusion = new Toggle("相机启用 Occlusion Culling") { value = true }; page.Add(occlusion);
            Text(page, "距离应用到指定层的所有物体。Default 层可能包含主角、建筑等重要物体，请先规划独立层；插件不会修改物体层级归属。距离变更支持 Undo，需保存场景。");
            var status = Text(page);
            ActionButton(page, "应用相机按层剔除设置", () => EnvironmentOptimization.ApplyCamera(camera.value as Camera, layer.value, distance.value, spherical.value, occlusion.value), status);
            var occluder = new Toggle("同时把选中物体标记为遮挡体（仅固定、不透明的大物体）"); page.Add(occluder);
            ActionButton(page, "选中物体及子物体：设置静态遮挡标记", () => EnvironmentOptimization.MarkOcclusion(occluder.value), status);
            ActionButton(page, "创建场景范围 OcclusionArea", EnvironmentOptimization.CreateOcclusionArea, status);
            ActionButton(page, "保存当前场景并烘焙遮挡数据", EnvironmentOptimization.BakeOcclusion, status);
            page.Add(new Button(() => EditorApplication.ExecuteMenuItem("Window/Rendering/Occlusion Culling")) { text = "打开 Unity Occlusion Culling 窗口" });
            page.Add(new HelpBox("遮挡剔除适合建筑、墙体等遮挡充分的场景。开阔地形收益可能有限。不要把移动物体、透明植被当静态遮挡体。只有相机开关而没有正确烘焙的数据，不会凭空增加遮挡结果。", HelpBoxMessageType.Warning));
            return page;
        }
        public static VisualElement Rendering()
        {
            var page = Page("LOD / 渲染检查", "LOD、材质数量、阴影与 Instancing 是常用优化方向。先测量，再只修改选中资源；本插件不生成低模，不修改全工程 Shader 或静态合批设置。");
            var audit = Text(page, EnvironmentOptimization.RenderingAudit());
            page.Add(new Button(() => audit.text = EnvironmentOptimization.RenderingAudit()) { text = "重新检查 LOD / Renderer" });
            Text(page, "创建 LOD：选中一个父物体，下面预先准备 LOD0、LOD1（可选 LOD2）直接子物体，分别放入已有的高、中、低模。默认屏幕占比阈值为 0.6 / 0.25 / 0.03，最后一级以下剔除。创建后请在 Inspector 调整。");
            var status = Text(page);
            ActionButton(page, "用选中根物体的 LOD0 / LOD1 / LOD2 创建 LODGroup", () => { EnvironmentOptimization.CreateLODFromChildren(Selection.activeGameObject); audit.text = EnvironmentOptimization.RenderingAudit(); }, status);
            ActionButton(page, "为选中物体的共享材质开启 GPU Instancing 标记", EnvironmentOptimization.EnableSelectedInstancing, status);
            page.Add(new Button(() => EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger")) { text = "打开 Frame Debugger 验证实际绘制" });
            Text(page, "Instancing 标记不保证减少 Draw Calls；URP 的 SRP Batcher、Shader 实例化支持、材质与网格一致性都会影响结果。材质修改支持 Undo，保存由你决定。");
            return page;
        }
        public static VisualElement Monitoring()
        {
            var page = Page("持续监测 / 基准 / 日志", "默认每秒采样，关掉面板后继续运行。编辑器日志保存在 Library/EnvironmentMaster/Monitoring，清理 Library 前请导出。指标是当前进程数据；编辑器渲染计数包含工具开销，不代表目标设备性能。");
            var enabled = new Toggle("持续采样（面板关闭后继续）") { value = EnvironmentMonitoring.Enabled };
            enabled.RegisterValueChangedCallback(e => EnvironmentMonitoring.Enabled = e.newValue); page.Add(enabled);
            var record = new Toggle("持续追加本地 JSONL 日志") { value = EnvironmentMonitoring.Record };
            record.RegisterValueChangedCallback(e => EnvironmentMonitoring.Record = e.newValue); page.Add(record);
            var interval = new FloatField("采样间隔秒（0.25–30）") { value = EnvironmentMonitoring.Interval };
            interval.RegisterValueChangedCallback(e => EnvironmentMonitoring.Interval = e.newValue); page.Add(interval);
            var camera = new ObjectField("监测相机（留空自动选择）") { objectType = typeof(Camera), allowSceneObjects = true, value = EnvironmentMonitoring.TargetCamera };
            camera.RegisterValueChangedCallback(e => EnvironmentMonitoring.TargetCamera = e.newValue as Camera); page.Add(camera);
            var summary = Text(page);
            var error = Text(page);
            page.schedule.Execute(() =>
            {
                summary.text = EnvironmentMonitoring.Summary();
                error.text = (EnvironmentMonitoring.Error ?? "") + "\n日志：" + EnvironmentMonitoring.LogPath;
                record.SetValueWithoutNotify(EnvironmentMonitoring.Record);
            }).Every(500);
            page.Add(new Button(EnvironmentMonitoring.SaveBaseline) { text = "把当前采样设为对比基准" });
            page.Add(new Button(() =>
            {
                string path = EditorUtility.SaveFilePanel("导出本次日志 CSV", "", "EnvironmentMaster-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv", "csv");
                if (!string.IsNullOrEmpty(path)) { try { EnvironmentMonitoring.ExportCSV(path); } catch (Exception ex) { error.text = ex.Message; } }
            }) { text = "导出本次会话 CSV" });
            page.Add(new Button(() =>
            {
                if (System.IO.File.Exists(EnvironmentMonitoring.LogPath)) EditorUtility.RevealInFinder(EnvironmentMonitoring.LogPath);
            }) { text = "定位日志文件" });
            var status = Text(page);
            ActionButton(page, "为当前场景添加 Player 运行监测组件", () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    if (root.GetComponentInChildren<EnvironmentRuntimeMonitor>(true)) { Selection.activeGameObject = root; return; }
                var go = new GameObject("EnvironmentMaster_RuntimeMonitor");
                Undo.RegisterCreatedObjectUndo(go, "添加运行监测"); go.AddComponent<EnvironmentRuntimeMonitor>(); Selection.activeGameObject = go;
                EditorSceneManager.MarkSceneDirty(go.scene);
            }, status);
            Text(page, "可选运行组件只在 Play / Player 工作，日志写入 Application.persistentDataPath/EnvironmentMaster；默认不会自动加入场景。发行构建中部分 Profiler 计数不可用时显示 -1（N/A），不是 0。");
            return page;
        }
    }
}
