# 当前场景一键光照烘焙

适用于 Unity 2022.3 LTS，仅在编辑器内运行，不进入游戏构建。无需第三方依赖。

## 使用

1. 等待 Unity 导入和编译完成。
2. 打开 `工具 > 光照烘焙 > 烘焙面板`，或顶部独立菜单 `光照烘焙 > 烘焙面板`。
3. 勾选需要自动处理的项目，点击 `预览将修改的项目` 查看影响范围。
4. 点击 `按勾选项准备并一键烘焙`。也可选择 `仅执行勾选的准备项目` 或 `仅烘焙当前场景（不自动准备）`。菜单中的一键烘焙沿用面板当前勾选项。
5. 在面板查看进度，可取消本插件启动的烘焙。完成后检查 Lighting 窗口的 Baked Lightmaps，再保存场景。

## 确定的默认行为

- 保留当前场景的 Lightmapper、分辨率、采样等全部参数，不强制使用 GPU。
- 默认勾选自动配置 Lighting Settings、补齐 UV2、将启用的 Realtime 光源转换为 Mixed。可取消任意勾选，或将目标光源模式切换为 Baked；已有 Baked / Mixed 光源保持不变。
- 静态标记默认不勾选。勾选后只为当前场景选中物体及其子物体中的 MeshRenderer / Terrain 添加 Contribute GI，不处理 SkinnedMeshRenderer，不清除其他静态标记。
- 补 UV 只处理启用、可见且参与 Contribute GI 的 MeshRenderer。FBX / OBJ 等通过 ModelImporter 开启 Generate Lightmap UVs 并重新导入；这会影响工程中共用该模型的其他场景。
- 原生独立网格 / 内置网格使用新副本生成 UV2，副本保存在场景旁的 `<场景名>_BakeGenerated` 文件夹，并只替换当前场景的 MeshFilter 引用。源网格不被覆盖。只读包内模型不会强行修改。
- 如果需要改变共享 Lighting Settings，会复制一份场景专用设置再关闭 Auto Generate、开启 Baked GI；原共享设置保持不变。
- 已有 UV2 不代表没有重叠，仍需检查导入器和烘焙警告。未标记 GI 的动态角色无需为了消除提示而标记静态。
- 开始前保存当前场景，未保存的新场景会弹出保存路径选择。取消保存则不启动。
- 完成时不自动保存场景，避免一并保存烘焙期间其他编辑。
- 没有 Baked / Mixed 光源仍允许烘焙，环境和自发光也可能贡献光照。

## 准备报告与备份

自动准备前会先保存当前场景，再将场景及将重新导入的模型 `.meta` 文件备份到工程 `Library/OneClickSceneBakeBackups/<时间戳>`。面板可展开 `准备报告 / 备份位置` 查看操作记录；这份备份位于 Library，清理 Library 前请复制到其他位置。

场景对象修改支持 Undo；模型导入设置和生成资产不随场景 Undo 撤销。需要完整恢复时，关闭或卸载目标场景，将备份 `Scene.unity` 复制回报告记录的原场景路径（保留原场景 `.meta`），再将备份下 `Assets/.../*.meta` 恢复到对应模型位置，回到 Unity 重新导入。

取消或准备失败时，可能已有部分项目完成，插件会停止，不继续启动烘焙。请检查报告再决定保留或恢复。已有 UV2 不重写，也不保证已有 UV 的质量；自动生成后仍需检查 Unity 的 UV 重叠、贴图密度等警告。

## 多场景与数据

使用 Unity 2022.3 的 `UnityEditor.Experimental.Lightmapping.BakeAsync(Scene)`，仅为点击开始时的活动场景生成光照数据，不关闭或切换其他场景。其他已加载场景依照 Unity 的规则仍然贡献光照；若希望完全独立的结果，请只加载目标场景。

光照数据和贴图的输出位置由 Unity 管理，通常位于场景文件旁的同名文件夹。`定位当前场景烘焙数据` 可在 Project 中定位当前活动场景的 Lighting Data Asset。

取消和失败不会恢复旧烘焙数据。面板的完成状态依据 Unity 回调，最终质量及错误应结合 Console、Scene/Game 和 Baked Lightmaps 检查。烘焙期间的任何 Console 错误都会使面板提示检查结果。

## 安装到其他工程

复制整个 `OneClickSceneBake` 文件夹到目标工程的 `Assets` 下；保留 `Editor` 目录结构。卸载时删除这个文件夹。

参考：[Unity 2022.3 指定场景异步烘焙 API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Experimental.Lightmapping.BakeAsync.html)。该接口在 Unity 中标为 Experimental，因此跨大版本升级后需重新验证。
