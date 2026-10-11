Wingbeat ♥ Aion 2 DPS Meter
==========================

Free local Aion 2 DPS meter for Windows with live damage, healing, PvP tracking,
and a customizable overlay. No external account or login is required.

Repository name: ``wingbeat-aion-dps-meter``.

Skills and healing breakdowns use transparent rows with contribution bars.
**Damage %** and **Healing %** are shares of the total in the current character
filter; bar widths use the same percentages and refresh with captured events.

Captured character readings
---------------------------

**Player stats** shows your latest captured attack, damage bonuses and combat
speed with coloured icons and prominent values. Stat IDs and raw values remain
available beneath each label; unknown stats use a neutral icon. Percentage fields use the protocol's
basis-point units. These are local-character snapshots, not other players' stats.
**Cooldowns** shows each skill's last reported remaining milliseconds and packet
time; it is not a live countdown. Readings are saved with fights.

Supported network metadata also includes 64-bit HP, explicit character-ID links
for matching party profiles, and short death records. EU identity records sharing
the death opcode stay separate. Unsupported layouts remain unavailable; full
RATmeter parity is not claimed.

Demo screenshots
----------------

These are real app screenshots using built-in synthetic demo data. Names,
numbers and encounters are illustrative, not recordings of real players.

.. figure:: docs/images/party-meter.png
   :alt: Wingbeat party meter showing ten demo players with class-coloured damage bars.
   :width: 100%

   Party damage breakdown with DPS, contribution and encounter duration.

.. figure:: docs/images/damage-overlay.png
   :alt: Compact transparent Wingbeat overlay with your character pinned above nine party members.
   :width: 390px

   Transparent damage overlay with your character pinned first and loot energy below.

Open the overlay's DAMAGE / HEALING menu to toggle **Class colours**. Each
player's DPS or HPS has a class symbol beside it; hover the symbol for its name.
Unknown classes use a neutral diamond. Turning class colours off uses your
chosen accent colour and keeps PvP opponents red. The choice is saved locally.

Overlay rows show your combat power from the local-character packet and party
members' CP from matched party profiles, or values entered in character details.
Start capture before changing zones to refresh your identity and CP; join a
party while capturing to receive its profiles. These packet layouts remain experimental. Missing CP remains
``--``; it is not guessed from item level or arbitrary numbers in a packet.
The overlay footer shows the last **local ping estimate** from the game's
client-clock echo. Other players' ping is unavailable and remains ``--``.
The party protocol's latency-state byte is not a millisecond measurement.

.. figure:: docs/images/party-rotations.png
   :alt: Wingbeat rotation view with separate damage and healing lanes and timeline zoom controls.
   :width: 100%

   Party rotation timelines with zoom controls and damage and healing events.

.. figure:: docs/images/pvp-tracking.png
   :alt: Wingbeat PvP view showing three demo opponents and incoming and outgoing damage.
   :width: 100%

   PvP tracking with multiple opponents and damage in both directions.

Build and run
-------------

Build with the .NET 10 SDK using ``powershell -File .\Build-Windows.ps1``.
The resulting Windows x64 package includes the .NET runtime. Run ``Wingbeat.exe``;
live capture additionally requires the separately installed Npcap driver.
Skill names are bundled inside the application and loaded automatically at startup,
including on a fresh offline installation. **Load skill names** in Settings imports
optional local overrides; entries not supplied by the import keep their bundled names.
If a local catalog is damaged, the bundled names remain available. Unknown skill IDs
still display numerically. Skill images are also included (377 PNGs covering 670
skill families), so the skills and healing views work offline out of the box.
Optional local icon overrides are supported; unavailable images use a placeholder.
The application also embeds 12,587 NPC template names and loads them at startup.
Captured spawn packets map temporary target entity IDs to these templates. Local
``npc-names.json`` overrides remain supported; missing identities retain their IDs.

Game decoding and healing support remain experimental and partial.

The project is being prepared for open-source release; its own license has not
yet been selected. Dependency terms are in ``THIRD-PARTY-NOTICES.txt`` and
``licenses/``. Local fight history, captures, settings and private notes are
excluded from Git. Run ``powershell -File .\Verify-Public.ps1`` after staging
changes and review the staged diff before publishing.
