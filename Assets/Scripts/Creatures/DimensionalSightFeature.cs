using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public sealed class DimensionalSightFeature : ScriptableRendererFeature
{
    public Material fishMaterial;
    SightPass pass;
    public override void Create() { pass = new SightPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents }; }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        var camera = data.cameraData.camera;
        var school = DimensionalSchool.Existing(camera);
        if (!fishMaterial || !school || !school.Player || school.Player.CarryCamera != camera ||
            !school.Player.PlayerController.enabled) return;
        pass.school = school; pass.material = fishMaterial;
        renderer.EnqueuePass(pass);
    }
    sealed class SightPass : ScriptableRenderPass
    {
        public SightPass() { requiresIntermediateTexture = true; }
        internal DimensionalSchool school;
        internal Material material;
        static readonly int DepthId = Shader.PropertyToID("_AngelWindowDepth");
        readonly Plane[] planes = new Plane[6];
        readonly List<DimensionStone> windows = new();
        readonly List<SeaAngelCreature> fish = new();
        readonly HashSet<SeaAngelCreature> honeyFish = new();
        readonly List<SeaAngelCreature> physicalFish = new();
        class DrawData { public List<DimensionStone> windows; public List<SeaAngelCreature> fish; public Material mat; public HashSet<SeaAngelCreature> honeyFish; }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var camera = frame.Get<UniversalCameraData>().camera;
            var resources = frame.Get<UniversalResourceData>();
            var player = school.Player;
            bool honey = player.TryGetComponent<PlayerHoneyEffects>(out var effect) && effect.HasHoneySight;
            Vector3 origin = player.transform.position;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            windows.Clear();
            foreach (var stone in school.Stones)
                if (!stone.IsDead && Vector3.Distance(origin,stone.transform.position)<=stone.playerRange &&
                    GeometryUtility.TestPlanesAABB(planes,stone.GetComponent<Renderer>().bounds)) windows.Add(stone);
            if (!honey && windows.Count==0) return;
            physicalFish.Clear();
            if (windows.Count > 0)
                foreach (var angel in school.PhysicalFish)
                    if (GeometryUtility.TestPlanesAABB(planes,new Bounds(angel.transform.position,Vector3.one*angel.BodyLength*2)))
                        physicalFish.Add(angel);
            fish.Clear(); honeyFish.Clear();
            foreach (var angel in school.AwakeFish)
            {
                var pos = angel.transform.position;
                if (!GeometryUtility.TestPlanesAABB(planes,new Bounds(pos,Vector3.one*angel.BodyLength*2))) continue;
                bool visible = honey && Vector3.Distance(origin,pos)<=effect.FishSightRange;
                if (visible) honeyFish.Add(angel);
                else
                    foreach (var stone in windows)
                        if (Vector3.Distance(pos,stone.transform.position)<=stone.fishRange) { visible=true; break; }
                if (visible) fish.Add(angel);
            }
            Vector3 eye = camera.transform.position;
            fish.Sort((a,b)=>(b.transform.position-eye).sqrMagnitude.CompareTo((a.transform.position-eye).sqrMagnitude));
            TextureHandle depth = default;
            bool needsMask = windows.Count > 0;
            if (needsMask)
            {
                var desc = graph.GetTextureDesc(resources.activeColorTexture);
                desc.name="Dimension window entry depth"; desc.format=GraphicsFormat.R32_SFloat;
                desc.clearBuffer=true; desc.clearColor=Color.white*65504; desc.filterMode=FilterMode.Point;
                depth=graph.CreateTexture(desc);
                using var mask=graph.AddRasterRenderPass<DrawData>("Dimension window entries",out var d);
                d.windows=windows;
                mask.SetRenderAttachment(depth,0);
                mask.SetRenderAttachmentDepth(resources.activeDepthTexture,AccessFlags.ReadWrite);
                mask.SetGlobalTextureAfterPass(depth,DepthId);
                mask.SetRenderFunc((DrawData data,RasterGraphContext ctx)=>{
                    foreach(var stone in data.windows)
                        ctx.cmd.DrawMesh(stone.Window.sharedMesh,stone.transform.localToWorldMatrix,stone.GetComponent<Renderer>().sharedMaterial,0,1);
                });
            }
            using var draw=graph.AddRasterRenderPass<DrawData>("Dimensional sea angels",out var draws);
            draws.fish=fish; draws.mat=material; draws.honeyFish=honeyFish; draws.windows=windows;
            if (needsMask) draw.UseTexture(depth);
            draw.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.ReadWrite);
            draw.SetRenderAttachmentDepth(resources.activeDepthTexture,AccessFlags.Read);
            draw.SetRenderFunc((DrawData data,RasterGraphContext ctx)=>{
                if (data.windows.Count > 0)
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.windows[0].GetComponent<Renderer>().sharedMaterial,
                        2, MeshTopology.Triangles, 3);
                foreach(var angel in data.fish)
                    for(int i=0;i<angel.Parts.Length;i++)
                    {
                        var part=angel.Parts[i];
                        ctx.cmd.DrawMesh(part.sharedMesh,part.transform.localToWorldMatrix,data.mat,0,data.honeyFish.Contains(angel)?1:0,angel.Colors[i]);
                    }
                foreach (var angel in physicalFish)
                    for (int i=0; i<angel.Parts.Length; i++)
                    {
                        var part=angel.Parts[i];
                        ctx.cmd.DrawMesh(part.sharedMesh,part.transform.localToWorldMatrix,data.mat,0,2,angel.Colors[i]);
                    }
            });
        }
    }
}
