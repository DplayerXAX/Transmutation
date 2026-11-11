using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Seeded, mesh-based slime-mould silk forest. All dimensions are metres.</summary>
[DisallowMultipleComponent]
public sealed class SilkForestManager : MonoBehaviour
{
    [Header("森林整体设置")]
    [InspectorName("随机种子"), Tooltip("相同种子和参数会生成相同的树林。")]
    public int seed = 73421;
    [InspectorName("森林范围（米）"), Tooltip("X 为横向宽度，Y 为纵向长度。")]
    public Vector2 forestSize = new Vector2(32, 28);
    [InspectorName("树林密度"), Tooltip("每 100 平方米的目标树数；受最小间距和数量上限约束。")]
    [Range(0, 12)] public float treeDensity = 3.2f;
    [InspectorName("树木数量上限"), Tooltip("限制最多生成多少棵树。")]
    [Min(1)] public int maximumTrees = 180;
    [InspectorName("最小树间距（米）"), Tooltip("两棵树干之间允许的最小水平距离。")]
    [Min(0.1f)] public float minimumSpacing = 3.2f;
    [InspectorName("中央通道宽度（米）"), Tooltip("在树林中央留出通道；设为 0 取消。")]
    [Min(0)] public float clearingWidth = 2.4f;
    [InspectorName("启动时生成"), Tooltip("运行时尚无生成内容时，自动生成树林。")]
    public bool generateOnStart = true;
    [InspectorName("树干碰撞体"), Tooltip("为树干添加简单的胶囊碰撞体。")]
    public bool trunkColliders = true;
    [InspectorName("贴合地面"), Tooltip("向下探测地面，将树根放置在地形或带碰撞体的表面上。")]
    public bool projectToGround;
    [InspectorName("地面图层"), Tooltip("只在这些图层中寻找可放置树根的地面。")]
    public LayerMask groundLayers = 1;
    [InspectorName("地面探测高度（米）"), Tooltip("从种植平面上方指定高度向下探测地面。")]
    [Min(1)] public float groundProbeHeight = 100;

    [Header("树木随机范围与形态")]
    [InspectorName("树干高度范围（米）"), Tooltip("到顶部树冠连接位置的高度；整体高度还包含树冠。")]
    public Vector2 heightRange = new Vector2(9, 16);
    [InspectorName("顶部球冠数量范围"), Tooltip("每棵树顶部球冠的随机数量，支持 1 至 5 个。")]
    public Vector2Int crownCountRange = new Vector2Int(1, 3);
    [InspectorName("顶部球冠半径范围（米）"), Tooltip("主要球冠的半径范围；附属球冠会额外缩小。")]
    public Vector2 crownRadiusRange = new Vector2(1.15f, 2.05f);
    [InspectorName("树干小球冠数量范围"), Tooltip("树干中段长出的小球冠数量，支持 0 至 6 个。")]
    public Vector2Int trunkCrownCountRange = new Vector2Int(0, 2);
    [InspectorName("树干小球冠半径范围（米）"), Tooltip("树干中段小球冠的随机半径。")]
    public Vector2 trunkCrownRadiusRange = new Vector2(0.35f, 0.8f);
    [InspectorName("树干腰部半宽范围（米）"), Tooltip("中段蛛丝束展开时的参考半宽。")]
    public Vector2 trunkRadiusRange = new Vector2(0.12f, 0.19f);
    [InspectorName("球冠形状不规则程度"), Tooltip("控制球冠轮廓的轻微起伏；0 为标准球形。")]
    [Range(0, 0.6f)] public float crownAsymmetry = 0.06f;
    [InspectorName("丝股交错程度"), Tooltip("控制各股蛛丝沿长度方向侧移和交错的幅度。")]
    [Range(0, 1)] public float trunkIrregularity = 0.85f;
    [InspectorName("树干扭转幅度（度）"), Tooltip("每棵树在此范围内随机扭转，沿高度平滑变化。")]
    [Range(0, 120)] public float trunkTwist = 65;
    [InspectorName("树冠孔缘包边厚度")]
    [Tooltip("相对树冠半径的比例，0 关闭隆起包边。")]
    [Range(0, 0.06f)] public float crownRimHeight = 0.018f;

