# Bad Dream: Coma — Accessibility Mod: Findings

## Target

| Property | Value |
| --- | --- |
| Engine | GameMaker Studio **1.4.1763** (VC_Runner, VM bytecode — not YYC) |
| Executable | `Bad Dream Coma.exe`, **x86 32-bit**, 3.9 MB, image base 0x400000 |
| Data | `data.win` 237 MB, FORM/GEN8, 10239 code entries, 4635 objects, 288 rooms, 2933 sprites |
| Build | GOG, 2020-09-16 |

The runner is a stock GMS 1.4 build with debug paths left in `.rdata`
(`c:\hudson\GMBase\GMGreen\GameMaker\Runner\VC_Runner\...`), which gives cheap
anchors for locating engine internals.

## Toolchain (all local to `e:\modgames\bdc\mod`)

- `Reloaded-II/` — portable 1.30.3. **`Loader\X86`** is the relevant loader (32-bit game).
- `src/prism/` — upstream clone, built to `src/prism/build-x86/prism.dll`, verified
  **x86 32-bit**, backends: nvda, jaws, sapi, onecore, uia, zoom_text, sense_reader,
  pc_talker, zdsr, boy_pc_reader.
- `tools/UTMT_CLI` — UndertaleModTool CLI 0.9.2.0, used for decompiling.
- Ghidra 12.1.2 headless project at `research/ghidra_proj` (9368 functions, 9301 strings).

### Build note
Batch files written from bash get LF endings and cmd silently fails on the `^`
continuations — it drops to an interactive prompt and the log shows only a banner.
All `.bat` here must be CRLF.

## The central finding: the menus are sprites, not text

**There is no menu text to intercept.** Every menu label is a pre-rendered sprite,
one frame per language, drawn with `draw_sprite`. Hooking `draw_text` — the usual
first move for a GameMaker game — returns nothing for menus.

`Main_Menu_Button` (the parent of the five title-screen buttons):

```gml
// Step_0
active = 0;
if (instance_exists(BLOCK_EXIT)) exit;
if (mouse_x > (x - xx) && mouse_x < (x + xx))
    if (mouse_y > (y - yy) && mouse_y < (y + yy)) {
        active = 1;
        if (mouse_check_button_pressed(mb_left) && !instance_exists(Pause))
            event_user(0);          // the button's action
    }
image_index = (Controller.language * 2) + active;

// Draw_0
draw_sprite(sprite_index, image_index, x, y);
```

Consequences for the design:

1. **Item identity comes from the object name**, not from drawn text — so the mod
   needs an object-name → spoken-label table, and that table is the deliverable
   that makes the menus readable.
2. `image_index = language * 2 + active` means the **hover state and the current
   language are both readable straight off the instance** (`active`, and
   `Controller.language`).
3. The menus are **mouse-driven and have no selection index** — there is no
   "currently selected item" to read. Keyboard navigation has to be *added*, not
   merely announced.

### Two different button mechanisms

| Menu | Mechanism | Hover state |
| --- | --- | --- |
| Title screen (5 buttons, parent `Main_Menu_Button`) | manual bbox test in `Step_0` | `active` var, 0/1 |
| Pause / options (`Game_Menu_*`, ~20 objects, no parent) | GameMaker built-in `Mouse_4` (sprite collision) | **none** — `_language_image_index()` sets only `image_index = Controller.language` |

So the pause menu gives us nothing to read at all; hover there must be computed by
the mod from instance bounding boxes.

## Menu inventory

Title screen — children of `Main_Menu_Button`:
`Main_Menu_New_Game`, `Main_Menu_Load_Game`, `Main_Menu_Options`,
`Main_Menu_Credits`, `Main_Menu_Exit`.

Pause/options — standalone `Game_Menu_*`: `Resume`, `Exit`, `Restart`, `Autosave`,
`Volume` (+ `Volume_Slider`, `Volume_Music_Slider`, `Volume_SFX_Slider`, `Volume_Mute`),
`Sound_3D`, `Brightness` (+ `Brightness_Slider`), `Screen_Window`,
`Screen_Window_Size`, `Screen_Set_Window`, `Screen_Fullscreen`,
`Screen_Set_Fullscreen`, `Screen_Fullscren_Center` *(sic — engine typo, match it exactly)*.
Confirmation dialog: `Game_Menu_Sure_Window`, `Game_Menu_Sure_Yes`, `Game_Menu_Sure_No`,
with actions under parent `Game_Menu_Sure_Action`
(`Sure_Exit_Game`, `Sure_Restart`, `Sure_Autosave`).

Other button families seen in `objects.txt`, for later:
`Menu_Btn` (3), `SLOT_BTN` (3), `Slot_01` (4), `Menu_Thing` (5), `Interactive_Object` (13).

## Open question — the runtime read path

The mod must read, per frame: the live instance list, each instance's object index,
`x`/`y`/`active`, and `Controller.language`. Candidate Ghidra anchors:

- `"Unable to find any instance for object index '%d' name '%s'"` @ `0x2a6818` —
  sits inside the instance lookup, which exposes the instance list traversal.
- `.\Files\Variable\Variable_BuiltIn.cpp` @ `0x2a584c` and
  `"INTERNAL ERROR: Adding too many variables"` @ `0x2a6730` — the variable subsystem,
  for resolving `active` / `language` by name.
- `"<unknown built-in variable>"` @ `0x2a675c`.

## Languages

`Controller.language` is an index 0-6, set by `_language()` from `Config.ini` `[Config] Lan`
(GOG build: no Steam, so it is read straight from the ini, defaulting to 0):

| 0 | 1 | 2 | 3 | 4 | 5 | 6 |
| --- | --- | --- | --- | --- | --- | --- |
| English | Polish | Spanish | Russian | German | French | Italian |

Sprite frame counts confirm the formula exactly: title-screen buttons have **14**
frames (7 languages x 2 hover states), pause buttons have **7** (7 x 1). So the
label table is keyed on object name and wants 7 language variants; English first.

Note `Controller_Create_0` sets `language = 5` before calling `_language()`, so 5 is
only a transient default and must not be read as "French" before `_language()` runs.
## The runtime API (solved)

The exe has **no `.reloc` and no ASLR** (`DllCharacteristics = 0x8000`), so it always
loads at `0x00400000` and every address below is a stable constant. The mod still
verifies bytes at each anchor on startup, so a different build fails loudly instead of
corrupting memory.

Rather than hardcode dozens of function addresses, the mod hardcodes **four anchors**
and resolves everything else *by name* at runtime from the runner's own registration
tables.

### Anchor 1 - the builtin function table

`Code_Function_Add(name, func, minargs, maxargs)` @ `0x00526BF0` fills a flat array
(`.\Files\Code\Code_Function.cpp`):

| | |
| --- | --- |
| array pointer | `*(void**)0x009D8D84` |
| entry count | `*(int*)0x009D8D88` |
| capacity | `*(int*)0x009D8D8C` |
| stride | `0x50` |
| `name` | `+0x00` (inline char array) |
| `func` | `+0x40` |
| `minargs` | `+0x44` |
| `maxargs` | `+0x48` (byte) |

2513 functions are registered (`research/table_functions.txt`).

### Anchor 2 - the builtin variable table

`Variable_BuiltIn_Add(name, getter, setter, flags)` @ `0x0040D050`, capacity 500
(overflow prints `INTERNAL ERROR: Adding too many variables`):

| | |
| --- | --- |
| array base | `0x007A40F8` |
| entry count | `*(int*)0x007A6038` |
| stride | `0x10` |
| `name` ptr | `+0x00` |
| `getter` | `+0x04` |
| `setter` | `+0x08` (0 = read-only) |

211 variables are registered (`research/table_variables.txt`).

### Anchor 3 - instance id -> CInstance*

`Instance_FromId(int id)` @ `0x0040D3B0` - a hash bucket walk
(`buckets = *(void**)0x0077A494`, `mask = *(int*)0x0077A498`, node `{?, next@4, id@8, inst@0xC}`).
Returns 0 for a dead id, so it doubles as a liveness check.

### Anchor 4 - image base `0x00400000`

### Calling conventions

Both recovered from decompiled callees, `__cdecl` throughout:

```c
// builtin function
void f(RValue* result, CInstance* self, CInstance* other, int argc, RValue* args);
// builtin variable getter / setter
bool getter(CInstance* self, int arrayIndex, RValue* out);
```

`RValue` is **16 bytes**: `double val @ 0x00`, `flags @ 0x08`, `int kind @ 0x0C`.
Kinds seen: `0` real, `1` string, `6` object (a raw `CInstance*` in the low dword).

### What the mod actually calls

Confirmed present in the tables (addresses for reference only - looked up by name):

| Builtin | Address | Use |
| --- | --- | --- |
| `instance_number` / `instance_find` | `0x004E28B0` / `0x004E2F30` | enumerate live buttons |
| `object_get_name` | `0x00506680` | identify a button |
| `window_mouse_set` | `0x004E5550` | warp the cursor for keyboard nav |
| `room_get_name` | `0x00506790` | detect screen changes |
| var `x`, `y` | `0x00404060`, `0x00404090` | button position |
| var `bbox_left/right/top/bottom` | `0x004043E0`/`420`/`4A0`/`460` | cursor target, hover test |
| var `object_index`, `id`, `visible` | `0x00404390`, `0x004042D0`, `0x00404350` | identity and filtering |
| var `mouse_x`, `mouse_y` | `0x00402960`, `0x00402990` | current pointer |

`bbox_*` is the important one: it removes any need to replicate the sprite-origin maths
that `Main_Menu_Button.Step_0` does by hand.

### Why `active` is never read

`variable_local_get` @ `0x004F9F50` resolves to
`Variable_GetValue(self, varId, idx, out)` @ `0x0040D170`, which only takes the clean
builtin path for `varId < 10000`; user-defined instance variables such as `active` go
down a murkier path that the decompiler does not recover cleanly.

It turns out not to matter. `active` is derived state - the game computes it from the
pointer against the button's bbox - and the mod already knows the bbox and, when it is
driving keyboard navigation, already knows which button it moved to. So the mod reads
only builtin variables and stays off that path entirely.

## Design (historical - the retired Reloaded-II build)

> Kept for the record. This describes the injection approach that was abandoned
> in favour of patching `data.win`; see *Design (current)* below.

Per frame, on the main thread (a `PeekMessageW` hook, the same tick pattern as the
Jackbox mod - GameMaker pumps messages on the main thread once per frame, and it
intercepts `WM_KEYDOWN` in the same place):

1. Enumerate live instances of the known menu objects via `instance_number` /
   `instance_find` -> `Instance_FromId`.
2. Read `bbox_*` for each, sort into reading order (y, then x).
3. **Arrow keys** move a selection index and call `window_mouse_set` to warp the real
   cursor to the button's centre. The game's own `Step_0` bbox test then lights the
   button up exactly as if the mouse had been moved by hand - no game logic is bypassed,
   and the highlight the sighted player sees stays correct.
4. **Enter** synthesises a real left click at that position, so it works for both button
   mechanisms (the manual `mouse_check_button_pressed` test *and* the built-in `Mouse_4`
   events).
5. Speak the label for the object name, from the label table.

Nothing in `data.win` is modified.
## Design (current)

Everything is appended to `Controller`'s Create and Step events. `Controller` is
persistent and placed in `Lvl_Main_Menu`, so one tick covers the whole game.

### Menus

Per frame: enumerate live instances of the known menu objects, sort into reading order
(y within a 24px row tolerance, then x), and speak the label the object name maps to.
Arrows move the index; Enter activates through the game's own handler.

### Dialogue

`Dialogue` is the parent of 843 conversation objects and, unlike the menus, holds **real
text**: `dialog_list` is a `ds_list`, `current_text` is the visible line, and user event 0
advances. Watching `current_text` therefore covers every conversation in the game with no
per-script work.

Its own key bindings matter, because two of them already exist:

| Key | Owner | Effect |
|---|---|---|
| `Enter` | the mod | advance one line |
| any arrow | the mod | repeat the current line |
| `Space` | **the game** (`Dialogue` KeyPress 32) | `instance_destroy()` - skips the whole conversation |
| `Escape` | **the game** (`Controller` KeyPress 27) | routes to the same space handler |

So the mod must *not* bind Space. An earlier version did, and pressing it advanced a line
and destroyed the box in the same frame.

Advancing goes through `event_perform(ev_mouse, 53)` - `ev_global_left_press`, the same
event a click raises - so the game's own guard (`alarm[0] == -1`, i.e. wait for the line
to settle) still applies and lines cannot be skipped by holding the key.

Repeating reads `a11y_dlg_last`, not `current_text`. Between lines `Mouse_53` blanks
`current_text` and sets `alarm[0] = 15`, so for a quarter of a second there is nothing to
read; repeating from the live variable would say nothing at all if the key landed there.

Dialogue advances only on input - `alarm[0]` is armed by a click, never by a timer, and
the only automatic case is a deliberately blank line - so speech can never be cut off by
the game moving on by itself.

### Chapter title cards

The screen that comes up after loading a save, and between chapters. `Chapter_Screen`
parents all 15 (`Chapter_Bridge` … `Chapter_Ending`, plus the eight `Chapter_End_*`),
and every one carries **real localised text** in two instance variables:

| | set by | example |
|---|---|---|
| `chapter` | `_chapter_names_set` / `_chapter_end_names_set` | "Chapter I:" |
| `description` | same | "BRIDGE" |

A grep for `_chapter_names_set` alone suggests the eight `Chapter_End_*` cards never set
them — they call `_chapter_end_names_set`, which is a different name that does not
contain the first as a substring. They do set both. Worth knowing, because reading a
variable that is genuinely absent is fatal.

End-of-chapter cards additionally show what was just unlocked, one `Status_Chapter_Info`
instance per item, each holding a `name`. Those are drawn as icons and their names appear
only on hover, so without reading them out they are lost entirely. The heading comes from
`_text_chapter_end_unlocked()`, a pure string getter.

These cards **ignore input for their first 30 frames** — `alarm[0]` is what sets
`active` — so Enter genuinely does nothing at first, exactly as a click would. Advancing
goes through the card's own space keypress, which keeps that guard and whichever room
transition the particular card wants (`Room_Translation`, or
`Room_Translation_Special` with a `target_room` for the end cards). Space and Escape
already work here unaided; Enter is the only addition.

### Inventory

The inventory is **not a screen you open**. `Inventory_Controller` draws up to eight
carried items down one side of the play area every step, positioned from
`instance_find(Item, n + shift)`, and a click makes one the active item. `I` therefore
toggles a *reading mode* rather than opening anything: it walks all carried items, not
just the eight on show, and drags the game's own `shift` window along so the display keeps
matching what is being read.

**How a click on an item actually reaches the item.** Nothing dispatches to `Item`
directly and there is no `Mouse_4` event, which made this look unsolvable at first. The
dispatcher is `Controller`'s **End Step**:

```gml
interactive_id = _interactive_get_id(_interactive_get_type());
with (interactive_id) event_user(1);                      // hover -> info text
if (mouse_check_button_pressed(mb_left) && interactive_id != -4)
    with (interactive_id) event_user(0);                  // click
```

