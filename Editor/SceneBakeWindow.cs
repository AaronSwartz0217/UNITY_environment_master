using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace OneClickSceneBake
{
    public sealed class SceneBakeWindow : EditorWindow
    {
        private Label sceneLabel;
        private Label statusLabel;
        private ProgressBar progress;
        private Button bakeButton;
        private Button prepareButton;
        private Button prepareOnlyButton;
        private VisualElement optionControls;
        private Label preparationPreview;
        private Label preparationReport;
        [SerializeField] private BakePreparationOptions options = new BakePreparationOptions();
        private Button cancelButton;
        private ScrollView issues;
        [SerializeField] private int selectedTab;

        [MenuItem("工具/环境大师/环境面板", false, 90)]
        [MenuItem("环境大师/打开面板", false, 0)]
        [MenuItem("工具/光照烘焙/烘焙面板", false, 100)]
        [MenuItem("光照烘焙/烘焙面板", false, 0)]
        public static void Open()
        {
            var window = GetWindow<SceneBakeWindow>("环境大师");
            window.minSize = new Vector2(680, 650);
        }

        [MenuItem("工具/光照烘焙/一键烘焙当前场景", false, 101)]
        [MenuItem("光照烘焙/一键烘焙当前场景", false, 1)]
        public static void BakeCurrent()
        {
            Open();
            GetWindow<SceneBakeWindow>().RunPreparation(true);
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var heading = new Label("环境大师 · Environment Master 2.0");
            heading.style.fontSize = 19;
            heading.style.marginLeft = 12;
            heading.style.marginTop = 10;
            rootVisualElement.Add(heading);
            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.flexWrap = Wrap.Wrap;
            rootVisualElement.Add(tabs);
            var host = new VisualElement();
            host.style.flexGrow = 1;
            rootVisualElement.Add(host);
            VisualElement[] pages = {
                BuildBakePage(),
                EnvironmentMaster.Editor.EnvironmentPanels.Textures(),
                EnvironmentMaster.Editor.EnvironmentPanels.Visibility(),
                EnvironmentMaster.Editor.EnvironmentPanels.Rendering(),
                EnvironmentMaster.Editor.EnvironmentPanels.Monitoring()
            };
            string[] names = { "光照烘焙", "纹理流送", "视野 / 遮挡剔除", "LOD / 渲染", "持续监测" };
            var buttons = new List<Button>();
            Action<int> select = index =>
            {
                selectedTab = Mathf.Clamp(index, 0, pages.Length - 1);
                for (int i = 0; i < pages.Length; i++)
                {
                    pages[i].style.display = i == selectedTab ? DisplayStyle.Flex : DisplayStyle.None;
                    buttons[i].style.unityFontStyleAndWeight = i == selectedTab ? FontStyle.Bold : FontStyle.Normal;
                }
            };
            for (int i = 0; i < pages.Length; i++)
            {
                int index = i;
                var button = new Button(() => select(index)) { text = names[i] };
                button.style.flexGrow = 1;
                tabs.Add(button); buttons.Add(button); host.Add(pages[i]);
            }
            select(selectedTab);
        }

        private VisualElement BuildBakePage()
        {
            var root = new ScrollView();
            root.style.flexGrow = 1;
            root.style.paddingLeft = root.style.paddingRight = 14;
            root.style.paddingTop = root.style.paddingBottom = 12;
            var title = new Label("当前场景 · 光照烘焙");
            title.style.fontSize = 19;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(title);
            sceneLabel = new Label();
            sceneLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(sceneLabel);
            root.Add(new HelpBox("勾选需要自动处理的项目，再点击准备并烘焙。保留现有分辨率、采样和后端；已有 UV2 不重写。", HelpBoxMessageType.Info));
            optionControls = new VisualElement();
            root.Add(optionControls);
            AddToggle("自动配置 Lighting Settings（关闭 Auto Generate / 开启 Baked GI）", options.configureLighting, value => options.configureLighting = value);
            AddToggle("自动补齐参与 GI 网格的光照 UV（UV2）", options.generateUV, value => options.generateUV = value);
            AddToggle("将当前场景启用的 Realtime 光源转换为下方模式", options.convertRealtimeLights, value => options.convertRealtimeLights = value);
            var mode = new EnumField("目标光源模式", options.lightMode);
            mode.RegisterValueChangedCallback(e => { options.lightMode = (PreparedLightMode)e.newValue; UpdatePreview(); });
            optionControls.Add(mode);
            AddToggle("将选中环境物体及子物体标记 Contribute GI", options.markSelectedObjects, value => options.markSelectedObjects = value);
            root.Add(new HelpBox("勾选补 UV 会重新导入模型，影响共用模型的其他场景；独立网格使用副本。准备前自动备份。静态标记只作用于选中的 MeshRenderer / Terrain，请勿选中动态角色。", HelpBoxMessageType.Warning));
            preparationPreview = new Label();
            preparationPreview.style.whiteSpace = WhiteSpace.Normal;
            root.Add(preparationPreview);
            root.Add(new Button(UpdatePreview) { text = "预览将修改的项目" });
            prepareButton = new Button(() => RunPreparation(true)) { text = "按勾选项准备并一键烘焙" };
            prepareButton.style.height = 38;
            root.Add(prepareButton);
            prepareOnlyButton = new Button(() => RunPreparation(false)) { text = "仅执行勾选的准备项目" };
            root.Add(prepareOnlyButton);
            bakeButton = new Button(SceneBakeService.Start) { text = "仅烘焙当前场景（不自动准备）" };
            bakeButton.style.height = 38;
            root.Add(bakeButton);
            progress = new ProgressBar { title = "就绪", highValue = 100 };
            root.Add(progress);
            statusLabel = new Label();
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(statusLabel);
            cancelButton = new Button(SceneBakeService.Cancel) { text = "取消本次烘焙" };
            root.Add(cancelButton);
            var reportFoldout = new Foldout { text = "准备报告 / 备份位置", value = false };
            preparationReport = new Label();
            preparationReport.style.whiteSpace = WhiteSpace.Normal;
            reportFoldout.Add(preparationReport);
            root.Add(reportFoldout);
            root.Add(new Button(RefreshChecks) { text = "重新检查当前场景" });
            root.Add(new Button(() => EditorApplication.ExecuteMenuItem("Window/Rendering/Lighting")) { text = "打开 Lighting 设置 / Baked Lightmaps" });
            root.Add(new Button(() =>
            {
                var asset = Lightmapping.lightingDataAsset;
                if (asset) { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
                else ShowNotification(new GUIContent("当前场景尚无 Lighting Data Asset"));
            }) { text = "定位当前场景烘焙数据" });
            issues = new ScrollView();
            issues.style.flexGrow = 1;
            issues.style.minHeight = 160;
            root.Add(issues);
            RefreshChecks();
            UpdatePreview();
            root.schedule.Execute(UpdateStatus).Every(250);
            return root;
        }

        private void AddToggle(string text, bool value, Action<bool> changed)
        {
            var toggle = new Toggle(text) { value = value };
            toggle.RegisterValueChangedCallback(e => { changed(e.newValue); UpdatePreview(); });
            optionControls.Add(toggle);
        }

        private void UpdatePreview()
        {
            if (preparationPreview != null) preparationPreview.text = BakePreparation.Preview(options);
        }

        private void RunPreparation(bool bake)
        {
            bool prepared = BakePreparation.Run(options);
            SceneBakeService.ReportPreparation(prepared ? "准备完成。详情见准备报告。" : "准备未完成，未启动烘焙。请展开准备报告查看原因。");
            RefreshChecks();
            UpdatePreview();
            if (prepared && bake) SceneBakeService.Start();
        }

        private void RefreshChecks()
        {
            if (issues == null) return;
            issues.Clear();
            foreach (var issue in SceneBakeService.Inspect(SceneManager.GetActiveScene()))
            {
                var row = new Label("• " + issue);
                row.style.whiteSpace = WhiteSpace.Normal;
                row.style.marginTop = 5;
                issues.Add(row);
            }
        }

        private string lastScene;
        private void UpdateStatus()
        {
            var scene = SceneManager.GetActiveScene();
            string key = scene.handle + ":" + scene.path;
            if (key != lastScene) { lastScene = key; RefreshChecks(); }
            sceneLabel.text = "当前活动场景：" + (string.IsNullOrEmpty(scene.path) ? scene.name + "（未保存）" : scene.path);
            bool busy = Lightmapping.isRunning;
            bool canOperate = !busy && !BakePreparation.IsPreparing && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
            bakeButton.SetEnabled(canOperate);
            prepareButton.SetEnabled(canOperate);
            prepareOnlyButton.SetEnabled(canOperate);
            optionControls.SetEnabled(canOperate);
            preparationReport.text = BakePreparation.LastReport;
            cancelButton.SetEnabled(busy && SceneBakeService.OwnsBake);
            progress.value = busy ? Mathf.Clamp01(Lightmapping.buildProgress) * 100 : 0;
            progress.title = busy ? "烘焙中 " + progress.value.ToString("F0") + "%" : "就绪";
            statusLabel.text = SceneBakeService.Status;
        }
    }

    [InitializeOnLoad]
    public static class SceneBakeService
    {
        private const string Prefix = "OneClickSceneBake.";
        private static bool cancelRequested;
        private static bool sawError;
        private static double startTime;
        public static bool OwnsBake => SessionState.GetBool(Prefix + "Running", false);
        public static string Status => SessionState.GetString(Prefix + "Status", "尚未开始。检查结果仅作提示，不会更改场景设置。");

        static SceneBakeService()
        {
            Lightmapping.bakeCompleted += Completed;
            EditorApplication.update += Monitor;
            Application.logMessageReceived += ObserveLog;
        }

        public static List<string> Inspect(Scene scene)
        {
            var result = new List<string>();
            if (!scene.IsValid() || !scene.isLoaded) { result.Add("没有可烘焙的活动场景。"); return result; }
            if (string.IsNullOrEmpty(scene.path)) result.Add("场景尚未保存，开始时需要先选择保存位置。");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) result.Add("请退出 Prefab 编辑模式后再烘焙。");
            Lightmapping.TryGetLightingSettings(out var settings);
            if (!settings) result.Add("未指定 Lighting Settings，请在 Lighting 窗口创建并配置。");
            else
            {
                if (settings.autoGenerate) result.Add("Auto Generate 已开启，请手动关闭后重试。");
                if (!settings.bakedGI) result.Add("Baked Global Illumination 未开启，请在 Lighting 设置中开启。");
            }
            int contributors = 0, notStatic = 0, uvMissing = 0, realtime = 0, bakedLights = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if ((GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.ContributeGI) == 0)
                    { notStatic++; continue; }
                    contributors++;
                    var filter = renderer.GetComponent<MeshFilter>();
                    var mesh = filter ? filter.sharedMesh : null;
                    if (mesh && !mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1)) uvMissing++;
                }
                foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                    if (terrain.enabled && terrain.gameObject.activeInHierarchy &&
                        (GameObjectUtility.GetStaticEditorFlags(terrain.gameObject) & StaticEditorFlags.ContributeGI) != 0) contributors++;
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                {
                    if (!light.enabled || !light.gameObject.activeInHierarchy) continue;
                    if (light.lightmapBakeType == LightmapBakeType.Realtime) realtime++; else bakedLights++;
                }
            }
            result.Add($"参与 GI 的 MeshRenderer / Terrain：{contributors}；Baked / Mixed 光源：{bakedLights}。");
            if (notStatic > 0) result.Add($"{notStatic} 个可见 MeshRenderer 未标记 Contribute GI；动态角色通常应保持如此。");
            if (uvMissing > 0) result.Add($"{uvMissing} 个参与 GI 的网格没有 UV2。请检查模型 Generate Lightmap UVs；已有 UV2 也需人工检查重叠。");
            if (realtime > 0) result.Add($"{realtime} 个 Realtime 光源不会烘焙，请按需要改为 Baked / Mixed。");
            if (bakedLights == 0) result.Add("没有启用的 Baked / Mixed 光源，仍可烘焙环境或自发光贡献。");
            if (SceneManager.sceneCount > 1) result.Add("只为当前活动场景生成数据；其他已加载场景仍会影响光照。请先保存其他场景的修改。");
            return result;
        }

        public static void Start()
        {
            if (Lightmapping.isRunning) { SetStatus("已有烘焙正在运行。"); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            { SetStatus("请在编辑模式且脚本编译完成后开始。"); return; }
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            { SetStatus("请先退出 Prefab 编辑模式。"); return; }
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) { SetStatus("没有有效的活动场景。"); return; }
            Lightmapping.TryGetLightingSettings(out var settings);
            if (!settings || settings.autoGenerate || !settings.bakedGI)
            { SetStatus("请先在 Lighting 窗口指定 Lighting Settings，关闭 Auto Generate，并开启 Baked GI。参数保持不变。"); return; }
            foreach (var warning in Inspect(scene)) Debug.Log("[当前场景烘焙] " + warning);
            if (string.IsNullOrEmpty(scene.path) || scene.isDirty)
                if (!EditorSceneManager.SaveScene(scene)) { SetStatus("场景保存已取消，未启动烘焙。"); return; }
            SessionState.SetString(Prefix + "Scene", scene.path);
            SessionState.SetBool(Prefix + "Running", true);
            cancelRequested = sawError = false;
            startTime = EditorApplication.timeSinceStartup;
            SessionState.SetString(Prefix + "Started", DateTime.UtcNow.ToString("O"));
            SetStatus("正在烘焙：" + scene.name + "。可继续编辑；完成后请检查并保存场景。");
            try
            {
                if (!UnityEditor.Experimental.Lightmapping.BakeAsync(scene))
                { SessionState.SetBool(Prefix + "Running", false); SetStatus("Unity 未能启动烘焙，请查看 Console。"); }
            }
            catch (Exception ex)
            {
                SessionState.SetBool(Prefix + "Running", false);
                SetStatus("启动失败：" + ex.Message);
                Debug.LogException(ex);
            }
        }

        public static void Cancel()
        {
            if (!OwnsBake || !Lightmapping.isRunning) return;
            cancelRequested = true;
            Lightmapping.Cancel();
            SessionState.SetBool(Prefix + "Running", false);
            SetStatus("已请求取消。请检查场景光照数据；取消不会恢复旧烘焙结果。");
        }

        private static void ObserveLog(string message, string stack, LogType type)
        {
            if (OwnsBake && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) sawError = true;
        }

        private static void Completed()
        {
            if (!OwnsBake || cancelRequested) return;
            SessionState.SetBool(Prefix + "Running", false);
            string path = SessionState.GetString(Prefix + "Scene", "");
            double elapsed = DateTime.TryParse(SessionState.GetString(Prefix + "Started", ""),
                null, System.Globalization.DateTimeStyles.RoundtripKind, out var started)
                ? (DateTime.UtcNow - started).TotalSeconds : 0;
            SetStatus(sawError
                ? "烘焙已结束，但期间出现错误，请查看 Console 并核对结果。"
                : $"Unity 已报告烘焙完成（{elapsed:F1} 秒）：{path}。请检查 Baked Lightmaps，并保存场景。");
            SceneView.RepaintAll();
        }

        private static void Monitor()
        {
            if (!OwnsBake || Lightmapping.isRunning) return;
            // Completion callback normally clears ownership first. A delayed check also covers
            // cancellation from Unity's Lighting window, backend failure and script reload.
            if (EditorApplication.timeSinceStartup - startTime < 2) return;
            SessionState.SetBool(Prefix + "Running", false);
            SetStatus("烘焙已停止，未收到完成通知。可能已取消或失败，请查看 Console。");
        }

        private static void SetStatus(string value) => SessionState.SetString(Prefix + "Status", value);
        public static void ReportPreparation(string value) => SetStatus(value);
    }
}
