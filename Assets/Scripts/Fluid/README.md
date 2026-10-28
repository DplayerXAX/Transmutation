# 液滴侵蚀原型

打开 `Assets/Scenes/ErosionFluidTest.unity` 并运行。坡顶水源默认每秒释放 8 滴，侵蚀会逐渐改变地形碰撞和后续流路。蓝绿色球体的条纹随速度流动，接触位置显示波纹，成功发布的侵蚀会释放少量碎屑。

`Assets/Scenes/Scene_Daniel_Erosion.unity` 是大地图副本。水源等地形碰撞准备完成后，自动布置在出生点附近可到达的缓坡上。沿坡观察水流，或用测试面板前往水源。原 `Scene_Daniel` 和项目构建场景列表未由本功能改动。

## 操作

| 按键 | 行为 |
| --- | --- |
| F1 | 开关全部水源；停水后已释放液滴继续运动 |
| F2 | 开关固定点持续挖掘，独立场景的位置为 `(-12, 4, -8)` |
| F3 | 清空并回收液滴，不恢复地形 |
| F4 | 清空液滴并重置侵蚀 |
| Tab | 开关鼠标测试面板，包含前往水源和"Rain near me"（在玩家前方立即降雨）按钮 |

独立场景另有分叉水源和洞壁水源，初始关闭，可在 Inspector 单独启用。场景包含 0.5 米采样的斜坡、分叉障碍、低洼围岸、平地、洞室、分块接缝、玩家、刚体方块、Fuzz 和 Tentacle 生物。F1 同时启用三个水源，适合压力测试。

## 侵蚀雨

`ErosiveRain` 让侵蚀雨在随机地点出现：开场 3 秒后第一场雨，在主摄像机 35 米内（`roamRadius` 设为 0 则在整个世界范围内）随机选一处已生成地形，云层位于范围内最高地面上方 14 米。每场雨持续 12–25 秒，前后各约 2 秒渐强渐弱，满强度每秒 20 滴，落在半径 6 米的圆内，并随随机风向以 0.6 米每秒漂移；漂到更高地面时云层随之抬升。雨停后间隔 8–20 秒，再到新的随机地点降雨。测试面板显示当前雨的位置、强度和剩余时间。

雨滴就是普通的池化液滴，同样侵蚀地形、加速触碰生物的衰老、可被抓取和蒸发；参数取自同一物体上的 `ErosiveFluidSource`（默认寿命 30 秒、半径 0.12 米、侵蚀速率 0.3、侵蚀半径 0.6 米）。这个源只提供参数，由雨负责生成，其 `Emitting` 就是降雨开关，所以 F1 会同时开关水源和雨。

`ErosiveFluidSystem.erosiveRain`（默认开启）会在子物体中没有 `ErosiveRain` 时，于启动时自动创建一个默认的"Erosive Rain"子物体。如需在 Inspector 调整参数，可手动在系统下添加带 `ErosiveRain` 的子物体（添加时源参数会自动设为雨滴默认值）；不需要雨时关闭该开关。`StartStorm(groundPoint)` 可在指定地点立即开始一场雨。

## 组件与接口

