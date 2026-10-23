# UNITY Environment Master · 环境大师 2.0

Unity **2022.3 LTS** 场景光照烘焙与优化工具。UI Toolkit 编辑器分栏面板，附可选 Player 监测组件。无需第三方运行库；在 Unity 2022.3.62f3c1 验证。

## 安装与入口

推荐：Package Manager → `+` → Add package from git URL：

```text
https://github.com/AaronSwartz0217/UNITY_environment_master.git
```

也可下载仓库，把 `Editor`、`Runtime` 和对应 `.meta` 复制到工程 `Assets/EnvironmentMaster` 文件夹。两种安装方式选一种；旧版 `Assets/OneClickSceneBake` 和本包使用相同程序集，请勿重复安装。

入口：`工具 > 环境大师 > 环境面板` 或 `环境大师 > 打开面板`。原 `光照烘焙` 入口保留。

## 五个分栏

| 分栏 | 提供的操作 |
| --- | --- |
| 光照烘焙 | 按勾选项配置 Lighting Settings、生成缺失 UV2、Realtime → Mixed/Baked、选中环境静态标记、仅准备、仅烘焙、准备并烘焙、进度与取消 |
| 纹理流送 | 扫描当前场景依赖；为普通/法线纹理启用 Mipmap 与 Streaming；当前质量档位预算；可选关闭 Read/Write、限制默认 Max Size；导入器备份和恢复 |
| 视野 / 遮挡剔除 | 相机按 Layer 距离剔除；球形距离；Occlusion 开关；选中静态物体遮挡标记；OcclusionArea；保存并烘焙遮挡数据 |
| LOD / 渲染 | Renderer、材质、阴影、三角形统计；已有 LOD0/1/2 模型创建 LODGroup；选中共享材质 Instancing 标记；Frame Debugger 入口 |
| 持续监测 | 定时采样、基准比较、600 条内存历史、持续 JSONL 日志、CSV 导出、可选 Player 监测组件 |

## 建议工作流程

1. 保留一个场景副本；在 Play 和目标设备记录优化前数据，固定相机、分辨率与质量档位。
2. 在纹理页扫描，选择预算，再应用并检查画面清晰度。预算仅作用于当前质量档位。
3. 对植物、小道具等规划单独 Layer，设置相机距离剔除。距离 0 使用远裁剪面；其他层不修改。
4. 仅为固定、不透明的大型墙体或建筑设置遮挡体；开启相机遮挡剔除后生成遮挡数据。
5. 有低模时创建 LODGroup，调整屏幕占比阈值；使用 Frame Debugger 确认实际绘制行为。
6. 准备并烘焙光照；在持续监测页设置基准、对比及导出数据。

## 准确理解剔除和监测

- Unity 本来就会按相机视锥剔除渲染。工具不逐帧切换 `GameObject.SetActive` 或 `Renderer.enabled`，不会把脚本、碰撞、其他相机和阴影粗暴禁用。
- 视野外物体仍可能贡献视野内阴影；渲染剔除不等于停用脚本、物理、卸载资源。
- 监测中的“视锥与层掩码内候选数”是包围盒估计，不是实际 Draw Calls；不计算遮挡、LOD、距离剔除或多相机最终结果。
- Draw Calls、Batches、SetPass、Triangles 来自可用的 ProfilerRecorder 进程计数，可能包含编辑器和其他相机开销。不可用为 `-1 / N/A`。
- FPS 只在 Play/Player 且至少两次有效采样后显示；帧耗时是采样平均帧间隔，**不是 GPU 时间**。
- 纹理和 Unity 内存指标是进程范围，不能当作当前场景独占内存或完整显存占用。
- Mipmap Streaming 预算包含非流送纹理，不是硬上限；非流送纹理可能使实际内存超过预算。真实设备是否支持及收益需实测。

## 监测数据

编辑器持续监测默认启用，采样间隔默认 1 秒，可设 0.25–30 秒。关掉面板后继续采样；可以独立关闭采样或日志写入。

- 编辑器 JSONL：`Library/EnvironmentMaster/Monitoring/editor-<UTC时间>.jsonl`。
- 每次脚本重载生成新日志段，旧段保留；CSV 导出当前日志段。清理 Library 前请导出。
- 内存缓存最近 600 条，基准在当前编辑器会话中保留；不同场景、相机或编辑/运行模式不直接比较。
- 点击“添加 Player 运行监测组件”后才会把组件加入当前场景；默认不自动加入。保存场景后可进入 Play 或构建。
- Player 日志：`Application.persistentDataPath/EnvironmentMaster/runtime-*.jsonl`，可通过组件关闭持久记录。
- 日志只写本地，不发送或上传。长期监测请自行管理磁盘容量；组件没有自动清理旧日志的行为。

## 修改范围与恢复

- 优化操作不会因打开面板自动执行。它们由明确按钮触发；监测只读取数据。
- UV 模型导入与纹理导入会影响共用该资产的其他场景。已有 UV2 不重写，也不保证无重叠。
- 原生网格补 UV 使用当前场景专用副本；不覆盖源 Mesh。
- 光照准备备份：`Library/OneClickSceneBakeBackups/<时间戳>`；生成资产位于场景旁 `<场景名>_BakeGenerated`。
- 纹理备份：`Library/EnvironmentMaster/Backups/<时间戳>`。恢复最近操作会覆盖该批导入器此后的修改，并恢复原质量档位的三个 Streaming 配置。
- Sprite、GUI、Lightmap、Cubemap、模型内嵌纹理及只读包资源不批量处理。纹理平台覆盖设置保持原样。
- 相机、静态标记、LOD 与材质修改支持 Undo。场景操作后请保存；纹理重导入、光照生成等磁盘操作不等同于场景 Undo。
- 遮挡烘焙要求只打开目标场景；多场景光照烘焙只为活动场景生成数据，但其他加载场景仍会影响光照。
- LOD 功能要求现成模型，不自动减面。Instancing 标记不保证发生实例化，SRP Batcher 等会影响执行路径。

## 验证

见 [VALIDATION.md](VALIDATION.md)。`Tests~/Editor/EnvironmentMasterValidation.cs` 是独立临时工程的验证入口；不要在生产工程运行该测试，它会创建测试资产并切换 Play。

参考：[Mipmap Streaming 预算](https://docs.unity3d.com/cn/2022.3/ScriptReference/QualitySettings-streamingMipmapsMemoryBudget.html)、[纹理统计 API](https://docs.unity3d.com/ja/2022.3/ScriptReference/Texture.html)、[相机按层剔除](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Camera-layerCullDistances.html)。
