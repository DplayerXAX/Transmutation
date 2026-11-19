using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>The part a creature plays in the cave's music.</summary>
public enum SongRole { Melody, Accompaniment, Drums }

/// <summary>How a species follows once played to.</summary>
public enum SongMotion { Walk, Fly, Rooted, Silent }

/// <summary>The player's controls, each of which plays a note while facing a creature.</summary>
public enum SongInput { W, A, S, D, Jump, Shift, LMB, RMB }

/// <summary>
/// Playing to creatures with the body's own controls (replaces the conductor baton; DarkTunnels.UseBaton brings it back).
/// Every living species has a role (melody, accompaniment, drums) and a motion in CreatureNoteTable, and a short phrase of
/// 3-4 inputs. Face one nearby and its phrase appears above it in planet glyphs; press the inputs in order, at any pace.
/// A finished phrase makes it follow for a while: melody leads, accompaniment circles, drums come behind. Rooted species
/// stay and sing where they are, counting while the player is near. Followers add their part to the music; all three
/// roles together are the full chord, which opens chord doors.
/// Only presses made while facing a creature count. Within reach and while carrying, the mouse keeps its usual work
/// (pick up, pet, throw), so mouse notes are played from a little way off.
/// </summary>
[DefaultExecutionOrder(-50)] // before the carrier and interactor read the mouse
public sealed class CreatureSong : MonoBehaviour
{
    public const float ListenRange = 16f;
    private const float LoseTargetAfter = 0.7f, CloseReach = 6f, SingRange = 20f;
    private const float WalkSpeed = 4f, FlySpeed = 5.5f;

    public static CreatureSong Instance { get; private set; }
    public static bool Active => Instance != null && Instance.isActiveAndEnabled;
    /// <summary>The mouse is playing notes now: pet, use and carry leave it alone.</summary>
    public static bool ConsumesMouse => Active && Instance.mouseTaken;
    /// <summary>All three roles are following.</summary>
    public static bool FullChord => Active && Instance.roleCount[0] > 0 && Instance.roleCount[1] > 0 && Instance.roleCount[2] > 0;
    public static bool IsFollowing(Creature c) => Active && Instance.byCreature.ContainsKey(c);

    public Creature Target => target;
    public int Progress => progress;
    public int RoleCount(SongRole role) => roleCount[(int)role];

    private sealed class Follower
    {
        public Creature creature;
        public CaveGlowCreature glow;
        public SongMotion motion;
        public Rigidbody body;
        public bool dynamicBody, ownMotion;
        public Vector3 lastPosition, goal;
        public Quaternion lastRotation;
        public float groundOffset, radius, nextPop;
        public int slot;
    }

    private struct Shape { public Vector3 offset; public float size; }

