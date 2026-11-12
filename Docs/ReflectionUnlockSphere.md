# Reflection unlock sphere

The runtime reflection amphitheater now builds a decorative black sphere above and just
beyond the tablet: space-local **(0, 19, 25)**, **radius 2.3 m**. This keeps it clear of the
writing surface and floor exhibits. It uses the existing ink shader with a restrained pale rim,
stepped shading, and faint hatching; it needs no extra lights, colliders, or rigidbodies.

## Implementation

1. Use `ReflectionCatalog.Exhibits` as the current unlock roster and `ReflectionProgress.Unlocked`
   as saved progress. Unique, nonempty stable IDs define the total; stale saved IDs are ignored.
2. Subdivide an icosphere, then repeatedly split the largest convex chunk with seeded planes.
   Both sides share each cut's boundary and cap, preserving the original solid's volume and
   surface. Outer normals are radial; interior fracture normals are flat.
3. Sort chunks by assembled centroid height, with deterministic coordinate tie-breaks. Each
   discovered creature owns the next lowest band, using valid IDs in saved discovery order.
   Partial progress builds a lower bowl with a jagged rim instead of patches on every side. The default
   232 fragments give eight pieces per current family. Dense mode always has at least one
   piece per ID; One Per Creature creates exactly one solid per ID.
4. Give locked chunks deterministic outward scatter, rotation, and optional gentle motion.
   Locked geometry clears the entire assembled sphere by at least 3.6 m by default; bounding
   radii and maximum drift are included. The reveal veil expands to cover the full halo.
   A progress notification restores the next lowest band with a stagger and
   smooth easing. Other animations continue independently. Settled pieces stop moving.
5. On entry, apply existing progress immediately. At completion, render the identical uncut
   outer mesh for a seamless sphere. An empty roster displays that neutral whole sphere.

Geometry is generated during visit setup and when the roster, seed, count, mode, or surface
detail changes. Configuration is checked twice per second and on unlock notifications;
geometry is never generated per frame. Radius scales the existing geometry. Roster changes
reassign pieces deterministically and snap to the new saved state.

`ReflectionProgress.Discover` still performs the existing nearby-creature scan. Its grants now
go through `Unlock(catalog, ids)`, which deduplicates and validates IDs, saves through the same
existing path, and emits one `Changed` notification per batch. The sphere only reads progress.
Visit destruction unsubscribes and releases its meshes and owned material.
Scatter directions use the fragment seed independently of assembled height. Remaining locked
pieces surround the sphere rather than gathering above it as the lower bands restore.
Reassignment never shifts the remaining debris.
The same number of valid discoveries produces the same restored shape regardless of creature
identity; ownership follows discovery order. Existing saves require no migration.

## Tuning and preview

Select **Meditation > ReflectionSpace > Unlock Sphere** in a reflection-enabled scene.
The settings expose position, radius, fracture seed, dense/exact-one mode, dense count,
surface detail, scatter distance/angle, motion toggle, drift amount/speed, rotation amount/speed,
restoration duration, stagger, and a material override. Scatter and drift distances use metres;
angles use degrees, speeds are angular phase rates, and restoration/stagger use seconds.

Use **Open sphere preview** for an isolated static editor preview. Its changes are local to
that window. During a reflection visit, select the generated **Creature Unlock Sphere**, or
the original ReflectionSpace component, for Shattered / Half / Whole, Animate Next Family,
Animate All Remaining, and Show Saved Progress. These controls never modify creature saves.

## Verification

**Tools > Capstone > Meditation > Validate Unlock Sphere** runs save-free geometry, identity,
progress-event, simultaneous-animation, roster-change, preview, and cleanup checks outside Play
mode. It verifies volume/surface conservation, convex solid winding, deterministic assignment,
stale/duplicate IDs, zero creatures, both modes, precise settling, and subscription disposal.

The implementation was compiled in Unity **6000.3.6f1 / URP 17.3.0** and inspected in
`Scene_Exhibition`'s actual runtime reflection visit. Real advancing frames verified drift,
static scatter, single and overlapping restorations, saved partial progress on re-entry,
one tracker per visit, and 233 meshes / one material after re-entry. The test used in-memory
progress, never creature save APIs. The existing player movement code emitted kinematic-body
velocity warnings; that unrelated controller was left unchanged.

Captures and the Play-mode check report are in `ArtCaptures/UnlockSphere/` at the project root.

Follow-up tuning: the sphere now sits at (0, 19, 25); every locked chunk clears the assembled
surface by at least 3.6 metres, including conservative bounds for rotation and drift. The veil
automatically fits the full spread. World and Daniel Play checks covered seated entry/return,
carry and relocation (including an oversized seat), invulnerability, and the fixed return post.
An isolated scene verified that adding RestSpot components creates exactly one shared rig.
Reflection suspension now preserves Hanging Spire geometry rather than destroying it on disable.
