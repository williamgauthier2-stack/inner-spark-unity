# Inner Spark — Design & Reference Doc

This is the living reference for the project: what the game is, how the code is put
together, and the vocabulary used throughout the scripts. Code comments cover the
*how*; this file is for the *why* and *what's planned* — add to it as decisions get made.

> **Note:** most of this was reconstructed from reading the code (2026-09-27), since no
> design notes existed yet. Sections marked 🟡 are inferred and worth confirming or
> replacing with your actual intent.

---

## 1. Concept 🟡

A puzzle game built around a printed circuit board. The player controls a **Spark**
that rides along copper **traces** between fixed stop points (**nodes**), starting at a
**Start** plug and trying to reach a **Goal** chip. The board has a front and back side;
**Via** nodes let the spark punch through to flip which side is "live," changing which
traces are available — this looks like the central puzzle mechanic (routing + side-switching).

*Open questions:* What's the intended difficulty curve / puzzle vocabulary beyond
side-flipping? Is there a target platform (PC/WebGL/mobile)? Any narrative framing, or
is it abstract puzzle-only?

## 2. Controls

*(Player controls. Level Editor controls are listed in the editor window's help box for each tool.)*

| Action | Keyboard | Gamepad |
|---|---|---|
| Move | WASD / Arrow keys | Left stick / D-pad |
| Flip side (on a Via) | Space | South button |
| Restart level | R / on-screen Restart button | Select |
| Confirm (advance after winning) | Space / Enter | South button |
| Pause / resume | Esc / on-screen Pause button | — |
| Advance dialog | Click Continue, or Submit while it's selected | Submit |
| Inspect board (tilt camera) | Hold left mouse + drag | — |

There is no Prev/Next level control any more (removed 2026-09-28) — winning advances to
the next level, and Stage Select in the Main Menu jumps to any level. Quitting is done
from the pause menu (Quit to Menu / Quit App), not Esc.

Movement reads an 8-way direction each frame; a new direction is only accepted once
input returns to neutral first (no key-repeat drift). Input is read in **screen space**,
so it stays intuitive even when the back of the board is shown mirrored
(`Spark.ScreenToBoard`).

While paused, all gameplay input is ignored — moves, flips, restart, confirm and mouse
inspection. Nothing pressed during the pause is queued up to fire on resume
(`PauseMenu.GamePaused`, checked by `Spark` and `BoardRig`). Gameplay input is also
blocked while a dialog is showing.

## 3. Vocabulary

| Term | Meaning |
|---|---|
| **Board** | Root of a level. Owns all nodes/traces/decorations, builds the movement graph and the generated 3D look. |
| **Node** (`PcbNode`) | A point the Spark can stop at. |
| **NodeType** | `Capacitor` (plain stop), `Via` (stop that exists on both sides — flip point), `Start` (spawn), `Goal` (level end), `Switch` (toggle point — see `SwitchMechanic` below). |
| **Trace** | A copper line between two nodes on one side of the board; the Spark slides along it automatically once entered. Can have bend points. |
| **Layer** | `Front` / `Back` — which face of the board something is on. Vias belong to both. |
| **Spark** | The player-controlled glowing sphere. |
| **Decoration** (`PcbDecoration`) | Purely cosmetic set-dressing (resistors, ICs, screw holes, silkscreen, copper pour...). Never part of the movement graph — invisible to gameplay and the level validator. |
| **Theme** (`PcbTheme`) | ScriptableObject holding every material, model override, and dimension used to render a board. One theme is shared by all levels. |
| **Rig** (`BoardRig`) | Camera pivot: frames the board, animates the turn-over, handles mouse-drag inspection. |

## 4. Architecture map

Runtime (`Assets/Script/PCB/`):
- **Board.cs** — collects nodes/traces/decorations under it, builds the exit graph (`TryPickExit`), triggers visual rebuilds, tracks front/back visibility.
- **Trace.cs** — path between two nodes (with bends), exposes path/distance queries used by both gameplay and the editor.
- **PcbNode.cs** — data-only component: type, layer, (goal) chip size, optional `model` override, and `rotationDegrees` (visual spin around the board normal, measured in board space like decorations — gameplay/trace directions ignore it).
- **PcbDecoration.cs** — data-only cosmetic component, deliberately excluded from the graph. *(Lives at `Assets/Script/PcbDecoration.cs`, one level up from the rest — same `Pcb` namespace.)*
- **Spark.cs** — player controller: reads 8-way input, walks the exit graph, animates movement/flip. Without a character it builds its own visuals (core sphere, glow, trail, direction arrows) from the theme. With a character (`model` set, via the `Spark_Sparky` prefab on `LevelManager.sparkPrefab`) it plays Animator states by name (Idle / MoveStart / MoveFinish / Win) and drives idle / travel / burst VFX; see PROGRESS 2026-09-29→30 for the sequence. `InputLocked` blocks input (dialog, goal) — never disable the component instead.
- **SwitchMechanic.cs** / **GateMechanic.cs** / **AndGateMechanic.cs** / **SwitchVisual.cs** — the switch mechanic. Two node kinds: `Switch` (normal) and `AndSwitch`, toggled with Space. A gate on a trace blocks the spark (both directions) while closed and lists its switches (Level Editor > Link tool): a normal gate flips on any press of any of its normal switches (`isOpen` = starting state); an AND gate opens only while all its AND switches are on (`inverted` = open until all on). `SwitchVisual` on a switch model swaps its ON/OFF children. Kinds are never mixed on one gate; Validate Level checks the connections.
- **DataMechanic.cs** / **HoverVisual.cs** — data pickups (capacitors only, Level Editor > Data tool). Stopping on the node collects it (it shrinks away); while any is uncollected `Board.GoalLocked` is true: a lock model (theme Goal Lock Prefab) hovers over the goal and reaching it doesn't win; the last pickup sends the lock away. Collected state is runtime-only, so a restart resets it. `HoverVisual` = the gentle bob + shrink-away used by both.
- **LockVisual.cs** — on a gate model: its CLOSED (Locked) and OPEN (Unlocked) children. Gates are built by `BoardVisuals.BuildGate` across the middle of their trace (model: gate's own → level Look → theme Gate Prefab / Gate Variants; paintable).
- **BoardRig.cs** — perspective camera framing, turn-over animation, mouse-drag tilt.
- **BoardVisuals.cs** — procedurally builds the entire 3D look (board slab, traces, node models, decorations); everything it creates is `HideFlags.DontSave` so only gameplay data is ever serialized. Uses built-in cube/cylinder shapes unless a model is assigned. Node/decoration models are placed at authored size; board tile / trace / trace bend models are **resized to fit** the theme's sizes (`Fit`), so the theme's numbers stay the source of truth for gameplay.
- **LevelManager.cs** — one per scene; owns the level list, spawn/restart/next/prev flow, win detection, on-screen HUD (`OnGUI`).
- **LevelList.cs** — ordered `ScriptableObject` list of level prefabs (the "play order").
- **PcbTheme.cs** — ScriptableObject: materials, default model slots (nodes, decorations, board tile, trace, trace bend), a **Catalog** of variant models offered by the Level Editor, and every tunable size/color.
- **PcbTypes.cs** — the core enums (`PcbLayer`, `NodeType`, `DecorType`).
- **PcbMechanics.cs** — defines `TraceMechanic` (`CanEnter` / `OnTraversed`) and `NodeMechanic` (`OnSparkArrive` / `OnSparkLeave`) base classes for gameplay modifiers beyond plain routing + flipping. First concrete pair added 2026-09-28 (`feat/switch`, merged 2026-09-29):
  - **SwitchMechanic.cs** (`NodeMechanic`, put on a `Switch` node) — `Toggle()` flips `isOn` and fires a `UnityEvent<bool> onToggle`. Triggered from `Spark.TryFlip()`: pressing the flip input on a `Switch` node calls `Toggle()` instead of the normal via-flip.
  - **GateMechanic.cs** (`TraceMechanic`, put on a `Trace`) — `CanEnter` blocks the Spark unless `isOpen`; `SetOpen(bool)` is the usual `onToggle` target. Auto-generates a red placeholder cube at the trace midpoint if no `closedVisual` is assigned.
  - Wired example: in `Level 04`, a Switch node's `onToggle` calls a Gate's `SetOpen` — a working switch-opens-gate puzzle piece. This single-switch-per-gate pattern (wire `onToggle` → `SetOpen` by hand in the Inspector) is still the way to do it when only one switch controls a gate.
  - 🟡 Worth a look: both scripts still have `Debug.Log` calls left in from development, and `GateMechanic.CanEnter` ignores the `reversed` parameter (blocks the trace from both directions, may or may not be intended).
  - **AndGateMechanic.cs** (added 2026-09-29, subclasses `GateMechanic`) — for the "several switches must *all* be on" case. Doesn't touch `GateMechanic`/`SwitchMechanic` at all; just assign a list of `SwitchMechanic`s in its `switches` field and it self-subscribes to each one's `onToggle` (no manual per-switch event wiring needed, unlike the single-switch case) and calls the inherited `SetOpen` whenever every switch in the list is on. Inherits the placeholder-visual behavior from `GateMechanic` unchanged.
- **PcbVisualOwner.cs** — tags generated 3D parts with the source node/trace/decoration so scene clicks select the real object.

Editor tooling (`Assets/Script/PCB/Editor/`):
- **PcbAssetSetup.cs** — auto-generates the theme, materials, and sprites under `Assets/PCB` on first load; also upgrades an incomplete theme.
- **PcbLevelEditorWindow.Look.cs** — the Level Editor's **Look (this level)** section (per-level default models, Copy Look From) and **Paint** tool (per-node/decoration models).
- **PcbLevelEditorWindow.cs** + **PcbLevelEditorWindow.Levels.cs** — `Tools > PCB > Level Editor`. Scene-view tools to place/drag/erase nodes, traces (with 45° auto-routing), and decorations; save/load levels as prefabs; a content-hash based "unsaved changes" indicator; and a **Validate Level** pass that flags: wrong start/goal counts, traces crossing layers without a via, ambiguous overlapping exits, exits only reachable via diagonal input, and vias with traces on only one side.
- **PcbSelectionRedirect.cs** — global hook so clicking a generated visual in the Scene view selects its owning node/trace instead.

Audio (`Assets/Script/Audio/`):
- **SoundBank.cs** — ScriptableObject (`Create > PCB > Sound Bank`): one clip + balance volume per sound (menu/gameplay music, UI click, start game, win, switch, pickup, goal unlock, gate open/close, fail, travel loop, dialog talking loop). Empty = silent.
- **AudioManager.cs** — one per scene; the first survives scene loads (music continues), duplicates remove themselves. Static, null-safe API (`Play(Sfx)`, `PlayMusic`, `SetPaused`, `SetTravelling`, `SetTalking`); auto-adds the click sound to every UI Button. Player Music/SFX volumes in PlayerPrefs.
- **VolumeSlider.cs** — on a UI Slider: Music or SFX volume.

Staging:
- **ScreenFader.cs** (UI) — self-creating black overlay; `FadeOut` / `FadeIn` / `LoadScene` (fade out → load → fade in, unless the new scene `Claim()`s the fade-in).
- **StageIntro.cs** / **FadeGroup.cs** — per-stage intro cinematic prefab on `Board.intro`: placed at the board's centre while black, Timeline plays after the fade-in, the camera follows its Camera Pose, then blends into the gameplay view. `FadeGroup` fades a whole model via one animatable Alpha.
- Stage start order (`LevelManager.EnterStage`): black → fade in → music → [intro] → `Spark.Appear()` → [dialog] → play. Restart: quick fade → appear → play. `LevelManager.boardAnchor` = where every board is centred (fixed spot in the room).

- Ending: winning the last stage in the Level List → `LevelManager.endingScene` ("Ending") with a fade; **EndingScreen.cs** there (ending music, click after 2 s → Main Menu).
- **Progress.cs** — saved furthest unlocked stage (PlayerPrefs); finishing a stage unlocks the next; Play continues from it; Stage Select only lists unlocked stages. Reset: Tools > PCB > Reset Progress (testing only).

UI & game flow (`Assets/Script/UI/`, uGUI + TextMeshPro, kept separate from `PCB/`):
- **GameFlow.cs** — static bridge carrying the chosen level index from the `MainMenu` scene into the gameplay scene (`RequestLevel` / `HasPendingRequest` / `TakeRequestedLevel`).
- **MainMenuController.cs** — Play / Stage Select / Quit on the `MainMenu` scene's main panel.
- **StageSelectController.cs** — builds one button per level from `LevelList` at runtime; Back returns to the main panel.
- **PauseMenu.cs** — Esc / Pause button toggles the pause panel (`Time.timeScale = 0`); Resume / Quit to Menu / Quit App; greys out the Restart button while paused; exposes static `GamePaused` for gameplay scripts.
- **DialogSequence.cs** — `ScriptableObject` (`Create > PCB > Dialog Sequence`): lines of `{ speakerName, portrait, text }`. One per stage, assigned on `Board.dialogSequence`.
- **DialogController.cs** — shows/advances the visual-novel-style dialog panel before play starts; `LevelManager` disables the Spark until it's dismissed.

Scenes: `MainMenu` (build index 0) → `SampleScene` (gameplay, build index 1).

## 5. Level data & workflow

- A level = a `Board` prefab under `Assets/PCB/Levels/`, referenced (in play order) by the single `LevelList.asset` at `Assets/PCB/LevelList.asset`.
- Saved prefabs contain **gameplay data only** — nodes, traces, decorations, and their settings. The 3D look is always regenerated at load (`Board.Rebuild`) from the shared `PcbTheme`, never serialized.
- **Visual choices are per level, stored as references:** which model is used follows
  node/decoration `model` → the level's `Board.look` → the theme slot. Levels store references to
  the chosen prefabs (not catalog indices), so reordering the theme's catalog never breaks a level.
- Levels are authored directly by drawing in the Scene view with the Level Editor window open, not through any external tool or file format.
- Current levels: `Level 1`, `Level 02`, `Level 03`, `Level 04` (4 total as of 2026-09-27).

## 6. Open design space 🟡

Fill these in as they get decided — flagging them so future sessions don't have to
reverse-engineer intent from code again:

- [x] What gameplay mechanics should `TraceMechanic`/`NodeMechanic` actually cover first? First one landed 2026-09-28: a `Switch` node toggling a `Gate` trace (see § 4). Still open: what comes after this — one-way traces, keys/pickups, timed elements?
- [ ] Target scope: how many levels, roughly what difficulty/length arc?
- [ ] Any progression/meta layer (level select screen, unlocks) beyond the built-in Prev/Restart/Next HUD?
- [ ] Target platform(s) and any performance constraints that should shape `BoardVisuals`' generated-geometry approach.
- [ ] Audio — nothing exists yet (no sound-related scripts).
- [ ] Visual identity beyond the placeholder-generated theme materials in `PcbAssetSetup`.
  Art has been imported into `Assets/Import Asset/` (character `Mesh_Sparky_Animated` + face/palette
  textures; `LevelProps`: capacitors, LEDs, switches ON/OFF, start/end plug nodes, PCB board modules,
  path module; a level scene FBX; portrait icons) but **not tested yet** and not wired into anything —
  likely destination is `PcbTheme`'s optional model slots (`capacitorPrefab`, `viaPrefab`,
  `startPrefab`, `goalPrefab`, `decorationPrefabs`) or the newer Catalog/Look system (§ 5).
  A second art drop (2026-09-29, "Andrii") added a large batch of ready-to-use pieces on top:
  per-color materials for LEDs/PCB boards/nodes, more `PlugNode` size variants, Sparky's own
  materials + textures, and three VFX prefabs (`VFX_BurstOfSparks`, `VFX_Sparky_Idle`,
  `VFX_Sparky_Trail`) — presumably for the Spark's idle/movement feel. Also not wired in yet.