    private readonly List<Follower> followers = new List<Follower>();
    private readonly Dictionary<Creature, Follower> byCreature = new Dictionary<Creature, Follower>();
    private readonly Dictionary<Creature, Shape> shapes = new Dictionary<Creature, Shape>();
    private readonly int[] roleCount = new int[3];
    private readonly int[] seeds = new int[3];
    private readonly Dictionary<Creature, SongInput[]> phrases = new Dictionary<Creature, SongInput[]>();
    private Creature target;
    private int progress;
    private float lost;
    private bool mouseTaken;
    private SongInput walkNote = SongInput.W;
    private float walkNoteAt = -10f;
    private Camera cam;
    private Rigidbody player;
    private PlayerCreatureCarrier carrier;
    private GlyphRow row;
    private GlyphRow[] hudRoles;
    private Transform hud;
    private readonly Transform[] hudHolders = new Transform[3];
    private readonly bool[] roleFollowing = new bool[3];
    private readonly List<Renderer>[] hudSeeds = { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
    private Material glowLine, seedDot;

    private static readonly string[] Layers = { "BGM_glim", "BGM_bell", "BGM_maw" };
    private static readonly string[] Notes = { "E", "G", "B", "D" };
    private static readonly Color[] RoleColors = { new Color(0.45f, 0.95f, 1f), new Color(1f, 0.78f, 0.25f), new Color(1f, 0.35f, 0.6f) };
    public static Color RoleColor(SongRole r) => RoleColors[(int)r];
    private static SongRole RoleOf(Creature c) => CreatureNoteTable.RoleFor(c.GetType());

    private void Awake()
    {
        Instance = this;
        gameObject.AddComponent<CreatureSongDrive>().Song = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (string layer in Layers) AudioManager.SetLayerWeight(layer, -1f);
    }

    // ---------------- Phrases ----------------

    public SongInput[] PhraseOf(Creature c)
    {
        if (phrases.TryGetValue(c, out SongInput[] known)) return known;
        // A species with its own music plays a motif from it (set in CreatureNotes.asset).
        SongInput[] motif = c is ToneCrate ? null : CreatureNoteTable.PhraseFor(c.GetType());
        if (motif != null) { phrases[c] = motif; return motif; }
        // The others keep a fixed phrase, so it can be learned; every Tone Mesa has its own.
        int seed = 17;
        foreach (char ch in c.GetType().Name) seed = seed * 31 + ch;
        if (c is ToneCrate) seed += Mathf.RoundToInt(c.transform.position.x * 3f) * 101 + Mathf.RoundToInt(c.transform.position.z * 3f);
        var rng = new System.Random(seed);
        SongRole role = RoleOf(c);
        SongInput[] pool = role == SongRole.Drums ? new[] { SongInput.W, SongInput.A, SongInput.S, SongInput.D, SongInput.Jump, SongInput.Shift }
            : role == SongRole.Accompaniment ? new[] { SongInput.LMB, SongInput.RMB }
            : new[] { SongInput.W, SongInput.A, SongInput.S, SongInput.D, SongInput.Jump, SongInput.Shift, SongInput.LMB, SongInput.RMB };
        var phrase = new SongInput[role == SongRole.Accompaniment ? 4 : 3 + rng.Next(2)];
        for (int i = 0; i < phrase.Length; i++)
        {
            do phrase[i] = pool[rng.Next(pool.Length)];
            while (pool.Length > 2 && i > 0 && phrase[i] == phrase[i - 1]);
        }
        if (role == SongRole.Accompaniment) phrase[1] = phrase[0] == SongInput.LMB ? SongInput.RMB : SongInput.LMB;
        phrases[c] = phrase;
        return phrase;
    }

    // ---------------- Each frame ----------------

    private void Update()
    {
        mouseTaken = false;
        if (cam == null) cam = Camera.main;
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (player == null && carrier != null) player = carrier.PlayerBody;
        if (cam == null || player == null) return;
        BuildVisuals();

        foreach (CaveGlowCreature c in CaveGlowCreature.All) c.Aimed = false;
        bool blocked = MeditationController.BlocksPlayerInput || RestSpotSystem.BlocksInteraction || !Application.isFocused && !Application.isBatchMode;
        Creature seen = blocked ? null : FindFaced();
        if (seen != null && seen != target) { target = seen; progress = 0; lost = 0f; }
        else if (target == null) { }
        else if (seen == null && (lost += Time.deltaTime) > LoseTargetAfter) { target = null; progress = 0; }
        else if (seen != null) lost = 0f;

        if (target != null)
        {
            if (target is CaveGlowCreature glow) glow.Aimed = true;
            mouseTaken = carrier.CarriedCreature == null && carrier.PendingCreature == null &&
                         (progress > 0 || Vector3.Distance(cam.transform.position, AimOf(target)) > CloseReach);
        }
        if (!blocked && Time.timeScale > 0f) ReadInputs(seen != null);
        UpdateFollowers();
    }

    private Shape ShapeOf(Creature c)
    {
        if (shapes.TryGetValue(c, out Shape s)) return s;
        s = new Shape { size = 0.6f };
        if (CreatureBounds.TryMeasure(c.transform, out Bounds b))
        {
            s.size = Mathf.Clamp(CreatureBounds.LargestSide(b) * 0.5f, 0.3f, 4f);
            s.offset = Vector3.ClampMagnitude(b.center - c.transform.position, 4f);
        }
        shapes[c] = s;
        return s;
    }

    private Vector3 AimOf(Creature c) => c is CaveGlowCreature glow ? glow.AimPoint : c.transform.position + ShapeOf(c).offset;
    private float SizeOf(Creature c) => c is CaveGlowCreature glow ? glow.AimSize : ShapeOf(c).size;

    private Creature FindFaced()
    {
        Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
        Creature best = null;
        float bestAngle = float.MaxValue, near = (ListenRange + 4f) * (ListenRange + 4f);
        IReadOnlyList<Creature> all = CreatureRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            Creature c = all[i];
            if (c == null) continue;
            Vector3 rough = c.transform.position - eye;
            if (rough.sqrMagnitude > near) continue;
            if (Vector3.Dot(rough, fwd) < 0f && rough.sqrMagnitude > 16f) continue;
            if (CreatureNoteTable.MotionFor(c.GetType()) == SongMotion.Silent) continue;
            if (c.IsDead || c.IsCarried || !c.isActiveAndEnabled || !c.IsInPhysicalWorld || c.IsBubbleCaptured) continue;
            if (c is CaveGlowCreature g && g.Hidden) continue;
            if (byCreature.ContainsKey(c)) continue;
            Vector3 aim = AimOf(c);
            Vector3 to = aim - eye;
            float d = to.magnitude;
            if (d > ListenRange || d < 0.2f) continue;
            float angle = Vector3.Angle(fwd, to);
            float allowed = Mathf.Max(7f, Mathf.Atan2(SizeOf(c), d) * Mathf.Rad2Deg);
            if (c == target) allowed *= 1.5f; // holding the aim on it is forgiving
            if (angle > allowed || angle >= bestAngle) continue;
            if (Physics.Linecast(eye, aim, DarkTunnels.TerrainMask, QueryTriggerInteraction.Ignore)) continue;
            best = c;
            bestAngle = angle;
        }
        return best;
    }