    [Header("网膜与孔洞")]
    [InspectorName("树干开孔程度")]
    [Tooltip("控制丝束疏密：越大，各股越分开、细膜越少，形成更通透的长缝。")]
    [Range(0, 1)] public float trunkOpenness = 0.94f;
    [InspectorName("树冠开孔程度")]
    [Tooltip("同时调整外壳与内部黏膜的开孔；越大越通透。")]
    [Range(0, 1)] public float crownOpenness = 0.92f;
    [InspectorName("丝束分流比例"), Tooltip("控制丝束局部分开后形成的长缝；保留原参数值。")]
    [Range(0, 1)] public float trunkPairedHoleChance = 0.72f;
    [InspectorName("斜向牵丝比例"), Tooltip("控制跨过相邻丝股的斜向连接比例，形成拉长的三角空隙。")]
    [Range(0, 1)] public float trunkTriangleHoleChance = 0.32f;
    [InspectorName("根部额外镂空程度"), Tooltip("提高后，根部丝股展开得更明显；不铺成连续裙边。")]
    [Range(0, 1)] public float rootOpenness = 0.9f;
    [HideInInspector, Range(0, 1)] public float tornWebAmount = 0.32f;
    [HideInInspector, Range(0, 1)] public float membraneCoverage = 0.2f;
    [HideInInspector, Range(0.02f, 0.4f)] public float crownWebThickness = 0.24f;
    [InspectorName("树冠内部黏膜层数"), Tooltip("树冠内部交错的破布状黏膜数量。")]
    [Range(0, 8)] public int interiorSheetCount = 3;
    [InspectorName("树干膜片厚度（米）"), Tooltip("树干各张网膜的实际厚度。")]
    [Range(0.003f, 0.04f)] public float trunkMembraneThickness = 0.014f;
    [InspectorName("球冠黏膜材质"), Tooltip("球冠外壳与内部破布状黏膜使用的材质。")]
    public Material silkMaterial;
    [InspectorName("银白蛛丝材质"), Tooltip("树干、根、分支与藤蔓使用的银白丝束材质。")]
    public Material strandMaterial;
    [InspectorName("半透明膜材质"), Tooltip("备用的半透明膜材质。")]
    public Material membraneMaterial;

    [Header("根部分叉与侧向支枝")]
    [InspectorName("根部分叉数量范围"), Tooltip("根部拆成独立的网膜根；支持 2 至 8 条，至少两条接地。")]
    public Vector2Int rootCountRange = new Vector2Int(3, 5);
    [InspectorName("根部展开距离范围（米）"), Tooltip("各条根从树干向外延伸的距离。")]
    public Vector2 rootSpreadRange = new Vector2(0.7f, 1.35f);
    [InspectorName("根部分叉高度范围（米）"), Tooltip("树干在离地多高的位置分成几条根；分叉之间保持开放。")]
    public Vector2 rootForkHeightRange = new Vector2(0.65f, 1.15f);
    [InspectorName("根梢上翘比例"), Tooltip("部分根梢向上弯曲而不接地；始终保留至少两条接地根。")]
    [Range(0, 1)] public float rootCurlChance = 0.3f;
    [InspectorName("回接丝束数量范围"), Tooltip("两端接回主干、中间向外拱开的侧向丝束数量。")]
    public Vector2Int sideBranchCountRange = new Vector2Int(2, 4);
    [InspectorName("回接丝束展开范围（米）"), Tooltip("控制侧向丝束向外拱开的幅度；实际最大偏移约为此值的一半。")]
    public Vector2 sideBranchLengthRange = new Vector2(0.75f, 1.7f);
    [InspectorName("回接丝束复股比例"), Tooltip("增加一股较细的平行回接丝束的概率，仍然两端连接主干。")]
    [Range(0, 1)] public float sideBranchForkChance = 0.45f;

