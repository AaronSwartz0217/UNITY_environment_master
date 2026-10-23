# 验证记录

日期：2026-10-23。环境：Windows、Unity 2022.3.62f3c1、独立临时项目。

## 环境大师 2.0

使用 `Tests~/Editor/EnvironmentMasterValidation.cs` 执行自动化检查，最终结果 **PASS**：

1. 相机指定层距离生效，其他层距离不变；GameObject 与 Renderer 没有被禁用。
2. 位于相机前后的两个测试 Renderer 得到正确的视锥候选计数；编辑模式 FPS 明确不可用。
3. 扫描场景依赖纹理，普通纹理被纳入、Sprite 被排除。
4. 实际重新导入纹理，Mipmap、Streaming、Read/Write 和 Max Size 按选择改变。
5. 当前质量级别的 Streaming 和预算生效，配置在退出编辑器前已写入 ProjectSettings 文件。
6. 恢复备份后，纹理导入设置和原 Streaming 质量配置恢复。
7. 选中物体静态遮挡标记生效，未选中物体标记不变。
8. 创建 OcclusionArea，实际遮挡烘焙生成 OcclusionCullingData 资产。该检查证明流程可运行，不代表具体场景的遮挡收益。
9. 使用已有模型创建三级 LODGroup，选中共享材质 Instancing 标记生效，渲染检查生成报告。
10. 五分栏 UI Toolkit 窗口创建成功。
11. 销毁面板后仍持续采样、写 JSONL；基准保留；导出多行 CSV。
12. 进入 Play 模式，确认帧数持续推进、FPS/帧间隔产生有效样本、运行监测组件连续写入多行日志。

运行程序集与编辑器程序集均通过本机 Unity 2022.3 程序集编译检查。

## 保留的光照烘焙功能

此前 v1.1 已在独立项目验证：导入模型生成 UV2、原生 Mesh 使用副本且保持三角形数量、未勾选项不变、选中环境静态标记、Realtime 转 Mixed、共享 Lighting Settings 不覆盖、重复准备不重复生成网格；准备后 CPU 烘焙成功生成 Lighting Data Asset。2.0 保留该流程，并集成到光照分栏。

## 验证边界

未在用户生产场景批量应用优化，没有上传场景素材或监测日志。未测量目标设备性能收益、发行 Player、GPU 烘焙、大型场景、不同平台纹理覆盖、复杂多相机和多场景组合。分栏创建经过编辑器运行验证，但尚未完成全部窗口尺寸的人工视觉验收。

测试脚本会创建资产、保存测试场景、切换 Play；仅在临时独立项目运行。示例命令：

```text
Unity.exe -batchmode -projectPath <临时工程绝对路径> -executeMethod EnvironmentMasterValidation.Run -logFile <日志绝对路径>
```

把插件装入临时工程，再将 `Tests~/Editor` 脚本复制到该工程的 `Assets/Tests/Editor`；为临时工程启用 Physics、ImageConversion、JsonSerialize、Terrain、Umbra 内置模块。结果写入该临时项目父目录。