    // Every control always sounds its note (these replace the old click, pick-up and drop sounds).
    // Only presses made while facing a creature count towards its phrase.
    private void ReadInputs(bool facing)
    {
        Keyboard k = Keyboard.current;
        Mouse m = Mouse.current;
        if (k != null)
        {
            if (k.wKey.wasPressedThisFrame) Hit(SongInput.W, facing);
            if (k.aKey.wasPressedThisFrame) Hit(SongInput.A, facing);
            if (k.sKey.wasPressedThisFrame) Hit(SongInput.S, facing);
            if (k.dKey.wasPressedThisFrame) Hit(SongInput.D, facing);
            if (k.spaceKey.wasPressedThisFrame) Hit(SongInput.Jump, facing);
            if (k.leftShiftKey.wasPressedThisFrame) Hit(SongInput.Shift, facing);
        }
        if (m != null)
        {
            if (m.leftButton.wasPressedThisFrame) Hit(SongInput.LMB, facing && mouseTaken);
            if (m.rightButton.wasPressedThisFrame) Hit(SongInput.RMB, facing && mouseTaken);
        }
    }

    private void Hit(SongInput input, bool counts)
    {
        AudioManager.PostEvent(CreatureNoteTable.EventFor(input), player.gameObject);
        if (input <= SongInput.D) { walkNote = input; walkNoteAt = Time.time; }
        if (counts) Press(input);
    }

    private static bool Held(SongInput i)
    {
        Keyboard k = Keyboard.current;
        if (k == null) return false;
        return i == SongInput.W ? k.wKey.isPressed : i == SongInput.A ? k.aKey.isPressed : i == SongInput.S ? k.sKey.isPressed : k.dKey.isPressed;
    }

    /// <summary>A footstep sounds the note of the movement key being held. True if the song took the step.</summary>
    public static bool PlayStep(GameObject emitter, int gridDivision)
    {
        if (!Active) return false;
        CreatureSong s = Instance;
        SongInput note = s.walkNote;
        if (!Held(note))
        {
            bool found = false;
            for (SongInput i = SongInput.W; i <= SongInput.D && !found; i++)
                if (Held(i)) { note = i; found = true; }
            if (!found) return true; // moving without a key (a slide, a push): no step note
            s.walkNote = note;
        }
        // The key press has just sounded this note.
        if (Time.time - s.walkNoteAt > 0.18f) AudioManager.PostEventOnGrid(CreatureNoteTable.EventFor(note), emitter, gridDivision);
        return true;
    }

