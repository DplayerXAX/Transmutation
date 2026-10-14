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
| Tab | 开关鼠标测试面板，包含前往水源按钮 |

独立场景另有分叉水源和洞壁水源，初始关闭，可在 Inspector 单独启用。场景包含 0.5 米采样的斜坡、分叉障碍、低洼围岸、平地、洞室、分块接缝、玩家、刚体方块、Fuzz 和 Tentacle 生物。F1 同时启用三个水源，适合压力测试。

## 组件与接口

- `ProceduralWorld.Erode(Vector3 worldPosition, float radius, float strength)`：在主线程提交笔刷。正 `strength` 是本次密度削减量，不是每秒速率；负值和非有限值忽略。有效半径至少是生成时网格间距的 1.5 倍，衰减由 `erosionFalloff` 控制。
- `ResetErosion()` 独立恢复基础地形；同一基础地形 `Generate` 保留已经应用的全局整数采样增量。种子、采样网格或基础密度参数改变时清除不兼容增量。当前版本仅保存到本次运行内存。
- `ChunkCreated` 只在分块首次建立对象时触发。`ChunkUpdated` 通知现有分块替换；`TerrainChanged` 在关联分块全部发布后通知接触失效；`ErosionApplied` 给出已发布的实际削减总量。
- `ErosiveFluidSystem` 共享对象池、接触采样、数量预算、粒子与波纹池；`ErosiveFluidSource` 配置发射、生命周期和侵蚀强度；`ErosiveFluidDroplet` 承担球形刚体物理，并继承 `Creature`：玩家可抓取（搬运时暂停寿命、继续侵蚀）和抚摸，热量达到水源的 `evaporationHeat` 时蒸发。死亡或被吃掉的液滴由系统销毁并补充新的池对象。
- 侵蚀同时检查层遮罩和分块的直接 `ProceduralWorldChunk.Owner`。普通 Ground 对象、装饰、其他世界和角色不会被当成可侵蚀地形。Water 层参与液滴间碰撞，Player 层排除；Default 层上的现有 `Creature` 通过碰撞对排除，普通环境保持碰撞。生物激活事件立即排除新生物与重新启用的生物；每秒发现后来添加的子碰撞体，池中液滴再启用时重新设置碰撞对。
- 静止/休眠液滴每个采样周期复查旧接触；多个接触共享同一份物理时间。侵蚀按接触面切向速度调整，暂停时不积累接触时间。

## 密度与更新

最终采样密度为基础密度加稀疏增量。笔刷请求先在样本上汇总；每个物理时间窗口按实际样本削减量执行世界预算，超限比例缩减，超额不补挖。

一个样本及其梯度法线邻域最多关联八个分块。更新批次冻结这些样本，新请求进入下一批；大范围请求拆成多个批次。共享全局位置和中心差分梯度使相邻分块的边界位置与法线一致。原本实心和无网格的分块仍记录状态，可被挖出新表面；完全空的结果清除旧碰撞。

初始网格、侵蚀网格和碰撞预烘焙共用有界后台任务，侵蚀构建与烘焙优先。网格在主线程限量上传，使用与 `MeshCollider` 相同的烘焙选项。关联网格全部完成后，同一 `FixedUpdate` 一起替换网格和碰撞；过期代次或版本的结果丢弃。退出/重新生成前等待已启动的原生烘焙，随后释放网格。地形更新唤醒附近刚体，Tentacle 抓点重新验证碰撞表面。场景中的 HandClimber 通过 `AnyTerrainChanged` 复查玩家抓点，附近表面可重新抓取，表面消失则恢复重力。

## 已验证的起始预算

| 设置 | 默认值 |
| --- | --- |
| 共享液滴上限 / 每源发射率 | 256 / 8 滴每秒 |
| 生命周期 / 半径 / 最大速度 | 15 秒 / 0.25 米 / 12 米每秒 |
| 接触采样 | 10 Hz |
| 侵蚀速率 / 可侵蚀系数 | 0.5 / 1 |
| 静止倍率 / 最大流动倍率 | 0.15 / 2 |
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
