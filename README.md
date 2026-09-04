# bdc_access

**A mod that makes *Bad Dream: Coma* fully accessible.**

*Bad Dream: Coma* is a point-and-click adventure with no text in it. Every menu label is a
picture, every note and poster is a picture, every puzzle hint is a mark on the mouse
cursor. It cannot be played without sight. This mod makes it playable with a screen
reader and the keyboard alone - the whole game, from the title screen to all three
endings.

Speech goes out through [Prism](https://github.com/ethindp/prism), so it lands in whatever
you already use: NVDA, JAWS, ZoomText, Narrator or SAPI.

Nothing is replaced. The mod calls the game's own handlers, so a sighted player sees the
same highlights and hears the same clicks, and the mouse works exactly as it always did.

---

## What it does

**Menus.** Reads every menu item, names each screen and its item count, and adds keyboard
navigation - the game itself is mouse-only, with no "selected item" at all. Sliders read
as percentages and adjust with left/right; toggles read their state and flip.

**The room.** Arrow keys walk everything you can interact with, left to right: *"3 of 7,
Basement Door, locked"*. `Enter` acts on it, which is exactly what clicking it does. Exits
are included - most of them are invisible hotspots - and **"Go back" is always first**.
Rooms are busy, so **A** and **D** narrow the list to Exits, Objects or Scenery.

**Scenery.** The art is content in this game. Every scene, and every close-up you can
open, has been described, so `Enter` on a piece of scenery says what the picture shows.

**Using items on things.** Hold an item and walk the room: the thing it works on says
**"USE crowbar HERE"**, and anything else that wants an item says so too. The game signals
this only with a mark on the cursor, so item puzzles were otherwise unsolvable.

**Dialogue.** Reads every line. `Enter` advances; any arrow key repeats the line you are on.

**Notes and posters.** All 26 - notes, newspapers, patient cards - are pictures with no
text behind them. Every one has been transcribed, phone numbers and patient records
included.

**Puzzles.** The dials, keypads, the phone, the fuse box, the board game and the one
drag-and-drop puzzle each read their own state, so a press is audible and a position is
knowable.

**You.** `H` reads your health and what you are carrying. `S` opens the status screen and
reads every condition, plus whether each ending is still reachable - the game shows that
as a padlock. Damage, pickups and conditions are announced as they happen; all three were
silent.

**Autosaves.** Announced. The game marks them with a caption that fades.

**Its own settings.** The last item on the title screen and in the pause menu opens the
mod's settings: danger warnings on/off, hints on/off, area names, hide clutter. They are
remembered in `A11y.ini`.

## Keys

| Key | Action |
| --- | --- |
| Arrow keys | Move between menu items, or between things in the room |
| Enter / Space | Activate |
| A / D | Filter the room: Everything, Exits, Objects, Scenery |
| I | Open / close the inventory reader |
| H | Health, and what you are holding |
| S | Status screen |
| F3 | Repeat the current item |
| F4 | Where am I - room, how many things, what is blocking |
| F5 | Describe the picture in this room again |
| F1 | Say the area name before every entry, on/off |
| F2 | Hide ambient clutter, on/off |
| Ctrl | Stop speech |

Keys that were already the game's own - `Escape` for the pause menu, `Space`/`Escape` to
skip a conversation, `S`, right-click for the interface, `Alt+Enter` for fullscreen - stay
the game's.

## Installing

1. Download the release package and unzip it.
2. **Close the game.**
3. Run `Install.bat`. If it cannot find the game, paste the folder holding `data.win`.
4. Start the game normally.

The installer backs up `data.win` to `data.win.BDC-A11Y-BACKUP` once, then patches *from
that backup* every time, so re-running it after an update is always clean rather than
layered. It also copies `bdcspeech.dll` and `prism.dll` next to the game.

**Removing it:** `Uninstall.bat` restores the backup and deletes the two DLLs. The game is
then exactly as it was.

You need a screen reader running, and nothing else. There is no launcher, no runtime and
no injection.

## How it works

The mod is a **patch to the game's `data.win`**. The accessibility code is GML, appended
to the `Controller` object - which is persistent, so one tick covers the whole game.

Every menu label is a pre-rendered sprite, one frame per language, drawn with
`draw_sprite`. There is no menu text anywhere to intercept, so the buttons are named from
a table keyed on GameMaker object name. Dialogue and items are the opposite case: both
carry real, already-localised text, so those needed no table at all.

`bdcspeech.dll` is a small C shim around Prism, called from GML with `external_define` /
`external_call` - GML can only pass doubles and strings across that boundary, which is why
a shim exists at all. If either DLL is missing the calls simply fail and the game plays on
as if unmodded.

The full reverse-engineering notes - every puzzle, every trap, and why each decision went
the way it did - are in [FINDINGS.md](FINDINGS.md).

## Repository layout

```
Install.bat / Uninstall.bat
gscripts/
  inject_a11y.csx     THE MOD - all injected GML lives here
  verify_a11y.csx     decompiles the patched file and checks every feature survived
  sweep_safety.csx    three static sweeps for the crash classes this game punishes
  (the UTMT and Ghidra scripts used for the research)
src/bridge/           bdcspeech.c, the GML <-> Prism shim, and its x86 build script
bin/                  the two prebuilt 32-bit DLLs
FINDINGS.md           reverse-engineering notes
```

## Building

The DLLs are prebuilt in `bin/`; rebuild only if you change them.

```
src\bridge\build_bridge_x86.bat     bdcspeech.dll
src\build_prism_x86.bat             prism.dll   (32-bit - the game is a 32-bit process)
```

Patching needs the [UndertaleModTool](https://github.com/UnderminersTeam/UndertaleModTool)
CLI in `tools\UTMT_CLI\`. The release package already includes it.

## Checking a patch

A patch can compile cleanly and still be wrong, so check the *decompiled* result:

```
tools\UTMT_CLI\UndertaleModCli.exe load "<game>\data.win" -s gscripts\verify_a11y.csx
tools\UTMT_CLI\UndertaleModCli.exe load "<game>\data.win" -s gscripts\sweep_safety.csx
```

The verifier confirms that `VarCount1` is still 0 - this game declares no globals of its
own, and introducing one makes the bytecode-15 runner die on first access - that no arrays
crept in, and that each feature's marker survived compilation. The sweeps look for reading
a variable before it is assigned, dereferencing an instance that was never checked for, and
dereferencing one after running its own code could have destroyed it. All three are crashes
this game has actually produced.

## Credits

- [Prism](https://github.com/ethindp/prism) by Ethin Probst - the screen-reader layer.
- [UndertaleModTool](https://github.com/UnderminersTeam/UndertaleModTool) - the patcher.
- *Bad Dream: Coma* is by Desert Fox. This mod ships none of its files; it patches your
  own copy in place and can put it back.
