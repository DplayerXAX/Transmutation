# 《易行 Transmutation》“巨兽 / 移动世界载体”Scope 与实现研究

**受众：** 游戏设计与 Unity 开发团队  
**研究日期：** 2026-08-28  
**项目基线：** Unity 6000.3.6f1、AI Navigation 2.0.9、单机第一人称  
**研究对象：** 能够平移或旋转、表面承载玩家、AI、生物与交互物的大型船只、载具或小型天体。本文把它称为“移动世界载体（Mobile World Carrier）”，以避免“巨兽”被误读为普通大型生物 AI。

## 直接结论

**建议做，但必须分档。首个可玩版本只承诺“船型巨兽”：单一载体、整体朝上、预定轨迹、缓慢平移与有限倾斜；玩家能站立、行走、跳跃并继承甲板速度；AI 只在载体内部或表面寻路。不要在同一个 milestone 同时承诺 Outer Wilds 式 360°小行星、真实轨道、多重局部重力和任意跨载体导航。**

这不是“加一个会动的大模型”，而是一个横切玩家控制、物理更新、相机、AI 导航、存档、关卡制作与世界模拟的参考系系统。当前工程尚未实现这些基础能力，所以应把它立为独立技术 epic，并用一艘灰盒船先验证，而不是直接进入内容制作。

推荐的第一版技术方向是：

1. `CarrierRoot` 是 scale = (1,1,1) 的 kinematic Rigidbody，在 `FixedUpdate` 中用 `MovePosition/MoveRotation` 沿可预测轨迹移动。
2. 船上几何、关卡锚点、生态单元、存档坐标全部以 carrier-local 表示；玩家动态物理仍在 world space 解算。
3. 玩家接地时记录“脚下承载体”和接触点速度，移动逻辑控制相对甲板速度；跳跃时一次性继承该接触点的线速度与角速度贡献。
4. AI 的权威路径数据保持在静止的船体局部空间；世界中的实体只是执行局部路径映射后的动作。不要把“每帧移动 NavMeshSurface”当作生产级默认方案。
5. 只有当玩法真的允许离开载体数公里、跨多个天体旅行时，才加入 floating origin / double global coordinates + local float physics bubble。

## 1. Scope 审核：当前设计与实现状态

### 1.1 设计稿并未确认“移动巨兽”

设计稿只确认“土地本身可以是一只活着的巨兽”，并把“巨兽是否会移动、回应玩家或参与区域生态规则”列为未决定；首版巨兽数量也仍是待确认项（`Design/易行_游戏设计稿.md` 第 120-132、251-255 行）。因此，本次描述属于一次重要的 scope 收敛，而不是已有功能的实现细化。