    /// <summary>The older action sounds, kept for scenes without the song.</summary>
    public static void ActionSound(string eventName, GameObject emitter)
    {
        if (!Active) AudioManager.PostEvent(eventName, emitter);
    }

    private void Press(SongInput input)
    {
        if (target == null) return;
        SongInput[] phrase = PhraseOf(target);
        if (input == phrase[progress])
        {
            progress++;
            if (progress >= phrase.Length) Complete();
            return;
        }
        // A wrong note starts the phrase over (it may itself be the first note).
        progress = input == phrase[0] ? 1 : 0;
        if (row != null) row.Shake();
    }

    private void Complete()
    {
        Creature c = target;
        target = null;
        progress = 0;
        Follow(c);
        AudioManager.PostEvent("Play_Crate_Thump", c.gameObject);
        if (cam != null) BatonNotePop.SpawnOver(cam.transform, AimOf(c) + Vector3.up * 0.9f, CreatureNoteTable.For(c));
    }

    private void Follow(Creature c)
    {
        if (byCreature.ContainsKey(c)) return;
        // One follower for each kind of note: a new one takes the place of the last.
        SongRole role = RoleOf(c);
        for (int i = followers.Count - 1; i >= 0; i--)
            if (followers[i].creature == null || RoleOf(followers[i].creature) == role) Release(i);
        var f = new Follower
        {
            creature = c,
            glow = c as CaveGlowCreature,
            motion = CreatureNoteTable.MotionFor(c.GetType()),
            body = c.GetComponent<Rigidbody>(),
            lastPosition = c.transform.position,
            lastRotation = c.transform.rotation,
            radius = Mathf.Clamp(SizeOf(c) * 0.6f, 0.15f, 0.8f),
            groundOffset = 0.3f,
        };
        f.dynamicBody = f.body != null && !f.body.isKinematic;
        f.ownMotion = f.glow != null || c is FuzzCreature; // these already know how to follow
        if (f.glow != null) { f.glow.Following = true; f.glow.SetMuted(false); f.glow.SetSolo(true); }
        if (c is TentacleCreature tentacle) { tentacle.ExternalMovement = true; tentacle.CrawlNormal = Vector3.up; }
        if (Physics.Raycast(c.transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit floor, 4f, DarkTunnels.TerrainMask, QueryTriggerInteraction.Ignore))
            f.groundOffset = c.transform.position.y - floor.point.y;
        followers.Add(f);
        byCreature[c] = f;
    }

    private void Release(int index)
    {
        Follower f = followers[index];
        followers.RemoveAt(index);
        byCreature.Remove(f.creature);
        if (f.creature == null) return;
        if (f.glow != null) { f.glow.Following = false; f.glow.SetSolo(false); }
        if (f.creature is TentacleCreature tentacle) tentacle.ExternalMovement = false;
    }

    private void UpdateFollowers()
    {
        for (int i = 0; i < 3; i++) roleCount[i] = 0;
        Vector3 p = player.position;
        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        for (int i = followers.Count - 1; i >= 0; i--)
        {
            Follower f = followers[i];
            Creature c = f.creature;
            bool gone = c == null || c.IsDead || !c.isActiveAndEnabled || f.glow != null && f.glow.Hidden;
            float distance = gone ? 0f : Vector3.Distance(c.transform.position, p);
            if (gone)
            {
                Release(i);
                continue;
            }
            int role = (int)RoleOf(c);
            if (f.motion == SongMotion.Rooted)
            {
                if (distance > SingRange) continue;
                roleCount[role]++;
                if (Time.time >= f.nextPop)
                {
                    f.nextPop = Time.time + 2.5f;
                    BatonNotePop.SpawnOver(cam.transform, AimOf(c) + Vector3.up * (SizeOf(c) + 0.3f), CreatureNoteTable.For(c));
                }
                continue;
            }
            f.slot = roleCount[role]++;
            f.goal = FollowPoint(f, (SongRole)role, p, forward, right);
            if (c.IsCarried) continue;
            if (f.glow != null)
            {
                if (!f.glow.Soloed) f.glow.SetSolo(true);
                f.glow.SetGuide(f.goal);
            }
        }
        for (int i = 0; i < 3; i++) AudioManager.SetLayerWeight(Layers[i], roleCount[i] > 0 ? 1f : -1f);
    }

