# 04 战斗场景：石砌训练庭院建模原型

这是可编辑的场景美术原型。当前保留已生成的 Blender 源文件、导出模型、预览图和验证报告；原用于一次性生成庭院的 Python / Blender 脚本已从仓库移除，避免与 Unity 客户端或服务端运行代码混淆。后区两座符文石柱使用 `SourceArt/Meshy/RuneObelisk` 中单独生成的 Meshy 道具。模型尚未替换现有 Unity 04 场景。

## 交付文件

| 文件 | 用途 |
| --- | --- |
| `TrainingCourtyard.blend` | 可编辑的 Blender 场景，包含模型、预览灯光、相机和比例人形 |
| `Exports/TrainingCourtyard.fbx` | 完整视觉模型，包含三个木桩及空锚点 |
| `Exports/TrainingCourtyard_EnvironmentOnly.fbx` | 不含木桩视觉网格的环境，方便后续独立生成训练目标 |
| `Exports/TrainingCourtyard.glb` | 带材质的通用 3D 预览/交换版本 |
| `Exports/TrainingCourtyard_CollisionProxies.fbx` | 平地及墙体简化碰撞代理几何；不是已经启用的 Unity Collider |
| `Exports/TrainingDummy_01.fbx` 等 | 独立木桩；底心原点，导出时位于世界原点 |
| `Previews/overview.png` | 场景整体构图 |
| `Previews/gameplay.png` | 接近第三人称目高的空间尺度预览 |
| `Previews/gate_detail.png` | 拱门、旗帜及石柱区域细节 |
| `geometry_report.json` | 从模型实际计算的尺寸、面数、材质、对象和坐标报告 |

## 尺度和布局

设计基座为 28 × 28 米，中央石砖区为 20 × 20 米。石砖顶面严格位于 Unity `Y = 0`，中心训练标记仅高 4 毫米。基座侧边装饰可能略超出 28 米的设计轮廓；准确包围盒见统计报告。

坐标统一按 Unity 米制表达：X 向右、Y 向上、Z 朝拱门。脚本转换到 Blender 为 `(-x, -z, y)`，其中 X 反转用于抵消本项目 Unity FBX 导入器实测的坐标手性转换。FBX 使用 `-Z Forward / Y Up`，烘焙空间轴变换；GLB 为 Y-up 的通用右手坐标交换版本，接入其他引擎时需检查该引擎的左右手约定。

| 空锚点 | Unity 坐标（米） |
| --- | --- |
| `Spawn_Player_01` | `(-2, 0, -7)` |
| `Spawn_Player_02` | `(2, 0, -7)` |
| `DummyAnchor_01` | `(-4, 0, 4)` |
| `DummyAnchor_02` | `(0, 0, 5)` |
| `DummyAnchor_03` | `(4, 0, 4)` |

## 修改方式

当前仓库不再保留自动生成脚本。若需要调整场景，应优先打开 `TrainingCourtyard.blend` 在 Blender 内直接编辑，再按既有文件名重新导出 FBX / GLB。若后续确实需要重新建立程序化建模流程，可以另起本地实验脚本；在确认会长期维护前，不建议把临时脚本提交到仓库。

## Blender 集合

- `VIS_Architecture`：基座、墙体、柱体、分段楔石拱门。
- `VIS_Paving`：平整地砖和轻量练习区标记。
- `VIS_Details`：弯曲旗面、长凳、训练器具、石块。
- `VIS_Vegetation`：边缘小范围藤蔓及草。
- `MESHY_Decor_AI_RuneObelisk`：两个共享几何和材质的 AI 石柱实例。
- `Props_TrainingDummies`：三个可独立选择、替换、导出的木桩。
- `Anchors_UnityMeters`：出生点和木桩位置。
- `COLLISION_Proxies_NotUnityColliders`：默认在 Blender 中隐藏的简化代理。
- `PREVIEW_ONLY_*`：仅预览使用的灯光、相机、地台背景及 1.8 米比例人形，不进入模型导出。

## 后续 Unity 接入

环境优先采用 `TrainingCourtyard_EnvironmentOnly.fbx`。后续玩家生成、相机、出生点和训练目标由对应业务模块管理，视觉模型对齐相应锚点。木桩单独模型可以挂到待实现的 TrainingDummy 对象下；目前原工程尚未实现这些角色与受击模块。

碰撞代理只提供轮廓，接入时需在 Unity 添加 `BoxCollider` 或静态 `MeshCollider`，关闭其 Renderer；不要为每块地砖创建碰撞体。三个木桩的命中碰撞留到 04D 与受击逻辑一起实现。

主体是无纹理 PBR 色块材质。石柱材质使用 AI 生成贴图，来源信息在对应目录保留。将 FBX 导入 Unity 时应同时复制 `Exports/Textures` 目录；实测只复制 FBX 不会自动绑定内嵌贴图。预览中的暖阳和冷阴影来自 Blender 灯光，不会随 FBX 自动配置 Unity 灯光。正式使用前仍需配置渲染管线材质、灯光、阴影距离以及烘焙光照所需的 UV2；模型并未制作专用光照贴图 UV。

本原型不含角色绑定、角色动画或旗帜实时布料模拟。旗帜为可直接导出的静态曲面。