`_interactive_get_id` walks `collision_point` at the mouse, deactivating each hit in turn
to find them all, then picks the shallowest `playable` one. So on any interactive object:

* **user event 0 is the click**, and
* **user event 1 is the hover**, which sets `Controller.text_info`.

That settles the question that stalled inventory support: `Enter` calls
`with (item) event_user(0)`, which is the genuine article, not an imitation of one - it
toggles `active`, updates `Controller.active_item`, and plays the game's own click sound.
`Item`'s `Mouse_56` (global left *release*) only clears the pressed-look flag, and its
user event 1 is a tooltip, so neither is an activation path.

Items carry real localised text: `name` per item, plus `active_txt` (" (Active)",
localised the same way) appended when the item is the active one.

Guards, mirroring `Inventory_Controller`'s own early exits: no `Chapter_Screen`, no
`NO_INVENTORY`, no `Item_Destroyed` mid-animation, no `Pause`/`BLOCK_EXIT`, no dialogue.
If any of those appear while the reader is open it closes itself rather than trapping the
arrow keys. Opening it also forces `show_gui = 1`, since a right click hides the whole
interface and reading out items that are not on screen would be a lie.

`Item_Break_Left`, `Item_Break_Right`, `Item_Destroyed` and `Item_Dissapear` are *not*
children of `Item` (`Item`'s parent is `Interactive_Object`), so `instance_number(Item)`
is a clean count of what the player is carrying.

### Surviving `game_load` (the one that bites)

Saving here is GameMaker's built-in `game_save()` / `game_load()` — `Slot_Load`,
`Game_Menu_Sure_Autosave`, `Level_Saver` and `Autosave` all use it. That has two
consequences that broke this patch in the field:

**`game_load` replaces every live instance with the stored one and does not run Create
events.** So the `Controller` that comes back has exactly the variables it had when the
save was written. Loading a save made *before* the mod existed gave:

```
FATAL ERROR in Step Event0 for object Controller:
Variable Controller.a11y_cy(100664, -2147483648) not set before reading it.
```

**Data structures are not part of a save file.** So a save made *with* the mod is worse,
not better: it restores the `ds_map` / `ds_list` ids from the session that wrote it. Those
ids are stale, and because GameMaker hands out ids sequentially they may since have been
given to one of the game's own lists — quietly reading and clearing the wrong structure
instead of raising an error.

The fix is an **ownership stamp**, not an existence check. Init adds a private key to its
own map, and the tick re-inits unless all three hold: the marker exists, the id is still
a map, and that map still carries the key.

**The state has to live on the marker, not on Controller** - and this took a second crash
to get right. The first version kept everything on Controller and used the marker only as a
flag. That is unsound, because the marker is **persistent and survives `game_load`, while
`game_load` replaces the Controller with the one from the save file**. Loading a save
written before the patch therefore left a live marker sitting beside a Controller with none
of the variables; the guard passed and the next line died on:

```
Variable Controller.a11y_lbl(100673, -2147483648) not set before reading it.
```

No read of a Controller variable can ever be safe here, so the guard must read only the
marker. The marker can only be created by the init, which seeds all 37 variables onto it in
the same event, so **if the marker exists, the marker has them all** - the one invariant
`game_load` cannot break.

Controller's copies are now a per-frame cache: the tick hydrates them from the marker at
the top and writes them back at the bottom, which leaves the several hundred lines in
between untouched and working on plain `a11y_` names. All three generated blocks - seed,
hydrate, persist - are built in the injector by reading the variable names back out of the
init source, so they cannot drift apart from it or from each other:

```
State carried on the marker: 37 variables.
State schema tag: 483822.
```

**"The marker exists" is still not enough**, which cost a third crash on the same save:

```
Variable Worm_February.a11y_lbl(100673, -2147483648) not set before reading it.
```

`game_load` restored a marker written by an **earlier build of this patch** - one where the
marker was only a flag and carried no state at all. Its variable set was not the set this
build expects, so reading it was just as fatal as reading Controller's had been. Any patch
that both persists across saves and evolves has this problem: an old save can hand back a
marker from any previous version of the schema.

The only variables guaranteed to exist on *any* instance regardless of which build wrote it
are **GameMaker's own built-ins**, so one of those carries a schema tag:

```gml
if (a_mk.friction == 483822)        // built-in: always defined, whoever wrote this instance
    if (ds_exists(a_mk.a11y_lbl, ds_type_map))
        if (ds_map_exists(a_mk.a11y_lbl, "@a11y"))
            a_live = 1;
```

`friction` is inert here - it only applies while `speed` is non-zero, and the marker never
moves, has no sprite and no events. The tag is **derived from the state variable names
themselves**, so any change to the state set invalidates every marker written by an earlier
build automatically, with no version number for anyone to forget to bump. It is written
last during seeding, so the tag can only be present on a marker whose seeding finished.

The same guard wraps the Escape handler, which reads the marker too.

```gml
var a_live = 0;
if (instance_exists(Worm_February))          // a marker only this patch can create
    if (ds_exists(a11y_lbl, ds_type_map))
        if (ds_map_exists(a11y_lbl, "@a11y"))
            a_live = 1;
if (!a_live) { <the whole init again> }
```

The init is one C# string used for both Controller's Create and this recovery path, so the
two cannot drift apart. It deliberately does **not** destroy the old structures first:
after a load there is no way to prove the ids are still ours, and freeing one of the
game's lists by mistake is far worse than leaking a handful of ours. A load happens a few
times a session at most.

**The check may not read any `a11y_` variable** - after an old save the whole point is
that they are absent, and reading one is exactly what crashes.

`variable_local_exists` is the obvious tool and **it is a dead end**, recorded in full
because it looks right twice over. Getting it to compile takes two registrations: the FUNC
chunk entry alone fails with `Failed to find function "variable_local_exists"`, because
UTMT validates against its own `Data.BuiltinList.Functions` (a public mutable dictionary,
2087 entries, carrying `variable_global_exists` but not the instance-scope one). With both
it compiles cleanly, decompiles correctly, and the runner does export it - builtin table
entry at `0x4fcad0`, one argument - and the game then dies at once with:

```
trying to index a variable which is not an array
```

the same misleading message this bytecode gives for adding a global. Everything else in
the check was ruled out first: `ds_type_map` compiles to a real constant (no VARI entry),
and `ds_exists` / `ds_map_exists` were already in use elsewhere in the patch.

**The marker object.** `instance_exists` is always safe to call, so the flag became an
instance of `Worm_February` - a leftover with no events, no sprite, no parent, never
placed in a room, and with **zero code entries pushing its asset index (4628)** anywhere
in the file. That last check is the one that matters: it also covers the 339 room
creation-code entries, which the decompiled GML dump does not include. Nothing in the game
can create or destroy it, so an instance can only ever be one this patch made.

It is created **last** in the init, so its presence means the init ran to completion, and
the instance is set `persistent` (or a room change would destroy it and force a needless
re-init every time) and `visible = 0`. Only once it is present is `a11y_lbl` safe to read
- and that read is still ownership-checked, because the marker and the map ids come back
from a save together while the structures those ids point at do not.

Nothing is added to the data file for any of this.

### The scene

Everything clickable in a room is a child of **`Interactive_Object`** (asset index 18 -
literally the number `_interactive_get_type` returns), and there are **2123 descendants**.
The keyboard walks the same set the mouse sweep does.

**There is no name and no verb stored anywhere.** Two things had to be dug out:

*The label.* Only **8 of the 972** hover handlers call `_info_text_set`, so info text is
almost never available. The label therefore comes from `object_get_name` with underscores
swapped for spaces - `Hospital_B_Ruins_Gate` reads as "Hospital B Ruins Gate". Verbose,
but these names are consistently descriptive, and where a real info text does exist it
wins.

*The verb.* The only record of what an interaction **is** turns out to be the mouse cursor
the hover sets. So the focused object gets a hover pass - `event_user(1)`, exactly what
the game does - and the resulting `Controller.cursor_image` maps to a word:

| sprite | means | why several |
|---|---|---|
| 43, 44, 49, 50, 408 | use | hand, redrawn for left-handed / hurt / fixed / no-hands |
| 39, 55, 52 | look | eye, redrawn for fixed eye / blind |
| 40 | go | |
| 46 | enter | |
| 45 | locked | |
| 41, 42 | hit | redrawn for left-handed / hurt |
| 47 | back | |
| 38 | no free hand | |

`text_info` is saved and restored around that probe, because Controller's End Step reads
it *before* clearing it and a stray value would flicker the on-screen tooltip. The cursor
fields it also touches are reset at the top of that same End Step, so they need no care.

**Activation is `event_user(0)`**, with no hover pass first: handlers call `_check_item`
themselves inside user event 0 rather than relying on state left by the hover, so a bare
click is complete. Verified by reading them rather than assumed.

**Do not filter on `visible`.** This was the bug that made the game unplayable while
looking like it worked. **1465 of the 2124** interactive objects are invisible by design:
every room exit and nearly every look-at hotspot is an unrendered collision shape built
from a placeholder sprite (`S_Test`, `S_Test_02`, `*_Mask`). A single bridge room:

```
  vis  inter mask  object
  NO   yes   yes   Bridge_Go_Up   [S_Test]        <- the way out of the room
  NO   yes   yes   Bridge_Sewer_Mask
  NO   yes   yes   Bridge_Wood_Mask
  yes  yes   yes   Bridge_Can
```

The game's own hit test is a `collision_point` sweep, which ignores visibility entirely.
Filtering on it silently discarded about two thirds of the game, exits included. The right
filter is a **mask** - `sprite_index == -1` means nothing the mouse could hit either -
checked at instance level, since a few objects are given their sprite at runtime.

**Going back is not an `Interactive_Object` at all.** `View_Back` is a bare object holding
the destination room in `lvl_back`, and the game's way of using it is pure mouse geometry:
its Step watches for the pointer straying more than `distance` (250px) from it, and only
then does a click on empty space walk you back. There is nothing for a keyboard to aim at,
so it is added to the list explicitly, pinned to the front - one predictable place for the
only way out beats spatial fidelity. Its own user event 0 ends in
`mouse_check_button_pressed` and so cannot be called from a keypress; the entry does what
that handler does instead (`_sound_play_simple` then `room_goto`). It is also not hover-
probed, because its user event 1 deactivates it.

**Do not honour its `active` flag, and do not hide it behind a filter.** Both mistakes
stranded the player in a close-up with no way out:

* `active` exists to stop an accidental click the instant a close-up opens, and it
  *oscillates*. `View_Back`'s Step fires user event 0 whenever the pointer is further than
  `distance` (250px) away; that handler then calls user event 1 - `active = 0; alarm[0] = 2`
  - if the pointer happens to be over any interactive object. A keyboard player never moves
  the pointer, so wherever it was left can hold `active` at 0 almost permanently. Honouring
  it made **Go back silently do nothing**. A deliberate Enter on an entry that says "Go
  back" is not ambiguous.
* It is listed in **every** filter mode, not just Everything and Exits. It is one entry and
  it is the way out; hiding it behind a mode leaves the player stuck with no indication
  that switching mode would give them a way back.

**Track the focused instance, not its index.** The list is rebuilt every frame and this
game's rooms change constantly - birds, rain, stains and spawner objects are all
`Interactive_Object` children, appearing and vanishing several times a second. The first
version keyed the focus off a signature of the list contents, so any of them reset the
cursor to the first entry and re-announced it: **"1 of 35, 1 of 35, 1 of 35"**, several
times a second, with the player's place lost each time.

So the focused *instance id* is remembered and located again in each rebuild. Only an
actual `room` change announces anything. A scene that merely reshuffles stays silent and
the focus follows its object wherever it moved to in the list; the one other time it
speaks is when the focused thing is genuinely gone - picked up, opened, walked away -
which is rare enough not to chatter. Leaving the scene for a conversation, the menu or the
status screen deliberately keeps the remembered position, so coming back is silent.

**Reading order is left to right by bounding-box centre** - these rooms are wide and
shallow, so one horizontal sweep matches the layout, unlike the menus' y-then-x.

**Exclusions.** `Item` is an `Interactive_Object` child too, but it is the inventory strip
and belongs to the `I` reader; its instance ids go into a skip map each frame.
`Menu_Btn`, `Status_Btn` and `Interface_Inventory_Up`/`Down` all have their own keys
already.

**`playable` is checked last, and that ordering is load-bearing.** Of the 2123
descendants, exactly two never set it - `Interface_Inventory_Up` and
`Interface_Inventory_Down`, the only ones whose Create omits `event_inherited` without
assigning it directly. They are excluded before that read is reached. (The third object
whose Create skips `event_inherited`, `Little_Nightmares_Cutting_Board`, sets
`playable = 0` itself.) Reading an unset variable is fatal in this runner, so this was
worth counting rather than assuming.

**Blocked exactly where the game blocks itself:** `_interactive_get_type` returns -4 while
an `Info` popup, a `Dialogue` or a `Cutscene` is up, so the scene reader stands down then
too, rather than offering things the game will ignore.

### Health

`Controller.hp` is accumulated **damage**, not remaining life - it only rises, and `_dmg`
converts it into one of five wound statuses as it crosses 5, 10, 15, 20 and 25:
`Status_Wounds`, `_Deep`, `_Critical`, `_Deadly`, `_Agony`.

Those `Status` objects are the game's own health display, and every one carries a properly
localised `name`, so they are what gets read rather than a raw number the game never shows
anyone. `_status_add` refuses duplicates, so a simple `instance_exists` test per level -
worst last - gives the current state. The same `Status` family also holds venom,
blindness and the story flags the S screen lists, which is where the "Also..." part of the
`H` readout comes from.

Taking a hit is otherwise conveyed only by a wince, a stain and a sound, none of which a
screen reader can pass on and none of which says how bad it now is - hence the watch on
`hp`. It is seeded from the live value at init so that loading a save, which restores `hp`
along with everything else, does not report the whole game's injuries at once.

### The status screen (S)

`Status_Menu` is a **report, not a menu**, and almost none of it is reachable without a
mouse:

* A grid of `Status` icons, laid out six to a row by the menu's own user event 1. Each
  carries a properly localised `name` that the game reveals **only while the pointer is
  over the icon** (`_info_text_set_bold`).
* Three ending indicators. Whether an ending is still reachable is conveyed purely by a
  padlock sprite drawn over it - the state itself is `Controller.ending_good` /
  `ending_neutral` / `ending_bad`.
* A Back button (`Status_Menu_Resume`, a `Mouse_4` handler).

Because the contents are positional rather than listed, the focus indexes them
arithmetically - statuses, then Good, Neutral, Bad, then Back - with no list to build.
Only Back does anything on Enter. Escape and a right click already close the screen, both
handled by the game itself (`Status_Menu` `KeyPress_27` and `Mouse_54`).

The same `Status` instances are what the `H` key reads, so the two agree by construction.

### Filtering the scene (A / D)

A room holds well over thirty interactive objects and most are scenery you can only look
at, which buries the handful that matter. `A` and `D` step through four views:
**Everything**, **Exits**, **Objects**, **Scenery**.

**The classification has to be static, and that is not a preference.** The only record of
what an interaction is, is the cursor its hover handler sets - so classifying a room by
probing it would mean firing `event_user(1)` on every object in it. Four of the 878
interactive objects with a hover handler have real side effects, and one of them is
disqualifying:

| object | what its hover does |
|---|---|
| `Screen_Spider_Mask` | **`_dmg(0.25)`** - listing the room would injure the player |
| `Bridge_01_Bird_Mask` | spawns two Birds, plays a sound, destroys itself |
| `Hospital_Fly_06` | spawns a replacement or starts a path |
| `EndingB_Hospital_Patient_Fat` | reveals gore, plays a sound |

So only the **focused** object is ever probed at runtime, which is exactly what hovering it
would do anyway. Everything else is classified **at patch time** by scanning each object's
hover bytecode for which `_cursor_*` scripts it can reach, walking the parent chain because
a child with no handler inherits one. The result is baked into Create as 826
`ds_map_add` entries (`1` = exit, `2` = usable, absent = scenery):

```
Scene categories: 282 exits, 544 usable, 1297 scenery.
```

Cross-checked against an independent analysis of the decompiled source: **0 mismatches**
across all 826.

**"Objects" is the baked category OR simply being visible**, and that hybrid matters.
`Bridge_Can` is a can you can kick - a visible prop with a click handler and *no hover
handler at all*, so the cursor test alone files it as scenery. Anything the game actually
draws is a thing, not scenery. Conversely a few genuinely usable hotspots are invisible,
so the baked category still has to count. **Scenery** is then what remains: unrendered,
not an exit, no cursor of its own - the look-at masks.

### Things gained

Picking something up and gaining a condition are both silent: the item just appears in the
strip down the side, and a status is an icon that names itself only on hover. Both are
caught by diffing the live `Item` and `Status` instances against the previous frame's, and
announced **queued rather than interrupting**, since a pickup often lands in the same
breath as a line of dialogue.

Both diffs are **skipped entirely while a `Pause` exists**. `Pause`'s Create calls
`instance_deactivate_all` and explicitly reactivates only `Controller`, `Music_Controller`,
`Light_Controller` and `Screen_Controller` - so Items and Statuses genuinely disappear for
the duration of the pause menu, the options screen and the status screen, all three of
which descend from `Pause`. Diffing through that would announce the entire inventory again
every time one of them closed. The lists are also seeded at init from what is already
present, so loading a save does not read out everything at once.

### Info popups - the notes, posters and patient cards

Clicking a note, poster, newspaper or patient card creates an `Info` instance. There are 28
of them and none adds any events of its own, so all the behaviour is `Info`'s.

**A crash, and it is the game's own latent bug.** `Info`'s **Draw** does
`draw_surface(info_surface, ...)`, but the only thing that ever creates that surface is
`Info`'s **Step**:

```gml
// Info, Step
if (!surface_exists(info_surface))
    info_surface = surface_create(1024, 768);
// Info, Draw
draw_surface(info_surface, 0, 0);
```

The instance is born partway through a frame, so if a Draw happens before that instance's
first Step it dies with **`Trying to use non-existing surface`**. Opening one from the
keyboard hits it every time.

**The guard has to live in `Info`'s Draw, and that was learned the hard way.** The obvious
fix - have Controller's Step top up any `Info` missing a surface - looks right and does
nothing, because Controller's Step runs *before* the click that creates the popup. By the
time the instance exists, the guard has already run for that frame. Prepending to `Info`'s
own Draw puts the check immediately before the `draw_surface` that needs it, which is the
only place that cannot be outrun by whoever created the instance, or when:

```gml
if (!surface_exists(info_surface))
{
    info_surface = surface_create(1024, 768);
    surface_set_target(info_surface);
    draw_clear_alpha(c_black, 0);
    surface_reset_target();
}
draw_set_alpha(0.1 * effect_number);        // the game's own code follows
```

All 28 children inherit both this Draw and `Info`'s Create, so one prepend covers every
popup and `info_surface` is always defined.

**The contents are pre-rendered art.** `_chapter_screen_draw`-style `draw_text` does not
appear anywhere here; each popup is one `draw_sprite`. Crucially they have **one frame
each**, not the seven that localised art in this game uses - so these are shown in English
to every player, and an English transcription is faithful rather than a substitution.

So all 26 with sprites were exported (`gscripts\export_info_sprites.csx`) and transcribed
into a lookup keyed on object. This is not flavour text: **`Hospital_Note_Call` is
"CALL ME!! 555-279" and `Flat_Phone_Info` is "637-511"** - phone numbers a puzzle needs.
Patient cards carry names, IDs and diagnoses. Without the transcriptions every word of it
is unreachable, and the puzzles that depend on them are unsolvable.

`Info_Test` has no sprite and is skipped. Enter closes a popup through its own global
left-press handler, so the appear/destroy guard still applies exactly as it would for a
click.

### Using an item on something

The mechanic behind every item puzzle - pick up the Controller, use it on the car - and the
one part of the game with **no textual feedback of any kind**. `Bridge_Car` is the whole
pattern:

```gml
// hover (user event 1)                    // click (user event 0)
if (!_item_noone())                        if (_check_item(1090))
{                                          {
    _activate_item_cursor();                   _sound_play_simple(204, 90, 0);
    _check_item(1090);                         room_goto(Lvl_Bridge_Controller);
}                                          }
else
    _cursor_no_item();
```

`_activate_item_cursor` raises `Controller.wrong_item`, and `_check_item` clears it again
if what you are holding is the right thing. A sighted player sees the "wrong item" mark
vanish from the cursor as they pass the car. There was nothing whatsoever to hear, so every
item puzzle was unsolvable except by trying Enter on all thirty-odd objects in a room.

The hover probe now captures `item_cursor` and `wrong_item` alongside the cursor sprite,
and says so outright:

* `, USE Controller HERE` - the object takes items and this is the right one
* `, takes an item, but not this one` - it wants something else

Both are read only for the focused object, so this costs nothing in chatter. `H` also
reports what is currently held, since an active item persists across rooms and there was
otherwise no way to be reminded without opening the inventory and walking it.

Sprite 38 (`_cursor_no_item`) is read as **"needs an item"** rather than the earlier "no
free hand": the same cursor is used both for an empty-handed player and for an object that
wants an item, and the latter is far and away the common case.

### Clutter

Spawners and litter are technically interactive but never worth listing, so names matching
`creator|trash` get a fourth category, kept out of the **Scenery** view and still present
in **Everything** so nothing becomes unreachable.

Applied **only to objects that would otherwise be scenery**, and that restriction matters:
ten trash objects - `Memories_Trash_Items`, `Hospital_Entrance_Trash` and friends - classify
as *usable*, because they are containers you can actually search. A blanket name filter
would have silently removed real content. Final split:

```
Scene categories: 282 exits, 544 usable, 1269 scenery, 28 clutter.
```

### Dials and keypads - state kept in `nbr`

The car remote is the pattern: `Bridge_Controller_Button` cycles `nbr` 0 to 3 and shows it
by swapping a sprite, `Bridge_Controller_Button_B` transmits, and the transmission only
works with the first dial on **3**. Both buttons work fine from the keyboard - they were
just *completely silent*, because a press changes nothing but an image.

Two fixes, both general rather than specific to this puzzle:

**Read the object back after acting on it.** A press is now audible at all, and picks up
any change of state or verb. Only when the object survived: if acting on it destroyed it -
picked up, opened, walked through - it stays quiet and the focus tracking reports next
frame where that left the player.

**Read `nbr` where an object keeps one.** 19 objects do, and they are exactly the ones that
need it: the two remote buttons, all eleven phone-keypad buttons (the puzzle those
transcribed phone numbers feed), and a few others. Detected at patch time by scanning each
object's own Create for a store to that variable.

**The scan has to require a store to SELF, and this is not theoretical.** A first pass
found 20 objects, one more than a source-level check. The extra was `Difference_Object`,
whose Create does:

```gml
Difference_Controller.nbr += 1;
```

That is a store to `nbr` on a *different* object, so `Difference_Object`'s own instances
never have the variable and reading it would have been the fatal unset-variable crash - on
a perfectly ordinary scenery object. In bytecode the two cases differ only in the
instruction's instance type:

```
Bridge_Controller_Button   Pop   instr.TypeInst=Self
Difference_Object          Pop   instr.TypeInst=1302      <- Difference_Controller
```

Requiring `TypeInst == Self` drops it back to 19. Worth remembering that a variable-name
scan alone is not enough to prove an instance has that variable.

### Escape backs out one level

Escape reaches `Controller`'s `KeyPress` 27, which opens the pause menu. With the inventory
reader or an info popup open that is the wrong level to back out to, so a guard is
**prepended** to that handler and exits early when there is something nearer to close.

It has to be prepended to the key event rather than handled in the tick: GameMaker runs Key
Press events **before** Step, so by the time the tick sees Escape the pause menu already
exists. Prepending also means an early `exit` stops the game's own handler running at all,
rather than undoing it afterwards.

Guarded on the marker object, because after a `game_load` of a save written before this
patch the Controller has none of the `a11y_` variables until the tick rebuilds them, and
reading an unset one is fatal. Everything is read off `Controller` instance 0 - the only
one that does accessibility work - since this event fires on every instance.

### GML object comparison is exact; the lookup functions are not

The single trap that has caused the most wasted time here, because the two halves disagree
and only one half is obvious.

**Parent-aware** (a parent matches all its descendants): `instance_exists`,
`instance_find`, `instance_number`, `with`, `collision_point`, `event_perform`.

**Exact, never parent-aware**: comparing `object_index` against an object.

So this looks completely reasonable and is wrong:

```gml
var n = instance_number(View_Back);          // finds Bridge_Controller_View - a CHILD
var i = instance_find(View_Back, 0);         // returns it
if (i.object_index == View_Back) { ... }     // FALSE. 1526 != 1580
```

Every close-up in the game uses its own `View_Back` child, so "Go back" was listed in every
room and did nothing in all of them - the entry appeared because the lookups are
parent-aware, and the action never fired because the comparison is not. It fell through to
the ordinary click path, which needs a real mouse press.

The fix is to identify entries by something the patch itself controls - the name stored in
the focus list - rather than by object identity.

Auditing for the same mistake elsewhere turned up one more: the scene list excluded the
interface buttons with `a_wo == Menu_Btn || a_wo == Status_Btn`, and **both have three
`Memories_*` children** that were slipping through. Exclusions are now a set baked at patch
time with descendants resolved in C#, where the parent chain can actually be walked:

```
Interface buttons excluded from the scene: 10.
```

### Mouse-position gates - a hard progression block

Some things here are gated purely on **where the physical mouse pointer is**, with no click
and no interactive object involved. `Bridge_05_Barier`'s Step is the worst case:

```gml
with (Bridge_Wire)
    if (point_distance(x, y, mouse_x, mouse_y) > 60)
        other.active = 0;
if (active && !Bridge_Controller.need_wire)
{
    Bridge_Controller.need_wire = 1;
    instance_create(x, y, Bridge_Doll_Cry_Sound);   // the crying that leads you onward
}
```

Until the pointer comes within 60px of `Bridge_Wire`, `need_wire` is never set, the doll
never cries, `Bridge_Stroller` never becomes visible or playable, and **the chapter cannot
be finished**. `Bridge_Wire` is not an `Interactive_Object`, so it was not even listed.

18 code paths test mouse proximity; only two do anything (`Forest_Leech` adds a status), and
only this one gates progression. But one is enough to make the game unfinishable.

Two fixes:

* **Focusing something moves the real pointer onto it** (`window_mouse_set`). Converted
  *through the current pointer position* rather than by absolute view arithmetic, so
  whatever constant offset exists between window and room space cancels out and only the
  scale has to be right - and the unindexed `view_wview` / `view_hview` are read exactly as
  the game's own `Screen_Controller` writes them. This also keeps the game's hover state,
  info text and cursor art correct for anyone watching, and stops `View_Back`'s `active`
  flag flickering off against a stale pointer.
* **Proximity targets are listed** even though they are not interactive, so they can be
  focused at all. Hand-curated, because the object being watched sits inside a `with` block
  in a *different* object's Step and cannot be identified reliably by static scanning.

### The one drag-and-drop puzzle

`Bridge_Clip` in `Lvl_Bridge_06` is the only object in the game that follows a held mouse
button (`Game_Menu_Volume` and `Game_Menu_Brightness` are the option sliders; `Test_Picture`
is debug leftover). Its Step is unusable from a keyboard by construction:

```gml
if (active)
{
    if (!mouse_check_button(mb_left)) { active = 0; ... exit; }   // drops instantly
    x = mouse_x;  y = mouse_y;
}
```

Its user event 0 only sets `active = 1`, and that Step cancels it the very next frame
because no button is held - so pressing it did precisely nothing.

The point of the drag is the clip's **collision event with `Bridge_06_Mask`**, which clears
`Bridge_Coin_Item.lightning`; until then, taking the coin shocks you instead. Note
`Bridge_Coin_Item` re-sets `lightning = 1` in its own End Step every frame, so the clip has
to be *resting* on the mask, not merely to have touched it once - which is exactly what a
mouse player achieves by dropping it there.

So Enter on the clip places it where a mouse player would have dropped it, aimed at the
target's bounding-box centre rather than its origin (the mask is a plain `S_Test_02`
rectangle whose origin may sit at a corner):

