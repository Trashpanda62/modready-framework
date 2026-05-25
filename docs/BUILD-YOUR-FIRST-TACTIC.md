# Build your first tactic with BetaDeps

This guide walks you through making your first Bannerlord tactic mod with the BetaDeps Tactics Editor. No coding required. You'll plan a battle by clicking, save it as a mod folder, and then other players can install your mod and use your tactic in their own battles.

If you've never made a Bannerlord mod before, this is the simplest possible path. Total time: about 15 minutes for your first tactic, faster after.

## What you need before starting

1. Mount and Blade II: Bannerlord installed.
2. BetaDeps v1.0 installed in your `Modules\` folder. (Download it from the Nexus mod page if you don't have it.)
3. The BetaDeps Tactics Editor enabled in your launcher. It ships inside the BetaDeps v1.0 zip as a separate folder named `BetaDeps.TacticsEditor`. Check the box next to it in the launcher just like you check any other mod.

That's it. No Visual Studio, no C#, no SubModule.xml editing.

## What "a tactic" actually means

A tactic is a saved plan that says "when a battle starts, put my infantry here facing this way, put my archers there, put my cavalry on the right flank, and tell them to hold position until the enemy gets close."

When you save your tactic, BetaDeps writes a folder under your `Modules\` directory that looks exactly like a regular Bannerlord mod. You can zip that folder up and upload it to Nexus or share it with a friend the same way any other mod gets shared.

## Step 1 — Start a battle

Load your campaign and pick a fight. Any battle will do — a bandit ambush is the simplest, but a siege or a field battle works equally well. Wait until you're past the deployment screen and your troops have spawned on the field.

## Step 2 — Enter the editor

Press **F11**. You'll see a banner appear at the bottom of the screen confirming you've entered edit mode. It also lists the key controls so you don't have to memorize them.

## Step 3 — Plan your formations

You have ten slots (1 through 9, plus 0 for the tenth). Each slot represents one formation. Here's the simplest possible workflow:

1. Press **1** to select slot 1. The banner confirms slot 1 is selected.
2. Move your mouse to where you want your first formation to stand. Anywhere on the ground works.
3. Click the **left mouse button**. A confirmation banner says "slot 1 placed at X,Y".
4. Press **R** a few times to rotate the formation's facing direction. Each press rotates 30 degrees. Hold Shift while pressing R for a finer 5-degree rotation.
5. Press **C** to cycle the formation class (Infantry → Ranged → Cavalry → HorseArcher). Pick the type you want this slot to be.
6. Press **F** to cycle the arrangement (Line → Loose → Circle → ShieldWall → Skein → Column → Square).
7. Press **B** to cycle the behavior (Hold → Defend → Charge → FollowMe → Retreat).

Now press **2** and repeat for slot 2. Keep going until you've placed every formation you want.

If you make a mistake, press the number of the slot you want to fix, then click to reposition it, or press **Delete** to remove that slot from the plan entirely.

## Step 4 — Save your tactic

Hold **Ctrl** and press **S**. The Tactics Editor will write a new folder under `Modules\` called something like `Tactic_20260525_143015` (the timestamp ensures every save is unique).

A confirmation banner tells you exactly where it was saved. If BetaDeps can write to your real Modules folder, it'll say "Enable 'Tactic_...' in the launcher next time you start the game." If for any reason it can't (some users have read-only Modules folders), it falls back to your Documents folder and tells you to copy it over manually.

## Step 5 — Rename your tactic (optional but recommended)

Close Bannerlord. Open your `Modules\` folder. Find the `Tactic_<timestamp>` folder you just made. Rename it to something memorable — like `BoldChargeTactic` or `DefensiveLineTactic`. Open `SubModule.xml` inside that folder with Notepad and change the `<Name value="..." />` line to whatever you want it to show up as in the launcher.

## Step 6 — Test it

Reopen Bannerlord's launcher. You should now see your tactic mod listed alongside everything else. Check the box next to it. Load your save. Start a battle.

When the battle begins, you'll see entries in BetaDeps's runtime.log confirming the tactic was applied — and your formations will spawn at the positions you saved. Watch them carry out your plan.

## Step 7 — Share it

If you want other players to use your tactic, all you need to do is zip up the entire `Modules\<YourTacticName>\` folder and upload the zip to Nexus, share it on Discord, or hand it to a friend. They unzip into their own `Modules\` folder, check the box in the launcher, and your tactic is theirs to use.

## What works in v1.0 vs what's coming

**Works now in v1.0:**

- Saving position and facing for up to 10 formations.
- Per-slot formation class, arrangement, and behavior are recorded in the plan file.
- At battle start, your saved positions are applied: formations move to where you placed them.
- Exported mod is a self-contained drop-in.

**Coming in v1.1:**

- Arrangement (Line / Loose / Circle / etc.) and behavior (Hold / Charge / etc.) currently log their intent but don't change the formations yet. v1.1 wires them through the engine's order controller. For v1.0, formation position is the main thing your tactic controls.
- A visible HUD overlay showing where each saved formation will appear, so you can see the plan in 3D while you build it.
- A name-the-tactic prompt before save instead of the timestamp default.

## Troubleshooting

**"Nothing happened when I pressed F11."** Make sure BetaDeps.TacticsEditor is checked in your launcher. Open `Modules\BetaDeps\runtime.log` and search for "TacticsEditor" — you should see "TacticsEditorMissionLogic initialized". If you don't, the module isn't loading. Run Mod Config → Run Self-Test to find out why.

**"I saved but the tactic isn't applied."** Check the launcher — did you enable the exported `Tactic_<timestamp>` folder? Tactics are full mods; you have to enable them just like any other mod.

**"It applied but formations aren't where I put them."** This usually means the formations you placed don't exist on the team in this battle (e.g. you placed 4 formations but this battle's team only has 3 live formations because of troop composition). Open runtime.log and search for `[Tactic_` — you'll see exactly which slots applied and which were skipped.

**Need help?** Run Mod Config → Run Self-Test → Upload to GitHub, or paste `Modules\BetaDeps\selftest.log` into a Nexus comment. Don't worry about the runtime.log — selftest.log embeds the relevant runtime triage automatically.