    private static Vector3 FollowPoint(Follower f, SongRole role, Vector3 p, Vector3 forward, Vector3 right)
    {
        switch (role)
        {
            case SongRole.Melody: // leads the way
                return p + forward * (4.5f + f.slot * 1.5f) + right * Mathf.Sin(Time.time * 0.7f + f.slot) * 1.2f + Vector3.up * 1.2f;
            case SongRole.Accompaniment: // goes round the player
                float a = Time.time * 0.8f + f.slot * 2.1f;
                return p + new Vector3(Mathf.Cos(a), 0.4f, Mathf.Sin(a)) * (3.2f + f.slot * 0.6f);
            default: // tramps behind
                return f.creature is ToneCrate ? p : p - forward * (3f + f.slot * 2.2f) + right * ((f.slot % 2 == 0 ? 1f : -1f) * 1.2f) + Vector3.up * 0.8f;
        }
    }

    // ---------------- Moving the followers that have no way of their own ----------------

    private static bool Free(Follower f) =>
        !f.ownMotion && f.motion != SongMotion.Rooted && f.creature != null && !f.creature.IsCarried && !f.creature.IsBubbleCaptured;

    /// <summary>Bodies under physics: steer them by velocity, after their own logic.</summary>
    internal void DriveBodies()
    {
        foreach (Follower f in followers)
        {
            if (!f.dynamicBody || !Free(f) || f.body == null || f.body.isKinematic) continue;
            Vector3 to = f.goal - f.body.position;
            bool walks = f.motion == SongMotion.Walk;
            if (walks) to.y = 0f;
            float speed = Mathf.Min(walks ? WalkSpeed : FlySpeed, to.magnitude * 2f);
            Vector3 v = to.sqrMagnitude > 0.2f ? to.normalized * speed : Vector3.zero;
            // A walker stops at an edge rather than running off it.
            if (walks && v != Vector3.zero &&
                !Physics.Raycast(f.body.position + v.normalized * (f.radius + 0.5f) + Vector3.up * 0.8f, Vector3.down, 3f, DarkTunnels.TerrainMask, QueryTriggerInteraction.Ignore))
                v = Vector3.zero;
            if (walks) v.y = f.body.linearVelocity.y;
            f.body.linearVelocity = v;
            f.body.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>Script-moved bodies: the song owns their position, so their own wandering cannot pull against it.</summary>
    internal void DriveTransforms()
    {
        float dt = Time.deltaTime;
        foreach (Follower f in followers)
        {
            if (f.creature == null) continue;
            if (!Free(f) || f.dynamicBody)
            {
                f.lastPosition = f.creature.transform.position;
                f.lastRotation = f.creature.transform.rotation;
                continue;
            }
            bool walks = f.motion == SongMotion.Walk;
            Vector3 p = f.lastPosition, to = f.goal - p;
            if (walks) to.y = 0f;
            float distance = to.magnitude;
            Quaternion rotation = f.lastRotation;
            if (distance > 0.45f && dt > 0f)
            {
                Vector3 dir = to / distance;
                float step = Mathf.Min(walks ? WalkSpeed : FlySpeed, distance * 2f) * dt;
                Vector3 from = p + Vector3.up * (walks ? f.radius + 0.25f : 0f);
                if (Blocked(from, f.radius, dir, step, out RaycastHit hit))
                {
                    Vector3 slide = Vector3.ProjectOnPlane(dir, hit.normal);
                    if (walks) slide.y = 0f;
                    dir = slide.sqrMagnitude > 0.01f ? slide.normalized : Vector3.zero;
                    if (dir == Vector3.zero || Blocked(from, f.radius, dir, step, out _)) step = 0f;
                }
                Vector3 next = p + dir * step;
                if (walks)
                {
                    // Stays on the ground it can reach: no walking off ledges or up cliffs.
                    if (Physics.Raycast(next + Vector3.up * 1.2f, Vector3.down, out RaycastHit floor, 3.2f, DarkTunnels.TerrainMask, QueryTriggerInteraction.Ignore))
                        next.y = Mathf.Lerp(p.y, floor.point.y + f.groundOffset, 1f - Mathf.Exp(-12f * dt));
                    else next = p;
                }
                p = next;
                Vector3 look = Vector3.ProjectOnPlane(dir, Vector3.up);
                if (step > 0f && look.sqrMagnitude > 0.01f)
                    rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(look.normalized, Vector3.up), 1f - Mathf.Exp(-5f * dt));
            }
            f.creature.transform.SetPositionAndRotation(p, rotation);
            if (f.body != null) { f.body.position = p; f.body.rotation = rotation; }
            f.lastPosition = p;
            f.lastRotation = rotation;
        }
    }

    private static bool Blocked(Vector3 from, float radius, Vector3 dir, float step, out RaycastHit hit) =>
        Physics.SphereCast(from, radius, dir, out hit, step + 0.05f, DarkTunnels.TerrainMask, QueryTriggerInteraction.Ignore);

    // ---------------- Note Seeds ----------------

    public int SeedCount(SongRole role) => seeds[(int)role];

    public void AddSeed(SongRole role)
    {
        seeds[(int)role]++;
        AudioManager.PostEvent("Play_Crate_" + Notes[(int)role]);
    }

    /// <summary>True when a follower of this role is within reach of a point (it wakes seed pods of its kind).</summary>
    public bool FollowerNear(SongRole role, Vector3 point, float range)
    {
        foreach (Follower f in followers)
            if (f.creature != null && RoleOf(f.creature) == role && Vector3.Distance(f.creature.transform.position, point) < range) return true;
        return false;
    }

    // ---------------- Glyphs ----------------

    private void BuildVisuals()
    {
        if (row != null) return;
        glowLine = new Material(Shader.Find("Capstone/Cave/GlowLine")) { name = "Song glyphs" };
        glowLine.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always); // rock never hides a phrase
        row = new GlyphRow("Song phrase", null, glowLine);
        seedDot = new Material(Shader.Find("Capstone/Cave/Glow")) { name = "Song seed" };
        hud = new GameObject("Song roles").transform;
        hud.SetParent(cam.transform, false);
        hudRoles = new GlyphRow[3];
        for (int i = 0; i < 3; i++)
        {
            hudHolders[i] = new GameObject("Role " + (SongRole)i).transform;
            hudHolders[i].SetParent(hud, false);
            hudRoles[i] = new GlyphRow("Glyph", hudHolders[i], glowLine);
            hudRoles[i].Root.localScale = Vector3.one * 0.035f;
            hudRoles[i].Set(new[] { SongGlyphs.Role((SongRole)i) });
        }
    }