- `ProceduralWorld.Erode(Vector3 worldPosition, float radius, float strength)`：在主线程提交笔刷。正 `strength` 是本次密度削减量，不是每秒速率；负值和非有限值忽略。有效半径至少是生成时网格间距的 1.5 倍，衰减由 `erosionFalloff` 控制。
- `ResetErosion()` 独立恢复基础地形；同一基础地形 `Generate` 保留已经应用的全局整数采样增量。种子、采样网格或基础密度参数改变时清除不兼容增量。当前版本仅保存到本次运行内存。
- `ChunkCreated` 只在分块首次建立对象时触发。`ChunkUpdated` 通知现有分块替换；`TerrainChanged` 在关联分块全部发布后通知接触失效；`ErosionApplied` 给出已发布的实际削减总量。
- `ErosiveFluidSystem` 共享对象池、接触采样、数量预算、粒子与波纹池；`ErosiveFluidSource` 配置发射、生命周期和侵蚀强度；`ErosiveFluidDroplet` 承担球形刚体物理，并继承 `Creature`：玩家可抓取（搬运时暂停寿命、继续侵蚀）和抚摸，热量达到水源的 `evaporationHeat` 时蒸发。死亡或被吃掉的液滴由系统销毁并补充新的池对象。
- 侵蚀同时检查层遮罩和分块的直接 `ProceduralWorldChunk.Owner`。普通 Ground 对象、装饰、其他世界和角色不会被当成可侵蚀地形。Water 层参与液滴间碰撞，Player 层排除；Default 层上的现有 `Creature` 通过碰撞对排除，普通环境保持碰撞。生物激活事件立即排除新生物与重新启用的生物；每秒发现后来添加的子碰撞体，池中液滴再启用时重新设置碰撞对。
- 液滴触碰其他生物时加速其寿命流速。生物实体与液滴互不碰撞，因此每个采样周期用略大于液滴半径（+0.05 米）的重叠检测判断触碰，被触碰的生物在随后 1.5 个采样周期内按 `max(AgeRate × contactAgeMultiplier, contactAgeFloor)` 每秒消耗寿命（默认倍率 4、下限 4；倍率设为 1 且下限设为 0 时关闭）。下限让 `AgeRate` 为 0、平时不会老死的生物（如场景里的 Tentacle，`age` 40）在水中也会衰老，持续接触约 10 秒老死。多滴水同时触碰同一生物不叠加；暂停模拟（被捕获等）的生物和其他液滴不受影响。`IsSoaked(creature)` 可查询当前是否处于触水状态。
- 触碰反馈：接触生物的液滴迅速泛起琥珀色，并有环纹从接触点向液滴内部扩散（`_LifeDrain` / `_DrainPoint`，颜色为材质的 `_DrainColor`）；离开后约 0.4 秒淡出，`LifeDrain` 给出当前强度。只有寿命确实被加速消耗时，接触点才会升起琥珀色尘粒（每个生物约每秒 6 粒，全局上限每秒 10 粒，沿用碎屑的 60 米距离裁剪），所以已暂停的生物只有液滴发光，没有尘粒。
- 静止/休眠液滴每个采样周期复查旧接触；多个接触共享同一份物理时间。侵蚀按接触面切向速度调整，暂停时不积累接触时间。
- 重写 `Creature.Invulnerable` 为 `true` 的生物（如矿石巨兽和晶蛹）不会被水加速衰老：`Soak` 和液滴的生物重叠检测都会跳过它们，液滴也不会因为碰到它们而泛起琥珀色。

## 侵蚀目标（ErosionTarget）

`ErosionTarget` 表示"能被侵蚀水磨损、但不是地形"的实体，例如矿石巨兽腹下的矿心（`OreHeart`）。它不改地形，只把侵蚀量通过 `Eroded(target, point, amount)` 事件交给拥有者，由拥有者决定怎么扣耐久。

- **注册**：组件启用时登记 `surfaces` 里的碰撞体；留空时登记所有子物体上的非触发碰撞体。静态查询有 `TryGet(collider, out target)`、`IsSurface(collider)`、`SurfaceLayers` 和 `SurfacesChanged`。从代码添加时，在物体激活前调用 `Configure(...)` 设置参数。
- **对水是实体**：即使挂在 `Creature` 下，这些碰撞体也不进入水滴的忽略列表（`RegisterActor` 和 `IgnoreActors` 会跳过）。目标晚于水滴注册时，`SurfacesChanged` 会取消已经设置过的忽略碰撞。生物的其他碰撞体仍然让水穿过，所以侵蚀雨可以穿过巨兽的身体落到矿心上。
- **三种侵蚀方式**：
  - **接触**：水滴碰撞时记录接触，按与地形相同的强度公式计算。地形之外的接触不会写入地形碎屑和涟漪效果。
  - **手持**：搬运中的水滴是运动学刚体，所以在地形检测之后，再用本场景的物理做一次重叠检测来判断接触。
  - **投掷**：`CreatureThrower` 投出的水滴命中目标时调用 `ErosiveFluidDroplet.TrySplash`，按冲击速度造成一次爆发侵蚀，随后水滴回到对象池。
- **限制**：
  - **每秒上限** `maximumPerSecond`（默认 1，0 表示不限）由所有接触水滴共享，防止靠反复蹭刷伤害。
  - **爆发侵蚀**：每次 `splashAmount`（默认 0.9），两次之间至少间隔 `splashInterval`（默认 0.15 秒）。
  - **倍率** `sensitivity` 作用于以上所有方式。
- **要求**：
  - **碰撞体形状**：必须是基本形状或凸网格，因为手持接触依靠 `ComputePenetration`，完全穿入非凸 MeshCollider 时检测不到。
  - **层**：必须在水会碰撞的层上（0/4/7/8/9）。
- **兼容性**：同一液滴同时接触地形和目标时，强度按接触数平分。场景里没有任何 `ErosionTarget` 时，地形侵蚀的行为和以前完全一样。

## 密度与更新