这个收敛与《易行》的核心方向相容：世界本身具有生命感、玩家观察系统自行互动、区域提供独特空间体验。但移动不应只是视觉奇观。Mobius 对《Outer Wilds》的设计复盘强调，动态世界需要让路径和变化服务玩家有意识的好奇心；若玩家只能随机选路，动态空间会增加迷失而不是发现感。[Mobius Digital: The Intentionality of Wandering](https://www.mobiusdigitalgames.com/news/the-intentionality-of-wandering)

### 1.2 工程基线接近空白

- 项目版本为 Unity 6000.3.6f1；安装了 AI Navigation 2.0.9。
- 唯一构建场景 `SampleScene` 只有相机、灯光与 Volume；没有玩家、巨兽、船体、AI 或 NavMeshData。
- 现有业务脚本只有基础第一人称移动、视角和高级 traversal；没有 moving platform、carrier frame、local gravity 或 floating origin。
- 当前控制器接地只保存 `RaycastHit`，没有保存支撑物 Rigidbody/Transform；地面速度控制、跳跃和 traversal 都使用 world-space `Vector3.up/down`。因此它只适合统一世界重力与静态地面。

### 1.3 当前代码的具体断点

`SmoothFirstPersonController` 的平面速度以世界 `Vector3.up` 投影；移动目标也是固定的世界速度。玩家站在移动船上不输入时，控制器会把自身世界水平速度压向 0，而不是相对甲板速度压向 0。跳跃逻辑先清空世界 Y 速度再施加世界向上的冲量，也没有继承平台速度。`AdvancedFirstPersonTraversal` 同样在墙跑、攀爬、抓边与跳跃中写死世界向上。

结论：当前控制器可以作为输入、状态和部分碰撞代码的参考，但不能通过少量补丁就成为“任意旋转载体 / 行星角色控制器”。船型首版可以重构；行星型需要把 `up`、gravity、ground frame、camera orientation 和 traversal 全部抽象化。

## 2. 建议的功能分档

### Tier A — 船型巨兽（首版推荐）

**承诺：** 一艘载体；世界仍有统一向下重力；船以平移、yaw 和轻微 pitch/roll 为主；玩家和 AI 在甲板/舱内活动；可上下船，但不支持船体翻转。

**排除：** 360°绕行、真实轨道、多个相互影响的重力源、任意高速翻滚、动态形变后实时重建整张导航网格。

**为什么合适：** 它验证“土地会移动”是否真的改善探索与系统互动，同时把最危险的变量约束在移动参考系、玩家承载和局部 AI 三项。Unity 官方建议物理运动在 `FixedUpdate` 内处理；kinematic Rigidbody 的连续运动应使用 `MovePosition/MoveRotation`，并可用 interpolation 平滑渲染。[Unity Rigidbody API](https://docs.unity3d.com/cn/6000.0/ScriptReference/Rigidbody.html) [Unity Rigidbody.MovePosition](https://docs.unity3d.com/jp/current/ScriptReference/Rigidbody.MovePosition.html)

### Tier B — 小行星型巨兽

**新增承诺：** 玩家可以绕整个天体行走；`up` 由局部重力提供；相机、跳跃、坡度、墙跑、AI 朝向和导航全部适应任意表面方向；载体可明显旋转。

**代价变化：** 这不是 Tier A 的美术变体，而是角色控制与导航模型的升级。Unity 的 `NavMeshAgent.updateUpAxis` 能使 agent 对齐当前 NavMesh 的 local up-axis，但这只解决朝向，不会自动解决持续移动网格的路径稳定性、局部重力选择和跨天体状态。[Unity NavMeshAgent.updateUpAxis](https://docs.unity3d.com/ja/2022.1/ScriptReference/AI.NavMeshAgent-updateUpAxis.html)

### Tier C — Outer Wilds 式多天体系统

**新增承诺：** 玩家可离开载体、飞越大距离、进入其他天体参考系；多个天体同时移动；需要全局坐标、局部物理 bubble、加载/卸载、浮动原点和跨 frame 的速度连续性。

《Outer Wilds》官方支持页说明其船、玩家、行星和世界运动建立在 60Hz 的复杂物理模拟上，提高 physics rate 会显著影响性能；这说明它是整个游戏的技术地基，不是普通移动平台功能。[Mobius Digital Support: High Frame-Rate Monitors](https://www.mobiusdigitalgames.com/supportforum.html) GDC 也把它描述为“随时间变化的大规模物理模拟”中的 4D 关卡设计。[GDC: The 4D Level Design of Outer Wilds](https://gdconf.com/article/see-the-4d-level-design-of-outer-wilds-deconstructed-at-gdc-2020/)

## 3. 结构设计：把巨兽当“移动区域”，不是普通 actor

### 3.1 四层结构

每只巨兽应拆成四个职责层：

1. **Macro Motion（宏观运动）**：轨迹、速度、姿态、停靠点、世界事件；只负责 carrier frame 的 pose。
2. **Local World（局部世界）**：甲板、舱室、地形、地标、导航数据和 spawn anchors；全部以 local coordinates 制作。
3. **Living System（生态系统）**：生物、温度、压力、反应网络与永久变化；使用稳定 local IDs 和 local positions 存档。
4. **Passenger Interface（承载接口）**：向玩家、AI、物体、相机提供当前 frame、surface velocity、gravity/up、上/下船和 frame transfer。

这样做能避免每个生物脚本各自处理“船动了怎么办”，也能让设计师在原点附近制作一个静止关卡，再由运行时统一映射到世界。

### 3.2 巨兽的移动必须产生可读的玩法信息

移动至少应承担一种明确功能，否则优先做视觉假移动：

- 改变可达性：经过桥接点、靠近其他区域、露出/遮蔽路径。
- 改变环境：进入冷热、风、压力或光照区，触发生物反应。
- 改变观察：从一个角度才能看见地标或理解两个系统的关系。
- 改变节奏：稳定期便于实验，运动期重组路径或生态。

《Outer Wilds》的开发复盘显示，小行星强曲率和洞穴会让玩家无目的徘徊，因此团队用远景地标、trail markers 和可推测的路径去支持“被好奇心驱动的选择”。对《易行》而言，移动巨兽更需要稳定地标、可辨认方向和运动前兆，因为低 UI 设计不能依靠地图文字补救。[Mobius Digital: The Intentionality of Wandering](https://www.mobiusdigitalgames.com/news/the-intentionality-of-wandering)

### 3.3 首版内容约束

建议首版只做：

- 1 只灰盒船型巨兽，约 80-200 m 长；1 个外部甲板 + 1 个内部舱室。
- 1 条循环轨迹，含静止、匀速、加速、转弯、轻微倾斜五段。
- 1 个上下船点；不支持运动中跨船跳跃。
- 1 种 AI agent，10-20 个并发；只在同一 carrier 内寻路。
- 2 个会受巨兽运动或环境变化影响的生物规则，用来验证“移动是系统的一部分”。
- 不做真实海浪浮力、可破坏船体、动态拓扑导航、多重重力或跨数公里旅行。

## 4. 推荐实现架构

### 4.1 Carrier frame

`CarrierRoot` 保持 uniform scale，最好为 (1,1,1)。视觉模型可以是子层级，但物理根不做非均匀缩放。Unity 的 Transform 层级适合让子物体继承 parent 的位置和旋转；但 Rigidbody 的物理解算仍是 global space，层级并不会创建“局部 PhysicsScene”。[Unity Transform manual](https://docs.unity3d.com/2022.1/Documentation/Manual/class-Transform.html) [Unity Rigidbody overview](https://docs.unity3d.com/cn/2022.2/Manual/RigidbodiesOverview.html)

载体应是 kinematic Rigidbody + compound colliders：

- 在 `FixedUpdate` 采样轨迹，调用 `MovePosition/MoveRotation`。
- 不用 Animator 或普通 `transform.position` 在 `Update` 中搬动物理船体。
- 可见网格按房间/区段拆分以便剔除；碰撞体使用简化 primitive/convex compound，避免一块超复杂动态 MeshCollider。
- 初版不让船体被玩家或小物件撞动；运动由游戏规则权威控制。

### 4.2 Player passenger motor

需要新增统一的 `IReferenceFrame/CarrierFrame` 概念，至少提供：

- 当前 pose 和前一 physics tick pose。
- 给定 world point 的 surface velocity。
- local-to-world / world-to-local point、direction 和 velocity 转换。
- 当前 gravity/up（Tier A 可固定返回世界向上；Tier B 再实现局部重力）。

接地时记录支撑 collider、Rigidbody/frame 与脚下 local anchor。玩家控制使用相对速度：

`v_relative = v_player - v_surface(feet)`

其中旋转载体脚下点速度为：

`v_surface = v_linear + omega × (feet - centerOfMass)`

Unity 的 `Rigidbody.GetPointVelocity(worldPoint)` 已包含 angular velocity，因此有 Rigidbody 时应直接使用它。[Unity Rigidbody.GetPointVelocity](https://docs.unity3d.com/cn/2023.2/ScriptReference/Rigidbody.GetPointVelocity.html)

接地状态下，让玩家的移动加速度逼近“相对甲板的目标速度”，而不是 world-space 目标速度。Catlike Coding 的移动地面实现也采用 connected body + connection velocity，并指出旋转载体必须跟踪接触点在平台 local space 的位置，而不是只看平台中心位移。[Catlike Coding: Moving the Ground](https://catlikecoding.com/unity/tutorials/movement/moving-the-ground/)

跳跃/掉落语义必须由设计先定：

- **物理直觉（推荐）**：离地瞬间把 `v_surface(feet)` 保留在 airborne velocity 中，之后玩家按世界/局部重力抛物运动。
- **粘船辅助**：空中短时间继续参考船 frame，操作更容易但会制造非惯性行为。
- **完全 local lock**：船怎么动玩家都跟着；最稳但会让高速运动缺乏物理反馈。

首版建议“物理直觉 + 温和容错”：继承速度、保留 coyote time，并限制船的角速度和加速度。不要把玩家 Rigidbody 简单 parent 到船；官方文档明确指出 Rigidbody 物理运动按 global 而不是 local 计算。[Unity Rigidbody overview](https://docs.unity3d.com/cn/2022.2/Manual/RigidbodiesOverview.html)

### 4.3 Fixed timestep、插值与相机

所有 carrier pose、玩家接触、速度继承和 origin shift 必须在固定物理 tick 的确定顺序中发生。项目当前 fixed timestep 是 0.02 s（50Hz）；Outer Wilds 使用 60Hz 只是案例，不是应直接复制的目标。

Unity 说明物理和渲染不同步会造成 Rigidbody 抖动；interpolation 可以平滑相机跟随对象，但会落后一个 physics tick，extrapolation 可能越过碰撞边界。[Unity: Apply interpolation to a Rigidbody](https://docs.unity3d.com/cn/2023.1/Manual/rigidbody-interpolation.html)

建议更新顺序：

1. `FixedUpdate` 计算 carrier 下一 pose。
2. `MovePosition/MoveRotation` 推进载体。
3. 玩家 motor 基于 previous/current carrier pose 计算 connection delta 与相对速度。
4. 物理解算。
5. `LateUpdate` 相机基于同一套插值后的 player/carrier pose 跟随，避免一个看旧 pose、一个看新 pose。

### 4.4 AI 与 NavMesh

项目安装的 AI Navigation 2.0.9 支持运行时/编辑时烘焙、动态障碍和 NavMesh links。[Unity AI Navigation 2.0.9](https://docs.unity3d.com/ja/6000.0/Manual/com.unity.ai.navigation.html) 但“支持 runtime build”不等于“持续移动并稳定承载 agent”。

当前包源码在 `NavMesh.onPreUpdate` 检测 `NavMeshSurface` Transform 变化，然后 `RemoveData()` 再以新的 position/rotation `AddData()`。这说明底层移动的是 NavMeshData 实例，而不是一个天然拥有 passenger-frame 连续性的局部 agent world。旧版 Unity 官方 NavMeshComponents FAQ 更直接说明：要一致支持“移动平台承载 agents”需要新的 API。[Unity NavMeshComponents FAQ](https://github.com/Unity-Technologies/NavMeshComponents)

因此建议：

- **生产方案：静止 local nav world。** 每艘船的 NavMesh 在一个不动的局部空间求路；目标、路径角点、avoidance 数据使用 ship-local 坐标。world entity 把局部期望速度/位置通过 carrier pose 转换后执行。
- 可用隐藏的 `NavMeshAgent` 作为 path planner：关闭 `updatePosition/updateRotation`，读取 `nextPosition/desiredVelocity/path.corners`，由自有 locomotion 驱动物理/动画实体。Unity 文档说明关闭 `updatePosition` 后可以显式控制 Transform。[Unity NavMeshAgent.updatePosition](https://docs.unity3d.com/cn/2019.2/ScriptReference/AI.NavMeshAgent-updatePosition.html)
- 上下船、跨载体与跳跃不要依赖一张不断变化的全局 NavMesh；用显式 frame-transfer 状态机和 portal/link。
- 船体内部结构不变时只烘一次；门、断桥或局部形变用 modifier、obstacle 或局部重建。不要每个 fixed tick 重烘整艘船。

可以做一个低成本对照实验，让 `NavMeshSurface` 直接跟船移动；但只有在长时间、转弯、重加速度、重新绑定和 path status 测试全部通过后，才可把它用于生产。

### 4.5 Floating origin：不是“看起来大”就需要

Unity GameObject Transform 和大多数实时渲染/物理位置是 single-precision。Unity 官方 High Precision Framework 用 double global coordinates + rebasing 处理大尺度场景，但它是实验性框架，并不会自动把 PhysX 全部变成双精度。[Unity High Precision Framework](https://github.com/Unity-Technologies/com.unity.gis.high-precision-framework)

学术与行业资料把常见方案归纳为：定期把世界平移回玩家附近、把世界分割为多个局部坐标区，或分段/连续地移动参考点。[Thorne, Using a Floating Origin to Improve Fidelity and Performance of Large, Distributed Virtual Worlds](https://www.researchgate.net/publication/331628217_Using_a_Floating_Origin_to_Improve_Fidelity_and_Performance_of_Large_Distributed_Virtual_Worlds) Avalanche 对大世界的经验也指出，8-16 km 世界坐标范围会让毫米级误差在实际计算中积累为厘米甚至更大。[Avalanche Studios: Creating Vast Game Worlds](https://www.humus.name/Articles/Persson_CreatingVastGameWorlds.pdf)

对 80-200 m 的船，不需要 floating origin。只有满足以下条件之一才进入 Tier C：

- 玩家可离开中心 2-5 km 以上，并且第一人称接触精度仍需毫米/厘米级。
- 同时存在相距很远但都需要全精度物理的天体。
- 可见尺度和近裁剪/远裁剪比已经产生深度精度与阴影问题。

届时使用：`double/sector global state + player-near float physics bubble + fixed-tick origin shift`。批量改 Transform 后，在物理查询前按需调用 `Physics.SyncTransforms`；Unity 建议避免为了兼容性长期启用 `autoSyncTransforms`，因为反复自动同步会有性能成本。[Unity Physics.autoSyncTransforms](https://docs.unity3d.com/ja/2022.3/ScriptReference/Physics-autoSyncTransforms.html)

## 5. 实施计划与工作量估算

以下为工程估算，不是来源中的事实；假设 1 名熟悉 Unity 物理的工程师、已有基础角色控制器、无联机、灰盒美术、PC 目标平台。

### Phase 0 — 设计锁定与技术 spike（3-5 个工作日）

- 锁定 Tier A、跳跃语义、最大线速度/角速度/加速度。
- 灰盒 30 m 平台测试：站立、走、跑、跳、落地、边缘、斜坡、旋转中心远端。
- 比较 dynamic Rigidbody player 与 kinematic CharacterController；以实际手感和抖动决定保留哪条。
- 验证 50Hz 与 60/100Hz 的成本和稳定性。

**退出条件：** 连续 10 分钟无明显滑移/穿透/相机抖动；站立误差、跳跃落点和平台速度继承可量化。

### Phase 1 — Carrier + player production slice（1.5-2.5 周）

- Carrier frame API、轨迹驱动、复合碰撞体。
- 重构玩家为 relative-to-support movement；跳跃继承 point velocity。
- 相机、可交互物、抓取物和上下船的 frame transfer。
- 单元/PlayMode tests 与 profiling harness。

### Phase 2 — Local AI + persistence（1.5-2.5 周）

- 静止 local NavMesh planner、world actuator、局部 avoidance。
- 门/障碍/links 与上下船状态机。
- carrier-local spawn、目标、交互状态和存档坐标。
- 10/20/50 agent 压测。

### Phase 3 — 关卡与系统集成（1-2 周）

- 80-200 m 灰盒巨兽；外部/内部 streaming 和剔除。
- 两种生态规则与载体运动发生可观察互动。
- 路径地标、运动前兆、无 UI 导航可读性 playtest。
- 异常恢复：卡住、落船、载入、carrier teleport/reset。

**Tier A 总量级：约 4-7 工程周 + 关卡/美术内容时间。** 若保留 wall-run/climb/ledge，并要求它们也支持移动载体，增加约 1-2 周和显著 QA 矩阵。

**Tier B 增量：约 4-8 工程周。** 包含局部重力、相机/up 过渡、球形/任意表面 traversal、AI orientation 与全套 locomotion 重测。

**Tier C 增量：至少 6-12+ 工程周。** 包含 global/local 坐标体系、floating origin、跨 frame 速度、加载/卸载、远近尺度渲染与更广的回归测试。它更接近项目架构而不是单个 feature。

## 6. 验收清单

### 玩家

- 载体匀速时，玩家站立 60 秒不会相对甲板漂移超过可感知阈值。
- 玩家向任意方向跑动的速度相对甲板一致，而不是相对世界一致。
- 跳跃保留脚下 point velocity；在旋转中心远近两处的落点符合设计语义。
- 加速、减速、转弯、轻微 pitch/roll、边缘、斜坡、台阶和移动门均测试。
- 相机在 30/60/120+ fps 渲染下不出现明显双重抖动。

### AI

- 载体连续运动和转向 10 分钟，agent 不掉 NavMesh、不丢路径、不被世界 origin 拉走。
- local 目标、追逐移动目标、互相避让、门、NavMeshLink 和上下船分别验证。
- 10/20/50/100 agent 的 frame time、path query time 和内存有 profiler 记录。

### 世界与存档

- 保存/读取后，生物和交互物恢复在相同 carrier-local 位置，而不是旧 world position。
- 载体 teleport/reset、场景重载和玩家掉落有明确恢复规则。
- 粒子、trail、line renderer、audio、VFX、shader world-position 输入逐项决定使用 world 还是 carrier-local。

### Scope gate

- 若 Tier A 的移动本身没有产生可观察的系统互动或更好的探索选择，则把载体保持逻辑静止，只做天空/远景运动；不要为了“真的在动”承担完整参考系成本。
- 未通过 Tier A 验收前，不开始 Tier B/C 内容生产。

## 7. 风险优先级

**P0：** 玩家相对速度与跳跃继承、载体更新顺序、相机插值、AI local path architecture。  
**P1：** 交互物/抓取物、存档 local coordinates、上下船、内部/外部剔除、运动前兆与路径可读性。  
**P2：** floating origin、动态拓扑 NavMesh、真实浮力、载体受力、多个同时可登载巨兽。除非 Tier C 已被明确批准，否则不进入首版。

## 8. 限制与不确定性

- Unity 官方旧 FAQ 对 moving NavMesh platforms 的限制写得最直接；当前 AI Navigation 2.0.9 的公开手册没有同样措辞。但项目本地 2.0.9 源码仍通过 remove/re-add NavMeshData 响应 surface Transform 变化。因此本文把直接移动 NavMeshSurface 评为“可做原型、生产高风险”，而不是绝对不可用。
- 没有权威资料能替项目给出最大船体尺寸、角速度、compound collider 数或 agent 数；这些必须由目标硬件和灰盒 profiler 决定。
- Outer Wilds 的技术方案是架构级案例，不应被当作可复制的 Unity recipe。其价值在于证明动态天体会同时重塑物理、关卡与内容管线。

## 9. Claim-to-source ledger

- **项目 scope 与当前实现：** `Design/易行_游戏设计稿.md`；`Assets/Movement Script/SmoothFirstPersonController.cs`；`Assets/Movement Script/AdvancedFirstPersonTraversal.cs`；`Packages/manifest.json`；`ProjectSettings/ProjectVersion.txt`；`ProjectSettings/TimeManager.asset`；`ProjectSettings/DynamicsManager.asset`。访问于 2026-08-28。
- **Rigidbody / FixedUpdate / point velocity / interpolation：** Unity Technologies，Unity 6 与 2023.x Scripting API / Manual，见正文链接。访问于 2026-08-28。
- **AI Navigation / NavMesh Agent：** Unity Technologies，AI Navigation 2.0.9、NavMeshAgent API；项目本地 package cache 的 `NavMeshSurface.cs`。访问于 2026-08-28。
- **Moving ground implementation pattern：** Jasper Flick，Catlike Coding，*Moving the Ground*，约 2020，见正文链接。访问于 2026-08-28。
- **Outer Wilds physics and level design：** Mobius Digital，2015-2026 官方博客与支持页；Game Developers Conference，2020/2021 session pages。访问于 2026-08-28。
- **Large-world coordinates：** Chris Thorne，*Using a Floating Origin to Improve Fidelity and Performance of Large, Distributed Virtual Worlds*；Emil Persson / Avalanche Studios，*Creating Vast Game Worlds*；Unity Technologies High Precision Framework。访问于 2026-08-28。

## 研究记录与停止条件

检索覆盖：Unity 6 Rigidbody/CharacterController/AI Navigation 官方文档与项目本地 2.0.9 package source；Outer Wilds 官方开发博客、支持页和 GDC 设计资料；moving-platform 专业实现教程；floating-origin 学术与行业资料。停止原因：玩家承载、导航、参考系、浮点精度、设计结构和项目差距均已有一手或交叉证据；继续搜索的新增结果主要是重复社区实现，预计不会改变“Tier A 先行、local frame 为核心、Tier B/C 独立批准”的结论。