    private void LateUpdate()
    {
        if (row == null || cam == null) return;
        row.Root.gameObject.SetActive(target != null);
        if (target != null)
        {
            SongInput[] phrase = PhraseOf(target);
            var glyphs = new List<Vector2[]>[phrase.Length];
            for (int i = 0; i < phrase.Length; i++) glyphs[i] = SongGlyphs.Input(phrase[i]);
            row.Set(glyphs);
            Vector3 aim = AimOf(target);
            float d = Vector3.Distance(cam.transform.position, aim);
            row.Root.position = aim + cam.transform.up * (0.5f + d * 0.03f + (target is CaveGlowCreature ? 0f : Mathf.Min(SizeOf(target), 2f) * 0.6f));
            row.Root.rotation = cam.transform.rotation;
            row.Root.localScale = Vector3.one * Mathf.Clamp(d * 0.05f, 0.16f, 0.7f);
            Color c = RoleColor(RoleOf(target));
            for (int i = 0; i < phrase.Length; i++)
                row.Paint(i, i < progress ? c * 1.5f : i == progress ? c * (0.75f + 0.2f * Mathf.Sin(Time.time * 6f)) : c * 0.3f);
            row.Animate();
        }
        // Bottom left of the view: only the kinds of note following now, side by side.
        float halfH = 0.62f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfW = halfH * cam.aspect;
        hud.localPosition = new Vector3(-halfW * 0.88f, -halfH * 0.8f, 0.62f);
        for (int i = 0; i < 3; i++) roleFollowing[i] = false;
        foreach (Follower f in followers) if (f.creature != null) roleFollowing[(int)RoleOf(f.creature)] = true;
        int shown = 0;
        for (int i = 0; i < 3; i++)
        {
            hudHolders[i].gameObject.SetActive(roleFollowing[i]);
            if (roleFollowing[i]) hudHolders[i].localPosition = new Vector3(shown++ * 0.06f, 0f, 0f);
            Color c = RoleColors[i];
            float lit = roleCount[i] > 0 ? 1.2f : 0.45f;
            if (FullChord) lit = 1.3f + 0.3f * Mathf.Sin(Time.time * Mathf.PI * 2f);
            hudRoles[i].Paint(0, c * lit);
            while (hudSeeds[i].Count < seeds[i])
            {
                var dot = VisualPrimitive.Create(PrimitiveType.Sphere);
                dot.transform.SetParent(hudHolders[i], false);
                dot.transform.localScale = Vector3.one * 0.006f;
                dot.transform.localPosition = new Vector3(-0.02f + hudSeeds[i].Count * 0.01f, -0.012f, 0f);
                var r = dot.GetComponent<Renderer>();
                var m = new Material(seedDot);
                m.SetColor("_Color", c * 1.5f);
                r.sharedMaterial = m;
                hudSeeds[i].Add(r);
            }
        }
    }

