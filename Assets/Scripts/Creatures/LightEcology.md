# 荧光生物组合

沿用现有 `Creature.Luminosity` / `PlayerCreatureStats`、`LuminosityQuery`、`LuminosityLighting` 和 `GlowBeeLight`。三个生物都继承 `Creature`，使用原有搬运、投掷和生命周期。

## 试玩

打开 `Assets/Scenes/Scene_LightEcologyTest.unity`，进入 Play Mode。

1. 面前的黄色物体是蜂蜜。右键拾取，按 F 涂身，让玩家发光。
2. 暗处，附近紫色翅膀的小飞蛾会飞向玩家并盘旋。
3. 按 L 开灯，同样的玩家荧光不足以形成明显对比，飞蛾会失去兴趣；再按 L 关灯，恢复吸引。
4. 拿起蓝白色闪光囊，长按右键蓄力，松开投向绿壳余辉荚附近。
5. 撞击会爆闪并暂时使附近生物失去视觉。余辉荚吸收强光，延迟 1.2 秒后发出持续 18 秒的柔光。
6. 飞蛾视力恢复后，会被暗处的余辉荚吸引。余辉荚可再次拾取、搬运或投掷，作为移动照明和诱饵。

单独测试闪光与战斗 AI，可以打开 `Assets/Scenes/Scene_FlashBloomTest.unity`。

## 资产

| 生物 | Prefab | 主要脚本 |
| --- | --- | --- |
| 闪光囊 | `Assets/Generated/FlashBloom/FlashBloom.prefab` | `FlashBloom.cs` |
| 趋光飞蛾 | `Assets/Generated/LightEcology/LightMoth.prefab` | `LightMoth.cs` |
| 余辉荚 | `Assets/Generated/LightEcology/AfterglowPod.prefab` | `AfterglowPod.cs` |

可将这些 Prefab 直接拖进已有场景。现有 Yoyo 场景、玩家 Prefab 和构建列表不需要更改。

## 行为与调参

闪光囊：平时亮度 3，强光峰值 1800；沿法线至少 4 米/秒的有效撞击或伤害触发。闪光衰减 0.8 秒，结束后冷却 8 秒。最大范围 10 米，致盲 3.5 秒。同类免疫，实体遮挡有效。完整参数见 `FlashBloom.md`。

趋光飞蛾：18 米内搜索光源，感知亮度至少 0.45，且光源相对于背景的对比至少为 3。玩家、蜂蜜、闪光囊和余辉荚都经过同一套光源查询，无单独的玩家吸引特判。被致盲、搬运或捕获时停止自主趋光移动。

环境亮度由 `AmbientLuminosity` 在 CPU 上估算，考虑 Flat/Trilight 环境光、天空/探针光、普通方向光/点光/聚光灯及实体遮挡；不是相机屏幕像素或精确的 URP 光照重建。生物和玩家身上的灯不重复计入背景，其发光由公共数值查询处理。其他可见荧光源会降低单个源的突出程度。没有 Light 或光照探针贡献的纯材质自发光，不自动成为环境照明。

余辉荚：12 米内感知亮度至少 10，且源的发射亮度至少 300 才能充能。短暂强光足以充能；普通蜂蜜亮度 50 不足。储光后延迟 1.2 秒，以亮度 100 开始释放，18 秒内逐渐衰减，最后回到微光 0.5。充能是生化反应，不要求视觉正常，因此可以吸收刚刚致盲自己的闪光。自身柔光不够再次充能，死亡清空能量。

## 验证

`FlashBloomValidation.RunBatch` 是隔离项目批处理入口，依次运行闪光、飞蛾、余辉荚检查。仅允许在 `.utmp/GlowBeeChecks/UnityProject` 中运行，结果位于 `.utmp/GlowBeeChecks/flash-bloom-results.txt`。

54 项 Play Mode 检查覆盖：实际 Rigidbody 落地、玩家搬运和蓄力投掷、强光及灯光数值、墙体遮挡、冷却与生命周期、Asura 攻击中断和恢复、鸟类视觉觅食恢复、明暗对比、真实飞近玩家、蜂蜜过期、余辉储光与三个生物的完整互动链。
