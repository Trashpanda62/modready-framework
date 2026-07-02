# Total Bannerwar — design notes

Optional add-on mod that expands ModReady with a free-camera + RTS-style battle command system. Ships as a separate Nexus download (not part of ModReady core). Clean-room reimplementation — references RTSCamera's public surface for compatibility, writes its own implementation in entirely new code.

Same legal/architecture model as ModReady to BUTR: original work, MIT, no source copied from upstream, API surface is not copyrightable (Google v. Oracle).

---

## Scope: what's in vs what's not

**In v1.0 (feature parity with RTSCamera v5.1.21):**
- F10 toggles free camera in any battle
- Smooth camera movement (WASD + mouse, shift to speed up)
- Stick camera to selected formation
- Control a friendly soldier after player character is downed (E to focus, E again to control)
- Time-speed control in battle (slow motion, pause, fast forward)
- Mouse-driven order issuing — left-click + drag to set formation position/orientation/width
- Order queuing with Shift+click (chain orders, execute one after the other)
- Middle-click to select formation (own troops) or charge target (enemy formation)
- Volley orders: auto volley, manual volley, volley fire
- Improved square/circle formation behavior (fixed unit placement bugs from base game)
- Multiple-formation relative-position locking when moving multiple at once
- Configurable hotkeys via ModReady Mod Config tab (no separate XML config file)
- All settings persisted via ModReady's MCM (no separate `Documents\.../RTSCamera` directory)

