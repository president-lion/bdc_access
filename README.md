# bdc_access

**A mod that makes *Bad Dream: Coma* fully accessible.**

*Bad Dream: Coma* is a point-and-click adventure with no text in it. you wake up in a nightmare and must solve the puzzles and explore the world. This game has a lot of pictures, and I mean a hole bunch, so much to the point where a lot of the pictures needed to be hand described with the help of AI.

The mod uses prism for it's speech output.
## What it does
the mod allows you to play the game in full, including having descriptions for all the objects you can inspect, as well as keyboard navigation to navigate everything, this game originally used the mouse only.
in a room, the a and d keys are used to cycle through interactable catigories, and the up and down arrow keys are used to brows between them, and pressing enter triggers the click on what ever you selected, so it's like traditional game menus.

the scenery and other objects in the game are very important and provides a lot of the content, so they have all been described and put into a category of it's own, and sometimes looking at things is needed to advance.
to use items, open the accessible inventory menu by pressing i, and select the item. it should say active. and now interacting with the objects that you can use that item on are highlighted, and pressing on them will use that item. Objects that require varius items are also highlighted as well, although which item it needs is not to avoid the game spoilage, however it tells you that what ever item your using on that object is not the right one.

reading dialogs are as simple as them popping up, which will send them directly to speech. pressing the enter key will continue in the dialog, wile pressing the arrow keys will repeat what ever text was just said.

things such as notes, pictures, and posters are also described for you to get a good sense of the world. that includes all the puzzles too, so you can do them accessibly!

pressing the h key will show your health status and your statuses that you've  found throughout your game. To get a more in depth review of which endings you can still reach, press the s key to go to a status menu.

there is a mod settings menu in the main menu that can be opened below the exit option that contains hints and other things you can toggle off for a hardcore gaming experience, although I'm not going to be held responsible for your bastardly choices.

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
| F4 | Where am I - room, how many things, diagnostic key |
| F5 | Describe the picture in this room again(used in endings) |
| F1 | Say the area name before every entry, on/off |
| F2 | Hide ambient clutter, on/off |
| Ctrl | Stop speech |

Keys that were already the game's own - `Escape` for the pause menu, `Space`/`Escape` to
skip a conversation, `S`, right-click for the interface, `Alt+Enter` for fullscreen - stay
the game's.

## Installing

1. Download the release package and unzip it.
2. **make sure the game isn't running.**
3. Run **`Setup bdc_access.exe`**.
4. Start the game normally, with your screen reader running.

The setup window opens with the game folder already filled in, if it could find it — so
`Alt+I` from there is the whole install. If it could not, type or paste the folder that
holds `data.win`, or press **Browse** (`Alt+B`) and pick that file. `Alt+U` will uninstall,
`Alt+C` exits, and the Details box keeps everything the installer said. If the game lives
somewhere only an administrator can write to, it says so and offers to restart itself
elevated, carrying your folder over so you aren't screwed.

There is a plain `Install.bat` and `Uninstall.bat` for those who want to run it themselves.

```
Install.bat ["path\to\game folder"]
```

The installer backs up `data.win` to `data.win.BDC-A11Y-BACKUP` once, then patches *from
that backup* every time, so re-running it after an update is always clean rather than
layered. It also copies `bdcspeech.dll` and `prism.dll` next to the game.

**Removing it:** `Uninstall.bat` restores the backup and deletes the two DLLs. The game is
then exactly as it was.

You need a screen reader running, and nothing else. There is no launcher, no runtime and
no injection.

## How it works, written by claude

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

## Repository layout

```
Install.bat / Uninstall.bat   the install, and the only copy of the logic
gscripts/
  inject_a11y.csx     THE MOD - all injected GML lives here
  verify_a11y.csx     decompiles the patched file and checks every feature survived
  sweep_safety.csx    three static sweeps for the crash classes this game punishes
src/installer/        the setup window - a front end over the two batch files
src/bridge/           bdcspeech.c, the GML <-> Prism shim, and its x86 build script
bin/                  the prebuilt setup program and the two 32-bit DLLs
make_release.bat      assembles release\ and zips it into the downloadable package
```

## Building

The DLLs are prebuilt in `bin/`; rebuild only if you change them.

```
src\installer\build_installer.bat   Setup bdc_access.exe
src\bridge\build_bridge_x86.bat     bdcspeech.dll
src\build_prism_x86.bat             prism.dll   (32-bit - the game is a 32-bit process)
```

The setup program builds with the C# compiler that ships inside Windows itself (.NET
Framework 4), so there is nothing to install to build it and nothing to install to run it.

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
here is all the help I've got from all the people who indirectly helped me with the mod.
- [Prism](https://github.com/ethindp/prism) by Ethin Probst - the speechmaster.
- [UndertaleModTool](https://github.com/UnderminersTeam/UndertaleModTool) - the patcher.
- *Bad Dream: Coma* is by Desert Fox. This mod has none of its files because it will get too large; it patches your
  own copy in place and can put it back.
so enjoy this mod, and let me know if you have feedback and things.