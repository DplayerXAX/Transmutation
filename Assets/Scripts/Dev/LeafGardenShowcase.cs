using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Repeatable Leaf Garden demonstration. All controls are development-only.</summary>
public sealed class LeafGardenShowcase : MonoBehaviour
{
    public LeafGarden Garden;
    public LeafGardenPresentation Leaves;
    public LeafTornadoLaunchVolume Launch;
    public Camera Observer;
    public SmoothFirstPersonController Player;
    public Rigidbody[] DemoBodies;
    public bool ShowControls = true;
    public bool Landscape;
    public LeafGarden[] Gardens;
    public Vector3 PlayerSpawn=new Vector3(0,39,-148);
    public int View { get; private set; } = 2;
    private Vector3[] homes;
    private Quaternion[] rotations;
    private Camera[] playerCameras;
    private float firstEvent = 5;
    private float averageFrame;
    private GUIStyle titleStyle, bodyStyle;
    private bool previousBackground;
    private GameObject crosshair;
    private GameObject avatar;
    private Renderer[] playerRenderers;
    private void Start()
    {
        previousBackground=Application.runInBackground; Application.runInBackground = true;
        crosshair=GameObject.Find("Crosshair HUD");
        avatar=GameObject.Find("First Person Body");
        playerRenderers=Player!=null ? Player.transform.root.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        playerCameras=Player!=null ? Player.transform.root.GetComponentsInChildren<Camera>(true) : new Camera[0];
        homes=new Vector3[DemoBodies.Length]; rotations=new Quaternion[DemoBodies.Length];
        for(int i=0;i<DemoBodies.Length;i++) { homes[i]=DemoBodies[i].position; rotations[i]=DemoBodies[i].rotation; }
        if(Landscape) { firstEvent=-1; SetView(0); StartCoroutine(OrientPlayer()); }
        else SetView(2);
    }
    private System.Collections.IEnumerator OrientPlayer()
    { yield return null; if(Player!=null) Player.SetLook(0,12); }
    public void SetView(int view)
    {
        View=Mathf.Clamp(view,0,2);
        bool walk=View==0;
        if(Player!=null)
        {
            Player.enabled=walk;
            if(Player.TryGetComponent<Rigidbody>(out var body)) { body.isKinematic=!walk; if(walk) body.WakeUp(); }
        }
        foreach(var camera in playerCameras) camera.enabled=walk;
        Observer.enabled=!walk;
        if(View==1) { Observer.transform.position=Landscape?new Vector3(6,39.5f,-108):new Vector3(5,2.4f,-22); Observer.transform.LookAt(Landscape?new Vector3(8,9,35):new Vector3(0,5,0)); Observer.fieldOfView=65; }
        if(View==2) { Observer.transform.position=Landscape?new Vector3(155,112,-200):new Vector3(85,46,-104); Observer.transform.LookAt(Landscape?new Vector3(0,10,10):new Vector3(0,30,0)); Observer.fieldOfView=62; }
        Leaves.ViewCamera=walk ? FindPlayerCamera() : Observer;
        if(Landscape && Gardens!=null) foreach(var patch in Gardens) patch.GetComponent<LeafGardenPresentation>().ViewCamera=Leaves.ViewCamera;
        if(crosshair!=null) crosshair.SetActive(walk);
        if(avatar!=null) avatar.SetActive(walk);
        if(playerRenderers!=null) foreach(var renderer in playerRenderers) renderer.enabled=walk;
        if(Observer.TryGetComponent<AudioListener>(out var listener)) listener.enabled=!walk;
        if(Player!=null) foreach(var playerListener in Player.transform.root.GetComponentsInChildren<AudioListener>(true)) playerListener.enabled=walk;
        Cursor.lockState=walk ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible=!walk;
    }
    private Camera FindPlayerCamera()
    { foreach(var camera in playerCameras) if(camera.enabled) return camera; return Observer; }
    public void Trigger() { firstEvent=-1; Garden.TriggerAt(Garden.transform.position); }
    public void SelectSize(int preset)
    {
        ResetShowcase(); Garden.SetPreset(preset); Leaves.Rebuild(Leaves.LeafCount); Trigger();
    }
    public void SetDensity(int count) { Leaves.Rebuild(count); }
    public void ResetShowcase()
    {
        Garden.ResetGarden(); Launch.Clear(); Leaves.Rebuild(Leaves.LeafCount); firstEvent=-1;
        if(Landscape && Gardens!=null) foreach(var patch in Gardens) if(patch!=Garden)
        { patch.ResetGarden(); patch.GetComponent<LeafTornadoLaunchVolume>().Clear(); var visual=patch.GetComponent<LeafGardenPresentation>(); visual.Rebuild(visual.LeafCount); }
        if(homes!=null) for(int i=0;i<DemoBodies.Length;i++)
        {
            if(DemoBodies[i]==null) continue;
            var body=DemoBodies[i]; body.position=homes[i]; body.rotation=rotations[i];
            body.linearVelocity=body.angularVelocity=Vector3.zero;
            if(body.TryGetComponent<LeafGardenWalker>(out var walker)) walker.ResetWalk();
        }
        if(Player!=null && Player.TryGetComponent<Rigidbody>(out var playerBody))
        { playerBody.position=Landscape?PlayerSpawn:new Vector3(0,1.3f,-18); if(!playerBody.isKinematic) playerBody.linearVelocity=Vector3.zero; }
    }
    public void MovePlayerToTornado()
    {
        SetView(0);
        var body=Player.GetComponent<Rigidbody>(); body.position=Garden.TornadoCenter+new Vector3(0,1.2f,0);
        body.linearVelocity=Vector3.zero;
    }
    private void Update()
    {
        averageFrame=Mathf.Lerp(averageFrame,Time.unscaledDeltaTime,0.04f);
        if(firstEvent>0) { firstEvent-=Time.deltaTime; if(firstEvent<=0) Trigger(); }
        // Recover demonstration bodies and the player if they leave the finite test floor.
        if(homes!=null) for(int i=0;i<DemoBodies.Length;i++)
            if(DemoBodies[i]!=null && DemoBodies[i].position.y < -12)
            { DemoBodies[i].position=homes[i]; DemoBodies[i].linearVelocity=Vector3.zero; }
        if(Player!=null && Player.transform.position.y < -12) ResetShowcase();
        var k=Keyboard.current; if(k==null) return;
        if(k.digit1Key.wasPressedThisFrame) SelectSize(0);
        if(k.digit2Key.wasPressedThisFrame) SelectSize(1);
        if(k.digit3Key.wasPressedThisFrame) SelectSize(2);
        if(k.gKey.wasPressedThisFrame) Trigger();
        if(k.rKey.wasPressedThisFrame) ResetShowcase();
        if(!Landscape)
        {
            if(k.tabKey.wasPressedThisFrame) SetView((View+1)%3);
            if(k.f1Key.wasPressedThisFrame) SetView(0);
            if(k.f2Key.wasPressedThisFrame) SetView(1);
            if(k.f3Key.wasPressedThisFrame) SetView(2);
            if(k.escapeKey.wasPressedThisFrame && View==0) SetView(2);
        }
        if(k.pKey.wasPressedThisFrame)
        {
            Garden.Automatic=!Garden.Automatic;
            if(Landscape && Gardens!=null) foreach(var patch in Gardens) patch.Automatic=Garden.Automatic;
        }
        if(k.hKey.wasPressedThisFrame) ShowControls=!ShowControls;
        if(k.lKey.wasPressedThisFrame) Leaves.DrawLeaves=!Leaves.DrawLeaves;
        if(k.digit4Key.wasPressedThisFrame) SetDensity(50000);
        if(k.digit5Key.wasPressedThisFrame) SetDensity(100000);
        if(k.digit6Key.wasPressedThisFrame) SetDensity(200000);
    }
    private void OnGUI()
    {
        if(!ShowControls) return;
        if(titleStyle==null)
        {
            titleStyle=new GUIStyle(GUI.skin.label) { fontSize=23, fontStyle=FontStyle.Bold, normal={textColor=new Color(1,0.83f,0.4f)} };
            bodyStyle=new GUIStyle(GUI.skin.label) { fontSize=13, normal={textColor=new Color(0.92f,0.92f,0.82f)} };
        }
        GUI.Box(new Rect(18,18,580,132),GUIContent.none);
        GUI.Label(new Rect(34,27,550,32),"LEAF GARDEN",titleStyle);
        GUI.Label(new Rect(34,62,550,24),$"{Garden.Phase}  /  {Garden.Height:0} m  /  {Leaves.Capacity:N0} leaves  /  {Launch.LaunchCount} launches  /  {averageFrame*1000:0.0} ms",bodyStyle);
        GUI.Label(new Rect(34,87,550,24),"1 / 2 / 3  tornado size     G  awaken     R  reset     P  automatic cycle",bodyStyle);
        GUI.Label(new Rect(34,111,550,24),(Landscape?"":"TAB  camera     ")+"4 / 5 / 6  density     L  leaves on/off     H  clean view",bodyStyle);
    }
    private void OnDestroy() { Application.runInBackground=previousBackground; Cursor.lockState=CursorLockMode.None; Cursor.visible=true; }
}
