# Luminosity

`Creature` and `PlayerCreatureStats` each hold one **Luminosity** number. The Inspector, lighting, and perception read that same stored value. There is no base/effective split or contribution calculation in the stats. Both expose `ICreatureStats`, so animals can read either without a player-specific branch. Existing heat, food, combat, and lifespan rules are unchanged.

Zero means no emission. Values above one are valid. `SetLuminosity` sets the number directly; negative or nonfinite inputs are rejected. Timed effects own their changes and cleanup. Perception separately checks whether a body is active and alive; the stat itself does not calculate lifecycle, duration, or modifiers.

Observer behaviour owns range, sensitivity, update frequency, and target selection. It can reuse one list:

```csharp
private readonly List<LuminosityReading> visibleLights = new List<LuminosityReading>(32);

// Run from the creature's sensing tick, using its eye and its own tuning values.
LuminosityQuery.Nearby(gameObject.scene, eye.position, detectionRadius,
    minimumBrightness, transform, LuminosityQuery.DefaultObstructionMask, visibleLights);
// Each reading exposes Source (ICreatureStats), EmittedLuminosity, Distance,
// and PerceivedBrightness. Behaviour chooses whether and how to react.
```

Perceived brightness is `source.Luminosity / (1 + distance * distance)`. Solid scenery on Default, Ground, and Wall layers blocks the query by default. Triggers and the source/observer's own colliders are ignored. Results are unsorted. Sources have no detection-radius setting: a bright body can be noticed beyond its lamp's illumination radius. This models emission, not sunlight, ambient illumination, or brightness of rendered pixels.

Membership is scene-local. Spawn/enable/disable changes invalidate a shared snapshot; movement positions refresh on demand at most every 0.1 seconds. Brightness and availability are read live. Queries reuse an octree, candidate lists, and raycast buffers; capacity can grow once when a larger population or unusually crowded ray requires it.

Honey coating attaches `HoneyLuminosityEffect`, which adds 50 directly to the player's number for 60 seconds, subtracts its 50 on expiry, and destroys itself. A player at 0 goes to 50 then back to 0; one at 10 goes to 60 then back to 10. Refreshing honey refreshes that component without stacking it. Disabling or destroying the component removes its addition once. Meditation pauses its timer and temporarily removes the addition. Hold honey and press F to coat; left click drinks it. Drinking gives 45 seconds of personal night vision without changing luminosity. Its light and exposure volume are active only inside the owning camera's render callbacks.

Glow Bees and physical honey start at **50 luminosity**. Their visual driver only reads the number; it does not add pulses or rewrite it. `LuminosityLighting` maps 50 luminosity to point-light intensity 1.2 and emission strength 3. Honey coating uses the same conversion. Bees keep their existing scent/social/foraging behaviours; luminosity does not add attraction by itself.

## YoYo placement

`Scene_Yoyo` has one `Glow Bee Colony — player test` with an editable `Nest Anchor` near the saved player start, one nest, four bees, and five nectar flowers. No extra spider is spawned. An assigned anchor list replaces the colony's rock/trunk search; empty lists preserve the existing automatic placement used by Scene Ariia. Nests and creatures still generate at runtime. Anchors project onto Ground-layer floors and reject obstructed positions.

## Validation

- `LuminosityValidation.Run()` checks shared queries, falloff, obstruction, lifecycle, contributions, and warm allocation behaviour in an isolated physics scene during Play Mode.
- `HoneyLuminosityValidation.Begin()` / `Finish()` checks the actual player camera and a temporary observer camera, plus coating strength, expiry, refresh, and cleanup. Allow frames to render between the two calls.
- `GlowBeeValidation.RunLive()` runs the existing ecology integration checks in an already-playing Scene Ariia without saving the scene or exiting the Editor. `IsRunning` and `LastResult` expose completion. The existing isolated batch entry point remains available.