    // ---------------- Test hooks (Editor/CaveBatonCheck) ----------------

    public void DebugPress(SongInput input) => Press(input);
    public void DebugFollow(Creature c) => Follow(c);
    public int FollowerCount => followers.Count;

    /// <summary>A row of glyphs drawn with thin glowing lines.</summary>
    private sealed class GlyphRow
    {
        public readonly Transform Root;
        private readonly Material material;
        private readonly List<List<LineRenderer>> glyphs = new List<List<LineRenderer>>();
        private List<Vector2[]>[] shown;
        private float shake;

        public GlyphRow(string name, Transform parent, Material material)
        {
            Root = new GameObject(name).transform;
            Root.SetParent(parent, false);
            this.material = material;
        }

        public void Set(List<Vector2[]>[] set)
        {
            if (shown != null && shown.Length == set.Length)
            {
                bool same = true;
                for (int i = 0; i < set.Length; i++) same &= ReferenceEquals(shown[i], set[i]);
                if (same) return;
            }
            shown = set;
            foreach (List<LineRenderer> g in glyphs) foreach (LineRenderer l in g) Object.Destroy(l.gameObject);
            glyphs.Clear();
            float width = 1.35f, left = -(set.Length - 1) * width * 0.5f;
            for (int i = 0; i < set.Length; i++)
            {
                var lines = new List<LineRenderer>();
                foreach (Vector2[] stroke in set[i])
                {
                    var go = new GameObject("Stroke");
                    go.transform.SetParent(Root, false);
                    var l = go.AddComponent<LineRenderer>();
                    l.sharedMaterial = material;
                    l.useWorldSpace = false;
                    l.alignment = LineAlignment.TransformZ;
                    l.widthMultiplier = 0.09f;
                    l.numCapVertices = 2;
                    l.numCornerVertices = 2;
                    l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    l.receiveShadows = false;
                    l.positionCount = stroke.Length;
                    for (int k = 0; k < stroke.Length; k++) l.SetPosition(k, new Vector3(left + i * width + stroke[k].x, stroke[k].y, 0f));
                    lines.Add(l);
                }
                glyphs.Add(lines);
            }
        }

        public void Paint(int glyph, Color c)
        {
            if (glyph >= glyphs.Count) return;
            foreach (LineRenderer l in glyphs[glyph]) l.startColor = l.endColor = c;
        }

        public void Shake() => shake = 1f;

        public void Animate()
        {
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 3f);
            foreach (List<LineRenderer> g in glyphs)
                foreach (LineRenderer l in g) l.transform.localPosition = Vector3.right * Mathf.Sin(Time.time * 45f) * shake * 0.12f;
        }
    }
}

