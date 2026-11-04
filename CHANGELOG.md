# Changelog

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
