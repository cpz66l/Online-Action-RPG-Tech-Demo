# Meshy 符文石柱试验

2026-09-08 已通过真实 Meshy API 生成、下载、Blender 整理，并在独立 Unity `6000.3.20f1` 工程验证模型导入。用途为训练庭院外围装饰。

## 结果

- `RuneObelisk_3m.blend`：可编辑源文件，贴图已打包；另含预览相机和灯光。
- `Exports/RuneObelisk_3m.fbx`：静态模型，底心原点，根位置/旋转为零、缩放为一。
- `Exports/RuneObelisk_3m.glb`：模型和贴图打包，适合 Blender 或支持 glTF 的查看器。
- `Exports/Textures/`：FBX 配套纹理，和 FBX 一起复制进 Unity；不要只复制 FBX。
- `Exports/SourceTextures/`：从原始 GLB 按语义提取的图片，内容未编辑；`texture_manifest.json` 记录通道和用途。
- `obelisk_preview.png`：Blender 实物渲染。
- `geometry_report.json` / `unity_import_report.json`：几何统计、Unity 实际导入结果和 FBX SHA256。

实测尺寸约 **1.0354 × 3.0000 × 1.0359 m**（Unity X/Y/Z），**5,580 三角面、1 个网格、1 个材质**。没有 Unity Collider、骨架或动画。

## Unity 材质

独立验证工程使用默认材质导入器；FBX 和 `Textures/` 同时复制后，BaseColor、Normal、Emission 被连接到 Standard 材质。此验证不代表现有项目的 URP 已完成适配。

正式接入时新建 `Universal Render Pipeline/Lit` 材质，并重映射 FBX 的 `Material_0`。可以使用 `SourceTextures/` 中的语义文件：BaseColor → Base Map；Normal 的 Texture Type 设为 Normal map 后放入 Normal Map；Emission 为可选。初版石材可先使用 Metallic 0、Smoothness 0.25，并在 Unity 灯光下调整。

原 `MetallicRoughness` 图片遵循 glTF：G 为 roughness、B 为 metallic；URP 的打包约定不同，不能把这张图直接当作 Unity Metallic/Smoothness 图使用。通道说明依据 [glTF 2.0 规范](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html) 和 [Unity URP 打包纹理说明](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/shaders-in-universalrp-channel-packed-texture.html)。

## 生成记录与复现

`preview_request.json` / `refine_request.json` 保存实际请求参数；任务 ID、成功状态、消耗记录分别在同目录 JSON。凭据只读取本机 `MESHY_API_KEY` 环境变量，没有写入文件。

本次为 `meshy-6` preview 20 credits + 2K/PBR refine 10 credits，合计 **30 credits**；余额 1650 → 1620。之后的 Blender 导出与渲染不调用 Meshy API。

在仓库根目录运行：

```powershell
& 'E:\Blender\blender.exe' --background --python 'SourceArt\Meshy\RuneObelisk\prepare_obelisk.py'
python 'SourceArt\Meshy\RuneObelisk\extract_source_textures.py'
```

以上命令使用已下载的 `RuneObelisk_Textured.glb`，不会重复消费生成额度。原始下载文件另行保留，整理后的静态 FBX 已烘焙轴转换，避免导入后重设旋转导致模型侧躺。

服务接口和流程见 [Meshy Text-to-3D 官方文档](https://docs.meshy.ai/en/api/text-to-3d)。素材来源与账号授权记录见仓库 `Docs/资产来源与许可.md`。
