# ExileCampaigns2 — PoE2

Campaign progression and objective overlay for ExileCore2. It guides the player
using local route data and does not automate movement or combat.

## Logic

1. Reads the current area, quest/server flags, entity metadata, and player state.
2. Resolves the active profile, route step, target area, and objective from the
   bundled `Data/poe2` route database.
3. Requests a cancellable path from the [Radar](../Radar/README.md) bridge when
   an objective route is available.
4. `Tick` advances objectives, records diagnostics, and persists progress;
   `Render` draws markers, indicators, progress, and the route.
5. Area changes, disable, hot reload, and dispose cancel stale paths and reset
   area state.

The route JSON carries optional PoE2 provenance metadata (`game`, client patch,
league, provenance, and UTC update time). It is displayed in the route status so
community guide/planner data cannot silently appear current after a patch.
User-edited routes preserve that metadata and are written through a unique
temporary file before replacement.

## Status

Build: **PASS**. Classification: **CURRENT_WITH_WARNINGS**; area IDs, quest
flags, and route targets still need a live 0.5.4e traversal.

Detailed report: [PoE2 plugin catalog](../../README.md) ·
[audit](../../../docs/plugins/ExileCampaigns2/AUDIT.md).
