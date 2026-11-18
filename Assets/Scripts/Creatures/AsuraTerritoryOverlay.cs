using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Optional surface coverage, visible in Game and Scene views during Play; no gameplay colliders.</summary>
[RequireComponent(typeof(AsuraTerritory))]
public sealed class AsuraTerritoryOverlay : MonoBehaviour
{
    [Tooltip("Off by default. Toggle in Play with F8, this checkbox, or the HUD button.")]
    public bool showCoverage;
    [Tooltip("Optional: display one linkage's actual connected patch instead of the combined territory.")]
    public AsuraLinkage focusLinkage;
    public Material overlayMaterial;
    private AsuraTerritory territory;
    private GameObject surfaceObject,canvasObject;
    private Mesh mesh;
    private Color[] colors;
    private byte[] ownership;
    private UnityEngine.UI.Text buttonText,legend;
    private float nextRefresh;
    private bool previous;
    private void Awake(){territory=GetComponent<AsuraTerritory>();BuildControls();}
    public void ToggleCoverage(){showCoverage=!showCoverage;nextRefresh=0;}
    private void Update()
    {
        if(Keyboard.current!=null && Keyboard.current.f8Key.wasPressedThisFrame)ToggleCoverage();
        if(buttonText!=null)buttonText.text="Territory: "+(showCoverage?"ON":"OFF")+"  [F8]";
        if(legend!=null)legend.gameObject.SetActive(showCoverage);
        if(!territory.Ready)return;
        if(showCoverage && mesh==null)BuildSurface();
        if(surfaceObject!=null)surfaceObject.SetActive(showCoverage);
        if(showCoverage && (!previous || Time.unscaledTime>=nextRefresh)){RefreshColors();nextRefresh=Time.unscaledTime+1;}
        previous=showCoverage;
    }
    private void BuildSurface()
    {
        var map=territory.surfaceMap;var vertices=new List<Vector3>();var triangles=new List<int>();
        ownership=new byte[map.surfaces.Length];colors=new Color[map.surfaces.Length*4];
        foreach(var s in map.surfaces)
        {
            Vector3 normal=Vector3.up;
            if(Physics.Raycast(s.position+Vector3.up*0.6f,Vector3.down,out var hit,1.2f,territory.groundLayers,QueryTriggerInteraction.Ignore))normal=hit.normal;
            var right=Vector3.Cross(normal,Vector3.forward).normalized;if(right.sqrMagnitude<0.1f)right=Vector3.right;
            var forward=Vector3.Cross(right,normal).normalized;float r=map.spacing*0.39f;
            var p=s.position+normal*0.2f;int first=vertices.Count;
            vertices.Add(p-right*r-forward*r);vertices.Add(p-right*r+forward*r);vertices.Add(p+right*r+forward*r);vertices.Add(p+right*r-forward*r);
            triangles.AddRange(new[]{first,first+1,first+2,first,first+2,first+3});
        }
        mesh=new Mesh{name="Asura surface coverage",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.MarkDynamic();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.colors=colors;mesh.RecalculateBounds();
        surfaceObject=new GameObject("Territory surface overlay",typeof(MeshFilter),typeof(MeshRenderer));surfaceObject.transform.SetParent(transform,false);
        // Vertices are expressed in world coordinates, like the baked surface graph.
        surfaceObject.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);surfaceObject.transform.localScale=Vector3.one;
        surfaceObject.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=surfaceObject.GetComponent<MeshRenderer>();renderer.sharedMaterial=overlayMaterial;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    private void RefreshColors()
    {
        System.Array.Clear(ownership,0,ownership.Length);
        foreach(var n in territory.Nodes)
        {
            if(n==null || !n.Connected || n.home==null || (focusLinkage!=null && n!=focusLinkage))continue;
            byte bit=(byte)(n.home.Polarity==CreaturePolarity.Light?1:2);foreach(int i in n.patch)ownership[i]|=bit;
        }
        int light=0,dark=0,shared=0,unclaimed=0;
        for(int i=0;i<ownership.Length;i++)
        {
            Color c;
            switch(ownership[i])
            {
                case 1:c=new Color(1,0.8f,0.12f,0.48f);light++;break;
                case 2:c=new Color(0.6f,0.22f,1,0.53f);dark++;break;
                case 3:c=new Color(1,0.95f,0.85f,0.62f);shared++;break;
                default:c=new Color(0.38f,0.53f,0.57f,0.16f);unclaimed++;break;
            }
            for(int j=0;j<4;j++)colors[i*4+j]=c;
        }
        mesh.colors=colors;
        legend.text=(focusLinkage!=null?"Selected linkage coverage\n":"Connected territory coverage\n")+"GOLD  Light     PURPLE  Dark\nIVORY  Overlap     GREY  Unclaimed\nSurface patches follow ground and vines.";
    }
    private void BuildControls()
    {
        canvasObject=new GameObject("Territory coverage controls",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvasObject.transform.SetParent(transform,false);
        canvasObject.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;canvasObject.GetComponent<Canvas>().sortingOrder=80;
        var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=0.5f;
        var panel=new GameObject("Coverage toggle",typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));panel.transform.SetParent(canvasObject.transform,false);
        var rect=panel.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,1);rect.anchoredPosition=new Vector2(-18,-18);rect.sizeDelta=new Vector2(270,40);
        panel.GetComponent<UnityEngine.UI.Image>().color=new Color(0.045f,0.06f,0.075f,0.92f);
        panel.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(ToggleCoverage);
        buttonText=Text(panel.transform,"Label",18);var labelRect=buttonText.rectTransform;labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;labelRect.offsetMin=labelRect.offsetMax=Vector2.zero;buttonText.alignment=TextAnchor.MiddleCenter;
        legend=Text(canvasObject.transform,"Coverage legend",16);var lr=legend.rectTransform;lr.anchorMin=lr.anchorMax=lr.pivot=new Vector2(1,1);lr.anchoredPosition=new Vector2(-18,-65);lr.sizeDelta=new Vector2(365,100);legend.alignment=TextAnchor.UpperRight;legend.gameObject.SetActive(false);
        if(FindFirstObjectByType<EventSystem>()==null)
        {var events=new GameObject("Territory UI events",typeof(EventSystem),typeof(InputSystemUIInputModule));events.transform.SetParent(canvasObject.transform);}
    }
    private static UnityEngine.UI.Text Text(Transform parent,string name,int size)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Text));go.transform.SetParent(parent,false);
        var text=go.GetComponent<UnityEngine.UI.Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=size;text.color=Color.white;text.raycastTarget=false;return text;
    }
    private void OnDisable(){if(surfaceObject!=null)surfaceObject.SetActive(false);if(canvasObject!=null)canvasObject.SetActive(false);}
    private void OnEnable(){if(canvasObject!=null)canvasObject.SetActive(true);}
    private void OnDestroy(){if(mesh!=null)Destroy(mesh);if(surfaceObject!=null)Destroy(surfaceObject);if(canvasObject!=null)Destroy(canvasObject);}
}
