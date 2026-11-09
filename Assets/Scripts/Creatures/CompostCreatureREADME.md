# Recycle

The current creature documentation is [RecycleNetworkREADME.md](RecycleNetworkREADME.md). This legacy filename remains for existing links and the retained `CompostCreature` script/prefab name.

The existing Compost Creature prefab is now an immovable fungal Recycle network. Its original script and prefab identities remain compatible with existing placements. The moving back, ramp, roaming and platform-riding checks are retired.

The body grows procedurally along connected terrain, including walls. Grown strands consume touching corpses using exclusive, reversible claims. Successful meals grant existing nutrition-based recycle points and strand-growth credit. Missing flowers take priority over new goo batches. Only flowers drop normal Goo Creatures.

Flowers are child creatures before pickup. Picking one leaves all colony points behind and starts a continuous 120-second detached lifetime. LMB on an aimed corpse feeds the portable flower; living targets receive a small harmless jolt. Feeding does not reset Age. Dead flowers seed one new network when they contact upward-facing terrain, unless already consumed or claimed.

Use Tools > Creatures > Recycle for asset migration and the focused gameplay check. Legacy Compost setup entry points route to the new design. The deterministic checks accelerate Age and do not run an ecology soak.

See [the technical design and research](../../../Docs/RecycleSurfaceDesign.md) for surface tracing, procedural animation, tuning, ownership, and verification.
