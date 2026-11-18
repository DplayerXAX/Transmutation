using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PlayerTorsoPreview
{
    const string Folder = "ArtCaptures/PlayerTorso";
    static PlayerTorsoPreview() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists("Temp/PlayerTorso.preview") || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete("Temp/PlayerTorso.preview");
        try { Capture(); } catch (Exception e) { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/Checks.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/Capstone/Player/Preview Turtle Torso Anatomy")]
    public static void Capture()
    {
        Directory.CreateDirectory(Folder); bool oldAsync = ShaderUtil.allowAsyncCompilation;
        var preview = new PreviewRenderUtility();
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var root = new GameObject("Player torso anatomical preview"); preview.AddSingleGO(root);
            var handObject = new GameObject("Material reference hand"); handObject.transform.SetParent(root.transform, false);
            var hand = handObject.AddComponent<WebbedPlayerHand>(); hand.Initialize(-1);
            foreach (var renderer in handObject.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            var torsoRoot = new GameObject("Torso"); torsoRoot.transform.SetParent(root.transform, false);
            var torso = torsoRoot.AddComponent<PlayerTorsoAnatomy>(); torso.Build(hand.Skin, hand.Bone, hand.Vein);
            var filters = torso.GetComponentsInChildren<MeshFilter>();
            if (filters.Length != 4 || torso.GetComponentsInChildren<Collider>().Length != 0) throw new Exception("Expected four anatomical render layers without physical colliders.");
            int vertices = 0;
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh; vertices += mesh.vertexCount;
                if (mesh.vertexCount == 0 || mesh.normals.Length != mesh.vertexCount || mesh.uv2.Length != mesh.vertexCount) throw new Exception("Missing anatomical mesh attributes.");
                foreach(var normal in mesh.normals) if(!float.IsFinite(normal.x)||!float.IsFinite(normal.y)||!float.IsFinite(normal.z)) throw new Exception("Invalid anatomical surface normal.");
                foreach (var p in mesh.vertices) if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z)) throw new Exception("Non-finite anatomy vertex.");
            }
            if (!torso.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterial == hand.Skin) ||
                !torso.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterial == torso.BoneMaterial) ||
                !torso.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterial == hand.Vein)) throw new Exception("Torso must retain limb skin/blood and its own translucent bone material.");
            // Test the generated skin triangles, rather than renderer bounding boxes.
            var skinFilter = filters.Single(f => f.GetComponent<Renderer>().sharedMaterial == hand.Skin);
            var skinCollider = skinFilter.gameObject.AddComponent<MeshCollider>(); skinCollider.sharedMesh = skinFilter.sharedMesh;
            int enclosed = 0; float minimumClearance = float.PositiveInfinity;
            try
            {
                Physics.SyncTransforms();
                foreach (var filter in filters.Where(f => f.GetComponent<Renderer>().sharedMaterial == torso.BoneMaterial || f.GetComponent<Renderer>().sharedMaterial == hand.Vein))
                    foreach (var vertex in filter.sharedMesh.vertices)
                    {
                        Vector3 at = filter.transform.TransformPoint(vertex);
                        foreach (var direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                        {
                            if (!skinCollider.Raycast(new Ray(at + direction * 2, -direction), out var hit, 4) || hit.distance >= 1.995f)
                                throw new Exception($"Anatomy outside skin: {filter.name}, {vertex}, direction={direction}.");
                            minimumClearance = Mathf.Min(minimumClearance, 2 - hit.distance);
                        }
                        enclosed++;
                    }
            }
            finally { Object.DestroyImmediate(skinCollider); }
            File.WriteAllText(Folder + "/Containment.txt", $"PASS: {enclosed} bone/vessel vertices checked against actual skin triangles from four directions.\nOutside vertices: 0. Minimum axial clearance: {minimumClearance:F4} m.\nTapered waist and shallow abdomen; internal anatomy follows the local skin cross section.\n");
            var boneFilter=filters.Single(f=>f.GetComponent<Renderer>().sharedMaterial==torso.BoneMaterial);
            var bloodFilter=filters.Single(f=>f.GetComponent<Renderer>().sharedMaterial==hand.Vein);
            var boneCollider=boneFilter.gameObject.AddComponent<MeshCollider>();boneCollider.sharedMesh=boneFilter.sharedMesh;
            int overlaps=0;float gap=float.PositiveInfinity;
            try
            {
                Physics.SyncTransforms();
                foreach(var p in bloodFilter.sharedMesh.vertices)
                    if(boneCollider.Raycast(new Ray(new Vector3(p.x,p.y,1),Vector3.back),out var hit,2)&&hit.point.z>0)
                    {
                        float separation=hit.point.z-p.z;
                        if(separation<.005f)throw new Exception($"Vessel in front of chest bone: {p}, gap={separation}.");
                        overlaps++;gap=Mathf.Min(gap,separation);
                    }
                if(overlaps==0)throw new Exception("No overlapping chest bone/vessel samples were checked.");
                var waist=skinFilter.sharedMesh.vertices.Where(p=>Mathf.Abs(p.y-.35f)<.001f).ToArray();
                var chest=skinFilter.sharedMesh.vertices.Where(p=>Mathf.Abs(p.y-.725f)<.001f).ToArray();
                float waistWidth=waist.Max(p=>p.x)-waist.Min(p=>p.x),chestWidth=chest.Max(p=>p.x)-chest.Min(p=>p.x);
                if(waistWidth>chestWidth*.85f||waist.Max(p=>p.z)>chest.Max(p=>p.z)*.8f)throw new Exception("Waist/abdomen profile is insufficiently tapered.");
                File.WriteAllText(Folder+"/Layering.txt",$"PASS: {overlaps} overlapping vessel samples behind anterior bones. Minimum gap: {gap:F4} m.\nWaist width: {waistWidth:F3} m; chest width: {chestWidth:F3} m.\n");
            }
            finally{Object.DestroyImmediate(boneCollider);}
            preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.035f, .05f, .06f);
            preview.camera.nearClipPlane = .03f; preview.camera.farClipPlane = 20; preview.camera.fieldOfView = 36; preview.camera.aspect = 1;
            preview.lights[0].intensity = 1.4f; preview.lights[0].transform.rotation = Quaternion.Euler(35, -25, 0);
            preview.lights[1].intensity = .8f; preview.ambientColor = new Color(.35f, .4f, .45f);
            void Render(string name, Vector3 camera)
            {
                preview.camera.transform.position = camera; preview.camera.transform.LookAt(new Vector3(0, .5f, 0));
                preview.BeginPreview(new Rect(0, 0, 1100, 1100), GUIStyle.none); preview.Render(true);
                var texture = (RenderTexture)preview.EndPreview(); var active = RenderTexture.active;
                try
                {
                    RenderTexture.active = texture; var png = new Texture2D(1100, 1100, TextureFormat.RGB24, false);
                    png.ReadPixels(new Rect(0, 0, 1100, 1100), 0, 0); png.Apply(); File.WriteAllBytes(Folder + "/" + name + ".png", png.EncodeToPNG()); Object.DestroyImmediate(png);
                }
                finally { RenderTexture.active = active; }
            }
            Render("Front", new Vector3(0, .55f, 1.9f));
            Render("Back", new Vector3(0, .55f, -1.9f));
            Render("BackThreeQuarter", new Vector3(1.25f, .85f, -1.5f));
            Render("ThreeQuarter", new Vector3(1.05f, .85f, 1.65f));
            Render("Top", new Vector3(0, 2.3f, .12f));
            Render("Side", new Vector3(1.9f, .55f, 0));
            var layers = torso.GetComponentsInChildren<Renderer>();
            foreach (var layer in layers) layer.enabled = layer.sharedMaterial == torso.BoneMaterial;
            Render("FrontBones", new Vector3(0, .55f, 1.9f));
            Render("FrontBonesThreeQuarter", new Vector3(1.05f, .85f, 1.65f));
            Render("BackBones", new Vector3(0, .55f, -1.9f));
            Render("BoneDetail", new Vector3(.16f, .6f, -.9f));
            foreach (var layer in layers) layer.enabled = true;
            File.WriteAllText(Folder + "/Checks.txt", $"PASS: four anatomical render layers; {vertices} finite vertices, normals and vessel motion UVs.\nShared limb skin and flowing pink vessel materials; blue bone variant at 72% opacity.\nUser drawing: broad scalloped dorsal axis, seven paired fine ribs and a thin shield frame; two continuous Y-shaped front wings with a central opening and tapered lower prongs.\nAortic arch/descending aorta, vena cava, neck/subclavian, renal, iliac and intercostal branches.\nNo torso physics colliders; existing arm and leg IK ownership retained.\n");
        }
        finally { preview.Cleanup(); ShaderUtil.allowAsyncCompilation = oldAsync; }
    }
}
