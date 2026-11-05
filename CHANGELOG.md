# Changelog

## 0.5.49 - 2026-11-05

Changes since 0.5.48:

- Smooth damage, healing, health, recovery, and skill-bar value changes with a 0.25-second transition. Ranked rows move smoothly when their order changes.
- Reuse enemy-row controls and skip unchanged values to reduce flicker. Fixed the own-player DPS row repeatedly clearing and rebuilding in My skills mode.
- Added rounded DPS and health bars. Removed dark resize footers while keeping resize handles.
- Moved DPS totals and party-health summaries into a compact header line. Ping is available through its separate overlay.
- Redesigned My skills as single-line bars with larger skill icons and fill widths based on each skill's percentage of your damage or healing.
- Party-health bars use a consistent height, with class icons and fresh captured distances inside the bars. Removed extra distance-warning lines; unavailable members remain gray.
- Your current enemy target gets a red border and moves to the top, matched by entity ID. Other enemies retain distance ordering. Above/below indicators now use bold direction icons.
- Nearby enemies automatically appears when opposing players enter captured view and hides when none remain. The header button enables or disables it, and the choice is saved.
- DPS overlays fade to 40% opacity after 15 seconds without party combat or healing. Activity or hovering restores full opacity.
- Loot notifications have a transparent background and show recent pickups for ten seconds, fading over the final two seconds. Loot history remains saved separately.

Windows x64 self-contained single-file executable: no separate .NET runtime installation required. Extract the ZIP and launch Wingbeat.exe from the "Winbeat ♥ Aion Dps Meter" folder. Npcap is required for packet capture.

## 0.5.48 - 2026-11-05

Changes since 0.5.47:

- Added an optional Nearby enemies overlay to the top header. Shows captured opposing players, class icons, CP, last captured HP, and distance when positions are fresh.
- Correctly distinguishes opposing Abyss teams using the captured faction override, including players who share your base faction.
- Compact health bars use translucent class colors, with bold CP text colored relative to your CP. Names, HP, CP, distance, and Tagged indicators fit inside each bar.
- Resize the overlay and its bars using the corner grip. Window size, placement, locking, and independent scale settings are saved locally. Shared hide controls also apply.
- Enemies sort nearest first, with unknown distances last. Up/down arrows indicate height differences beyond 200 world units; distance and arrows clear when position data becomes stale.
- Tagged marks players you have damaged in the current fight without requiring the killing blow. Tags clear on observed death/revival. This indicates captured participation, not guaranteed server assist credit.
- Debuffed party members now have dark-green health bars and a darker translucent regeneration preview; out-of-range members remain gray.
- Fixed a stuck Alt-input state that could prevent overlays from hiding when the game cursor was visible.

Windows x64 self-contained single-file executable: no separate .NET runtime installation required. Extract the ZIP and launch Wingbeat.exe from the "Winbeat ♥ Aion Dps Meter" folder. Npcap is required for packet capture.