```
Lvl_Bridge_06:  Bridge_Coin_Item (592, 508)   Bridge_Clip (1146, 280)   Bridge_06_Mask (516, 245)
```

**Collision timing bit here, and it generalises.** GameMaker runs collision events *after*
Step and *before* End Step:

```
Begin Step -> Alarms -> Keyboard/Mouse -> Step -> Collision -> End Step -> Draw
```

The game dispatches clicks in Controller's **End Step**, so by then the clip's collision has
already cleared `lightning`. This patch's tick lives in the normal **Step**, one phase
earlier, so it saw the value the coin's own End Step had reset to 1 on the previous frame:
the coin read as electrified and taking it shocked the player even with the clip correctly
placed. That collision is therefore re-evaluated with `place_meeting` at the top of the
scene block, so the readout and Enter both agree with what the game is about to do moments
later in the same frame.

Any state produced by a collision event has this hazard - **the tick runs a phase before the
game's own click dispatch.**

The coin also now reads **"Coin, electrified"** or **"Coin, safe to take"**. The game's only
cue for that state is the sparks, so without it there is no way to know the grounding
worked - and `lightning` is safe to read because `Bridge_Coin_Item`'s Create always sets it.

### The hospital phone

Twelve keys is a miserable thing to arrow through, and a phone number is something you
already know how to type. The number row and the numpad now press the matching key through
its own user event 0 - the same handler a click runs, so the press animation, the key's
cooldown and the dial tone all still happen - and the digit keys are dropped from the scene
list, leaving only what a number key cannot press: the receiver and the star.

`Hospital_Telephone_Number.number` is the dialled string, and it inserts the dash itself
after three digits, so reading it back gives exactly the display a sighted player sees.
Both numbers it accepts are the ones transcribed off the notes: **555-279** and **637-511**.

Note `nbr` on these is a **string** - the character the key types - where everywhere else in
the game it is a dial position. Same variable name, different meaning, so the readout says
"key 7" here and "position 2" on the car remote.