    [Header("树间藤蔓与 Y 形连接")]
    [InspectorName("藤蔓连接密度"), Tooltip("邻近树之间尝试生成连接的概率。")]
    [Range(0, 1)] public float vineDensity = 0.48f;
    [InspectorName("藤蔓最大连接距离（米）"), Tooltip("超过此树间距离时，不生成连接。")]
    [Min(0)] public float maximumVineDistance = 11;
    [InspectorName("每树连接数量上限"), Tooltip("一棵树最多参与的连接组数；Y 形连接也计入。")]
    [Range(1, 4)] public int maximumVinesPerTree = 2;
    [InspectorName("藤蔓下垂深度范围（米）"), Tooltip("连接膜带随机下垂的深度。")]
    public Vector2 vineSagRange = new Vector2(0.8f, 2.6f);
    [InspectorName("藤蔓网膜展开宽度（米）"), Tooltip("控制膜带在连接处展开的宽度。")]
    [Range(0.05f, 2f)] public float vineWebWidth = 0.65f;
    [InspectorName("Y 字连接比例"), Tooltip("0 仅生成双树连接；越大越优先尝试三树共享的 Y 形连接。")]
    [Range(0, 1)] public float yJunctionChance = 0.55f;

    [SerializeField, HideInInspector] private Transform generatedRoot;
    [SerializeField, HideInInspector] private int generatedTreeCount;
    [SerializeField, HideInInspector] private int generatedVineCount;
    [SerializeField, HideInInspector] private int generatedCrownCount;
    [SerializeField, HideInInspector] private int generatedYJunctionCount;
    public int GeneratedTreeCount => generatedTreeCount;
    public int GeneratedVineCount => generatedVineCount;
    public int GeneratedCrownCount => generatedCrownCount;
    public int GeneratedYJunctionCount => generatedYJunctionCount;
    public Transform GeneratedRoot => generatedRoot;

