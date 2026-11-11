# 黏菌蛛丝树林

按参考图实现的程序化树林：多股纤细蛛丝组成树干，根部张开成破网，球形树冠由具有真实孔洞的黏菌网壳构成。孔缘有厚薄变化、湿润珠光和局部半透明膜；部分孔中保留断丝。每棵树可有多个大小不同的顶部球冠，以及长在树干中段的小球冠。相邻树冠之间可生成下垂的多股蛛丝和破损薄膜，连接有概率与数量上限。

## 查看成品

- 示例场景：`Assets/Scenes/Scene_SilkForest.unity`
- 树林 Prefab：`Assets/Prefabs/World/SilkForest.prefab`
- 菜单：`Tools > Capstone > Silk Forest > Open Demo Scene`
- 实际渲染：`ArtCaptures/SilkForest/ForestOverview.png`、`CanopyDetail.png`、`PlayerScale.png`

示例中橙色火柴人高 **1.8 米**，仅用于比例参考。默认种子生成 **29 棵树、89 个球冠、21 组树间蛛丝连接**。树冠内部没有挡住孔洞的实心球。

## 在自己的场景中使用

拖入树林 Prefab，或执行 `Tools > Capstone > Silk Forest > Create Forest In Current Scene`。

选中 `黏菌蛛丝树林 · Silk Forest Manager`，调整 Inspector 参数，再点击 **生成 / 重新生成树林**。点击 **换一个随机种子并生成** 可尝试另一种分布。同一套参数和种子会产生相同结果；参数范围中的 **X 是最小值，Y 是最大值**。

| 参数 | 作用与默认值 |
| --- | --- |
| Forest Size | 种植区域长宽，默认 32 × 28 米；球冠与根部可能伸出区域 |
| Tree Density | 每 100 平方米的目标树数，默认 3.2 |
| Minimum Spacing | 树干之间的最小水平间距，默认 3.2 米 |
| Clearing Width | 中央通道宽度，默认 2.4 米；设为 0 取消 |
| Maximum Trees | 限制树数，默认 180；间距不足时实际数量可少于目标 |
| Height Range | 到顶部树冠连接位置的树干高度，默认 9–16 米；整体高度还包含球冠 |
| Crown Count Range | 每树顶部球冠数，默认 1–3；支持 1–5 |
| Crown Radius Range | 顶部球冠半径范围，默认 1.15–2.05 米；附属球冠额外缩小以形成大小差异 |
| Trunk Crown Count Range | 树干上的小球冠数，默认 0–2；支持 0–6 |
| Trunk Crown Radius Range | 树干球冠半径，默认 0.35–0.8 米 |
| Trunk Radius Range | 整束细丝树干的半径，默认 0.065–0.13 米 |
| Crown Asymmetry | 球冠的不规则程度 |
| Crown Web Thickness | 球冠孔洞周围的黏菌组织厚度 |
| Torn Web Amount | 球冠中断丝和树间破网的程度 |
| Membrane Coverage | 局部半透明黏膜出现比例 |
| Trunk Strands | 每条树干的主要丝束数 |
| Vine Density | 邻近树之间生成蛛丝连接的概率；0 为不连接 |
| Maximum Vine Distance | 允许连接的最远树间距离 |
| Maximum Vines Per Tree | 每树连接数上限，默认 2 |
| Vine Sag Range | 蛛丝下垂深度范围 |
| Vine Web Width | 连接蛛丝展开的网带宽度 |

打开 `Project To Ground` 可把树根投射到已有地形或带 Collider 的地面上。将 `Ground Layers` 设置为地面所在 Layer；`Ground Probe Height` 决定从种植平面上方多高处开始向下探测。`Trunk Colliders` 为树干提供简单的胶囊碰撞体。

## 保存与材质

场景保存时，编辑器会自动把新生成的网格保存到 `Assets/Generated/SilkForest/`；重新打开场景不会丢失树林。Inspector 的 **保存为 Prefab（含网格）** 可保存当前结果。生成操作只替换该 Manager 自己的生成根节点。

`Generate On Start` 用于启动时尚未生成的 Manager；已有烘焙几何的实例会直接使用保存的网格。运行时可调用 `Regenerate()`、`ClearGenerated()`，读取 `GeneratedTreeCount`、`GeneratedCrownCount`、`GeneratedVineCount`。

材质位于 `Assets/Materials/World/SilkForest/`：

- `MAT_SilkPearl`：网壳和丝束，可调主体色、阴影色、湿润高光、珠光和彩色边缘。
- `MAT_SilkMembrane`：局部半透明黏膜，可调透明度与薄膜干涉色。

主要脚本：`SilkForestManager.cs`、`SilkForestGeometry.cs`。几何按树合并，树间连接另行合并，避免每根蛛丝各自创建 Renderer。默认示例约 **138 万顶点**；大规模使用时可通过树密度、球冠数量和树林面积控制总量。

## 验证

在独立 Unity 6000.3.6f1 项目中通过 29 项检查，包括数量范围、固定种子复现、零密度、连接数上限、有效网格索引、保存场景时持久化、保存后重开、多实例共用网格，以及两种 URP Shader 编译。三张预览均由 Unity 实际渲染并经过视觉检查。

检查记录：`ArtCaptures/SilkForest/validation-results.txt`。验证入口为 `SilkForestValidation.RunBatch`，该入口会创建与替换测试场景，应在独立验证项目中执行。
