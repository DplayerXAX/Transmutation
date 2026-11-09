using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Authored demonstrations only. Each reset recreates the chosen finite encounter.</summary>
public sealed class AsuraShowcase : MonoBehaviour
{
    public GameObject[] Templates;
    public Camera Observer;
    public SmoothFirstPersonController Player;
    public TextMesh Status;
    public int Station { get; private set; }
    private GameObject encounter;
    private int view;
    private Camera[] playerCameras;
    private static readonly string[] titles = { "CONTACT DUEL", "HURT & SIZE", "MELEE CROWD", "MIXED CLASH", "BLAST CORE", "ROLLING BOMBER" };
    private void Start()
    {
        playerCameras = Player.transform.root.GetComponentsInChildren<Camera>(true);
        Select(0);
    }
    public void Select(int station)
    {
        Station = Mathf.Clamp(station, 0, Templates.Length - 1);
        if (encounter != null) { encounter.SetActive(false); Destroy(encounter); }
        // Projectiles and drops spawn at scene root, outside the authored encounter hierarchy.
        foreach (var creature in FindObjectsByType<Creature>(FindObjectsSortMode.None))
            if (creature.gameObject.scene == gameObject.scene) { creature.gameObject.SetActive(false); Destroy(creature.gameObject); }
        foreach (var flash in FindObjectsByType<AsuraBlastFlash>(FindObjectsSortMode.None))
            if (flash.gameObject.scene == gameObject.scene) Destroy(flash.gameObject);
        encounter = Instantiate(Templates[Station]); encounter.name = "Active encounter"; encounter.SetActive(true);
        if (Player.TryGetComponent<Rigidbody>(out var body)) { body.linearVelocity = Vector3.zero; body.position = new Vector3(-9f, 1.5f, -7f); }
        view = 0; ApplyView();
    }
    public void SetView(int index) { view = index % 3; ApplyView(); }
    private void ApplyView()
    {
        Observer.enabled = view != 2;
        foreach (var camera in playerCameras) camera.enabled = view == 2;
        Player.enabled = view == 2;
        if (Player.TryGetComponent<Rigidbody>(out var body)) body.isKinematic = view != 2;
        Observer.transform.position = view == 0 ? (Station == 0 || Station == 5 ? new Vector3(0f, 2.1f, -6.5f) : new Vector3(0f, 3.8f, -13f)) : new Vector3(12f, 19f, -18f);
        Observer.transform.LookAt(new Vector3(0f, 0.7f, 0f));
        Status.transform.SetParent(Observer.transform, false);
        Status.transform.localPosition = new Vector3(0f, 1.9f, 6f);
        Status.transform.localRotation = Quaternion.identity;
        Status.characterSize = 0.08f;
        Cursor.lockState = view == 2 ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = view != 2;
        Status.text = titles[Station] + "\n1-6: encounter   R: reset   TAB: side / overview / walk\nH: hurt sample (station 2)";
    }
    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        for (int i = 0; i < 6; i++) if (keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) Select(i);
        if (keyboard.rKey.wasPressedThisFrame) Select(Station);
        if (keyboard.tabKey.wasPressedThisFrame) SetView(view + 1);
        if (Station == 1 && keyboard.hKey.wasPressedThisFrame)
            foreach (var creature in encounter.GetComponentsInChildren<Creature>())
            {
                if (creature.ApplyDamage(5f) && creature.TryGetComponent<Rigidbody>(out var body))
                    CreatureImpulse.Apply(body, Vector3.back * 3f + Vector3.up);
            }
    }
}