最终采样密度为基础密度加稀疏增量。笔刷请求先在样本上汇总；每个物理时间窗口按实际样本削减量执行世界预算，超限比例缩减，超额不补挖。

一个样本及其梯度法线邻域最多关联八个分块。更新批次冻结这些样本，新请求进入下一批；大范围请求拆成多个批次。共享全局位置和中心差分梯度使相邻分块的边界位置与法线一致。原本实心和无网格的分块仍记录状态，可被挖出新表面；完全空的结果清除旧碰撞。

初始网格、侵蚀网格和碰撞预烘焙共用有界后台任务，侵蚀构建与烘焙优先。网格在主线程限量上传，使用与 `MeshCollider` 相同的烘焙选项。关联网格全部完成后，同一 `FixedUpdate` 一起替换网格和碰撞；过期代次或版本的结果丢弃。退出/重新生成前等待已启动的原生烘焙，随后释放网格。地形更新唤醒附近刚体，Tentacle 抓点重新验证碰撞表面。场景中的 HandClimber 通过 `AnyTerrainChanged` 复查玩家抓点，附近表面可重新抓取，表面消失则恢复重力。

## 已验证的起始预算

| 设置 | 默认值 |
| --- | --- |
| 共享液滴上限 / 每源发射率 | 1024 / 8 滴每秒 |
| 生命周期 / 半径 / 最大速度 | 30 秒 / 0.25 米 / 12 米每秒 |
| 接触采样 | 10 Hz |
| 侵蚀速率 / 可侵蚀系数 | 0.5 / 1 |
| 静止倍率 / 最大流动倍率 | 0.15 / 2 |
| 触水生物寿命流速倍率 / 下限 | 4 / 4 每秒 |
| 侵蚀雨 满强度 / 半径 / 云高 | 20 滴每秒 / 6 米 / 14 米 |
| 侵蚀雨 持续 / 间隔 | 12–25 秒 / 8–20 秒 |
| 世界实际密度削减预算 | 8 每秒 |
| 后台并发 / 每帧侵蚀网格上传 | 2 / 2 |
| 每物理步发布 | 一批，最多八个关联分块 |
| 碎屑 / 波纹上限 | 128 粒子 / 32 个波纹；60 米距离裁剪 |

水体保持球形液滴表现，未引入合并水面碰撞。存档、泥沙沉积和岩块坍塌留待后续。

## 验证方法

沿用项目运行时批处理驱动模式，`ErosionTestDriver` 只在 `-erosionTest` 参数出现时创建。完整验收覆盖持续挖坑、玩家与生物下降、面/边/角位置与法线、实心块新表面、空块移除、重复生成、过期任务、暂停、预算、采样频率、对象池回收、休眠、下坡堆积、持续穿透，以及侵蚀前后穿过固定截面的液滴轨迹。改道要求横向路径改变，同时有液滴进入原来为实心的新沟槽。

在隔离项目副本运行编辑器方法，避免关闭或切换正在编辑的场景：

```powershell
# 首次建立场景；之后场景资产已随本功能交付。
Unity.exe -batchmode -projectPath <隔离副本> -executeMethod ErosionSceneSetup.CreatePrototype -quit
Unity.exe -batchmode -projectPath <隔离副本> -executeMethod ErosionTestLauncher.CreateIntegrationAndBuild -quit
# 独立程序功能验收
ErosionValidation.exe -batchmode -nographics -erosionTest -erosionTestOut <输出文件>
# 1080p 三股水流压力测试
ErosionValidation.exe -batchmode -force-d3d11 -screen-width 1920 -screen-height 1080 -erosionTest -erosionPerformance -erosionStress -erosionTestOut <输出文件>
# 大地图回归与性能
ErosionValidation.exe -batchmode -force-d3d11 -screen-width 1920 -screen-height 1080 -erosionTest -erosionPerformance -erosionIntegration -erosionTestOut <输出文件>
```

性能驱动关闭 VSync 和帧率限制，正常时间倍率，预热 10 秒、采集 30 秒。无窗口程序的默认后缓冲不绘制，因此驱动每帧明确提交一个 1920×1080 URP 摄像机渲染请求，并导出实际渲染纹理供画面检查。记录循环帧时间 P95、物理、网格上传、碰撞烘焙、发布、批次更新延迟和内存。此离屏测试包含场景绘制提交，不包含窗口呈现和显示器等待；编辑器或其他硬件的帧时间需另测。测试构建修改自身名称与分辨率设置，应在隔离副本中执行。

实测结果见同目录 `ValidationResults.md`；详细输出位于项目 `Logs/Erosion*.txt`，渲染截图为同名 PNG。
