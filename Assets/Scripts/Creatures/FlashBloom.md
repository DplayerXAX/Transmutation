# Flash Bloom / 闪光囊

第一个荧光生物。复用 `Creature.Luminosity`、`LuminosityLighting`、`GlowBeeLight`、`LuminosityQuery`，以及 `CreatureImpactRelay` 的真实碰撞和投掷命中通知。

## 试玩

打开 `Assets/Scenes/Scene_LightEcology.unity` 进入 Play Mode。玩家起点前方偏右的 `Luminous Habitat` 遮光棚内放有两个闪光囊，以及趋光飞蛾、余辉荚和蜂巢。右键拾取，长按右键蓄力，松开投掷；将生物投向地面，观察强光、视觉失能及余辉荚蓄光。墙体会遮挡效果，投掷落点会改变哪些对象暴露在闪光下。

在其他已有玩家的场景中，可用 `Tools > Creatures > Flash Bloom > Place Near Player` 放置一个实例；也可直接拖入 `Assets/Generated/FlashBloom/FlashBloom.prefab`。

## 默认参数

| 参数 | 默认值 |
| --- | --- |
| 平时 Luminosity | 3 |
| 闪光峰值 Luminosity | 6000 |
| 闪光时长 | 1.5 秒，前 0.2 秒保持峰值 |
| 闪光结束后的冷却 | 8 秒 |
| 最大致盲半径 | 10 米 |
| 致盲时长 | 3.5 秒 |
| 感知亮度门槛 | 12，沿用 luminosity / (1 + distance²) |
| 自由物理撞击触发速度 | 沿碰撞法线的接近速度至少 1 米/秒 |
| 玩家投掷命中 | 任意固体命中都触发，包括低蓄力、飞虫和擦碰；再次投掷可重新触发 |

受到有效撞击或伤害触发防御，轻触和正常静置不会反复触发。玩家投掷使用独立的命中通知，在恢复运动所有权后触发，不受普通防御冷却和法线速度门槛阻挡。该物种免疫自身和同类的闪光。尸体不再发光。死亡、搬运、泡泡捕获、蛛网捕获等状态继续走原有公共生命周期。

`FlashBlindness` 暂时阻断视觉感知，不关闭整个生物或 Rigidbody。公共视线和光源查询会尊重该状态；Asura 取消目标及攻击，鸟停止视觉觅食，松鼠暂时看不到玩家。其他基于距离、接触或物种专属感官的逻辑仍然遵循各自规则。`FlashBloomVisuals` 添加短暂扩散光晕；18 米内出现在视野中且未被遮挡的闪光，还会产生随距离衰减的短暂白光。光晕独立于生物身体大小，不影响搬运范围和碰撞。

调试时可在受影响生物的 Inspector 中查看 `FlashBlindness.remaining`；闪光囊上可调整光强、时长、半径与冷却。

## 验证

`FlashBloomValidation.RunBatch` 仅允许在 `.utmp/GlowBeeChecks/UnityProject` 隔离副本中执行，避免更改已打开的工作场景。它检查遮挡、视觉恢复、实际 AI、物理落地、玩家搬运投掷流程和独立场景整合。结果写入隔离目录的 `flash-bloom-results.txt`。