    private struct Crown { public Vector3 centre; public float radius; public List<Vector3> sockets; }
    private sealed class Tree { public Vector3 foot; public float height; public int canopyCount; public readonly List<Crown> crowns = new List<Crown>(); }
    private System.Random random;
    private readonly List<Mesh> ownedMeshes = new List<Mesh>();
    private readonly List<Material> ownedMaterials = new List<Material>();
    private float R(float lo, float hi) => Mathf.Lerp(lo, hi, (float)random.NextDouble());
    private float R(Vector2 range) => R(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    private int Count(Vector2Int range, int minimum, int maximum)
    {
        int lo = Mathf.Clamp(Mathf.Min(range.x, range.y), minimum, maximum);
        return random.Next(lo, Mathf.Clamp(Mathf.Max(range.x, range.y), lo, maximum) + 1);
    }
    private void Start() { if (generateOnStart && generatedRoot == null) Regenerate(); }

    [ContextMenu("Generate forest / 生成树林")]
    public void Regenerate()
    {
        ClearGenerated();
        random = new System.Random(seed);
        var root = new GameObject("Silk forest · generated");
        root.transform.SetParent(transform, false);
        generatedRoot = root.transform;
        EnsureMaterials();
        var trees = new List<Tree>();
        float sx = Mathf.Clamp(Mathf.Abs(forestSize.x), 1, 500);
        float sz = Mathf.Clamp(Mathf.Abs(forestSize.y), 1, 500);
        int target = Mathf.Clamp(Mathf.RoundToInt(sx * sz * Mathf.Clamp(treeDensity, 0, 12) / 100), 0, Mathf.Clamp(maximumTrees, 1, 500));
        // Bounded rejection sampling; density never overrides the minimum spacing.
        for (int attempt = 0; trees.Count < target && attempt < target * 100; attempt++)
        {
            var foot = new Vector3(R(-sx / 2, sx / 2), 0, R(-sz / 2, sz / 2));
            if (Mathf.Abs(foot.x) < Mathf.Max(0, clearingWidth) * 0.5f) continue;
            bool crowded = false;
            foreach (var existing in trees)
                if (new Vector2(existing.foot.x - foot.x, existing.foot.z - foot.z).sqrMagnitude < Mathf.Pow(Mathf.Max(0.1f, minimumSpacing), 2)) { crowded = true; break; }
            if (crowded) continue;
            if (projectToGround)
            {
                Vector3 origin = transform.TransformPoint(foot) + Vector3.up * Mathf.Max(1, groundProbeHeight);
                if (!Physics.Raycast(origin, Vector3.down, out var hit, Mathf.Max(1, groundProbeHeight) * 2, groundLayers, QueryTriggerInteraction.Ignore)) continue;
                foot = transform.InverseTransformPoint(hit.point);
            }
            var tree = new Tree { foot = foot, height = Mathf.Clamp(R(heightRange), 2, 60) };
            BuildTree(tree, trees.Count);
            trees.Add(tree);
        }
        generatedTreeCount = trees.Count;
        BuildVines(trees);
    }

    private void EnsureMaterials()
    {
#if UNITY_EDITOR
        if (strandMaterial == null)
            strandMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/SilkForest/MAT_TensionedSilk.mat");
#endif
        if (strandMaterial == null)
        {
            strandMaterial = new Material(Shader.Find("Capstone/Silk Forest/Pearlescent Silk")) { name = "Tensioned silver silk" };
            strandMaterial.SetColor("_BaseColor", new Color(0.88f,0.93f,1));
            strandMaterial.SetFloat("_Iridescence",0.12f);
            ownedMaterials.Add(strandMaterial);
        }
        if (silkMaterial == null)
        {
            var shader = Shader.Find("Capstone/Silk Forest/Pearlescent Silk");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            silkMaterial = new Material(shader) { name = "Silk forest · transient silk" };
            ownedMaterials.Add(silkMaterial);
        }
        if (membraneMaterial == null)
        {
            var shader = Shader.Find("Capstone/Silk Forest/Thin Film");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            membraneMaterial = new Material(shader) { name = "Silk forest · transient membrane" };
            ownedMaterials.Add(membraneMaterial);
        }
    }

    private Vector3 Spine(Tree tree, float t)
    {
        return tree.foot + new Vector3(Mathf.Sin(t * 3.1f) * tree.height * 0.022f, tree.height * t, Mathf.Sin(t * 4.3f) * tree.height * 0.012f);
    }

    private void BuildTree(Tree tree, int index)
    {
        var silk = new SilkForestGeometry.MeshBuffer();
        var film = new SilkForestGeometry.MeshBuffer();
        var strands = new SilkForestGeometry.MeshBuffer();
        float trunkRadius = Mathf.Clamp(R(trunkRadiusRange), 0.02f, 0.4f);
        float mainRadius = Mathf.Clamp(R(crownRadiusRange), 0.2f, 5);
        Vector3 mainCentre = Spine(tree, 1) + Vector3.up * mainRadius * 0.55f;
        Vector3 contactDirection = (Spine(tree, 0.75f) - mainCentre).normalized;
        Vector3 contact = SilkForestGeometry.CrownPoint(mainCentre, mainRadius, contactDirection, crownAsymmetry)-contactDirection*mainRadius*0.1f;
        float stemEndT = Mathf.Clamp01((contact.y-tree.foot.y)/tree.height);
        Vector3 correction = contact - Spine(tree, stemEndT);
        Vector3 RawStem(float t) => Spine(tree, t*stemEndT) + correction*Mathf.Pow(t,4);
        float rootHeight=Mathf.Clamp(R(rootForkHeightRange),0.2f,tree.height*0.22f);
        float baseT=Mathf.Clamp(rootHeight/Mathf.Max(1,contact.y-tree.foot.y),0.025f,0.24f);
        Vector3 Stem(float t)=>RawStem(Mathf.Lerp(baseT,1,t));
        var roots=SilkForestGeometry.RootLayout(tree.foot,Stem(0),Count(rootCountRange,2,8),rootSpreadRange,rootCurlChance,random);
        if(projectToGround)
            for(int i=0;i<roots.Length;i++)
                if(roots[i].grounded&&Physics.Raycast(transform.TransformPoint(roots[i].end)+Vector3.up*groundProbeHeight,Vector3.down,out var rootHit,groundProbeHeight*2,groundLayers,QueryTriggerInteraction.Ignore))
                    roots[i].end=transform.InverseTransformPoint(rootHit.point);
        SilkForestGeometry.ForkedRoots(strands,roots,trunkRadius,trunkMembraneThickness,trunkOpenness<=0?0:Mathf.Lerp(trunkOpenness,1,rootOpenness),
            trunkIrregularity,trunkTwist,trunkPairedHoleChance,trunkTriangleHoleChance,random);
        SilkForestGeometry.SilkBundle(strands,Stem,trunkRadius,trunkRadius*1.05f,mainRadius*0.23f,
            trunkMembraneThickness,tornWebAmount,random,trunkOpenness,trunkIrregularity,trunkTwist,trunkPairedHoleChance,trunkTriangleHoleChance,rootOpenness,9,false,true);
        int count = Count(crownCountRange, 1, 5);
        tree.canopyCount=count;
        for (int c = 0; c < count; c++)
        {
            float radius = c == 0 ? mainRadius : Mathf.Clamp(R(crownRadiusRange), 0.2f, 5) * R(0.55f, 0.88f);
            float a = c * 2.4f + R(-0.35f, 0.35f);
            Vector3 centre = c == 0 ? mainCentre : Spine(tree,1) + new Vector3(Mathf.Cos(a)*mainRadius*1.02f,R(-0.55f,0.5f)*mainRadius,Mathf.Sin(a)*mainRadius*1.02f);
            Vector3 branchStart=Stem(c==0?0.86f:R(0.74f,0.91f));
            Vector3 attachDirection=c==0?contactDirection:(branchStart-centre).normalized;
            AddCrown(silk,film,tree,centre,radius,attachDirection);
            if(c>0)AddBranch(strands,branchStart,centre,radius,attachDirection,trunkRadius);
        }
        int buds = Count(trunkCrownCountRange, 0, 6);
        for (int b = 0; b < buds; b++)
        {
            float t = R(0.28f, 0.74f), radius = Mathf.Clamp(R(trunkCrownRadiusRange), 0.12f, 2);
            float a = R(0, Mathf.PI * 2);
            Vector3 centre = Spine(tree, t) + new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * radius * 0.9f;
            Vector3 branchStart=Stem(Mathf.Clamp01(t-0.1f));
            Vector3 attachDirection=(branchStart-centre).normalized;
            AddCrown(silk,film,tree,centre,radius,attachDirection);
            AddBranch(strands,branchStart,centre,radius,attachDirection,trunkRadius*0.55f);
        }
        int loops=Count(sideBranchCountRange,0,8);
        for(int branch=0;branch<loops;branch++)
        {
            float low=R(0.13f,0.65f),angle=R(0,Mathf.PI*2),reach=Mathf.Clamp(R(sideBranchLengthRange),0.2f,4)*0.48f;
            float high=Mathf.Min(0.94f,low+R(0.15f,0.32f));
            Vector3 outward=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            AddReturningBranch(strands,Stem(low),Stem(high),outward,reach,trunkRadius);
            if(R(0,1)<sideBranchForkChance)
                AddReturningBranch(strands,Stem(Mathf.Lerp(low,high,0.15f)),Stem(Mathf.Lerp(low,high,0.85f)),
                    Quaternion.AngleAxis(R(-25,25),Vector3.up)*outward,reach*0.65f,trunkRadius*0.55f);
        }
        var holder = new GameObject($"Silk tree {index + 1:00} · {count} crowns + {buds} buds");
        holder.transform.SetParent(generatedRoot, false);
        MakeMesh(holder.transform, "Crown membranes", silk, silkMaterial);
        MakeMesh(holder.transform, "Tensioned silk strands", strands, strandMaterial);
        MakeMesh(holder.transform, "Sticky thin films", film, membraneMaterial);
        if (trunkColliders)
        {
            var collider = holder.AddComponent<CapsuleCollider>();
            collider.center = tree.foot + Vector3.up * tree.height * 0.5f;
            collider.height = tree.height;
            collider.radius = trunkRadius * 1.5f;
        }
    }

    private void AddReturningBranch(SilkForestGeometry.MeshBuffer silk,Vector3 start,Vector3 end,Vector3 outward,float reach,float radius)
    {
        SilkForestGeometry.SilkBundle(silk,t=>SilkForestGeometry.ReturningStrandPoint(start,end,outward,reach,t),
            radius*0.3f,0.004f,0.004f,trunkMembraneThickness*0.6f,tornWebAmount,random,trunkOpenness,
            trunkIrregularity,trunkTwist*0.3f,trunkPairedHoleChance,trunkTriangleHoleChance,0,6);
    }

    private void AddBranch(SilkForestGeometry.MeshBuffer silk, Vector3 start, Vector3 centre, float radius, Vector3 direction, float width)
    {
        Vector3 contact=SilkForestGeometry.CrownPoint(centre,radius,direction,crownAsymmetry)-direction*radius*0.1f;
        Vector3 Branch(float t)=>Vector3.Lerp(start,contact,t)-Vector3.up*Mathf.Sin(t*Mathf.PI)*0.08f;
        SilkForestGeometry.SilkBundle(silk,Branch,Mathf.Max(0.028f,width*0.45f),width*1.3f,radius*0.22f,
            trunkMembraneThickness*0.7f,tornWebAmount,random,trunkOpenness,trunkIrregularity,trunkTwist,trunkPairedHoleChance,trunkTriangleHoleChance,rootOpenness);
    }

    private void AddCrown(SilkForestGeometry.MeshBuffer silk, SilkForestGeometry.MeshBuffer film, Tree tree, Vector3 centre, float radius, Vector3 attachmentDirection)
    {
        var sockets=SilkForestGeometry.Crown(silk, film, centre, radius, crownAsymmetry, crownWebThickness,
            tornWebAmount, membraneCoverage, random, attachmentDirection, interiorSheetCount,crownOpenness,crownRimHeight);
        tree.crowns.Add(new Crown { centre=centre,radius=radius,sockets=sockets });
        generatedCrownCount++;
    }

    private Vector3 VineSocket(Crown crown,Vector3 target)
    {
        Vector3 direction=(target-crown.centre).normalized;
        Vector3 best=crown.centre+direction*crown.radius;float score=-2;
        foreach(var socket in crown.sockets)
        {
            float value=Vector3.Dot((socket-crown.centre).normalized,direction);
            if(value>score){score=value;best=socket;}
        }
        return best-(best-crown.centre).normalized*crown.radius*0.22f;
    }

    private void BuildVines(List<Tree> trees)
    {
        // Connection sampling should not depend on the number of random draws
        // used for roots, twigs, or other mesh details.
        random = new System.Random(unchecked(seed + 104729));
        var degrees=new int[trees.Count];
        var pairs=new HashSet<long>();
        int cap=Mathf.Clamp(maximumVinesPerTree,1,4);
        long Key(int a,int b)=>((long)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
        bool Near(int a,int b)=>Vector3.Distance(trees[a].foot,trees[b].foot)<=maximumVineDistance;
        Crown Canopy(int t)=>trees[t].crowns[random.Next(trees[t].canopyCount)];
        var holder=new GameObject("Canopy silk membranes and shared Y junctions");
        holder.transform.SetParent(generatedRoot,false);
        // A Y is one shared three-arm connection, with a single physical junction.
        for(int a=0;a<trees.Count;a++)
        {
            if(degrees[a]>=cap||R(0,1)>vineDensity||R(0,1)>yJunctionChance)continue;
            var neighbours=new List<int>();
            for(int n=0;n<trees.Count;n++)if(n!=a&&degrees[n]<cap&&Near(a,n)&&!pairs.Contains(Key(a,n)))neighbours.Add(n);
            neighbours.Sort((x,y)=>(trees[x].foot-trees[a].foot).sqrMagnitude.CompareTo((trees[y].foot-trees[a].foot).sqrMagnitude));
            int b=-1,c=-1;
            for(int i=0;i<neighbours.Count&&b<0;i++)for(int j=i+1;j<neighbours.Count;j++)
            {
                int x=neighbours[i],y=neighbours[j];
                if(!Near(x,y)||pairs.Contains(Key(x,y)))continue;
                var ab=trees[x].foot-trees[a].foot;var ac=trees[y].foot-trees[a].foot;
                if(Vector3.Cross(ab,ac).magnitude<minimumSpacing*minimumSpacing*0.45f)continue;
                b=x;c=y;break;
            }
            if(b<0)continue;
            Crown ca=Canopy(a),cb=Canopy(b),cc=Canopy(c);
            Vector3 middle=(ca.centre+cb.centre+cc.centre)/3;
            float ground=Mathf.Max(trees[a].foot.y,Mathf.Max(trees[b].foot.y,trees[c].foot.y));
            middle.y=Mathf.Max(ground+1.5f,Mathf.Min(ca.centre.y,Mathf.Min(cb.centre.y,cc.centre.y))-Mathf.Max(0,R(vineSagRange))*0.5f);
            var mesh=new SilkForestGeometry.MeshBuffer();
            foreach(Crown crown in new[]{ca,cb,cc})
            {
                Vector3 socket=VineSocket(crown,middle);
                SilkForestGeometry.WebBridge(mesh,socket,middle,Mathf.Min(Mathf.Max(0.05f,vineWebWidth),crown.radius*0.4f),0.25f,random,true);
            }
            MakeMesh(holder.transform,$"Y junction {generatedYJunctionCount+1:00} - trees {a+1}, {b+1}, {c+1}",mesh,strandMaterial);
            degrees[a]++;degrees[b]++;degrees[c]++;
            pairs.Add(Key(a,b));pairs.Add(Key(a,c));pairs.Add(Key(b,c));
            generatedYJunctionCount++;generatedVineCount++;
        }
        var silk=new SilkForestGeometry.MeshBuffer();
        for(int a=0;a<trees.Count;a++)for(int b=a+1;b<trees.Count;b++)
        {
            if(degrees[a]>=cap||degrees[b]>=cap||!Near(a,b)||pairs.Contains(Key(a,b))||R(0,1)>vineDensity)continue;
            Crown ca=Canopy(a),cb=Canopy(b);
            Vector3 start=VineSocket(ca,cb.centre),end=VineSocket(cb,ca.centre);
            float distance=Vector3.Distance(start,end);if(distance<0.7f)continue;
            float sag=Mathf.Min(Mathf.Max(0,R(vineSagRange)),distance*0.3f);
            sag=Mathf.Min(sag,Mathf.Max(0,Mathf.Min(start.y-trees[a].foot.y,end.y-trees[b].foot.y)-1));
            float width=Mathf.Min(Mathf.Max(0.05f,vineWebWidth),Mathf.Min(ca.radius,cb.radius)*0.4f);
            SilkForestGeometry.WebBridge(silk,start,end,width,sag,random);
            degrees[a]++;degrees[b]++;generatedVineCount++;
        }
        MakeMesh(holder.transform,"Paired tensioned silk bundles",silk,strandMaterial);
    }

    private void MakeMesh(Transform parent, string label, SilkForestGeometry.MeshBuffer data, Material material)
    {
        if (data.vertices.Count == 0) return;
        var mesh = data.CreateMesh(label);
        ownedMeshes.Add(mesh);
        var obj = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = obj.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = material == membraneMaterial ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided;
        renderer.receiveShadows = true;
    }

    [ContextMenu("Clear generated forest / 清除生成的树林")]
    public void ClearGenerated()
    {
        var meshes = new HashSet<Mesh>(ownedMeshes);
        if (generatedRoot != null)
        {
#if UNITY_EDITOR
            // Recover transient previews after a domain reload. In a player build
            // only ownedMeshes may be released; baked prefab meshes can be shared
            // by other forest instances.
            foreach (var filter in generatedRoot.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && !UnityEditor.EditorUtility.IsPersistent(filter.sharedMesh)) meshes.Add(filter.sharedMesh);
#endif
            generatedRoot.gameObject.SetActive(false);
            Release(generatedRoot.gameObject);
        }
        generatedRoot = null;
        foreach (var mesh in meshes) if (mesh != null) Release(mesh);
        ownedMeshes.Clear();
        generatedTreeCount = generatedVineCount = generatedCrownCount = generatedYJunctionCount = 0;
    }

    private static void Release(UnityEngine.Object obj)
    {
#if UNITY_EDITOR
        // Baked meshes/materials belong to the asset database, not this instance.
        if (UnityEditor.EditorUtility.IsPersistent(obj)) return;
#endif
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }
    private void OnDestroy()
    {
        foreach (var mesh in ownedMeshes) if (mesh != null) Release(mesh);
        foreach (var mat in ownedMaterials) if (mat != null) Release(mat);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var old = Gizmos.matrix; Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.4f, 0.95f, 0.8f, 0.65f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(Mathf.Abs(forestSize.x), 0.05f, Mathf.Abs(forestSize.y)));
        Gizmos.color = new Color(0.9f, 0.8f, 0.5f, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(clearingWidth, 0.1f, Mathf.Abs(forestSize.y)));
        Gizmos.matrix = old;
    }
#endif
}