**Family membership is not enough to identify a keypad key.** `Hospital_Fish_Tank` also
descends from `Hospital_Phone_Btn`, which is odd but real, and its Create is nothing but
`event_inherited()` - so it silently inherits `nbr = "1"`. Selecting the keypad by family
would have made typing 1 fire the fish tank's own handler, *and* hidden the fish tank from
the scene list as though it were a digit key. The set is therefore restricted to objects
whose **own** Create assigns `nbr`, which only the eleven real keys do:

```
Phone keypad objects: 11.       (12 before the restriction - the fish tank)
```

### The board game

`Lvl_Hospital_Board` (and `Lvl_Flat_Board`), reached by phoning **637-511**. Mechanically it
already worked from the keyboard - press the die, then press your piece - because both are
ordinary `Interactive_Object` hotspots. What was missing was **every piece of information in
it**, all of which is drawn and nothing else:

| | how the game shows it | where it actually lives |
|---|---|---|
| the roll | a sprite frame on `Board_Dice` | `Board_Controller.die_result` |
| your position | where a token sits on the board | `Board_Controller.position` |
| dying | the token jumps back | `Board_Controller.death` |

All three are plain numbers and `Board_Controller`'s Create sets them, so they are safe to
read. The roll is **watched rather than announced on the press**: `Board_Dice_Roll` runs an
animation first and only sets `die_result` when it finishes.

So a turn now reads: *"Rolled 4. On square 12, moving to 16. Press Enter on your piece."*
then *"Square 16."* - and the two hotspots, which are otherwise nameless and invisible, read
as *"Die, ready to roll"* / *"Die, showing 4"* and *"Your piece, square 12, moves to 16"*.

**The die stops responding when the game ends, and says nothing.** `Board_Dice`'s handler
simply `exit`s once the board is finished - past square 25 on the hospital board
(`Board_Controller.end_game`), past 72 on the flat one
(`Flat_Controller.board_game_ended`) - while the die stays visible, `playable`, and to a
screen reader still reads as "ready to roll". It is indistinguishable from a broken control.
Both flags are set in their own Creates, so both are safe to read; the end is now announced
when it happens and the die reports **"game over"**.

Landing on a `Dead_Pointer` sets `death`, and the next roll silently teleports the piece
back to the last `Checkpoint_Pointer`, so that is called out too.

### Key ownership

Dialogue and the inventory reader take the arrows and Enter while they are up, and the
menu navigation stands aside - otherwise one keypress would be acted on twice. `Ctrl`
(stop speech) sits outside all three so it works everywhere.

Nothing else in `data.win` is modified: no new objects, no new scripts, no room edits. The
single exception is a guard prepended to **`Info`'s Draw**, which fixes a crash in the
game's own code - see the Info popups section for why it cannot live anywhere else.

### The reception computer and Lvl_Computer

The computer in `Lvl_Hospital_Reception` opens a whole fake operating system in its own
room. It was the least accessible thing found so far - four separate mechanisms, none of
which the mod could reach.

**Getting in.** `Hospital_Reception_Computer`'s hover does nothing at all unless
`Hospital_Controller.computer_wire` is set, so it read as a bare name with no verb and no
hint. The cable is `Hospital_Reception_Wire_Mask`, a plain toggle whose hover is
`_cursor_hand()` either way - the same shape of bug as the hall light switch. Both now
speak their state. (Using floppy disk item 1168 on the computer instead sets
`Hospital_Controller.floppy` and reveals the Floppy icon.)