/// <summary>Runs after every creature's own logic, so the song has the last word on where a follower goes.</summary>
[DefaultExecutionOrder(500)]
public sealed class CreatureSongDrive : MonoBehaviour
{
    internal CreatureSong Song;
    private void FixedUpdate() { if (Song != null) Song.DriveBodies(); }
    private void LateUpdate() { if (Song != null) Song.DriveTransforms(); }
}

/// <summary>
/// Planet-script glyphs for the song, in the lexicon's box (about 1 wide, 1.4 tall, base at 0) and its radicals:
/// chevrons are direction, a bar is ground, a hook is the hand, a ring is a whole body.
/// </summary>
public static class SongGlyphs
{
    private static Vector2 V(float x, float y) => new Vector2(x, y);
    private static readonly Dictionary<SongInput, List<Vector2[]>> inputs = new Dictionary<SongInput, List<Vector2[]>>
    {
        { SongInput.W, new List<Vector2[]> { new[] { V(-0.42f, 0.42f), V(0f, 1.05f), V(0.42f, 0.42f) } } },
        { SongInput.S, new List<Vector2[]> { new[] { V(-0.42f, 1f), V(0f, 0.36f), V(0.42f, 1f) } } },
        { SongInput.A, new List<Vector2[]> { new[] { V(0.28f, 0.28f), V(-0.34f, 0.7f), V(0.28f, 1.12f) } } },
        { SongInput.D, new List<Vector2[]> { new[] { V(-0.28f, 0.28f), V(0.34f, 0.7f), V(-0.28f, 1.12f) } } },
        { SongInput.Jump, new List<Vector2[]> { new[] { V(-0.38f, 0.72f), V(0f, 1.22f), V(0.38f, 0.72f) }, new[] { V(-0.44f, 0.16f), V(0.44f, 0.16f) } } },
        { SongInput.Shift, new List<Vector2[]> { new[] { V(-0.42f, 0.3f), V(-0.08f, 0.7f), V(-0.42f, 1.1f) }, new[] { V(0.02f, 0.3f), V(0.36f, 0.7f), V(0.02f, 1.1f) } } },
        { SongInput.LMB, new List<Vector2[]> { new[] { V(0.24f, 0.14f), V(0.24f, 1.02f), V(0.04f, 1.26f), V(-0.26f, 1.16f), V(-0.36f, 0.88f) }, new[] { V(-0.42f, 0.3f), V(-0.14f, 0.3f) } } },
        { SongInput.RMB, new List<Vector2[]> { new[] { V(-0.24f, 0.14f), V(-0.24f, 1.02f), V(-0.04f, 1.26f), V(0.26f, 1.16f), V(0.36f, 0.88f) }, new[] { V(0.14f, 0.3f), V(0.42f, 0.3f) } } },
    };
    private static readonly List<Vector2[]>[] roles =
    {
        // Melody: a line rising to a ring.
        new List<Vector2[]> { new[] { V(-0.45f, 0.16f), V(-0.16f, 0.62f), V(0.04f, 0.5f), V(0.2f, 0.86f) }, Ring(0.3f, 1.06f, 0.19f) },
        // Accompaniment: two curves lying side by side.
        new List<Vector2[]> { new[] { V(-0.4f, 0.2f), V(-0.18f, 0.7f), V(-0.32f, 1.2f) }, new[] { V(0.08f, 0.2f), V(0.3f, 0.7f), V(0.16f, 1.2f) } },
        // Drums: ground bars struck from above.
        new List<Vector2[]> { new[] { V(-0.42f, 0.16f), V(0.42f, 0.16f) }, new[] { V(-0.3f, 0.46f), V(0.3f, 0.46f) }, new[] { V(0f, 0.72f), V(0f, 1.26f) } },
    };

    private static Vector2[] Ring(float x, float y, float r)
    {
        var p = new Vector2[13];
        for (int i = 0; i < p.Length; i++)
        {
            float a = i / 12f * Mathf.PI * 2f;
            p[i] = new Vector2(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r);
        }
        return p;
    }

    public static List<Vector2[]> Input(SongInput i) => inputs[i];
    public static List<Vector2[]> Role(SongRole r) => roles[(int)r];
}
