Wingbeat ♥ Aion 2 DPS Meter
==========================

Wingbeat is a free, local DPS meter for Aion 2 on Windows. Track damage,
healing and PvP encounters, inspect your skills, and review saved fights.
No account, external login or subscription is required.

Features
--------

* Live damage and healing meters, with breakdowns by character, skill and target.
* Party views for up to 10 players and PvP tracking for multiple opponents.
* A transparent overlay with your identified character pinned first, class
  colours and symbols, five themes, custom colours, and a movable, resizable window.
* Transparent skill and healing tables with contribution bars and percentages
  that follow the current character filter.
* Zoomable rotation timelines with event inspection, scrubbing and opener views.
* Captured combat power, local ping estimates, loot energy and player stats.
* Player-stat icons, clear values, and each skill's last reported cooldown.
* Saved fights, character trends and comparisons using your local fight history.
* Bundled skill names, skill images and NPC names for offline display.

Getting started
---------------

1. Download and extract the Windows x64 release ZIP.
2. Run ``Wingbeat.exe``. The package includes the .NET 10 runtime.
3. Install `Npcap <https://npcap.com/#download>`_ for live capture. Wingbeat
   prompts you with the official download link if the driver is missing.
4. Select your network adapter in Settings and start capture.
5. Change zones while capturing to refresh your character identity and CP.
   Joining a party while capturing lets Wingbeat receive party profiles.
6. Open the overlay and choose Damage or Healing from its menu.

Saved fights and the built-in demo can be viewed without Npcap.

Using the overlay
-----------------

Drag the header to move the overlay and use the resize grip to change its size.
The position lock prevents accidental movement. Your identified character stays
at the top while the remaining rows scroll.

The DAMAGE / HEALING menu controls the meter mode, party/PvP scope, compact rows
and **Class colours**. Class symbols appear beside DPS or HPS; unknown classes
use a neutral diamond. Settings are saved locally.

Reading your fights
-------------------

**Skills and Healing** show contributions for the current character filter.
Bar widths match **Damage %** or **Healing %**, making the largest contributions
easy to compare. Click a player's meter row to inspect their skills.

**Rotations** shows damage and healing on a shared timeline. Zoom in to inspect
individual events, pin an event to step through the sequence, or follow the
latest activity.

**Player stats** shows your latest captured attack, damage bonuses and combat
speed with coloured icons. Stat IDs and raw values remain below each label;
unknown stats stay visible with a neutral icon. **Cooldowns** shows the last
reported remaining time, rather than a running countdown.

**Saved fights and Trends** let you revisit encounters and follow a character's
performance. CP-bracket reports and top-performer comparisons use only your
saved fights and need enough comparable encounters to produce results.

Names and images included
-------------------------

Every build includes 16,899 skill names, 377 skill images covering 670 skill
families, and 12,587 NPC template names. Catalogs load automatically at startup;
no separate download is needed.

Captured spawn packets connect temporary target IDs to NPC names. Unknown
players and targets keep their IDs until identifying data arrives. Existing
saved NPC records can resolve through the bundled catalog when a template ID
was recorded.

**Load skill names** in Settings supports local overrides. Optional local icon
and NPC-name overrides are also supported. Missing or damaged local data falls
back to bundled data where available.

Current limits
--------------

Game decoding and healing support remain experimental and partial. Information
is available only when the relevant packets are captured and supported.

* Your CP comes from local-character packets; other players' CP comes from
  matched party profiles. Missing CP remains ``--``.
* Ping is an estimate for your connection. Other players' ping is unavailable.
* Detailed player stats and cooldown readings describe your local character.
* Healing totals do not distinguish effective healing from overhealing.
* Performance comparisons use local history, not a global player ranking.

Screenshots
-----------

These screenshots use synthetic demo data. Names, values and encounters are
illustrative.

.. figure:: docs/images/party-meter.png
   :alt: Wingbeat party damage meter with class-coloured bars.
   :width: 100%

   Party damage, DPS and encounter duration.

.. figure:: docs/images/damage-overlay.png
   :alt: Transparent Wingbeat overlay with the local character pinned first.
   :width: 390px

   Compact overlay with class symbols and loot energy.

.. figure:: docs/images/party-rotations.png
   :alt: Wingbeat party rotation timeline with damage and healing lanes.
   :width: 100%

   Zoomable party rotation timelines.

.. figure:: docs/images/pvp-tracking.png
   :alt: Wingbeat PvP view showing incoming and outgoing damage.
   :width: 100%

   PvP tracking across multiple opponents.

Build from source
-----------------

On Windows with the .NET 10 SDK, run::

    powershell -File .\Build-Windows.ps1

The standalone Windows x64 ZIP is written to ``releases/``. Npcap remains a
separate driver installation.

Dependency and game-data notices are in ``THIRD-PARTY-NOTICES.txt`` and
``licenses/``.