**New in Total Bannerwar (not in RTSCamera):**
- **True pause-with-orders** — Total War style, time stops while issuing orders, resumes on confirm
- **Total War-themed visual style** — Bannerwar-styled order indicators, selection rectangles, formation outlines
- **Tactical preset** — one-click "play it like Total War" sensible defaults (so users don't have to configure 30 hotkeys)
- **MCM integration** — uses ModReady's settings tab natively instead of a separate XML config

**Out of scope for v1.0:**
- Multiplayer — single-player only, same as RTSCamera
- Naval-specific features (RTSCamera ships some, defer to v1.1)
- Save-data integration (RTSCamera is intentionally save-clean — we match this)

---

## Architecture

Total Bannerwar ships as one or two assemblies in a separate Module:

```
Modules/
├── ModReady/                 # required dependency
└── TotalBannerwar/
    ├── SubModule.xml
    └── bin/Win64_Shipping_Client/
        ├── TotalBannerwar.dll        # main module
        └── TotalBannerwar.UI.dll     # XML prefab extensions (optional split)
```

`SubModule.xml`:
- Dependency: ModReady (required) + Bannerlord.Harmony (provided by ModReady)
- Single SubModuleClassType: `TotalBannerwar.TotalBannerwarSubModule`

Key components (clean-room, our own implementation; names chosen to avoid collision with RTSCamera's):

| Component | Responsibility | Hooks |
|---|---|---|
| `FreeCameraMissionLogic` | Per-mission state machine: free-cam toggle, character-AI delegation when toggled, restoration on toggle-off | Harmony patches on `MissionGauntletSpectatorControl` + the spectator-camera lifecycle |
| `FreeCameraView` | Renders the orthographic top-down view, handles WASD input, mouse rotation | Replaces vanilla third-person camera while active |
| `OrderInputController` | Translates mouse clicks/drags into formation orders | Harmony patches on `OrderTroopPlacer` to intercept mouse-positioned commands |
| `OrderVisualOverlay` | Draws world-space order indicators (selection rectangles, target arrows, formation outlines) | Uses TaleWorlds' DebugExtensions/Mission rendering, or Gauntlet world-space overlays |
| `TimeControlLogic` | Pause / slow-motion / fast-forward on hotkey | Patches `Mission.SetMissionTimeSpeedSlower` / time-dilation |
| `FormationOrderQueue` | Per-formation order queue (Shift-click chained orders) | Wraps `Formation.SetMovementOrder` calls with our queue manager |
| `VolleyOrderSystem` | Auto / manual / fire volley | New `Formation.MovementOrder` extension that times unit aim + fire |
| `TBHotKeyConfig` | Hotkey registration with ModReady's MCM — no per-mod XML config file | Implements `IHotKeyConfig` from ModReady framework |
| `TotalBannerwarSubModule` | Entry point, registers all logic into MissionStartingHandler | OnSubModuleLoad / OnMissionScreenInitialize |

Settings storage: lives entirely in ModReady's `Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Global\TotalBannerwar.json`. No separate config dir. Users see settings in Mod Config like every other mod.

---

## Implementation notes from RTSCamera reconnaissance

RTSCamera is MIT-licensed and source-available at https://github.com/lzh-mb-mod/RTSCamera. Studied for API surface and architectural hints, NOT for source-code reuse.

Key things their README teaches us about how Bannerlord exposes this stuff:
- Free-cam uses Bannerlord's existing **spectator-camera infrastructure** (`MissionGauntletSpectatorControl`). The trick is unlocking the spectator-cam from spectator-only states so it works in active battle.
- The base game's `OrderTroopPlacer` already handles drag-to-place orders; their command-system mod just patches it to expand the mouse-driven order set.
- Hotkey config: they implement their own `HotKeyManager` because Bannerlord's input system is keyboard-first. We can do simpler — register our hotkeys through ModReady's framework once that's in v1.5.
- Their config persists to two separate XML files (`RTSCameraConfig.xml`, `CommandSystemConfig.xml`) under `Documents\...\Configs\RTSCamera\`. We collapse this into ModReady's standard MCM JSON.

Specific patches RTSCamera references (visible via strings analysis):
- `Patch_MissionGauntletSpectatorControl` — unlocks spectator cam in active battle
- `Patch_OrderTroopPlacer` — mouse-driven order placement
- `Patch_NavalMovementOrder`, `Patch_ShipOrder` — naval-specific (defer to v1.1)
- `Postfix_OrderController_OnTroopOrderIssued` — order-issued callback for chaining

The same patches will exist in Total Bannerwar but written from scratch in our own classes.

---

## Hour estimate

Clean-room reimplementation, full feature parity + Total War extras.

| Component | Hours |
|---|---|
| FreeCameraMissionLogic + spectator-cam unlock | 15–20 |
| FreeCameraView (top-down camera, WASD/mouse control) | 10–15 |
| OrderInputController (mouse → formation order translation) | 15–25 |
| OrderVisualOverlay (selection rects, target arrows, world-space drawing) | 10–15 |
| TimeControlLogic (pause/slow/fast) | 5–10 |
| FormationOrderQueue (Shift+chain) | 8–12 |
| VolleyOrderSystem | 8–12 |
| Hotkey config UI in ModReady MCM | 4–6 |
| Settings integration (read/write via ModReady) | 2–4 |
| Total War extras (true pause, themed visuals, preset) | 8–15 |
| Testing across battle types (field, siege, naval-skip, hideout) | 8–15 |
| Documentation + Nexus listing + first release | 4–6 |
| **TOTAL** | **97–155 hours** |

Realistic timeline: 3–6 months of part-time effort, depending on cadence.

---

## Why a separate mod, not part of ModReady

- **Single responsibility.** ModReady is a dependency framework. Adding a full RTS-control feature would make ModReady an opinionated content mod, which conflicts with the "drop-in dependency replacement" pitch.
- **Optional opt-in.** Some users want the dependency stack but don't want overhead camera. Shipping Total Bannerwar separately means ModReady users aren't paying for code they don't use.
- **Faster iteration on each.** ModReady and Total Bannerwar can ship independently. A ModReady polish release doesn't have to wait for Total Bannerwar testing.
- **Showcases ModReady.** Total Bannerwar becomes the "look what's possible on the ModReady framework" demo. Helps justify other authors targeting ModReady.

---

## Phase ordering

Don't start Total Bannerwar until ModReady v1.0 ships. v1.0 needs to be stable and documented before we ask anyone to build on top of it. After v1.0:

1. **v1.5 ModReady framework primitives ship first.** Total Bannerwar will lean on the hotkey manager, MCM integration, and overlay-drawing helpers we add to ModReady in v1.5. Building Total Bannerwar without those means duplicating that work in TB itself; better to invest in ModReady once and reuse.
2. **Total Bannerwar v0.1 — alpha**, free-cam + basic commands only, ships as "preview" Nexus listing for feedback.
3. **Total Bannerwar v0.5** — feature parity with RTSCamera.
4. **Total Bannerwar v1.0** — Total War extras (true pause, themed UI), settings polished.

---

## Reference source files to study (RTSCamera, MIT)

Public source at https://github.com/lzh-mb-mod/RTSCamera. Layout (per the repo's `.gitignore` and namespace strings dumped from the live DLLs) is:

```
source/
├── RTSCamera/                       # main camera module
│   ├── RTSCamera.csproj
│   ├── RTSCameraSubModule.cs        # entry point — OnSubModuleLoad / Harmony installer
│   ├── MissionStartingHandler.cs    # registers mission logics on battle start
│   ├── Logic/                       # MissionLogic subclasses
│   │   ├── FlyCameraMissionLogic.cs # free-cam toggle state machine
│   │   └── SubLogic/                # per-feature sub-logics
│   ├── View/                        # MissionView subclasses (camera/HUD)
│   │   └── FlyCameraMissionView.cs  # ★ THE TOP-DOWN CAMERA IMPLEMENTATION
│   ├── Patch/                       # Harmony patches
│   │   ├── Patch_MissionGauntletSpectatorControl.cs   # ★ unlocks spectator-cam in active battle
│   │   ├── Naval/                   # naval-specific patches (defer to TB v1.1)
│   │   ├── Fix/                     # base-game bug fixes
│   │   └── TOR_fix/                 # The Old Realms compat (irrelevant to TB)
│   ├── Config/
│   │   ├── RTSCameraConfig.cs       # the XML-persisted config model (we replace this with MCM)
│   │   └── HotKey/                  # hotkey definitions
│   └── CampaignGame/                # campaign-map behavior (limit-distance skill checks)
└── RTSCamera.CommandSystem/         # command-system module
    ├── RTSCamera.CommandSystem.csproj
    ├── CommandSystemSubModule.cs    # entry point
    ├── Logic/
    │   └── SubLogic/
    ├── Orders/
    │   └── VisualOrders/            # ★ world-space order indicators (selection rectangles, arrows)
    ├── Patch/
    │   ├── Patch_OrderTroopPlacer.cs  # ★ THE MOUSE → FORMATION-ORDER TRANSLATION
    │   └── Postfix_OrderController_OnTroopOrderIssued.cs
    ├── Config/
    │   └── HotKey/                  # HotKeyManager, IHotKeySetter
    ├── QuerySystem/                 # formation-state queries for command UI
    ├── AgentComponents/             # per-agent state attached to soldiers
    ├── Usage/                       # high-level usage helpers
    ├── Utilities/                   # math/raycast helpers
    └── View/                        # in-mission UI overlays
```

When implementing each Total Bannerwar component, fetch the corresponding ★ files first:

| TB component | Study these RTSCamera files |
|---|---|
| `FreeCameraMissionLogic` | `source/RTSCamera/Logic/FlyCameraMissionLogic.cs`, `source/RTSCamera/Patch/Patch_MissionGauntletSpectatorControl.cs` |
| `FreeCameraView` | `source/RTSCamera/View/FlyCameraMissionView.cs` |
| `OrderInputController` | `source/RTSCamera.CommandSystem/Patch/Patch_OrderTroopPlacer.cs`, `source/RTSCamera.CommandSystem/Patch/Postfix_OrderController_OnTroopOrderIssued.cs` |
| `OrderVisualOverlay` | `source/RTSCamera.CommandSystem/Orders/VisualOrders/` (all files) |
| `TimeControlLogic` | search for `SetMissionTimeSpeedSlower` and `slow motion` in `source/RTSCamera/Logic/` |
| `FormationOrderQueue` | the order-queue logic lives in `source/RTSCamera.CommandSystem/Logic/SubLogic/` (need to identify exact file by reading the folder) |
| `VolleyOrderSystem` | the volley logic lives in `source/RTSCamera.CommandSystem/Orders/` |
| `TBHotKeyConfig` | `source/RTSCamera.CommandSystem/Config/HotKey/HotKeyManager.cs` and related |

Raw URLs (replace `<path>` with the file path above):
- `https://github.com/lzh-mb-mod/RTSCamera/raw/refs/heads/master/<path>`
- `https://raw.githubusercontent.com/lzh-mb-mod/RTSCamera/master/<path>`

License: MIT. Quoting from their README: "You can get source code at github.com." — Original work by lzh-mb-mod (李振华 / Li Zhenhuan). When Total Bannerwar ships, the credits should mention RTSCamera as the conceptual prior art that proved the approach works on Bannerlord.

---

## Open questions

- **Naval support timing.** RTSCamera has naval-specific patches. Do we ship them in TB v1.0, or defer to v1.1? Naval testing requires War Sails DLC; complicates QA.
- **Multiplayer.** RTSCamera is SP-only. Bannerlord MP is mostly skirmish; adding RTS command to MP changes balance. Likely permanent "out of scope."
- **Compatibility with vanilla F10.** Bannerlord may already bind F10 to something (likely not, but verify). If conflict, switch to F11 or accept the override.
- **Multi-monitor / ultrawide.** Top-down ortho cam needs correct aspect handling. Test on 21:9 and 32:9 early.
