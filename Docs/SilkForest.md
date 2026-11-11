# 黏菌蛛丝树林

按参考图实现的程序化树林：树干、根、分支和藤蔓统一使用银白的立体蛛丝束。多股有厚度的扁丝沿拉力方向交错、分开、收拢，以不规则的长缝和细长斜膜形成空隙；不再用四面打孔管或球形接头。丝束具有空间截面，各丝股为封闭的扁透镜截面，换视角仍有体积。球冠保留连续组织和大小不同的孔洞，孔缘有隆起包边，内部有交错的破布状黏膜。每棵树可有多个顶部球冠，以及可调数量的树干小球冠。

## 查看成品

- 示例场景：`Assets/Scenes/Scene_SilkForest.unity`
- 树林 Prefab：`Assets/Prefabs/World/SilkForest.prefab`
- 菜单：`Tools > Capstone > Silk Forest > Open Demo Scene`
- 实际渲染：`ArtCaptures/SilkForest/ForestOverview.png`、`CanopyDetail.png`、`PlayerScale.png`

示例中橙色火柴人高 **1.8 米**，仅用于比例参考。随附场景保留已调整的 **2.76** 密度。树干中段带孔的小球是可选的小球冠，可将 **树干小球冠数量范围** 设为 0–0 关闭；原先根和裸枝连接处的球形接头已移除。

## 在自己的场景中使用

拖入树林 Prefab，或执行 `Tools > Capstone > Silk Forest > Create Forest In Current Scene`。

选中 `黏菌蛛丝树林 · Silk Forest Manager`，直接拖动 **树干开孔程度** 和 **树冠开孔程度**。树干滑条现在控制丝束的分离与细膜疏密，越大越通透；松开滑条后自动更新预览。树冠滑条同时影响外壳和内部黏膜。根、侧枝及承托球冠的枝条使用相同的丝束结构。

**丝束分流比例** 控制沿长度变化的分流程度，**丝股交错程度** 控制侧移与交错，**斜向牵丝比例** 控制跨股连接。空隙由丝股之间的距离形成，主要呈细长的梭形与三角形，而不是在完整管壁上排布圆孔。旧参数的序列化字段保留，已有实例可以继续使用。

根部不再共享一个分叉点。各条根的丝股从主干下部不同高度、不同位置逐渐向外释放，主干底端也取消收束；每条根由独立扁丝形成，不再使用会先收紧再展开的丝束包络，也不生成跨股连接。根梢保持散开。至少两组根接地，其余有概率向上翘起；打开地形投射时，接地根尖分别探测地面高度。树干中上段的侧向丝束两端接回主干，只有中段向外拱开；复股同样回接主干，不再生成向外悬空的裸枝。

其他参数调整后点击 **生成 / 重新生成树林**。点击 **换一个随机种子并生成** 可尝试另一种分布。同一套参数和种子会产生相同结果。Inspector 的所有参数均有中文名称和悬停说明；范围输入标注为 **最小 / 最大**，森林范围标注为 **宽度 / 长度**，两种材质的自定义属性也有中文名称。

| 参数 | 作用与默认值 |
| --- | --- |
| Forest Size | 种植区域长宽，默认 32 × 28 米；球冠与根部可能伸出区域 |
| Tree Density | 每 100 平方米的目标树数；新 Manager 默认 3.2，随附 Prefab 保留 2.76 |
| Minimum Spacing | 树干之间的最小水平间距，默认 3.2 米 |
| Clearing Width | 中央通道宽度，默认 2.4 米；设为 0 取消 |
| Maximum Trees | 限制树数，默认 180；间距不足时实际数量可少于目标 |
| Height Range | 到顶部树冠连接位置的树干高度，默认 9–16 米；整体高度还包含球冠 |
| Crown Count Range | 每树顶部球冠数，默认 1–3；支持 1–5 |
| Crown Radius Range | 顶部球冠半径范围，默认 1.15–2.05 米；附属球冠额外缩小以形成大小差异 |
| Trunk Crown Count Range | 树干上的小球冠数，默认 0–2；支持 0–6 |
| Trunk Crown Radius Range | 树干球冠半径，默认 0.35–0.8 米 |
| Trunk Radius Range | 树干丝束腰部的参考半宽，默认 0.12–0.19 米 |
| 丝股交错程度 | 默认 0.85；控制丝股的侧移与交错幅度 |
| 树干扭转幅度（度） | 默认 65；每树在范围内随机扭转，沿高度平滑变化 |
| 树冠孔缘包边厚度 | 默认 0.018，表示树冠半径的 1.8%；0 关闭隆起包边 |
| Crown Asymmetry | 球冠的不规则程度 |
| 树干开孔程度 / Trunk Openness | 默认 0.94；提高数值使丝股分开、细膜变少 |
| 丝束分流比例 | 默认 0.72；控制丝股分离幅度沿长度的变化 |
| 斜向牵丝比例 | 默认 0.32；控制跨股斜向连接的比例 |
| 根部额外镂空程度 | 默认 0.9；提高后根部丝股展开更明显 |
| 树冠开孔程度 / Crown Openness | 默认 0.92；同时调整球冠外壳和内部黏膜的开孔 |
| Interior Sheet Count | 球冠内部交错的破布状黏膜数量，默认 3 |
| Trunk Membrane Thickness | 丝股与细膜厚度的参考值，默认 0.014 米 |
| 根部分叉数量范围 | 默认 3–5 条；至少保留两条接地根 |
| 根部展开距离范围 | 默认 0.7–1.35 米 |
| 根部分叉高度范围 | 默认离地 0.65–1.15 米；矮树会限制分叉高度 |
| 根尖上翘概率 | 默认 0.3；控制非必需接地根的上翘概率 |
| 回接丝束数量范围 | 默认每树 2–4 条，两端连接主干，不包括承托球冠的枝条 |
| 回接丝束展开范围（米） | 默认 0.75–1.7，实际向外最大偏移约为该值的一半 |
| 回接丝束复股比例 | 默认 0.45，增加较细的平行回接丝束 |
| Vine Density | 邻近树之间生成蛛丝连接的概率；0 为不连接 |
| Maximum Vine Distance | 允许连接的最远树间距离 |
| Maximum Vines Per Tree | 每树连接数上限，默认 2 |
| Vine Sag Range | 蛛丝下垂深度范围 |
| Vine Web Width | 连接蛛丝展开的网带宽度 |
| Y 字连接比例 / Y Junction Chance | 默认 0.55；0 为仅双树连接，1 为优先尝试 Y 形连接，受距离、位置和连接上限约束 |

