# 04 战斗场景建模试验

本目录存放迭代04的可编辑美术源文件、导出模型、真实渲染预览和验证记录。现有 `Client/UnityProject` 的业务代码和战斗场景尚未接入这些模型。

## 先看这些文件

| 入口 | 内容 |
|---|---|
| `TrainingCourtyard/Previews/overview.png` | 训练庭院整体实物渲染 |
| `TrainingCourtyard/Previews/gameplay.png` | 约1.8m比例人形下的空间预览 |
| `TrainingCourtyard/TrainingCourtyard.blend` | 可在 Blender 中继续编辑的完整场景 |
| `TrainingCourtyard/README.md` | 庭院文件清单、尺寸、分组与 Unity 接入说明 |
| `Meshy/RuneObelisk/obelisk_preview.png` | Meshy 生成石柱的实物渲染 |
| `Meshy/RuneObelisk/README.md` | Meshy 请求结果、30 credits 消耗、贴图与 Unity 导入说明 |

推荐用 **Blender 控制地面、围墙、拱门和木桩的尺度与布局，用 Meshy 试做少量独立装饰道具**。本版庭院已将一个 Meshy 石柱模型实例化两次，展示两条工作流的组合效果。

## 文件如何进入 Unity

1. 先复制 `TrainingCourtyard/Exports/TrainingCourtyard_EnvironmentOnly.fbx` 和旁边 `Textures/` 到 Unity 项目的美术目录，用于环境。
2. `TrainingDummy_01.fbx` 等木桩文件独立导入，后续挂在训练目标的 Visual 子节点下。若只是查看完整构图，也可导入包含三只木桩的 `TrainingCourtyard.fbx`。
3. 在 Unity 新建 URP 材质并映射颜色和贴图，添加地面/围墙 BoxCollider；`CollisionProxies.fbx` 只提供代理几何，不会自动变成 Unity 物理碰撞组件。
4. 将环境、出生点和相机统一布局到原点附近，再由作者继续 BattleBootstrap、本地角色和相机控制。

Blender 的灯光和预览比例人形没有导出到 FBX。报告中的模型导入检查也不等同于现有游戏场景已接入、URP 外观已验证或角色已经可玩。

项目阅读结果、现有训练场偏移与尺寸问题、完整 Unity 接入步骤，以及后续角色 Humanoid / Idle / Run / Attack01 路线见 `Docs/开发日志/迭代04-场景建模与角色动画方案.md`。来源与账号授权记录见 `Docs/资产来源与许可.md`。

本目录只保留可编辑源文件、导出资产、预览图和来源记录；临时 Python / Blender 生成脚本已清理，不属于 Unity 客户端或服务端运行链路。`.validation-unity/` 为独立验证工程，已忽略，不属于运行资产。庭院与石柱各自的 `unity_import_report.json` 保存实际导入结果和所检验文件的 SHA256。
