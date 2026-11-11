# Prefab 目录索引

游戏 Prefab 统一放在 `Assets/Prefabs`，按用途及生物系统分类。移动已有资产时保留原 `.meta` 和 GUID；场景和其他 Prefab 中的引用继续有效。

## 这次补齐的 Prefab

| 资产 | 路径 | 用途 |
|---|---|---|
| 荧光蜂蜜 | `Creatures/LightEcology/GlowHoney.prefab` | 可搬运、单击食用、长按涂身；手持时可涂其他目标。 |
| 滑板鞋拾取物 | `Equipment/SkateShoes/SkateShoes.prefab` | 直接拖入场景；左键穿上，K 切换穿脱。根节点位于鞋底附近。 |
| 滑板鞋模型 | `Equipment/SkateShoes/SkateShoesVisual.prefab` | 已保存的双鞋模型及材质，用于拾取展示和玩家脚部。 |

运行时引用保存在 `Assets/Resources/GameplayPrefabLibrary.asset`，资产本体仍位于本目录。蜂巢产蜜、出生点鞋子和穿戴模型通过该引用表使用正式 Prefab。

鞋子材质：`Assets/Materials/Equipment/SkateShoes/`。蜂蜜材质：`Assets/Materials/Creatures/LightEcology/GlowHoney.mat`。

已有光生态、闪光花等材质仍留在原材质目录；`Generated` 不再存放游戏 Prefab。

## 分类清单

| 目录 | 数量 | Prefab |
|---|---:|---|
| `Creatures/Asura/` | 6 | `BasicProjectile.prefab`, `BlastCore.prefab`, `ExplosiveProjectile.prefab`, `GeneralAsura.prefab`, `MeleeAsura.prefab`, `RangedAsura.prefab` |
| `Creatures/Birds/` | 2 | `BirdBones.prefab`, `BirdCreature.prefab` |
| `Creatures/Bubbles/` | 4 | `Bubble Negative.prefab`, `Bubble.prefab`, `BubbleFlower.prefab`, `TrappingBubble.prefab` |
| `Creatures/Compost/` | 1 | `CompostCreature.prefab` |
| `Creatures/Fire/` | 2 | `Fire Flower.prefab`, `FireSource.prefab` |
| `Creatures/LightEcology/` | 4 | `AfterglowPod.prefab`, `FlashBloom.prefab`, `GlowHoney.prefab`, `LightMoth.prefab` |
| `Creatures/OreColossus/` | 1 | `GeodeLarva.prefab` |
| `Creatures/Prototypes/` | 2 | `ProceduralCreature.prefab`, `SignedFieldSun.prefab` |
| `Creatures/Seeds/` | 1 | `CorruptSeed.prefab` |
| `Creatures/Spiders/` | 2 | `SkySpider.prefab`, `SkyWeb.prefab` |
| `Creatures/Squirrels/` | 3 | `Food.prefab`, `Nest.prefab`, `Squirrel.prefab` |
| `Creatures/Traversal/` | 2 | `BounceShroom.prefab`, `GooCreature.prefab` |
| `Equipment/SkateShoes/` | 2 | `SkateShoes.prefab`, `SkateShoesVisual.prefab` |
| `Gameplay/Tutorial/` | 1 | `StartingDragonFuel.prefab` |
| `Gameplay/UI/` | 1 | `InteractionHintsTrigger.prefab` |
| `Player/` | 1 | `PlayerObjects.prefab` |
| `Systems/Squirrels/` | 2 | `SquirrelMuseumDisplay.prefab`, `SquirrelSystemManager.prefab` |
| `WorldDecor/` | 132 | 132 个场景装饰变体，按 Decor_类型_序号命名。 |

## 历史原型

`Creatures/Prototypes/ProceduralCreature.prefab` 是已有的历史原型，包含旧脚本缺失；本次保留原资产及 GUID，未删除其组件。它不属于可直接使用的新功能资产。

课程示例 `Assets/topics/06 creating meshes/Cylinder.prefab` 保留在教学目录；第三方资源也保留原目录。

## 后续创建规则

- 新游戏 Prefab 保存到本目录对应分类，材质保存到 `Assets/Materials`。
- 保留已有 Prefab 的 `.meta`，避免破坏场景引用。
- 更新相关 Setup 工具中的路径；按名字选择生物的编辑器工具使用 `CreaturePrefabPaths.Resolve`。
- 补建蜂蜜和滑板鞋资产的菜单：`Tools > Capstone > Prefabs > Create Missing Honey And Skate Shoes Assets`。该工具保留已存在的 Prefab，不覆盖手工调整。