树间连接使用同样的交错扁丝束，中间收细、在附着处展开。Y 形连接由三条丝束收拢到同一位置，不使用圆球接头。每棵参与树计入一组连接；每树连接上限同时适用于双树和 Y 形连接。

打开 `Project To Ground` 可把树根投射到已有地形或带 Collider 的地面上。将 `Ground Layers` 设置为地面所在 Layer；`Ground Probe Height` 决定从种植平面上方多高处开始向下探测。`Trunk Colliders` 为树干提供简单的胶囊碰撞体。

## 保存与材质

`Assets/Generated/SilkForest/SilkForestMeshes*.asset` 使用 Git LFS 保存，规则位于仓库根目录 `.gitattributes`。提交树林改动时同时提交该规则、实际使用的网格及其 `.meta`，不要仅忽略仍被场景引用的网格。新电脑拉取后需安装 Git LFS 并执行 `git lfs pull` 获取完整网格。

本次生成过程中不再被场景或 Prefab 引用的新增网格已移至本地 `.utmp/SilkForestMeshArchive/` 备份；该目录不参与提交。已有历史提交中的网格未改动。

场景保存时，编辑器会自动把新生成的网格保存到 `Assets/Generated/SilkForest/`；重新打开场景不会丢失树林。Inspector 的 **保存为 Prefab（含网格）** 可保存当前结果。生成操作只替换该 Manager 自己的生成根节点。

`Generate On Start` 用于启动时尚未生成的 Manager；已有烘焙几何的实例会直接使用保存的网格。运行时可调用 `Regenerate()`、`ClearGenerated()`，读取 `GeneratedTreeCount`、`GeneratedCrownCount`、`GeneratedVineCount`。

材质位于 `Assets/Materials/World/SilkForest/`：

- `MAT_SilkPearl`：树冠、内部黏膜、树干、根、分支和藤蔓统一使用的材质，恢复先前树冠的银蓝珠光、湿润高光和轻微虹彩。
- `MAT_TensionedSilk`：兼容已有实例的旧材质引用，外观同步为同样的珠光参数；新生成的树林直接共用 `MAT_SilkPearl`。

Shader 的 **丝质柔光混合（0为珠光）** 设为 0，恢复此前树冠的着色方式；1 为柔光丝质版本。材质调整直接作用于已保存的网格，无需重新生成树林。材质专项验证记录为 `ArtCaptures/SilkForest/material-validation-results.txt`。
- `MAT_SilkMembrane`：局部半透明黏膜，可调透明度与薄膜干涉色。

主要脚本：`SilkForestManager.cs`、`SilkForestGeometry.cs`。每棵树的球冠与丝束按材质分别合并，普通树间连接另行合并，每组 Y 连接保存为一个网格。大规模使用时可通过树密度、球冠数量和树林面积控制总量。实际树数、连接数与顶点数见验证记录。

## 验证

在独立 Unity 6000.3.6f1 项目中验证实际生成的树林，包括丝束疏密控制、多视角空间体积、扁丝法线和封口、根部分叉与接地、所有可见参数的中文名称和说明、球冠孔缘包边、球冠圆度、内部黏膜、数量范围、种子复现、零密度、双树与 Y 连接、连接上限、实际网格有效顶点/法线/索引、保存后重开、多实例共用网格，以及两种 URP Shader 编译。另保留旧膜片基础算法的回归检查；当前树干使用 `SilkBundle`。

预览由 Unity 实际渲染并经过视觉检查。`SilkStrandsDetail.png` 展示横向蛛丝束；`YJunctionDetail.png` 展示三树 Y 形连接；`ConnectionDetail.png` 展示树冠接合处；`TrunkFront.png` 与 `TrunkQuarter.png` 展示交错扁丝；`RootDetail.png` 展示分流根束；`BranchDetail.png` 展示实际树干侧枝；`CanopyInterior.png` 为移除前半球外壳的内部剖视图。

检查记录：`ArtCaptures/SilkForest/validation-results.txt`。验证入口为 `SilkForestValidation.RunBatch`，该入口会创建与替换测试场景，应在独立验证项目中执行。