**Mouse-only buttons.** `Computer_Exit_Window` (every window's X) and `Computer_OK_Button`
(every dialog's OK) have a `Mouse_4` handler and nothing else, and neither descends from
`Interactive_Object`. The game's own click dispatch never raises them, so no window could
be closed from the keyboard at all. Escape now does what those buttons do - destroy the
window they belong to - guarded on `Computer_Window_Opener` exactly as they are. It also
sweeps up orphaned buttons, because `Computer_Virus_Alert` is the one window with no
Destroy event and so does not clean up its own OK button.

The paint palette, `Computer_Paint_Color_Take`, is `Mouse_4`-only for the same reason. It
is listed through a new `a11y_extra` mechanism - things that belong in the scene list
despite not being `Interactive_Object`s - and activated with `event_perform(ev_mouse, 4)`.

**Windows are where the chapter talks to you.** Every window sets a localised
`window_name`, and the five dialogs a second localised line in `text`. All of it is drawn
directly by the window's Draw event, with no object to focus. The tick now announces the
newest window. Which windows carry body text is decided at patch time by reading each
Create, since reading an unset variable is fatal.

**The paint puzzle is pure colour.** Each `Computer_Drawing_Element` has a `color_target`,
and `Computer_Paint_Window`'s Step sets `Hospital_Controller.drawing` only while every one
matches:

| element | target |
|---|---|
| Sky | cyan |
| Mountain | dark green |
| Sun | yellow |
| Tree | brown |
| Tree leaves | dark green |
| Apples | red |
| Cloud | white (already correct) |

**The 14-swatch palette does not contain brown.** Its colours are cyan, blue, green, dark
green, yellow, olive, orange, red, magenta, purple, black, white, dark grey and grey -
`make_color_rgb(118, 55, 32)` is not among them. The tree trunk's colour exists **only** on
the example strip, so the Take Colour tool is mandatory for at least that one step no matter
how the palette is reached.

The five swatches of `Computer_Paint_Color_Example` are exactly the five colours needed, so
the puzzle is solvable from the example strip alone via the Take Colour tool - useful,
because those five ARE `Interactive_Object`s while the 14-swatch palette is not. Colour
values are now spoken by name (GameMaker packs these as `R + G*256 + B*65536`, which is why
the familiar constants look reversed). The element's target is deliberately NOT read out -
working out which colour goes where is the puzzle - but an element that matches says so,
which is the same feedback the picture gives a sighted player.

**1 and 2 arm the paint tools.** Which tool is armed decides what a press on the picture
does, and arrowing to them between every colour change is most of the work in the puzzle.
Both keys go through the same user event 0 a click runs, so the toggle, the deselect of the
other tool and the click sound all still happen. Guarded on `Computer_Paint_Window`
existing, because the hospital phone keypad reads the same digits.

**Desktop icons are dropped from the list while a window is open**, since they are behind it
and are not what you are working on. Which icons those are cannot be read off the objects:
Coma, Drawing and Medicine are `Computer_Icon` descendants exactly like Trash and Floppy.
What separates them is that the desktop ones are **placed in `Lvl_Computer`'s room
definition** while the others are created by the window that contains them, so the set is
baked from the room. Getting this wrong would hide Medicine (cures the virus) and Drawing
(opens the paint program) and make the chapter unfinishable. `Computer_Games_Icon` is
excluded from the hidden set so the way out is always one entry away, and nothing is hidden
on the bare desktop, where the icons are the only thing there is to do.

**Icons need two presses**: the first sets `active` and only the second opens the window.
Nothing marked the first press. Icons and paint tools carry a localised `name`, which is
much better than the object name, and their `active` state is now spoken.

**The virus chain**, for reference: `virus` starts at 1, and blocks the Internet, Clock and
Drawing icons behind `Computer_Virus_Alert`. Curing it needs the Medicine icon, which only
appears in the Trash window once `Hospital_Controller.medicine` is set - by putting item
1167 in the reception bin (`Hospital_B_Reception_Trash_Mask`). The way OUT of the computer
is the **Coma icon**, inside the Games window: its target is `End_Computer`, whose Create is
nothing but a `room_goto` back to the reception.

`Computer_Clock` only offsets a display of the real system clock; `hour_offset` is read
nowhere else, so it is flavour rather than a puzzle.

### The apartment board game (Lvl_Flat_Board)

Reached by taking `Item_Board_Game` (1177) from `Flat_Bathroom_Game` in the toilet and using
it on `Flat_Kitchen_Board_Game`, which only becomes playable once `Item_Mail` exists - so the
letter has to be finished first.

The mechanics were already read out - roll, position, death, game over - but **the board
itself was invisible**, which is most of the game:

* The 57 squares are `Position_Pointer` instances carrying `nbr`. `Dead_Pointer` and
  `Checkpoint_Pointer` are its children, so one parent-aware sweep covers all three.
* Landing on an **active** `Dead_Pointer` kills you back to the active checkpoint.
* Which monsters appear at all depends on the statuses you are carrying:
  `Board_Bat` destroys itself unless `Status_Vampire` exists, and so on for spider, butcher,
  cyclops, scarecrow, poison ivy, poacher.
* Each hazard can be **crossed out with the Pen** (item 1173). Each correction decrements
  `Board_Controller.correct`, which starts at `4 + round(statuses / 2)`, and the hazard's
  user event 2 clears its square's `Dead_Pointer.active`. When `correct` hits 0 the pen is
  destroyed and no more corrections are possible.

None of that is stored anywhere reachable: a hazard knows its square only as a literal
inside its own user event 2. So the square is found **at runtime by nearest pointer**. The
room data says that is sound - every hazard's nearest pointer is within 33 pixels and for
every deadly one it is a `Dead_Pointer` - and the 40-pixel cutoff sits well below the
square spacing, so it cannot pick a neighbour.

Now added: each hazard says its square, whether it is `deadly` or `crossed out`, and how
many corrections remain; and the piece says what the roll lands on - `DEADLY`, `safe,
crossed out`, `a checkpoint`, or `the end of the board`. That last one matters because a
player carrying `Status_Dishonesty` may reroll, and knowing the destination is the entire
basis for deciding whether to.

17 hazards classified. `Board_Bully_Spikes` x4 are not among them - they are not
`Interactive_Object`s and their nearest pointer is a plain `Position_Pointer`, so they are
decoration rather than a hazard.

#### The board needs two more pieces, and neither is in apartment 1

`Item_Board_Game` only puts the board on the kitchen table. `Flat_Board_Mask` - the board
itself, inside `Lvl_Flat_Board` - counts item drops and does nothing until it has **both**:

* **1180, the die**, from `Flat_B_Room_02_Diece_Mask`.
* **1181, the pawn**, from `Flat_B_Hall_Coat_Mask`, which additionally destroys item 1100
  (the scissors) and awards the `SCISSORS` achievement when its `scissors` flag is set.

Both are in **apartment B**, through the mirror. Until `a >= 2` the mask sets
`Board_Dice.playable = 1` and destroys itself; before that the die is invisible and
unplayable, so it is not in the object list at all and the room reads as an empty board
with no way to start. Pressing Space there now says so, and says what is missing.

#### Rerolling is the die, not the sign

`Board_Reroll_Dishonesty` has **only a Create event** - it deletes itself unless
`Status_Dishonesty` exists. It is a hint sprite, not a button: its `visible` is the cue,
and the reroll is pressing **the die a second time**. `Board_Dice`'s handler bails out on
`die_result > 0 && !reroll`, so a second press only gets through while
`Board_Controller.reroll` is set, and taking it calls `_status_add(1049)`. Both the die
entry and the roll announcement now say when a reroll is on the table.

#### The rest of the rules, which are all drawn and nowhere else

* Checkpoints move as you pass them: past 15 the checkpoint becomes square 16, past 39 it
  becomes square 40. `Checkpoint_Pointer.active` is what death reads.
* The flat board ends past square **72**; `Lvl_Hospital_Board` - the same objects, a second
  board - ends past **25**. The old readout assumed 72 in both, so it never announced the
  hospital board's finish; it now asks the room.
* Passing square 24 while carrying `Status_Corrector` fires `Board_Bell_Cutscene` once.
* Finishing with neither `Status_Cheater` nor `Status_Corrector` adds status 1033.
* Landing on 64, 66 or 67 with `Status_Shield` gives the `WARRIOR` achievement; using the
  last correction gives `GAME_MODDER`.

#### Both ways to cheat at it cost the good ending, silently

`_status_add` takes an **object index**, not a number, so the bare integers in these
handlers are objects: 1048 is `Status_Corrector`, 1049 is `Status_Cheater`, 1033 is
`Status_Perfect_Game` ("Devoted Player").

Crossing a hazard out with the pen calls `_status_add(1048)`; taking a Dishonesty reroll
calls `_status_add(1049)`. Both of those Creates run the same two lines:

```gml
lock_good_ending = 1;
Controller.ending_good = 0;
```

That is the whole mechanism - immediate, permanent, and with no cue of any kind. The board
just draws an X. `neutral_good = 1` in both, so the neutral ending survives; only the good
one is gone. Finishing the board with neither status adds `Status_Perfect_Game`, which is
the same judgement stated positively, and passing square 24 while carrying
`Status_Corrector` is what rings `Board_Bell_Cutscene`.

So the pen is not a tool for winning the board, it is the board's temptation, and the game
never says the price. Both prompts now do: the hazard says `the first one costs the good
ending` and the die says `which costs the good ending` - and only while there is still an
ending to lose, since `_status_add` is a no-op once the status exists.

#### Space plays a turn

A turn is two presses on two objects at opposite ends of a room list of over fifty entries
- the board contributes 57 numbered squares of its own - and the game runs 30-odd turns.
Space now does whichever of the two the game is waiting for: roll, move, or return to the
checkpoint after a death. Enter on either entry still works, and Enter on the die is still
how a reroll is taken, so nothing is taken away.

Both handlers are plain logic with no mouse test inside them, so `event_user(0)` reaches
them exactly as a click would. `Board_Dice.playable` is deliberately **not** bypassed - see
above; and Space is inert while `Info`, `Dialogue`, `Cutscene`, `Pause`, the inventory
reader or `Board_Dice_Roll` (the roll animation) is up. The world reader binds Enter only,
so nothing else in the room wanted the key.

### The old house: the kitchen masks, and the bin that is a drawing

Three things in the Memories chapter name themselves after the wrong object.

**The portrait is not what refuses.** `Memories_Kitchen_Portrait_Mask`'s handler is

```gml
if (!Memories_Kitchen_Mask_03.alive) { ... room_goto(Lvl_Memories_Portrait_02); }
else with (Memories_Kitchen_Mask_03) event_user(2);
```

so while that mask is alive the press goes somewhere else entirely, and user event 2
spawns `Memories_Mask_Cutscene`, flips the mask to its screaming frame, plays sound 651
and blinks `Memories_Light_Kitchen`. The portrait reads as a look-at that simply does not
work, and the object that actually said no is elsewhere in the list. It now says
`blocked by the screaming mask below it`.

**Which rag is wanted is decided in Create, per visit.** All three of
`Memories_Kitchen_Mask`, `_02`, `_03` carry `alive`, `rag` and `wanted_item`:

```gml
wanted_item = 1188;
if (Controller.ending_good && !instance_exists(Status_Germs)) wanted_item = 1189;
```

1188 is the dirty rag (from `Memories_Toilet_Rag` or `Memories_Kitchen_Rag`); 1189 is the
same rag washed at `Memories_Outside_Pond_Wash` in the pond outside - which **destroys
itself** on Create if `Status_Cyclops` or `Status_Germs` exists or the good ending is
already gone, so on a spoiled run the clean rag cannot be made and is not wanted either.
Presenting the dirty rag on a good run does nothing at all, with no cue. The masks now
say `covered, press to take the rag back` or `uncovered and screaming, needs the CLEAN
rag`. Watch out for `Memories_Toilet_Goo`, which consumes 1188 permanently.

`Memories_Mask_Cutscene` has **no events of its own** - the mask's `alarm[0] = 60` destroys
it - so the generic "press Enter to carry on" was a promise nothing could keep. It is now
its own state (`a_cut = 3`), announced as the scream and offering no key.

**The bin in the kid's room is a drawing.** Two objects share the same few pixels:
`Memories_Room_Trash_Mask` (sprite `S_Memories_Room_Trash_Mask`, depth 30) is the drawn bin
and its whole handler is one rustle sound, while the way *into* it is
`Memories_Room_Kid_Paper_03` - the third drawing on the wall, an invisible `S_Test_03`
hotspot at depth 0. `_interactive_get_id` prefers the lower depth, so the mouse always hits
the drawing. Its handler branches on `Memories_Controller.death_get_out` (set by
`Memories_Death_Outside_Cutscene`): before, it opens a drawing; after, it goes to
`Lvl_Memories_Trash`, via `Lvl_Memories_Item_Holder` on the first visit only - that detour
exists purely to flip every `Item` to `persistent`, since the trash room carries
`NO_INVENTORY`.

Nothing about the entry changes when its meaning does, so the list named the door after a
piece of paper. The two are now `Bin, look inside` and `Bin, rustle it`.

### The bin holds nothing, and the key is cut out of a drawing

`Memories_Trash_Items` reads as a pick-up because the verb is baked from a flat scan for
`_item_add` in the press handler. Here that call is conditional:

```gml
Memories_Controller.room_ereased = 1;
_item_dissapear();                      // a puff of the sprite, NOT a pickup
...
if (!Controller.ending_good && Controller.neutral_bad) _item_add(1106);
```

So on a good run the pile holds nothing whatsoever - and 1106 is the hospital doctor's key
being handed back, not anything belonging to this chapter. What the press is really for is
`room_ereased`, which is unconditional and does two things, both elsewhere:

* `Memories_Kitchen_Room_Parents` now opens `Lvl_Memories_Room_B`, the erased version of
  the parents' room, instead of `Lvl_Memories_Room`.
* `Memories_Handle`'s Room Start flips it from `image_index = 0, playable = 0` to
  `image_index = 1, playable = 1` - a handle appears on the wall, and being playable it
  enters the object list on its own. Taking it gives item **1192**, which fits
  `Memories_Hall_Drawer` in the hall: one press seats the handle, a second opens the
  drawer and reveals `Memories_Hall_Paper`.

The label now mirrors the game's own condition rather than the flat scan, and the press
says `Rubbish cleared out. There was nothing in it to take.` - it destroys itself, so the
usual re-read fell silent, which after a promised pick-up reads as a broken control.

**The key (item 1107) is not in the bin.** It is cut out of a drawing with the scissors
(item 1100, `Memories_Scissors` on the road): either `Memories_Drawing_Key_Mask` in
`Lvl_Memories_Treasure` or `Memories_Drawing_Map_Key` in `Lvl_Memories_Map`. Both handlers
are `if (_check_item(1100))` and nothing else, so without the scissors held they are
silent no-ops.

The door to the parents' room has its own guard: `Memories_Kitchen_Room_Parents` opens only
when `!Memories_Controller.mystery_box && !Memories_Kitchen_Mask.alive`, and otherwise
fires that mask's scream - the same misdirection as the portrait, on a different object.

### The chapter cards wait in silence over birdsong

`Lvl_Memories_End_Chapter` holds `Chapter_End_Memories`, `Light_Chapter`, `Room_Start_Black`,
`NO_INVENTORY` and **two `Bridge_Chapter_Birds`** - that last one is the loop of chirping,
and it is the ambience on every chapter card in the game, not a sound belonging to the
ending.

The card already read out its text and its newly unlocked statuses, then said nothing more,
because it is waiting for a press: `Chapter_End_*`'s KeyPress 32 and Mouse 53 both create a
`Room_Translation_Special` at their chapter's target room. Unlike the opening cards, the
end-of-chapter ones do **not** check `active`, so the press works as soon as
`Room_Start_Black` is gone. Silence over looping birdsong is indistinguishable from a run
that has stopped, so the card now ends with `Press Enter to carry on.`, and F3 repeats it
like everywhere else.

Where it goes from the Memories card: room 90, `Lvl_Memories_Flush`, whose only instance is
`Memories_Flush` - its Create flushes the texture pages, destroys `Memories_Asset_Controller`
and calls `room_goto(Lvl_Ending_Saver)` (241). So Memories is the last playable chapter
before the ending sequence, which is its own run of `Lvl_Ending_*` rooms starting at
`Lvl_Ending_Chapter` and `Lvl_Ending_Start`.

### Picture close-ups (F5)

Sixteen rooms in the old house are one drawn image and nothing else. The object list in them
can only ever say `Go back`, so the picture - the entire content of the room, and in several
cases the puzzle itself - was silent.

These are the same shape as the `Info` popups and were solved the same way: export the
sprite, read it, transcribe it. `gscripts/export_pic_sprites.csx` writes them to
`research/pic_sprites`. `ExportAsPNG` on the sprite's first texture entry gives the cropped
image, not the whole page.

All **48** of them are described, across every chapter. Read on entering the room, ahead of
the object list - queued, since the first entry would otherwise cut the description off at
the second word - and repeated on **F5**.

The definitive way to find them is a parent-aware sweep for a `View_Back` descendant: 50
rooms have one, and the two board-game rooms are the only ones excluded, since the board
already reads itself.

**The picture is not always an object.** In the drawing rooms it is, but in most of the
others the whole scene is a single full-room **tile** pointing at a background asset
(`B_Bridge_Car`, `B_Hospital_Soup`, `B_Graveyard_Head`). Room *backgrounds* are empty
everywhere - `r.Backgrounds` yielded nothing for all 50 - so `r.Tiles` is what has to be
read. Object sprites in those rooms are props and collision masks on top; `S_Car_Dead_Mask`
is a flat green blob. `gscripts/export_closeups.csx` and `export_closeup_bgs.csx` write both
sets to `research/pic_sprites` and `research/pic_bgs`.

Four carry state, because the cut is the point of the drawing. The game shows a cut by
making a second sprite visible over the hole (`Memories_Drawing_Map_Cut`,
`Memories_Drawing_Key_Cut`, `Memories_Happy_Sun_Cut`), and the photo instead flips to
`image_index = 1`; all four now say so.

Several are load-bearing rather than atmosphere:

* **Room_Drawing** is a plan of the parents' room with the trapdoor labelled `Basement` and
  an arrow pointing at it - it is how you learn the trapdoor is there.
* **Map** and **Treasure** each have a key drawn in one corner, and that corner is exactly
  where `Memories_Drawing_Map_Key` / `Memories_Drawing_Key_Mask` sit. The drawing is telling
  you what to cut out.
* **HospitalB_Callendar** is a three by three grid of numbers with every one scribbled out
  except the centre, which reads **13**. That number exists nowhere else.
* **Hospital_Soda_Machine** is a keypad of twelve buttons numbered 1 to 11, with the second,
  eighth and twelfth burnt black - so the reachable numbers, and which are dead, were
  visible information only.
* **Memories_Trash** is not rubbish at all: the sheets are torn up pages of an *inventory*,
  captioned with the things you carry - Worms, Magnifier, Pen, Coin, Battery, Glue, Rotten
  Fish, Scissors.

### F4: where am I, and why is the list empty

`Nothing here` in every filter is the one report a player cannot act on, because the same
sentence covers three completely different situations: the room really is bare, something
invisible is holding interaction off, or this patch has filtered the room away by mistake.
F4 separates them. It says the room name, how many `Interactive_Object` instances are in
the room, how many of those are usable (a sprite and `playable != 0`), and then names
whatever is blocking - `Info`, `Dialogue`, `Cutscene`, `Pause`, `Chapter_Screen`,
`Room_Translation`, `NO_INVENTORY`, the inventory reader.

The room name is a **baked table** (288 entries, `Lvl_` stripped): `room_get_name` has 0
uses in this game, so it is not in the FUNC chunk and cannot be emitted - the same
constraint as `string_char_at`, `string_copy`, `string_delete` and `ord`.

`playable` is skipped for `Interface_Inventory_Up`/`Down`, the only two
`Interactive_Object` descendants that never set it; reading an unset variable is fatal.

Read-only, so it is safe to press anywhere, including mid-cutscene.

### Transitions are not blocked cutscenes

The cutscene announcement fired on the flat mirror, which is a **successful** action: pressing
`Flat_Hall_Mirror_Enter` creates `Flat_Hall_Mirror_Translation`, a `Cutscene` whose whole job
is to draw a 31-frame wipe and then `room_goto(Lvl_Flat_B_Hall)`; its Destroy spawns
`Hall_Mirror_Translation_End` to wipe back in at the far side. Both block interaction, so the
generic "nothing can be used yet" line was read to a player who had just used the mirror
correctly.

Transitions are now their own state and say "Transporting." instead, and they take no key -
there is no handler to press and it is over in half a second either way. The set is baked at
patch time from `Cutscene` descendants whose name contains *Translation*, the game's own term
for these: 5 objects, the two mirror halves plus their two ending-chapter equivalents.

`Room_Translation` is deliberately NOT included. It is not a `Cutscene`, it fires on ordinary
room changes, and announcing it would put "Transporting." in front of every doorway in the
game.

### A dangling-else regression, and the guard against it

The cutscene fix above broke the inventory: pressing I opened the reader, but the arrows
still walked the room. The cause was structural, not logical.

The scene reader is written as the **else** of the block that decides there is nothing to
read:

```gml
if (!a_world) { ...stay quiet... }
else          { ...the entire scene reader... }
```

The cutscene handling was inserted between that closing brace and its `else`, so the `else`
silently re-bound to the new `if (a_cut)`. The scene reader then ran whenever a cutscene was
*not* blocking - which includes while the inventory is open, during Info popups, and on the
pause menu. It compiles, it verifies, and every marker check still passes, because nothing
about the text is wrong. Only playing that exact case shows it.

The injector now asserts the adjacency after building the tick and refuses to patch if it
has been broken. Confirmed by reintroducing the regression against a copy: the patch aborts
with the message rather than producing a broken `data.win`.

This is the second failure of this shape - the first was a raw quote ending a verbatim
string early. Both are cases where a textual edit changes the *structure* around it, and
neither is caught by checking that the right strings are present. Both now have a guard that
runs before every install.

### The letter in apartment 1

`Flat_Kitchen_Letter` opens `Lvl_Flat_Mail`, a close-up with **two** envelope objects and
four things to put in. Every wrong press is silent - each stage falls straight through its
if-chain and does nothing when a piece is missing.

**Stage 1, `Flat_Mail`** - needs `Item_Coin_5` (1097) *and* `Item_Bill` (1174), in either
order. Then a press with nothing selected reveals `Flat_Mail_02`:

```gml
if (!coin)            { if (_check_item(1097)) { coin = 1; ... exit; } }
if (image_index == 0) { if (_check_item(1174)) { ... image_index = 1; } }
else if (coin)        { Flat_Mail_02.visible = true; ... instance_destroy(); }
```

The coin is easy to miss: with the bill in but no coin, `image_index == 0` is false and
`else if (coin)` is false, so the press does nothing whatsoever.

**Stage 2, `Flat_Mail_02`** - needs `Item_Pen` (1173) and `Item_Stamp` (1175), then a press
with nothing selected gives `Item_Mail` (1176).

**The final press must be made with no item selected.** `_check_item(1175)` is tested
first and `exit`s, so still holding the stamp re-runs the stamp branch - destroying another
stamp - and never reaches the finish branch. That reads exactly like the letter refusing to
be picked up. An item is deselected by pressing it again in the inventory: `Item`'s user
event 0 sets `Controller.active_item = -4` when the item pressed is already the active one.

Both envelopes now read out what they are still missing, and say to press with nothing
selected once they are ready.

### The dark bathroom, and why "mirror the game exactly" was not enough

Reported as the game freezing in `Lvl_Flat_Bathroom`. It was not a freeze - it was the mod
correctly going silent in a room the player then could not leave.

`_interactive_get_type()` returns -4, meaning nothing is interactive, whenever an `Info`, a
`Dialogue` **or a `Cutscene`** exists. The tick mirrors that, and for Info, Dialogue, Pause
and the chapter card that is right: each has its own reader here, so going quiet is a clean
handover. `Cutscene` has no reader, and there the same rule is a trap.

`Bathroom_Dark_Back` is a `Cutscene` (not a `View_Back`, not an `Interactive_Object`). It
draws a 0.9-alpha black rectangle over the whole screen and destroys itself only when
`Flat_Controller.bathroom_light` is set - so in an unlit bathroom it exists for as long as
the player does. The result: the game refuses every object in the room, the mod says
nothing because it is faithfully mirroring that, and the one thing that still works is
`Bathroom_Dark_Back`'s own `Mouse_53` - a **global left press anywhere** - which walks you
back to the hall. No key in the mod produced one, so the room was a silent dead end whose
only exit was reloading.

Now a blocking cutscene is announced once, and Enter or Space performs the press. Scoped
with `with (Cutscene)` rather than fired globally: that is the object class holding
everything up, and a real broadcast would reach handlers with nothing to do with it. Note
`Bathroom_Dark_Back` sets `active = 0` on room start and only arms after `alarm[0] = 30`,
so the first half second genuinely does nothing.

The general lesson: mirroring the game's own "nothing is interactive" rule is only safe
where something else takes over the announcing. Wherever it is not, the mirror produces
silence that reads as a crash.

### Switches, valves, curtains and drawers

A switch tells you nothing about what it did. Its hover is the same hand cursor either way,
and **the state lives on a different object** - the hall light switch writes
`Hospital_Hall_Light_02.active`, the shower valve writes `Hospital_WC_Shower.active` - so
there is nothing on the switch itself to read at runtime.

The pairs are derived by `gscripts/find_toggles.py`, which reads the decompiled dump for the
two shapes a toggle takes here (`if (V) V = 0; else V = 1;` and `V = !V;`), plain, dotted, or
inside a `with` block. **21 in the entire game**, of which 16 are worth announcing - `locked`
is already covered by the cursor verb, `Test_Status` is a debug object, and
`Hospital_Reception_Mouse` toggles its own `visible` to advance the screen image, where any
on/off wording would be a guess.

A blunt rule over variable names would have been wrong: `active` is set by 47 objects and
mostly means "currently being dragged" (`Bridge_Clip`), not "switched on".

Wording is per row, because `active` covers a light, a billboard, a curtain and a running
shower and no single phrase is honest for all four. Every row is validated at patch time -
both objects must exist and the target must really set the variable in its own Create,
`visible` excepted as a builtin - and a row that fails is dropped and reported rather than
compiled into a read that would crash the game.

### The hospital elevator and the hall lights

`Hospital_Elevator_Button` (in `Lvl_Hospital_Hall_03`) is gated on
`Hospital_Controller.hall_light`:

* Hover: `if (!used || (!door.open && hall_light)) _cursor_hand(); else if (!hall_light)
  _cursor_locked();`
* Press: opens the door only `if (!Hospital_Elevator_Door.open && Hospital_Controller.hall_light)`.

So the button reads "use" the very first time regardless - `used` starts at 0 - and every
time after that it reads "locked" while the lights are out. The first press is not wasted,
it just does nothing except make the call-button light visible.

The **only** thing in the whole game that writes `hall_light` is `Hosptital_Hall_Light`
(the game's own misspelling), a light switch that lives in a **different room**,
`Lvl_Hospital_Hall`. It is a plain toggle with a `_cursor_hand()` hover, so it read as
"use" whether the lights were on or off - there was no way to hear the state at either end
of the dependency, and no way to hear that the elevator was waiting on it.

The tick now says which way the switch is set, and appends the reason to the elevator
button. `Hospital_Controller` is persistent and its Create always sets `hall_light`, so both
reads are safe wherever it exists.

`hall_light` starts at 1, which means an elevator found locked is always the result of the
switch having been flipped - it is never the game's initial state.

### Which announcements may interrupt

`bdc_speak`'s second argument is `interrupt`: non-zero cuts off whatever is being said, zero
queues behind it. Getting this wrong is silent - the message is produced, it just never
finishes - so it is worth being deliberate about.

Everything the player asked for interrupts: arrow keys, F3, pressing Enter on something.
Two cases must NOT:

* **The focused object vanished.** Nobody asked for that announcement - the focus moved
  because the thing under it disappeared - and the commonest way for that to happen is
  picking it up, which queues `<item> added to your inventory` on the very same frame. The
  scene block runs after the gained-items diff, so interrupting cut the pickup message off
  mid-word every single time.
* **F1 and F2.** Both announce the new setting and then re-read the current entry. That
  entry was cutting off the announcement that had just been made.

### Spoken names: the area prefix (F1)

Nearly every object is named after the room it sits in - `Graveyard_Cementery_Gtave` in
`Lvl_Graveyard_Cementery` - so the area was repeated in front of all thirty-odd entries in
the list while carrying no information: it is the same for everything in the room. 2,436 of
the 4,635 objects get a shorter name.

Worked out **at patch time**, not at runtime, for two reasons. Doing it in GML would need
`string_char_at`, `string_copy`, `string_delete` and `room_get_name`, none of which this
game uses anywhere - and a builtin the compiler declines to emit is a failure that shows up
only once the patched game runs (see the `variable_local_exists` note). It is also strictly
better information: which rooms an object really appears in is knowable here and is not
derivable from its name.

Leading tokens are matched against the room name with `Lvl_` removed, the **last** token is
never stripped so nothing can be reduced to nothing, and an object placed in more than one
room keeps the **shortest** strip of any of them - so a name is never cut by an area it is
not currently in. Mismatches simply do not strip: `Graveyard_Cementary_Gate` (the game
spells it both ways) keeps one token, `Hosptital_Hall_Light` keeps all of them.

Because the match is mechanical, F1 toggles it - a name it gets wrong is better heard in
full. The clutter filter below is F2, and **repeat moved from F1 to F3**.

### Ambient clutter (F2)

The graveyard rooms carry eight `Grave_Debris_Mask` hotspots each, 30 objects in all, whose
entire handler is a random scraping sound. They are hidden from Objects and Scenery but
kept in Everything, so nothing is unreachable - a couple of them do respond to item 1091
with a dialogue.

### "use" versus "pick up"

The hand cursor is the game's catch-all: taking an item, pulling a lever, opening a drawer
and shaking a scarecrow all draw the same hand, so everything read as "use". The press
handler distinguishes them - taking an item is the one that calls `_item_add` - so the 204
objects that do are read out as "pick up" instead. Decided at patch time by scanning the
user event 0 bytecode, walking the parent chain since a child with no handler inherits one.

### The forest leech - a second unwinnable mouse-proximity trap

The worst one found since `Bridge_05_Barier`, and on the good route it is fatal.

`Forest_Swamp_Head` gives the leech (item 1138). Taken **without the Glove active**, it
attaches to you: `Item_Leech.ready = 0`, one point of damage, `Forest_Leech.visible = true`.
The cauldron then refuses the leech - `if (!Item_Leech.ready) exit;` - silently.

The only thing in the game that ever sets `ready` back to 1 is `Forest_Leech`'s own Step:

```gml
if (point_distance(x, y, mouse_x, mouse_y) < 20)
{
    image_xscale += 0.005;      // grows only while the POINTER is near it
    ...
}
// at image_xscale >= 1: Item_Leech.ready = 1, it drops off, _status_add(1036)
```

No click, no handler, and `Forest_Leech` has no parent - it is not an `Interactive_Object`,
so it never reached the scene list and there was nothing to aim at. From 0.5 to 1.0 at
0.005 a frame is ~100 frames of the pointer simply resting on it.

Fixed the same way as the wire: `Forest_Leech` joins `a11y_prox`, so focusing it warps the
real pointer onto it and leaves it there. It gets its own label rather than the generic
"look at this", because what it needs is for you to stay put.

Proximity entries are now also gated on `visible`. `Bridge_Wire` is always visible so
nothing changes there, but `Forest_Leech` starts invisible and only appears once it is
actually on you - listing it before that would have been a phantom entry in every swamp
room.

### Good-route ingredients, and the second worm

`ending_good` with a real worm on the fishing rod (`fake_bait = 0`) needs **ten**: spiny
flower, nasty mushroom, rotten fish, dirty worm, leech, egg, berries, grain, and - only if
`Status_Blood_Hands` was up when the recipe was read - blood and salt.

The catch is that **a worm is needed twice**: once as bait, once as an ingredient. The
second one loops back through the pot. `Forest_Lake_Fishes` gives the rotten fish first;
its Room Start then checks `Forest_Controller.fish_in_pot` and only then spawns the flies
that make a worm available:

```gml
if (Forest_Controller.fish_in_pot && !flies) { ...flies and worms... flies = 1; }
```

So: cast the rod, take the fish, **put the fish in the cauldron**, come back to the lake,
and press the fish again for the worm. `Forest_Lake_Fishes` itself only becomes visible and
playable once `recipe && fishing_rod_water`.

### The forest cauldron

`Forest_House_Inside_Cooking` accepts each ingredient, bumps
`Forest_Controller.cooking_progress`, and sets `done_cooking` only once
`cooking_progress >= Forest_Controller.ingredients`. Filling the can is an **`else if` on
that flag**:

```gml
if (!done_cooking) { ...ingredients... }
else if (_check_item(1139)) { _item_add(1140); ... }   // Item_Drink_Can -> Item_Energy_Drink
```

So with one ingredient missing, pressing the pot with the can produces no sound, no message
and no wrong-item mark - the branch simply is not reached. Indistinguishable from a broken
control, and the only cue the game gives is that the bubbling animation swaps
`Forest_House_Inside_Bubble_Maker` for `Bubble_02_Maker`.

**The recipe is conditional, and its length is fixed when you read it.**
`Dialogue_Forest_Recipe` computes `ingredients` at that moment:

| always | spiny flower, nasty mushroom, rotten fish (3) |
|---|---|
| `!fake_bait \|\| hospital_worm \|\| !ending_good` | dirty worm, leech (+2) |
| `Status_Blood_Hands > 0` | blood, salt (+2) |
| `Status_Bird_Like == 0` | egg (+1) |
| `ending_good` | berries, grain (+2) |
| otherwise | motor oil (+1) |

Because the statuses are read once, washing your hands after reading the recipe does not
shorten it. The tick now says `N of M ingredients in`, or `ready, use the can on it`.

Two things that already worked and did not need changing: the leech is refused until
`Item_Leech.ready`, and the hover raises `wrong_item` for it, so the mod already says "takes
an item, but not this one" - and `Item_Leech`'s own `name` becomes "Leech (sucking blood)",
which the inventory reader reads out. Salt is not consumed, only deactivated.

### Inside the crypt

`Lvl_Graveyard_Crypt_Inside` holds one real puzzle and two things that look like puzzles and
are not.

**The sign** (`Graveyard_Crypt_Inside_Sign_Small`) is the progression. It starts
`image_alpha = 0, playable = 0` - invisible AND unclickable, so the mod correctly does not
list it. Using **Matches (item 1119)** on `Graveyard_Crypt_Candle_Mask` lights both candles
at once (its handler ends `with (object_index) instance_destroy()`), which spawns
`Graveyard_Crypt_Sign_Cutscene`; 30 frames later that sets `appear = 1` and the sign fades
in until `playable = 1`. Pressing it sets `Graveyard_Controller.night`, `Controller.cavanas
= 23` and goes to `Lvl_Graveyard_Sign`. There, `Graveyard_Sign_Darkness` sets
`hidden_open = 1`, which slides `Graveyard_Crypt_Hidden_Door` open back in the crypt.

**Ars Moriendi** (`Graveyard_Ars_Moriendi`) is flavour. `_cursor_eye()`, and reading it
spawns a one-line dialogue: `"Ars Bene Moriendi"`. Nothing is gated on it. It is gated
itself on `Controller.blind` needing item 1116.

**The scythe blade** (`Graveyard_Crypt_Inside_Scythe_Blade`) is a pure trap: `_dmg(0.5)` the
first time, a scraping noise every time after, no state set anywhere. It has no hover
handler, so it has no cursor and lands in Scenery reading as an ordinary name.

That last one prompted a general fix. An object whose press calls `_dmg` and never calls
`_item_add` only ever costs you health - 37 of them in the game - and they now say ", hurts
you". Objects that give an item are excluded on purpose: `Graveyard_Flower` wounds you only
when picked bare-handed and is something you are meant to take, so warning every time would
be wrong.

### The glove and the flower

`Graveyard_Glove` is in `Lvl_Graveyard_Gravedigger_Home` (the shed) and gives item 1127.
`Graveyard_Flower` is in **`Lvl_Graveyard_Gate`** and gives item 1121. The flower then goes
into `Graveyard_Jug` in `Lvl_Graveyard_Flower`, which sets `Graveyard_Controller.flower`.

Picking the flower without the glove **as the active item** costs a wound and a status:

```gml
if (!_check_item(1127) && !instance_exists(Status_Shield))   // _check_item = ACTIVE item
{ ... _dmg(1); _status_add(1035); }
```

This exposed a second item-targeting idiom the mod did not handle. The one already covered
is `_activate_item_cursor` plus the `wrong_item` flag. This is the other: the hover sets the
cursor straight to the **held item's own** `cursor_image`.

```gml
if (_check_item(1127))
    _cursor_different(Item_Glove.cursor_image);
else
    _cursor_hand();
```

A sighted player sees the glove icon appear over the flower. For the mod that cursor id
matched no known verb, so holding the glove made the entry read as a bare name with **no
verb at all** - strictly worse than holding nothing, which at least said "pick up". The tick
now compares the probed cursor against `active_item.cursor_image` and says ", USE Glove
HERE". The same idiom appears on `Forest_Flower` and `Hospital_B_Bus_Spider_Baby`.

A `pick up` fallback was added underneath it: anything whose press calls `_item_add` but
whose hover draws an unrecognised cursor now says so rather than reading as a bare name.

### Where the pickaxe goes

`Graveyard_Pickaxe` is in `Lvl_Graveyard_Flower`, and starts `playable = 0`: it only becomes
clickable once `Graveyard_Scarecrow_First` has been used. Its one and only use is
`Graveyard_Cementery_Gtave` in `Lvl_Graveyard_Cementery` - the game's own typo for "Grave" -
which consumes item 1123 and reveals `Graveyard_Cementery_Pickaxe`, `Grave_02` and
`Grave_03`.

Nothing in the game is called a cement block. The nearest names in that room are
`Graveyard_Cementery_Brick` / `_02` (sound-only), the five `Graveyard_Cementery_Wall_Break`
pieces (sound, then destroy themselves) and `Graveyard_Cementery_Grave_Part` x4 - all
flavour, none of them a puzzle.

### Spoken names: the trailing `Mask`

`_Mask` is the game's own suffix for a bare collision shape, and 642 of the 4,635 objects
carry it - it is on essentially every look-at hotspot, so reading the object name out
verbatim ended almost every announcement with the word "mask".

The trim is baked at patch time into `a11y_pretty` (object -> spoken name), not done with
string surgery each frame, and the runtime falls back to the plain underscore-to-space
rule for anything absent from the map. Two constraints shaped the rule:

* Only a **trailing** `Mask` token goes, and only when two or more tokens remain.
  `Cyclops_Mask` and `White_Mask` are actual masks, and stripping them would leave
  "Cyclops" and "White". `Memories_Kitchen_Mask_Rag` keeps its middle `Mask` too.
* `Masked` is a different token and is never touched - the masked man has a lot of
  objects.
* Trailing numbers survive: `Ending_Flat_Picture_Mask_02` becomes "Ending Flat Picture 02",
  because the number is what tells two copies of the same hotspot apart.

The two synthetic scene entries are skipped, since the branches that build their labels
test the **stored** name (`@prox`, `View_Back`) rather than the spoken one.

### The story happens in pictures, and the mod was calling it an error

Reported from play: *"sometimes when story events are happening it says nothing can be
used yet"*. That line was the mod's, and it was wrong twice over.

The game refuses all interaction while a `Cutscene` exists - `_interactive_get_type`
returns -4 - so the tick mirrored that and said **"Nothing here can be used yet. Press
Enter to carry on."** But of the game's **74** Cutscene descendants, exactly **three**
have a global left-press handler (`Bathroom_Dark_Back`, `Bathroom_B_Dark_Back`,
`Memories_Night`). The other 71 ignore a click completely and end on their own alarm. So
71 times out of 74 the mod told the player to press a key at the precise moment no key
does anything - which reads as the mod being broken at exactly the moments the game is at
its most dramatic.

Worse, saying "nothing can be used" is a description of the *interface* when what the
player wanted was a description of the *scene*. An earthquake, a monster dropping out of
the dark, a door being hammered on from the other side - all of it is drawn, none of it is
text, and none of it existed for a screen reader.

Three things fixed it.

**Which cutscenes take a key** is baked (`a11y_cutkey`) by walking the parent chain for an
actual `Mouse` event with subtype 53. Only those get the Enter offer, and only they act on
it. A cutscene with no handler now says **"Something is happening. Wait."** - a state, not
an instruction. F3 during any scene repeats what the scene is.

**Story events are announced by two sweeps**, both baked and both validated at patch time.

* *Something appears* (`a11y_ev` plus the parallel lists `a11y_evo` / `a11y_evt` /
  `a11y_evon`): 41 objects whose arrival is an event. The announcement is the 0-to-1
  **edge** of `instance_number(obj) > 0`, not the presence, so a scene that sits on screen
  is heard once and one that can happen again - the bridge earthquake, the kitchen mask's
  scream - is heard every time. A `ds_map` cannot be walked with anything this game
  already calls, which is why the same rows exist twice, once as a map for the cutscene
  reader and once as lists for the sweep.
* *A flag flips* (23 rows): the chapter controllers keep the entire story as instance
  variables - `Graveyard_Controller` alone has 29 - and setting one is exactly what
  "something happened" means. `Bridge_Controller.doll_broken`, `Hospital_Controller
  .fingers`, `Graveyard_Controller.zombie_cut`, `Controller.beehive_destroyed`,
  `Controller_Hospital_B.butcher_wc` and the rest.

The flag reads are **generated GML, not a runtime table**. Reading a variable whose name
is only known at runtime needs `variable_instance_get`, and a builtin this game does not
already call is a failure that shows up only once the patched game runs - the
`variable_local_exists` lesson. Generating `instance_find(Bridge_Controller, 0)
.doll_broken` means the compiler resolves the name at patch time, where a mistake is a
build error. The previous value of each row lives at that row's index in `a11y_flgl`.

**At most one event is announced per frame.** Several arrive in pairs - smashing the pram
doll sets `doll_broken` and `device_broken` together, the scarecrow's attack creates a
cutscene and a screen shake in the same instant - and two sentences fighting over the
speech channel is worse than the second being dropped.

**Both sweeps are skipped while a `Pause` exists**, and this is not optional. `Pause`'s
Create calls `instance_deactivate_all` and reactivates only Controller, Music, Light and
Screen, so every chapter controller genuinely stops existing for as long as the pause
menu, the options screen or the status screen is open. Without the guard both sweeps read
every flag back as 0, recorded that, and then replayed the whole story one line at a time
as soon as the menu closed.

### instance_exists is not a promise that instance_find will return an instance

The second crash this patch shipped, and the more dangerous of the two because the guard
that had it was the first thing the tick does on every frame:

```
Variable <unknown_object>.<unknown variable>(100055, -2147483648) not set before reading it.
at gml_Object_Controller_Step_0
```

There is no object name and no variable name in that message because there was no instance
to name. The shape that produces it is the one used everywhere in this file:

```gml
if (instance_exists(SomeObject))          // true
{
    var a = instance_find(SomeObject, 0); // noone
    if (a.field)                          // fatal
```

`instance_deactivate_all` is what pulls them apart, and **every pause menu in this game
calls it** - `Pause`'s Create deactivates everything and reactivates only Controller,
Music, Light and Screen. A deactivated instance still answers `instance_exists` in this
runtime, while `instance_find` walks the ACTIVE list and returns noone. Anything that runs
unconditionally in the tick and reads a field through `instance_find` is therefore one
pause away from killing the game.

It was reported from inside the VHS film, because `Nightmares_GUI_MENU` is an
Interactive_Object whose whole press handler opens `Game_Menu` - so it is the one place the
scene reader itself offers the player a button that pauses the game.

The rule now, everywhere in the patch: **guard the instance the find returned, never the
object it came from.** All 24 sites were converted, including the generated switch table
(which reads through a `a11y_sw` scratch instance) and the generated story-flag rows, and
the marker recovery guard in both the tick and the Escape prepend. The pattern is checked
by a verifier marker rather than by memory.

Worth noting what the fix cost elsewhere: the verifier marker for post-load recovery was
the literal string `instance_exists(Worm_February)`, so correcting the guard turned it red.
The needle now tests the mechanism - `instance_find(Worm_February, 0)` and the schema-tag
comparison - which is what it should always have done.

### A settings screen with no pixels in it

The mod now has its own options, reachable as the last item on the title screen and in the
pause menu. Four of them: warn about danger, hints about what to use, say the area before
every name (what F1 already did), and hide blood, mess and rubble (F2). They are kept in
**A11y.ini**, so a preference set today is still set tomorrow.

The interesting part is how a new menu item gets into a game whose menus are pictures.
It does not. The row is a **synthetic entry in the reader's own list** - object id `-4`,
name `@a11y`, `cy` of 99999 so the reading-order sort always leaves it last. Nothing is
drawn and nothing on screen changes; a sighted player sees exactly the menu they always
saw. That cost three small special cases and no new sprites, no new objects, and no risk
to the game's own menu logic:

* the scan appends the row when `Game_Menu` exists or the title buttons are live;
* `a11y_lbl` and `a11y_act` carry `@a11y` like any other button name, so the reader
  speaks and navigates it with no changes at all;
* the Enter handler checks the name before the instance, because `instance_exists(-4)`
  is false and every other path already guards on it.

The screen it opens is the same idea one level further: no objects, no drawing, just a
five-row list the tick owns. While it is up the menu reader and the world reader both
stand aside, Escape closes it from the prepend on `KeyPress 27`, and if the menu it hangs
off disappears underneath it - the game resumed with a click, the title screen moved on -
the scan closes it, so the world reader can never be left standing aside for a screen
nobody can reach.

Left and Right flip a setting as well as Enter, and every row is announced through the
same short delay the world reader uses, for the same reason: see the note above on the
key that throws the answer away.

**What the toggles actually cover.** Warnings: `, hurts you`, the electrified coin, and
the three places the board game says a choice costs the good ending. Hints: `USE <item>
HERE` in both its forms, `takes an item, but not this one`, what the kitchen masks want,
the portrait the mask below it is blocking, the hall lights the elevator is waiting on and
the cable the computer is waiting on. The *state* half of those sentences is never
suppressed - a mask still says it is uncovered and screaming, it just stops telling you
that a rag fixes it. The board game's own readouts stay whatever the toggles say, because
there the readout is the interface: a sighted player can see which squares are crossed out
and which are deadly, and hiding that would not remove a hint, it would remove the game.

### The game saves and does not say so

`Autosave` writes `Auto.sav`, stamps the date into `Info.ini`, and creates `Autosave_Info`
- a caption at the top of the screen that fades out over about a second and a half. There
was nothing to hear, so there was no way to know the game had saved or where dying would
put you back.

`Autosave_Info` is created on the line after `game_save` returns and nothing else creates
it, so its arrival *is* the save. Watched by count rather than by instance, because it
destroys itself once it has faded and the next autosave makes a fresh one.

Queued rather than interrupting. An autosave usually fires on arriving somewhere, which is
exactly the moment the room and its contents are being read out, and cutting that off to
say the game had saved would trade the more useful sentence for the less.

### The key that asks for the answer is the key that throws it away

Reported from play: pressing Enter on an entry acts on it - you can hear the object react -
and then says nothing. Arrowing onto the same entry reads it out perfectly. Pressing again
is silent again.

There is nothing wrong with the code, and I checked that carefully before touching
anything. The decompiled Step does exactly what it should: the press runs, the instance
survives, `a_wspeak = 1`, `a_wint = 1`, the label is built and handed to the bridge. The
speech bridge has no de-duplication, Prism's NVDA backend has none, and no other code path
in the tick can cancel it afterwards. The utterance is sent on every press.

It is thrown away by the screen reader, and by design. **NVDA ships with "Speech interrupt
for Enter key" turned on**, and there is no equivalent rule for the arrow keys. Speech
handed over in the same instant as the Enter that asked for it is cancelled before anyone
hears it. That is precisely the shape of the report - arrow audible, Enter silent, the
action itself working fine - and no amount of reading the GML would ever have found it,
because the GML is correct.

So nothing a press has to say is said on the frame of the press any more. `a11y_wpend`
holds it for three frames, and keeps holding while Enter is still down: fifty milliseconds
is imperceptible, and by then the reader has finished interrupting itself. Everything the
press branch used to speak immediately now goes into `a11y_wsay` and comes out together
when the timer fires - the scenery description, "Rubbish cleared out.", "Paperclip moved
into place.", the ending confirmations - with the entry's own re-read queued behind it as
before.

Three things cancel a pending announcement rather than letting it arrive late and wrong:
an arrow key or mode change (the player has asked for something newer), a room change (the
arrival announcement is the better answer), and the list going empty.

The general rule, worth remembering for any screen-reader mod: **a screen reader's own
keyboard handling can silently discard what you just said.** If a control acts but does
not speak, and the same announcement works when reached another way, suspect the key
rather than the code.

### Asking a thing what it is can delete it

Reported from play: looking at the wooden sign on the bridge killed the game with

    Unable to find any instance for object index '100068' name '<undefined>'

100068 is a room-placed instance, and the room file names it: `Bridge_01_Bird_Mask` in
`Lvl_Bridge_01`, sitting at x=362 - right on top of the two `Bridge_Light_Billboard`
instances at 260 and 366. It is the crows on the sign.

The whole of that object is one event:

```gml
instance_create(388, 173, Bird);
var ins = instance_create(413, 185, Bird);
ins.direction = irandom_range(50, 80);
_sound_play(27, Listener_Position.x - 100, Listener_Position.y - 100, 0, 50, 50, 1, 0, 50, 0.6);
instance_destroy();
```

and that event is `Other_11` - **the hover**. It has no press handler at all. Getting near
the sign scares the birds off it, and then the hotspot is gone. That is the entire
interaction.

The reader asks every focused entry what it is by running the game's own hover and reading
the cursor it sets, because the cursor is the only place the kind of interaction is
recorded. On this one object, asking the question destroys the thing being asked, and the
very next line that touches it - `ds_map_exists(a11y_hurt, a_wt2.object_index)` - is fatal.

**Both existing safety sweeps pass on this code.** Nothing here reads a variable before
assigning it, and the dereference *is* guarded: `instance_exists(a_wt2)` is tested and
passes. The instance dies between the guard and the read, killed by a call this patch made
itself. That is a third failure mode, and it now has its own sweep: after a
`with (x) event_user(...)`, any dereference of `x` before a fresh `instance_exists(x)` is
flagged.

The fix is not another guard, though. Objects whose hover calls `instance_destroy` are
found at patch time - scanning the hover code of the object and its parents, and one level
into any script it calls - and baked into `a11y_nohover`. There are **two** in the whole
game. Those are never probed; the label falls back to the object's own name and the cursor
reads as neutral, which is honest, because nothing was asked. The birds still scatter: the
pointer warp that follows every focus change puts the real mouse on the entry, and the
game fires its own hover in its own End Step a moment later. The world behaves exactly as
it does for a mouse player - it is just not this patch pulling the trigger in the middle of
reading a label off the thing.

The injector had in fact *known* about this object since the categories were first baked -
there is a comment naming `Bridge_01_Bird_Mask` and `Hospital_Fly_06` as hovers with real
side effects, concluding that probing only the focused object is "exactly what hovering it
would do anyway". True, and beside the point: the problem was never the side effect, it was
that the side effect was self-deletion.

The whole of the label building after the probe is also wrapped in one fresh
`instance_exists(a_wt2)` now. The baked set is the fix; the wrap is so that forty
dereferences do not quietly depend on that set staying complete. Three sites elsewhere got
the same treatment - the paint tool, the inventory item and the two nested guards below -
and with those in place all three sweeps come back clean.

Two `&&` conditions were nested while I was in there:
`instance_exists(a_wt) && ds_map_exists(a11y_scn, a_wt.object_index)` and
`instance_exists(a_pi3) && a_pi3.visible`. GML's short-circuit evaluation is a compiler
option, not a language guarantee, so a condition that tests an instance and dereferences it
in the same expression is only safe if that option happens to be on. The first of those was
one destroyed-on-press object with a scenery description away from being the same crash.

### The last screen counted the things in your pockets

Reported from play: F4 in the ending screen said `9 interactive objects, 7 usable`, and
there was nothing there to find.

Both halves of that were wrong, for different reasons.

The count was wrong because `Item` is a child of `Interactive_Object` and every item in
the game is **persistent**. What you are carrying travels with you from room to room and
was being counted as furniture standing in the room. Everywhere else in the game that
merely inflated the number by however many things were in your pockets; in the ending
screen it *was* the number, because `Lvl_Last_Screen_Bad` contains no interactive object
whatsoever. Its entire instance list is `Bed_Scene`, two `Bridge_Chapter_Birds`, a
`Listener_Position`, `NO_INVENTORY`, `NO_MENU`, `Room_Start_Black` and
`The_End_Controller`. Nine items in the inventory, seven of them with `playable` set, and
F4 announced them as a roomful.

The object list itself had always been right - it drops items through the same skip map
the sweep builds - so the two disagreed, which is the worst of both. The F4 count is now
taken over exactly the population the list is built from: items out, interface buttons out
through `a11y_skip` rather than by name, so the `Memories_` variants of `Menu_Btn` and
`Status_Btn` go with them. When that comes to zero it says **"Nothing in this room can be
used"** rather than "0 interactive objects, 0 usable", which is a sentence about the room
instead of a broken-sounding readout.

And there was something to find, but nothing could ever have found it.
`The_End_Controller` is what ends the game: a ten second timer arms it, and after that a
click **anywhere on the screen** creates `End_Game` and returns to the menu. It is not an
`Interactive_Object`, so Controller's click dispatch never offers it and the scene sweep
cannot see it; its handler is a *global* left press, which is not a press on an instance
that Enter could reach. `NO_MENU` is in the room as well, so Escape did nothing either.
The ending screen was a room with no contents and no way out of it - the player's only
option was to kill the process.

It is now listed through `a11y_extra`, the same route as the paint palette, and Enter
runs `event_perform(ev_mouse, 53)` on it. Its `active` flag is honoured, unlike
`View_Back`'s: that one oscillates against the pointer position and refusing on it lost
real presses, whereas this is set once by an alarm and never goes back, so "Not yet. Let
the ending play out first." is the truth rather than a dropped keypress.

### All three endings are the same drawing

`Lvl_Last_Screen_Bad`, `Lvl_Last_Screen_Good` and `Lvl_Last_Screen_Neutral_Good` are one
tile each, and it is the same tile: `B_Bed`, the bedroom from the very first screen of the
game, seen from the pillow, looking down the duvet at your own bare feet. You wake up
where you started. Everything that distinguishes the endings is **light and sound** laid
over that one picture - `Ending_Good_Light` and birdsong in the good one,
`Ending_Window_Rain` on the window in the neutral-good one, and in the third
`Light_Controller.darkness` set to 0.8 with a black `S_Bed_Darkness` overlay at 0.75
alpha. A sighted player is told which ending they got entirely by how dark the room is.
There was nothing at all to hear.

All three are now `a11y_pics` rooms, so the picture is read on entry and F5 repeats it,
with the light named at the end of it.

The room name needed overriding too, and not only for tidiness: `Lvl_Last_Screen_Bad`
serves **both** the bad and the neutral ending - `Bed_Scene`'s Create picks between them
on `Controller.ending_neutral` after the room has already loaded - so "Last Screen Bad"
was both an announcement of the outcome and, half the time, the wrong one. All three read
"Your bedroom, the end", and the description carries the difference the way the picture
does.

### A room with nothing in it, on purpose

Also from the VHS film. Its kitchen opens with **every single object set to
`playable = 0`**: the scene is a clown walking across the room, and only when he leaves
does `Little_Nightmares_Door.alarm[0]` fire and make the door usable. For those seconds the
room genuinely contains nothing, which from the keyboard is indistinguishable from a room
that is empty for good - so the player stops waiting and starts looking for a bug. Which is
exactly what happened.

Two fixes. `Little_Nightmares_Clown` joined the story-event table, so arriving in the
kitchen says what the scene is and that nothing can be used until he has gone. And the
scene reader now announces the **nothing-to-something edge**: when a list that held zero
entries gains one, it says so. Only that edge - anything looser chatters without stopping,
since birds, rain and stains are all `Interactive_Object` children and the list churns
several times a second outdoors.

`Nightmares_GUI_MENU` also joined the skip set. Its entire press handler is
`instance_create(x, y, Game_Menu)`, which is what Escape already does, and it is
**persistent**, so it followed the player out of the film into every room afterwards. It
was also, before this, the only entry in the kitchen's list - a button whose only effect
was to open a menu and, until the fix above, crash on the way back out.
`Nightmares_GUI_Skip` is deliberately still listed: it is the only way to skip the film.

### The ordering guard: a generated block cannot outrun its own ds_list_create

Shipped broken once, and worth recording in full. Every generated block fills structures
the init creates a few lines above it, and the ORDER of those two things is load-bearing:
GML has no hoisting, so a `ds_list_create()` that lands BELOW the block that fills it means
the first generated line reads a variable that is not set, which this runner treats as
fatal. The game died on Controller's Create with:

```
Variable Controller.a11y_evon(100681, -2147483648) not set before reading it.
```

Nothing about the failure is visible at build time. The C# runs cleanly, the patch imports,
all 227 verifier markers are found, and the decompiled output looks correct - because it IS
correct apart from the order. Adding one more variable to a block that already had three
was enough to do it.

So `CheckOrder` now runs immediately before every init substitution: for each placeholder,
every `a11y_` name the generated block touches must be assigned somewhere in the init text
ABOVE that placeholder, or the build throws. Proven by reintroducing the bug against a
copy - the third guard in this file built that way, after the raw-quote check and the
dangling-else assertion.

### The tape in the canteen is a whole second game

Reported as *"a huge portion of the game doesn't work"*. Putting the VHS tape into the
canteen television does not play a video: it drops the player into **Our Little
Nightmare**, a fourteen-step point-and-click of its own, in a cartoon kitchen, playing as
a clown. Four rooms, its own item set (`Item_Nightmare_*`), its own GUI, and an ending
that writes `SkipSitcom=1` into `Config.ini` so a Skip button exists on later runs.

Mechanically it was already fine from the keyboard, which is why nothing looked obviously
broken: every object in it descends from `Interactive_Object` and sets `playable`, and only
the one thing you are meant to press next is ever playable, so the scene list is short and
correct. Two of the four rooms have **no `View_Back` at all** - the way out is *created* by
the press that finishes the room (`Little_Nightmares_Fish_Fridge` spawns
`Little_Nightmares_Fridge_Back`), which the existing Go-back handling picks up as soon as
it exists.

What it had none of was words. No dialogue but one note, no interface text, and every
press starts the clown **walking** for two or three seconds before the next thing becomes
usable. From the keyboard that reads as a control that did nothing, twice over: the press
is silent, and then the list refuses to change.

`Little_Nightmares_Clown_02.step` is the film's own progress counter, 0 to 14, incremented
the instant he arrives and immediately used to reveal whatever comes next - so reading it
off is exactly the beat a sighted player is watching for. One sentence per step. Only an
**increase** is announced, and the counter is only reset when `Little_Nightmares_Effects`
(the persistent television-static object, alive for exactly as long as the film) is gone -
not merely when the clown is off screen, or stepping back from a close-up repeated the
last thing he did.

The four rooms also got picture descriptions and hand-written names ("The tape, the
kitchen"), since the internal names put the film in the hospital, which is where the
television is and not where the player now is.

### Descriptions for the art itself

Reported as *"you should make descriptions for scenery objects"*. The close-up rooms were
already done; this is the props.

Every `Interactive_Object` descendant that is placed in a room, is visible, and has a
sprite that is not one of the `S_Test` placeholders - **480 of them** - was exported to PNG
and laid out in labelled 5x4 contact sheets by `gscripts/sheets.py`, twenty-four sheets,
and transcribed by eye. 298 got a sentence of their own.

The other 156 got one by **family**, and that is the important half of the finding. Roughly
half of everything drawn in this game is numbered decoration: `Graveyard_Gate_Wall_Break`
01 to 08, `Bridge_02_Walk_Break` 01 to 03, `Crypt_Wall_Part` 01 to 09. Looking at them
settled what they are - eight to ten copies per room of one crack in the concrete or one
broken-off chip, and nothing else. Six regexes cover the lot. They also went into the F2
clutter filter, which is what the same report asked for from the other direction: *"there
are a lot of blood and mess objects, so you can filter them out"*. Clutter went from 170
objects to 267.

**Nothing usable can be hidden by that filter.** A name matching the pattern is only a
suggestion; the object is then checked the three ways the rest of the injector decides what
an object is FOR - its hover reaching an exit or action cursor, its press calling
`_item_add`, its press calling `_check_item` - and any hit disqualifies it. 22 name matches
are kept for exactly that reason.

The description is read on the **first** Enter and not the second. `a11y_scn_id` holds
whichever instance was last described, so holding Enter on one thing does not read the
paragraph again, and moving to anything else re-arms it. The short label still follows,
**queued** behind the description rather than interrupting it: plenty of these are things
whose press also changed something, and swallowing the state readout to make room for the
picture would trade one silence for another.

### Two bugs in the scenery descriptions, found from play

**The anti-repeat rule was keyed off the wrong thing.** It remembered the last object
PRESSED, not the one in focus, so after pressing anything with a description the rule
latched: walk along a wall, come back to the same bin, press it again, and it stayed
silent because nothing else described had been pressed in between. From the player's side
that is a description that works once and then never again - reported as *"it announces the
object again and again"*, which is the short label doing its job with the description
missing behind it. `a11y_scn_id` is now cleared whenever `a11y_w_id` changes, so leaving
the entry re-arms it, which is what the rule always meant.

**The Scenery view and the described set are disjoint, by construction.** Everything
transcribed from a sprite is by definition an object the game DRAWS. The Scenery filter is
`a_cat == 0 && !visible` - the bare collision shapes - so browsing Scenery finds nothing
described at all. Descriptions turn up under Everything and Objects and nowhere else.

Those hotspots have no art of their own; the picture of what they cover is the room
background under them. `gscripts/export_hotspots.csx` plus `gscripts/hotspot_sheets.py`
crop each one out of its room's artwork, draw the hotspot's own box on the crop, and tile
them into labelled sheets the same way the props were done: 698 hotspots across 155 rooms,
35 sheets. **All 35 have now been transcribed** - 681 of the 698 have a description, and
the shipped build carries 1135 described objects in all.

Each cell of those sheets shows **two** pictures of the same hotspot, and that is what makes
the pass possible at all. On the left is the object's own sprite; on the right the room
crop. Roughly half of these objects are bare collision shapes laid over art that belongs to
the room - their sprite is a featureless coloured blob and the crop is the picture. The
other half are real drawings that are merely invisible until the story reveals them - their
sprite is the picture and the crop is an empty patch of floor. Nothing in the data tells the
two apart (a mask sprite is not named differently and is not `S_Test`), so both are shown
and the transcriber picks. The blob colours are arbitrary and are the tell.

The 17 left bare are deliberate: two bird spawners and four interface buttons that the scene
list already drops, `Board_Button` and `Board_Dice` which have far richer readouts of their
own, three that carry a hand-written label override elsewhere in the tick, and six whose
crop is genuinely unreadable. A wrong description is worse than none.

Getting the crop to land on the right pixels took two corrections, both of which produced
sheets that looked plausible and were wrong:

* The background art is black line work on a **transparent** ground. `convert("RGB")`
  mattes that onto black, so the first sheets were 698 black rectangles.
* `ExportAsPNG` writes the texture page item **cropped to its non-empty area**, not the
  background's nominal size. `B_Bridge_01` is a 1280x720 background whose art is a
  1156x600 rectangle at TargetX 56, TargetY 101. Room coordinates therefore need the tile
  offset, the tile scale AND the texture item's TargetX/TargetY subtracted, or every box
  lands about 80 pixels low. The check that settled it was a hotspot whose name says what
  it is - `Bridge_Tire_Mask` has to land on the tyre.

An attempt to skip the transcription by having each hotspot **inherit** the description of
whatever described prop it overlaps was built and then thrown away. Overlap as a fraction
of the smaller box gives a wall-sized mask a perfect score for containing one small prop,
and it produced things like a plank described as a crow. Tightened to intersection over
union at 0.75 - the only honest form of the test, since a hit box laid over a thing has
very nearly that thing's box - exactly 10 of 698 survived. The masks are hand-placed and
their sizes have nothing to do with the art, so there is no shortcut here.

### Two windows that keep no state at all

*"when you open and close the window it should say opened/closed based on its status"*.
`Hospital_Hall_03_Window_Open` and `HospitalB_Hall_03_Window` are the only two windows you
can work, and neither writes a variable: the press just swaps `image_index` between the
open frame and the shut one. `find_toggles.py` looks for assignments and so cannot see
them, and the switch table's validator had to learn that `image_index`, like `visible`, is
a builtin every instance already has.

The wording is **inverted** - frame 0 is OPEN - and that is not a guess.
`Hospital_B_Number_Window` only lets the queue ticket blow out of the building while
`HospitalB_Hall_03_Window.image_index` is 0, and the close branch is the one that stops the
city traffic playing.

### The ticket says 3 and the room is calling 13

`Item_Doctor_Number_3`'s localised name is just "Number" in every language. The 3 is drawn
on the sprite and written down nowhere - and the number is the whole puzzle, since the
waiting room is calling 13 (the status you collect for working that out is literally an
icon reading `13 = 3`). `a11y_iname` appends to the game's own localised name rather than
replacing it, and applies in all four places an item name is spoken: the inventory reader,
the pick-up announcement, and both forms of "USE <item> HERE". The two hotspots the ticket
lies on say the number too.

### Words on the mirror in the hospital toilets

Taking the eye out of the plughole makes `Hospital_WC_Mirror_Text` visible and fires the
Steam achievement called BUTCHER. It is a sprite, not an object you can press, and what it
shows is three words finger-painted on the glass in blood: **I'm glad I could HELP**. That
is the whole of the scene and there was nothing there to hear.

It is not an event-table row, because the object is in the room from the start and only
becomes VISIBLE - which is the wrong edge for a table that watches for an instance turning
up. It gets its own small block, plus a suffix on the mirror hotspot so it can be re-read.

### The hospital waiting room is not in wing B

Room names for the F4 report are derived mechanically - drop `Lvl_`, underscores to
spaces - which is right nearly everywhere and wrong where the internal name carries a build
letter the player has never seen. `Lvl_Hospital_B_Waiting` is the waiting room; the B is
which *version* of the hospital the room belongs to, not a wing. Nine rooms are now named
by hand and the override table reports itself stale if one of them stops existing.

## The silent-audio bug (not caused by the mod)

The game was completely silent - no music, no effects - and stayed silent on the
untouched original `data.win`, so none of this came from the accessibility patch.

**Root cause: the system-wide `%APPDATA%\alsoft.ini` sets `drivers=-dsound`.**

The exe does **not** use `OpenAL32.dll`. OpenAL Soft **v1.12.854 (2011)** is compiled
straight into `Bad Dream Coma.exe` - its source paths are still in the binary
(`.\OpenAL\Alc\ALc.c`, `alcConfig.c`, `dsound.c`, `.\OpenAL\OpenAL32\alThunk.c`). That
build has exactly **two** backends, whose names sit adjacent in `.rdata`:

| | |
| --- | --- |
| `dsound` | `0x3440EC` |
| `winmm` | `0x3440E4` |

There is no WASAPI backend. Removing `dsound` leaves only the 2011 waveOut path, which
does not work on this machine - so audio initialisation quietly produced nothing, with no
error, no dialog and no log.

That embedded copy still reads the same `%APPDATA%\alsoft.ini` as modern OpenAL, and the
config on this system was written for a *current* OpenAL Soft: it also sets `hrtf`,
`default-hrtf`, `sample-type` and `stereo-mode`, none of which v1.12 recognises. Only the
keys it does understand appear in its string table: `drivers`, `resampler`, `sends`,
`frequency`, `cf_level`, `slots`, `sources`, `period_size`, `periods`.

### The fix

This build reads its config from **exactly two places**, and the string table shows both
and nothing else:

```
00344bb8  ALSOFT_CONF
00344bc4  \alsoft.ini      <- leading backslash: concatenated onto %APPDATA%
```

There is no bare `alsoft.ini` string, so there is **no game-folder or working-directory
config** - that was added to OpenAL Soft years later. Dropping a config next to the exe can
never work, which is worth knowing before trying it.

That leaves two ways in, and the first one was tried first:

**1. `ALSOFT_CONF` (the launcher).** `Play Bad Dream Coma.bat` points it at
`alsoft-bdc.ini`, which restores `drivers=dsound,winmm`. It is read *after* the system
config, so it wins for this game and changes nothing for anything else.

This works, but **only when the game is started through the launcher**. Starting the exe,
a shortcut or GOG gives no environment variable and the silence comes straight back - which
it duly did, twice.

**2. Retargeting the config path in the exe (the actual fix).** The `\alsoft.ini` literal
is patched to `\bdcoal.ini`, an **11-byte, same-length** swap at file offset `0x00343fc4`,
so nothing in the binary moves - no relocation, no size change, no checksum concerns. The
game then reads `%APPDATA%\bdcoal.ini`, which carries `drivers=dsound,winmm`.

The original is kept as `Bad Dream Coma.exe.BDC-AUDIO-BACKUP`. Verified as exactly one
occurrence before patching, sitting immediately beside `ALSOFT_CONF` and `general` in
`.rdata` - which confirms it is the `alcConfig.c` literal and not some unrelated path:

```
b'line "%s"\n\x00\x00ALSOFT_CONF\x00\\alsoft.ini\x00general\x00on\x00\x00yes\x00'
```

Audio now works from any launch path. The system-wide `alsoft.ini` is left exactly as it
was and still applies to every other program - something on this machine evidently wants
`-dsound`. `ALSOFT_CONF` still layers on top, so the launcher survives as an override for
testing audio settings without editing the file in `%APPDATA%`.

The trade: this game no longer honours the system OpenAL settings at all. Those are HRTF,
float32 and 48 kHz - written for a modern OpenAL Soft, largely unusable by a 2011 build,
and one of them is what silenced it.

### Diagnostic dead ends, recorded so they are not repeated

- **`OpenAL32.dll` in the game folder does nothing.** OpenAL is static in the exe. A local
  override is never loaded, and `ALSOFT_LOGFILE` against it stays empty for the same
  reason - which is easy to misread as "the game never initialises audio".
- **`Process.Modules` / `tasklist /m` cannot enumerate this 32-bit process** from a 64-bit
  shell; both returned only the WOW64 shims. Not evidence of anything.
- **Force-killing the game truncates any OpenAL log.** Close the window instead.
- Audio data was never the problem: `AUDO`, `SOND` and `AGRP` are byte-identical to the
  original, every asset keeps its 2020 timestamp, and `Config.ini` has all volumes at 1.0.

### Separately: `audiogroup1.dat` really is missing

`AGRP` declares 26 groups (index 0 `audiogroup_default`, embedded in `data.win`; 1-25 map
to `audiogroupN.dat`). The install shipped only 2-25 - group 1, `Ambient_Hit`, is absent.
This was **not** the cause of the silence, but it is a genuine gap in the installation. A
minimal valid empty group (`FORM`>`AUDO`, zero entries, 20 bytes) now stands in so a group
load finds an empty group rather than a missing file. Verify/reinstall through GOG to get
the real one back; it will simply overwrite the placeholder.
