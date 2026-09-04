// Bad Dream: Coma - accessibility injection.
//
// Appends GML to the Controller object, which is persistent and placed in Lvl_Main_Menu,
// so from the main menu onward it survives every room change and gives one global tick.
// One event outside Controller is touched - a guard prepended to Info's Draw, which fixes
// a surface crash in the game's own code that cannot be fixed from anywhere else. See the
// note by that prepend. Otherwise nothing: no new objects, no new scripts, no room edits,
// and not a line
// of any menu object's own code.
//
//   UndertaleModCli.exe load <data.win> -s inject_a11y.csx -o <out.win>
//
// Notes that cost real time to discover, kept so they are not rediscovered:
//
//  * This game is bytecode 15 and declares NO globals of its own (VarCount1 == 0).
//    Introducing any `global.foo` raised that count and the runner then died on the very
//    first access with 'trying to index a variable which is not an array' - a misleading
//    message, no array involved. Everything therefore lives as INSTANCE variables on
//    Controller, which is persistent, so the state still survives room changes.
//  * ds_list, never arrays: it keeps us off the array opcodes entirely.
//  * The compiler can only emit calls to functions the game already references. Anything
//    absent from the FUNC chunk (variable_local_exists, for one) will not compile.
//  * GML comments do NOT survive compilation, so the idempotence marker must be real code.

using System;
using System.Linq;
using UndertaleModLib.Models;

EnsureDataLoaded();

if (Data.IsYYC())
{
    Console.WriteLine("YYC build: no code to patch.");
    return;
}

var globalCtx = new GlobalDecompileContext(Data);
var settings = Data.ToolInfo.DecompilerSettings;

var controller = Data.GameObjects.ByName("Controller");
if (controller == null)
{
    Console.WriteLine("ERROR: Controller object not found - is this Bad Dream: Coma?");
    return;
}

var createEvt = controller.EventHandlerFor(EventType.Create, Data);
if (createEvt != null)
{
    var current = new Underanalyzer.Decompiler.DecompileContext(globalCtx, createEvt, settings)
                      .DecompileToString();
    if (current.Contains("a11y_ready"))
    {
        Console.WriteLine("Already patched. Nothing to do.");
        return;
    }
}

// ---------------------------------------------------------------------------
// Init - Controller's Create event, runs once per instance.
// ---------------------------------------------------------------------------
// ---------------------------------------------------------------------------
// Scene categories, worked out here and baked into the patch.
//
// This CANNOT be done at runtime. The only record of what an interaction is, is the
// cursor its hover handler sets, and probing every object in a room to find out would be
// actively harmful: of the 878 interactive objects with a hover handler, four have real
// side effects, and Screen_Spider_Mask's calls _dmg(0.25). Listing a room would injure
// the player. Bridge_01_Bird_Mask spawns birds and deletes itself; Hospital_Fly_06
// likewise; EndingB_Hospital_Patient_Fat reveals gore. Only the FOCUSED object is ever
// probed at runtime, which is exactly what hovering it would do anyway.
//
// Except for the two that DELETE THEMSELVES. Those are not a side effect to be accepted -
// they leave the label code reading fields off an instance that no longer exists, which
// crashed the game the first time anyone looked at the bridge sign. They are baked into
// a11y_nohover and skipped; the pointer warp still lands on them, so the game fires its
// own hover a moment later and the birds still scatter.
//
// So the same question is answered statically instead, by scanning each object's hover
// bytecode for which cursor scripts it can reach. Walks the parent chain, because a child
// with no handler of its own inherits one.
var interactiveObj = Data.GameObjects.ByName("Interactive_Object");

bool DescendsFromInteractive(UndertaleGameObject o)
{
    for (var p = o; p != null; p = p.ParentId)
        if (p == interactiveObj) return true;
    return false;
}

UndertaleCode HoverCodeFor(UndertaleGameObject o)
{
    for (var p = o; p != null; p = p.ParentId)
    {
        var lst = p.Events[(int)EventType.Other];
        if (lst == null) continue;
        foreach (var ev in lst)
            if (ev.EventSubtype == 11 && ev.Actions.Count > 0)
                return ev.Actions[0].CodeId;
    }
    return null;
}

var exitFns = new HashSet<string> { "_cursor_go", "_cursor_enter", "_cursor_back" };
var actFns = new HashSet<string> { "_cursor_hand", "_cursor_hit", "_cursor_high",
                                   "_check_item", "_activate_item_cursor",
                                   "_cursor_no_item", "_cursor_different" };

// Spawners and litter that are technically interactive but never worth listing. Applied
// ONLY to objects that would otherwise be scenery - ten trash cans are real interactables
// you can search, and hiding those would cost the player actual content.
var junkRx = new System.Text.RegularExpressions.Regex("creator|trash",
                 System.Text.RegularExpressions.RegexOptions.IgnoreCase);

// Objects that keep a counter in 'nbr' - the dial on the car remote, every phone-keypad
// button, and a few others. Pressing one of these changes only a sprite, so from the
// keyboard they read as doing nothing at all. Detected by looking for a store to that
// variable in the object's own Create, which means every instance is guaranteed to have it
// and reading it can never be the fatal unset-variable case.
UndertaleCode OwnCreateFor(UndertaleGameObject o)
{
    var lst = o.Events[(int)EventType.Create];
    if (lst == null) return null;
    foreach (var ev in lst)
        if (ev.Actions.Count > 0) return ev.Actions[0].CodeId;
    return null;
}

bool KeepsCounter(UndertaleGameObject o)
{
    var code = OwnCreateFor(o);
    if (code?.Instructions == null) return false;
    foreach (var ins in code.Instructions)
        if (ins.Kind == UndertaleInstruction.Opcode.Pop &&
            ins.ValueVariable?.Name?.Content == "nbr" &&
            // MUST be a store to SELF. Difference_Object's Create does
            // 'Difference_Controller.nbr += 1' - a store to nbr on a DIFFERENT object, so
            // its own instances never have the variable and reading it would be the fatal
            // unset-variable crash. Cross-object stores carry the target's index here
            // instead of Self, which is the only thing that separates the two cases.
            ins.TypeInst == UndertaleInstruction.InstanceType.Self) return true;
    return false;
}

// The interface buttons, which all have their own keys already and are only clutter in the
// scene list. Baked as a set INCLUDING DESCENDANTS, because GML object comparison is exact
// and never parent-aware: Menu_Btn and Status_Btn each have three Memories_* variants that
// an 'object_index == Menu_Btn' test silently lets through.
// Nightmares_GUI_MENU is the same case one chapter later: it is an Interactive_Object
// whose entire press handler is 'instance_create(x, y, Game_Menu)', which is what Escape
// already does - and it is PERSISTENT, so it followed the player out of the film and into
// every room after it. Nightmares_GUI_Skip is deliberately NOT here: it is the only way to
// skip the film, and it only exists at all on a second run.
var skipRoots = new[] { "Menu_Btn", "Status_Btn",
                        "Interface_Inventory_Up", "Interface_Inventory_Down",
                        "Nightmares_GUI_MENU" };
var skipObjs = new HashSet<UndertaleGameObject>();
foreach (var rootName in skipRoots)
{
    var root = Data.GameObjects.ByName(rootName);
    if (root == null) continue;
    foreach (var o in Data.GameObjects)
        for (var p = o; p != null; p = p.ParentId)
            if (p == root) { skipObjs.Add(o); break; }
}

// The hospital phone keypad, family included. Its digit keys are driven straight from the
// number row instead of being listed, so the scene list keeps only what a number key cannot
// press - the receiver and the star.
var phoneLines = new System.Text.StringBuilder();
var phoneRoot = Data.GameObjects.ByName("Hospital_Phone_Btn");
int nPhone = 0;
if (phoneRoot != null)
{
    foreach (var o in Data.GameObjects)
    {
        bool isPhone = false;
        for (var p = o; p != null; p = p.ParentId)
            if (p == phoneRoot) { isPhone = true; break; }

        // Family membership alone is NOT enough. Hospital_Fish_Tank also descends from
        // Hospital_Phone_Btn - a genuinely odd bit of parenting in this game - and its
        // Create is nothing but event_inherited(), so it silently inherits nbr = "1".
        // Including it would have made typing 1 fire the fish tank's own handler, and
        // would have hidden the fish tank from the scene list as though it were a keypad
        // digit. Require the object to set nbr ITSELF, which only the real keys do.
        if (isPhone && !KeepsCounter(o)) isPhone = false;

        var n = o.Name?.Content;
        if (!isPhone || string.IsNullOrEmpty(n)) continue;
        if (!System.Text.RegularExpressions.Regex.IsMatch(n, "^[A-Za-z_][A-Za-z0-9_]*$")) continue;
        phoneLines.Append("    ds_map_add(a11y_phone, " + n + ", 1);\n");
        nPhone++;
    }
}
Console.WriteLine($"Phone keypad objects: {nPhone}.");

var skipLines = new System.Text.StringBuilder();
foreach (var o in skipObjs)
{
    var n = o.Name?.Content;
    if (!string.IsNullOrEmpty(n) &&
        System.Text.RegularExpressions.Regex.IsMatch(n, "^[A-Za-z_][A-Za-z0-9_]*$"))
        skipLines.Append("    ds_map_add(a11y_skip, " + n + ", 1);\n");
}
Console.WriteLine($"Interface buttons excluded from the scene: {skipObjs.Count}.");

// Spoken names. Object names are read out with the underscores turned into spaces, which
// meant hundreds of them ended in the word "Mask" - it is the game's own suffix for a
// bare collision shape, and it is on every look-at hotspot in the game. Trimmed here at
// patch time rather than with string surgery at runtime.
//
// Only a TRAILING "Mask" goes, and only when at least two tokens are left: "Cyclops_Mask"
// and "White_Mask" are actual masks, and the masked man's objects say "Masked", which is
// a different token and is left alone. Trailing numbers survive the trim, since they are
// what tells two copies of the same hotspot apart.
bool SpeakableName(string n) =>
    !string.IsNullOrEmpty(n) &&
    System.Text.RegularExpressions.Regex.IsMatch(n, "^[A-Za-z_][A-Za-z0-9_]*$");

// The name as tokens, with a trailing "Mask" dropped. Null when nothing changed.
System.Collections.Generic.List<string> TrimMask(string n)
{
    var toks = new System.Collections.Generic.List<string>(n.Split('_'));
    if (toks.Count < 3) return null;
    int last = toks.Count - 1;
    while (last >= 0 && System.Text.RegularExpressions.Regex.IsMatch(toks[last], "^[0-9]+$")) last--;
    if (last < 0) return null;
    if (!string.Equals(toks[last], "Mask", StringComparison.Ordinal)) return null;
    toks.RemoveAt(last);
    return toks;
}

var picLines = new System.Text.StringBuilder();
picLines.Append(@"
    // The end. All three ending rooms are the same drawing - the bedroom of the very
    // first screen, seen from the pillow - and what tells the endings apart is only the
    // light in it and what is audible outside the window. A sighted player is told which
    // ending they got by that light; nothing said it out loud.
    ds_map_add(a11y_pics, Lvl_Last_Screen_Bad, ""You are lying in your own bed, looking down the duvet at your own bare feet. The wardrobe stands against the far wall, the window between the curtains has a plant on the sill and rooftops beyond it, a chest of drawers with books on top stands to the right and the bedroom door is on the left. It is the room the game started in."");
    ds_map_add(a11y_pics, Lvl_Last_Screen_Good, ""You are lying in your own bed, looking down the duvet at your own bare feet. The wardrobe stands against the far wall, the window between the curtains has a plant on the sill and rooftops beyond it, a chest of drawers with books on top stands to the right and the bedroom door is on the left. It is the room the game started in. Morning light is coming in at the window."");
    ds_map_add(a11y_pics, Lvl_Last_Screen_Neutral_Good, ""You are lying in your own bed, looking down the duvet at your own bare feet. The wardrobe stands against the far wall, the window between the curtains has a plant on the sill and rooftops beyond it, a chest of drawers with books on top stands to the right and the bedroom door is on the left. It is the room the game started in. It is raining on the window."");
    ds_map_add(a11y_pics, Lvl_Memories_Portrait, ""A child's drawing headed Our Family, with two little hearts. Three stick figures stand in a room: a smiling man labelled Dad, a woman with long hair, red lips and red shoes labelled Mom, and a frowning figure in a cap labelled Me. Off to the right a dog labelled Spike lies on the ground, scribbled over with a grey cross."");
    ds_map_add(a11y_pics, Lvl_Memories_Portrait_02, ""An untitled family drawing with five figures. On the left a stooped old man with a walking stick and an old woman, both greyed out under a large smudged cross. On the right a small child holds the hand of a blonde woman, and beside her stands a frowning figure in a cap."");
    ds_map_add(a11y_pics, Lvl_Memories_Map, ""A child's drawing headed Treasure map, with TOP SECRET written in red. In the middle a house with two windows and a door, a kennel in front of it labelled Map with an arrow pointing at it, a pond below, and pine trees to either side. At the left a leafy tree with a small box on its trunk, labelled Key with an arrow. A key is drawn in the bottom left corner."");
    ds_map_add(a11y_pics, Lvl_Memories_Treasure, ""A child's drawing captioned Love is the key to happiness, with a key drawn in the top right corner. A dark haired man and a blonde woman in red shoes stand with their outer arms raised, each holding a hand of the child in a cap between them. A dog sits at their feet. Three red hearts float around them."");
    ds_map_add(a11y_pics, Lvl_Memories_Drawing_Happy, ""A child's drawing headed Happy Day, in teal, with a yellow sun in the top left corner, two hearts and a couple of birds. Below, green hills and trees, and the child in a cap with his arm round the dog, a red heart between their heads."");
    ds_map_add(a11y_pics, Lvl_Memories_Toy_Guards, ""A child's drawing headed Toy Guards. Three balloon heads on coiled springs, all with wide eyes and round open mouths. On the right a stick figure screams with its arms flung out, a rectangle of tape over its mouth, holding a pair of scissors."");
    ds_map_add(a11y_pics, Lvl_Memories_Spike_Dead, ""A child's drawing headed Bye SPIKE, with a crying face. The child in a cap stands with a hand to his eyes, tears running, over the dog lying dead on the ground. Far off on the hill behind him is a small figure carrying a scythe."");
    ds_map_add(a11y_pics, Lvl_Memories_Mothers_Day, ""A child's drawing headed HAPPY MOTHER'S DAY, with I love you very much written in the corner. The child in a cap sits on a chair holding out a flower to his mother, who sits facing him. She and her chair are greyed out under a smudged cross. A heart hangs between them."");
    ds_map_add(a11y_pics, Lvl_Memories_Dad, ""A child's drawing headed Stay with me Dad, with I love you so much written in the corner. A man lies in a hospital bed under a blanket with a drip stand beside him. The child in a cap stands at the foot of the bed, frowning."");
    ds_map_add(a11y_pics, Lvl_Memories_Letter_Death, ""A letter in neat print. Dear Death, please don't take my parents! You can have all my toys. I'll clean my room everyday, I promise! I know sometimes I'm rude and impolite, but I'll change! I'll do anything! Just don't take my parents, I beg you. Laid over the corner of it is a smaller note in blue handwriting: I'm sorry, I have to. Don't try to stop me. Signed, D."");
    ds_map_add(a11y_pics, Lvl_Memories_Toy_Army, ""A child's drawing headed Toy army to stop Death. Four things are drawn and labelled: a box with a question mark on it and a coiled spring flying out, labelled Mystery box trap; a teddy bear with angry eyes and fangs, labelled Teddy; a car with a row of spikes along its roof, labelled Car; and a handful of drawing pins, labelled Tacks."");
    ds_map_add(a11y_pics, Lvl_Memories_Room_Drawing, ""A child's drawing headed Parents Room. A wardrobe, a standing lamp and a bed along the far wall, a door on the left, and set into the floor a hatch, labelled Basement with an arrow pointing at it."");
    ds_map_add(a11y_pics, Lvl_Memories_Grave, ""A child's drawing with WHY?! written large across the top and I tried at the bottom. The child in a cap stands in a graveyard beside a headstone with a flower laid at its foot. A bare dead tree leans over the scene on the right."");
    ds_map_add(a11y_pics, Lvl_Memories_Photo, ""Not a drawing but a grey pencil photograph. A boy stands in a forest clearing holding a paper cup, a satchel across his chest, trees and bushes behind him. A fishing rod leans against a stick to his right. Litter is scattered on the ground to his left: a bottle, a crumpled bag and cans."");
    ds_map_add(a11y_pics, Lvl_Memories_Letter, ""A plain white envelope, sealed, lying face up. Nothing is written on it."");
    ds_map_add(a11y_pics, Lvl_Memories_Trash, ""Torn sheets of paper scattered over each other. They are pages of an inventory: one is headed Inventory and shows labelled drawings of the things you carry - Worms, Magnifier, Pen, Coin, Battery, Glue, Rotten Fish, Scissors - and the rest are ripped up pictures of places. Lying on top of them are small blank cards, and cards showing a magnifying glass, a pair of scissors, a hook, and a key."");
    ds_map_add(a11y_pics, Lvl_Bridge_Controller, ""A close-up of the bridge control box: a rounded metal housing with a small hatch on its front, a strip of tape stuck across it in an X, and an aerial rising from the top ending in a ball."");
    ds_map_add(a11y_pics, Lvl_Bridge_Controller_Broken, ""The bridge control box with its front torn open. A tangle of thick cut wires spills out of the left side, a lever stands on the right, and the aerial above it is snapped and frayed. The tape is still stuck across the top in an X."");
    ds_map_add(a11y_pics, Lvl_Bridge_Stroller, ""A close-up of a pram, hood up, seen from the foot end. The mattress and lining are grubby and spattered with dark stains. It is empty."");
    ds_map_add(a11y_pics, Lvl_Bridge_Car, ""A crashed car seen from behind, its windows smashed and the far door hanging open, aerial bent into a zigzag. A body has fallen half out of the driver's side and lies face up in a wide pool of blood, its eyes drawn as crosses."");
    ds_map_add(a11y_pics, Lvl_Bridge_Trunk, ""The open boot of the car seen from above, lined and empty, the lid raised behind it."");
    ds_map_add(a11y_pics, Lvl_Hospital_Soda_Machine, ""The keypad of a vending machine. Twelve buttons in a grid of three across and four down, numbered one to eleven, with the second, eighth and twelfth burnt black and unreadable. The cracked display panel and a coin slot sit above them, and the cracks run out across the casing."");
    ds_map_add(a11y_pics, Lvl_Hospital_UFO, ""Almost nothing: the top edge of a domed shape crossing the view, drawn as a single curve with a scatter of small round hollows along it."");
    ds_map_add(a11y_pics, Lvl_Hospital_Nose, ""A close-up of a nose, hatched in pencil, with dark red blood smeared under and around both nostrils and spattered up the side."");
    ds_map_add(a11y_pics, Lvl_Hospital_Sink, ""A close-up of a washbasin, cracked across the bowl and chipped at the rim, with a single tap and a plug hole. Dark spots and streaks are dotted around the inside."");
    ds_map_add(a11y_pics, Lvl_Hospital_Fish_Tank, ""A round glass fish bowl, half filled. A fat goldfish lies on its side at the bottom, and a long pale worm is coming out of its mouth."");
    ds_map_add(a11y_pics, Lvl_Hospital_Soup, ""A bowl of thick soup with a spoon resting in it. Chunks float in the broth, and a worm is curled in the bowl of the spoon."");
    ds_map_add(a11y_pics, Lvl_Hospital_Bird_Dead, ""A dead bird lying on its side, wings and long tail splayed out, drawn in heavy scribbled black. Its beak hangs open and its eye is shut."");
    ds_map_add(a11y_pics, Lvl_Hospital_Teddy, ""A teddy bear lying on a bed beside a pillow. Its head has been torn off and the neck left as a ragged frayed hole; the four stubby limbs are splayed out."");
    ds_map_add(a11y_pics, Lvl_HospitalNF_Acid, ""A large bottle tipped on its side, the stopper out, with a wide pool of dark green liquid spreading from the neck across the whole width of the view."");
    ds_map_add(a11y_pics, Lvl_HospitalNF_Spider_Acid, ""The same pool of green liquid. Sitting in it is a round white spider the size of a head, bristled, with three eyes, two of them crossed out."");
    ds_map_add(a11y_pics, Lvl_Forest_Hand, ""A close-up of a hand, palm down and blotched with dark patches, fingers ending in long pointed nails. The wrist is torn off raggedly at the bottom left."");
    ds_map_add(a11y_pics, Lvl_Forest_Head, ""A severed head lying on its side on the ground, the face bloated and pitted, one eye drawn as a heavy spiral. A fat worm crawls out of the neck."");
    ds_map_add(a11y_pics, Lvl_Forest_Wheel, ""A car tyre standing upright on its tread, seen almost head on, with a spoked hubcap. Something black and spiky is stuck to the tyre wall on the left."");
    ds_map_add(a11y_pics, Lvl_Flat_Help, ""A scrap of paper, torn along the top, with Help me and two exclamation marks printed on it in heavy black letters. Nothing else is written."");
    ds_map_add(a11y_pics, Lvl_Flat_Mail, ""A close-up of an envelope lying flap up, the flap open."");
    ds_map_add(a11y_pics, Lvl_Flat_Sink, ""A close-up of a washbasin, cracked across the bowl and chipped at the rim, with a single tap and a plug hole."");
    ds_map_add(a11y_pics, Lvl_Flat_Pictures, ""Three framed photographs on a wall, each hung from a nail. On the left a young woman with dark bobbed hair. In the middle, larger, a couple holding a baby: a long haired woman and a man with wide staring eyes, the baby with a flower on its bib. Below right, an old woman in glasses, unsmiling."");
    ds_map_add(a11y_pics, Lvl_Flat_Pictures_02, ""Five framed photographs on a wall. Top left a baby sitting on the floor with alphabet blocks; top right a bald old man staring straight out. Middle left a small run down house with a tiled roof; middle right four children crowded together on a sofa. Below them, a boy riding a bicycle along a dirt track."");
    ds_map_add(a11y_pics, Lvl_Graveyard_Fly, ""An enormous fly with a human head instead of a face. The head is bald and swollen, eyes shut and mouth grimacing, joined to a bristled insect body with folded wings and clawed legs."");
    ds_map_add(a11y_pics, Lvl_Graveyard_Sign, ""A round metal sign hanging on a chain, blank in the middle, with five small black stars spaced evenly around the rim."");
    ds_map_add(a11y_pics, Lvl_Graveyard_Drawing, ""A child's drawing of a graveyard: rows of headstones and crosses running back over two hills, a bare dead tree on the left, and a distant chapel at the top right. A hot air balloon floats in the sky above the tree."");
    ds_map_add(a11y_pics, Lvl_Graveyard_Head, ""A severed head lying on its side in a spreading pool of blood, the skull open at the back with the brain showing, one eye wide and staring and the other fallen out onto the ground beside it."");
    ds_map_add(a11y_pics, Lvl_Hospital_B_Windows, ""Three tall windows in a row, all of them blacked out and scribbled over so nothing can be seen through them. A cobweb hangs under the sill of the middle one."");
    ds_map_add(a11y_pics, Lvl_HospitalB_Mouse_Trap, ""A wooden mousetrap seen from the side, the bar sprung shut across it and the bait pin still standing."");
    ds_map_add(a11y_pics, Lvl_HospitalB_Callendar, ""A calendar page, three numbers across and three down. Every number has been scribbled out in heavy black except the one in the centre, which reads thirteen."");
    ds_map_add(a11y_pics, Lvl_EndingB_Drawing_Cementery, ""A child's drawing of a graveyard: rows of headstones and crosses over two hills, a bare dead tree on the left, a distant chapel at the top right, and an empty sky."");
    ds_map_add(a11y_pics, Lvl_Ending_Drawing_Hospital, ""A child's drawing captioned I went to the hospital. A low flat building with double doors and rows of windows, a tree standing either side of the path, and grass in front."");
    ds_map_add(a11y_pics, Lvl_Hospital_B_Nightmares_Title, ""The tape starts. A title card reads Our Little Nightmare in fat red letters, with a clown walking in from either side of the screen. The whole picture rolls and hisses like a worn videotape."");
    ds_map_add(a11y_pics, Lvl_Hospital_B_Nightmares, ""A cartoon kitchen, drawn in ink and filmed through television static. A door on the far left with a coat and a mat beside it, a small framed picture and some notes pinned to the wall, then a run of worktop under two glass-fronted wall cupboards full of jars. In the middle a gas cooker with an oven under it, a window over the sink, a low cupboard beside it, a bank of three drawers, a microwave, and a big rounded fridge at the right with a note stuck to the door. A clock hangs above the cooker."");
    ds_map_add(a11y_pics, Lvl_Hospital_B_Nightmares_Fridge, ""The inside of the fridge, close up. Bare shelves, and a fish lying on one of them."");
    ds_map_add(a11y_pics, Lvl_Hospital_B_Nightmares_Cutting, ""The worktop, close up. A chopping board with the fish on it."");
    ds_map_add(a11y_pics, Lvl_Nightmares_End_Screen, ""The tape has run out. The screen is filled with broadcast colour bars."");
");
Console.WriteLine($"Picture rooms described: 53.");

// Room names, for the F4 "where am I" report. Baked because room_get_name is absent from
// this game's FUNC chunk - see the note on builtins that cannot be emitted.
//
// The mechanical name is right nearly everywhere and wrong where the internal name carries
// a build letter the player has never seen: Lvl_Hospital_B_Waiting is the waiting room,
// and the B is which VERSION of the hospital the room belongs to, not a wing. Overridden by
// hand where that happens; anything absent keeps the derived name.
var roomNames = new Dictionary<string, string>
{
    { "Lvl_Hospital_Waiting",         "Waiting room" },
    { "Lvl_Hospital_B_Waiting",       "Waiting room" },
    { "Lvl_EndingB_Hospital_Waiting", "Waiting room" },
    { "Lvl_Ending_Hospital_Waiting",  "Waiting room" },

    // The film inside the VHS tape. The internal names put it in the hospital, which is
    // where the television is, not where the player now is.
    { "Lvl_Hospital_B_Nightmares_Title",   "The tape, opening titles" },
    { "Lvl_Hospital_B_Nightmares",         "The tape, the kitchen" },
    { "Lvl_Hospital_B_Nightmares_Fridge",  "The tape, the fridge" },
    { "Lvl_Hospital_B_Nightmares_Cutting", "The tape, the worktop" },
    { "Lvl_Nightmares_End_Screen",         "The tape, the end" },

    // The three ending screens. Lvl_Last_Screen_Bad serves BOTH the bad and the neutral
    // ending - Bed_Scene picks between them on Controller.ending_neutral once it is
    // already in the room - so the internal name is not even reliably true, and reading
    // out the word Bad announces the outcome before the screen has said anything.
    { "Lvl_Last_Screen_Bad",          "Your bedroom, the end" },
    { "Lvl_Last_Screen_Good",         "Your bedroom, the end" },
    { "Lvl_Last_Screen_Neutral_Good", "Your bedroom, the end" },
};

var roomLines = new System.Text.StringBuilder();
int nRoomOver = 0;
for (int ri = 0; ri < Data.Rooms.Count; ri++)
{
    var raw = Data.Rooms[ri].Name?.Content;
    if (string.IsNullOrEmpty(raw)) continue;
    string rnm;
    if (roomNames.TryGetValue(raw, out rnm)) nRoomOver++;
    else
    {
        rnm = raw;
        if (rnm.StartsWith("Lvl_", StringComparison.Ordinal)) rnm = rnm.Substring(4);
        rnm = rnm.Replace('_', ' ');
    }
    roomLines.Append("    ds_map_add(a11y_rooms, " + ri + ", \"" + rnm + "\");\n");
}
foreach (var k in roomNames.Keys)
    if (Data.Rooms.ByName(k) == null)
        Console.WriteLine($"  room name override is stale, no such room: {k}");
Console.WriteLine($"Room names baked for the F4 report: {Data.Rooms.Count}, " +
                  $"{nRoomOver} of them named by hand.");

var prettyLines = new System.Text.StringBuilder();
int nPretty = 0;
foreach (var o in Data.GameObjects)
{
    var pn = o.Name?.Content;
    if (!SpeakableName(pn)) continue;
    var toks = TrimMask(pn);
    if (toks == null) continue;
    prettyLines.Append("    ds_map_add(a11y_pretty, " + pn + ", \"" +
                       string.Join(" ", toks) + "\");" + "\n");
    nPretty++;
}
Console.WriteLine($"Names with a trailing Mask trimmed: {nPretty}.");

// ---- the area name -------------------------------------------------------
//
// Nearly every object is named after the room it sits in - Graveyard_Cementery_Gtave in
// Lvl_Graveyard_Cementery - so the area gets repeated in front of all thirty-odd entries
// in the list while carrying no information at all: it is the same for everything there.
//
// The strip is worked out HERE rather than at runtime. Doing it in GML would need
// string_char_at, string_copy, string_delete and room_get_name, none of which this game
// uses anywhere, and a builtin the compiler will not emit is a failure that shows up only
// once the patched game runs. It is also better information: the rooms an object really
// appears in are known here and are not knowable from its name.
//
// Leading tokens are matched against the room name with Lvl_ removed, and the LAST token
// is never stripped, so nothing can be reduced to nothing. An object placed in more than
// one room keeps the SHORTEST strip of any of them, so a name is never cut by an area it
// is not currently standing in.
var stripByObj = new Dictionary<string, int>();
foreach (var r in Data.Rooms)
{
    var area = r.Name?.Content;
    if (string.IsNullOrEmpty(area)) continue;
    if (area.StartsWith("Lvl_", StringComparison.Ordinal)) area = area.Substring(4);
    var at = area.Split('_');

    foreach (var g in r.GameObjects)
    {
        var on = g.ObjectDefinition?.Name?.Content;
        if (!SpeakableName(on)) continue;
        var ot = on.Split('_');
        int k = 0;
        while (k < at.Length && k < ot.Length - 1 &&
               string.Equals(at[k], ot[k], StringComparison.Ordinal)) k++;
        if (stripByObj.TryGetValue(on, out var prev)) stripByObj[on] = Math.Min(prev, k);
        else stripByObj[on] = k;
    }
}

var shortLines = new System.Text.StringBuilder();
int nShort = 0;
foreach (var o in Data.GameObjects)
{
    var sn = o.Name?.Content;
    if (!SpeakableName(sn)) continue;
    if (!stripByObj.TryGetValue(sn, out var k) || k < 1) continue;

    var toks = TrimMask(sn) ?? new System.Collections.Generic.List<string>(sn.Split('_'));
    if (k >= toks.Count) continue;      // the trailing Mask may have been all that was left
    toks.RemoveRange(0, k);

    shortLines.Append("    ds_map_add(a11y_short, " + sn + ", \"" +
                      string.Join(" ", toks) + "\");" + "\n");
    nShort++;
}
Console.WriteLine($"Names with the area stripped: {nShort}.");

// ---- Lvl_Computer, a small operating system inside the game ---------------
//
// The reception computer opens a fake desktop with icons, windows, dialogs and a paint
// program, and almost none of it survives contact with a keyboard:
//
//  * every window's close button (Computer_Exit_Window) and every dialog's OK button
//    (Computer_OK_Button) responds ONLY to a real Mouse_4 press on the instance. They are
//    not Interactive_Objects, so Controller's click dispatch never sees them and neither
//    does the scene list. Handled by Escape instead - see escapeFix.
//  * the paint palette (Computer_Paint_Color_Take, 14 swatches) is Mouse_4-only too.
//    Listed through a11y_extra and activated with a synthetic press.
//  * the windows carry real localised text in window_name, and the dialogs a second line
//    in 'text' - "This computer is infected!!", "Printer is NOT connected." - which is
//    where the chapter's actual instructions live. Nothing announced them.
//  * the paint puzzle is pure colour: fill each element with its target colour. Nothing
//    about a colour is textual.
//
// Reading any of those variables is only safe on an object that really sets it, so
// membership is decided here rather than guessed at runtime.
bool HasSelfVar(UndertaleGameObject o, string v)
{
    for (var p = o; p != null; p = p.ParentId)
    {
        var code = OwnCreateFor(p);
        if (code?.Instructions == null) continue;
        foreach (var ins in code.Instructions)
            if (ins.Kind == UndertaleInstruction.Opcode.Pop &&
                ins.ValueVariable?.Name?.Content == v &&
                ins.TypeInst == UndertaleInstruction.InstanceType.Self) return true;
    }
    return false;
}

bool Descends(UndertaleGameObject o, UndertaleGameObject root)
{
    if (root == null) return false;
    for (var p = o; p != null; p = p.ParentId)
        if (p == root) return true;
    return false;
}

var compLines = new System.Text.StringBuilder();
int nComp = 0;
var iconRoot  = Data.GameObjects.ByName("Computer_Icon");
var toolRoot  = Data.GameObjects.ByName("Computer_Paint_Icon");
var elemRoot  = Data.GameObjects.ByName("Computer_Drawing_Element");
var winRoot   = Data.GameObjects.ByName("Computer_Window");
var swatchSet = new HashSet<string> { "Computer_Paint_Color_Example_Take", "Computer_Paint_Color_Take" };
var fileSet   = new HashSet<string> { "Computer_Paint_Save", "Computer_Paint_Print", "Computer_Paint_Delete" };
foreach (var o in Data.GameObjects)
{
    var cn = o.Name?.Content;
    if (string.IsNullOrEmpty(cn)) continue;
    if (!System.Text.RegularExpressions.Regex.IsMatch(cn, "^[A-Za-z_][A-Za-z0-9_]*$")) continue;

    int fam = 0;
    if (o != iconRoot && Descends(o, iconRoot) &&
        HasSelfVar(o, "name") && HasSelfVar(o, "active")) fam = 1;          // desktop icon
    else if (o != toolRoot && Descends(o, toolRoot) &&
        HasSelfVar(o, "name") && HasSelfVar(o, "active")) fam = 2;          // paint tool
    else if (o != elemRoot && Descends(o, elemRoot) &&
        HasSelfVar(o, "color") && HasSelfVar(o, "color_target")) fam = 3;   // drawing part
    else if (swatchSet.Contains(cn) && HasSelfVar(o, "color")) fam = 4;     // colour swatch
    else if (fileSet.Contains(cn) && HasSelfVar(o, "name")) fam = 5;        // save/print/delete
    if (fam == 0) continue;

    compLines.Append("    ds_map_add(a11y_comp, " + cn + ", " + fam + ");\n");
    nComp++;
}
Console.WriteLine($"Computer desktop objects classified: {nComp}.");

// Windows worth announcing, and which of them carry a second line of body text. Both
// checked rather than assumed: reading an unset variable is fatal in this runner.
var winLines = new System.Text.StringBuilder();
int nWin = 0, nWinText = 0;
foreach (var o in Data.GameObjects)
{
    var cn = o.Name?.Content;
    if (string.IsNullOrEmpty(cn)) continue;
    if (!System.Text.RegularExpressions.Regex.IsMatch(cn, "^[A-Za-z_][A-Za-z0-9_]*$")) continue;
    if (o == winRoot || !Descends(o, winRoot)) continue;
    if (!HasSelfVar(o, "window_name")) continue;
    bool body = HasSelfVar(o, "text");
    winLines.Append("    ds_map_add(a11y_cwin, " + cn + ", " + (body ? 1 : 0) + ");\n");
    nWin++;
    if (body) nWinText++;
}
Console.WriteLine($"Computer windows: {nWin}, of which {nWinText} carry body text.");
// The paint program's drawing parts are created by the window, never placed in a room, so
// the area pass above cannot see them and they read as "Computer Drawing Sky". They are the
// one family where the puzzle is to name them out loud repeatedly, so strip the two dead
// tokens. Restricted to Computer_Drawing_Element's descendants on purpose: Computer_Drawing
// _Icon is a desktop icon, not a part of the picture, and must keep its name.
int nElem = 0;
foreach (var o in Data.GameObjects)
{
    var en = o.Name?.Content;
    if (!SpeakableName(en)) continue;
    if (o == elemRoot || !Descends(o, elemRoot)) continue;
    var et = new System.Collections.Generic.List<string>(en.Split('_'));
    if (et.Count < 3) continue;
    et.RemoveRange(0, 2);
    shortLines.Append("    ds_map_add(a11y_short, " + en + ", \"" +
                      string.Join(" ", et) + "\");" + "\n");
    nElem++;
}
Console.WriteLine($"Drawing parts given short names: {nElem}.");

// The desktop icons, as opposed to the icons that live INSIDE windows. Both are
// Computer_Icon descendants and nothing about the objects tells them apart - what does is
// that the desktop ones are placed in Lvl_Computer's room definition while Coma, Drawing
// and Medicine are created by the window that contains them. That distinction matters:
// hiding the wrong ones would hide Medicine (which cures the virus) and Drawing (which
// opens the paint program) and make the chapter unfinishable.
//
// Computer_Games_Icon is deliberately excluded, so the way out of the computer - Games,
// then the Coma icon inside it - is always listed.
var deskLines = new System.Text.StringBuilder();
int nDesk = 0;
var deskSeen = new HashSet<string>();
foreach (var r in Data.Rooms)
{
    if (r.Name?.Content != "Lvl_Computer") continue;
    foreach (var g in r.GameObjects)
    {
        var o = g.ObjectDefinition;
        var dn = o?.Name?.Content;
        if (!SpeakableName(dn) || dn == "Computer_Games_Icon") continue;
        if (!DescendsFromInteractive(o)) continue;
        if (!deskSeen.Add(dn)) continue;
        deskLines.Append("    ds_map_add(a11y_desk, " + dn + ", 1);\n");
        nDesk++;
    }
}
Console.WriteLine($"Desktop icons hidden behind an open window: {nDesk}.");

// Cutscenes that are room transitions rather than something happening. The mirror in the
// flats is one: pressing it creates Flat_Hall_Mirror_Translation, a Cutscene whose whole
// job is to draw a 31-frame wipe and then room_goto - and its Destroy spawns
// Hall_Mirror_Translation_End to wipe back in at the other side. Both block interaction,
// so the generic "nothing can be used" line fired at a player who had just successfully
// used the mirror.
//
// The game names them all *Translation*, which is what this matches, restricted to actual
// Cutscene descendants so an unrelated object cannot slip in.
var transLines = new System.Text.StringBuilder();
int nTrans = 0;
var cutRoot = Data.GameObjects.ByName("Cutscene");
foreach (var o in Data.GameObjects)
{
    var tn = o.Name?.Content;
    if (!SpeakableName(tn)) continue;
    if (o == cutRoot || !Descends(o, cutRoot)) continue;
    if (tn.IndexOf("Translation", StringComparison.OrdinalIgnoreCase) < 0) continue;
    transLines.Append("    ds_map_add(a11y_trans, " + tn + ", 1);\n");
    nTrans++;
}
Console.WriteLine($"Cutscenes that are room transitions: {nTrans}.");

// The board game's hazards. Each monster or set of spikes drawn on the board sits on one
// square, and landing on that square kills you - the link is a Dead_Pointer whose 'active'
// the hazard's user event 2 clears when the hazard is crossed out with the pen. None of
// that is anywhere in the object: the hazard knows its square only as a literal inside
// that handler, and the player knows it only by looking at the board.
//
// So the square is found at RUNTIME by nearest pointer, which the room data says is sound:
// every hazard's nearest pointer is within 33 pixels, and for every deadly one it is a
// Dead_Pointer. Baked here is only the question of which objects are board hazards at all.
// Board_Dice and Board_Button are excluded - they have their own readouts already.
var boardLines = new System.Text.StringBuilder();
int nBoard = 0;
foreach (var o in Data.GameObjects)
{
    var bn = o.Name?.Content;
    if (!SpeakableName(bn)) continue;
    if (!bn.StartsWith("Board_", StringComparison.Ordinal)) continue;
    if (bn == "Board_Dice" || bn == "Board_Button") continue;
    if (!DescendsFromInteractive(o)) continue;
    boardLines.Append("    ds_map_add(a11y_board, " + bn + ", 1);\n");
    nBoard++;
}
Console.WriteLine($"Board hazards that now say their square: {nBoard}.");

// ---------------------------------------------------------------------------
// Story events, in three tables.
// ---------------------------------------------------------------------------
// The game tells a story in pictures. Something walks in, something is torn open, the
// screen shakes, a door is hammered on from the other side - and none of it is text, so
// none of it existed. Worse than absent: while any of it is on screen the game refuses
// every object in the room, so the one thing the player DID hear was the accessibility
// layer saying "Nothing here can be used yet", which reads as a bug in the mod at the
// exact moment the game is at its most dramatic.
//
// Two tables cover it, because events arrive in two shapes.
//
//  * SOMETHING APPEARS. A cutscene object, a monster, a screen shake. Watched by object
//    index and announced when an instance turns up. Re-armed on every room change, so an
//    event that can happen twice is heard twice, and one that lingers is heard once.
//  * A FLAG FLIPS. The chapter controllers keep the whole story as a set of variables -
//    Graveyard_Controller alone has 29 - and setting one is exactly what "something
//    happened" means. Watched by value and announced on the 0-to-something edge.
//
// Both are baked and both are validated below: an object that does not exist, or a flag
// its holder does not set in Create, is dropped with a message rather than compiled into
// a read that would kill the game.
var eventRows = new (string obj, string text)[]
{
    // ---- chapter 1, the bridge
    ("Bridge_Earthquake",
     "An earthquake. The bridge shakes hard enough to throw you off your feet."),
    ("Bridge_Masked_Man",
     "A man in a mask is standing further along the bridge, watching you."),
    ("Bridge_Masked_End",
     "The masked man is right in front of you."),
    ("Bridge_Homeless_Difference",
     "The drawing changes while you are looking at it."),

    // ---- chapter 2, the hospital
    ("Hospital_Patient_Pain",
     "The patient screams."),
    ("Hospital_Make_Call",
     "You dial. The phone rings at the other end."),
    ("Hospital_Recieve_Call",
     "The phone rings."),
    ("Hospital_Call_Back",
     "Somebody is calling back."),
    ("HospitalNF_Cyclops_Cutscene",
     "The lift doors open. There is something enormous standing in them."),

    // ---- chapter 3, the graveyard
    ("Graveyard_Candle_Burn",
     "The candle catches and burns down fast."),
    ("Graveyard_Cementery_Black_Screen",
     "You swing the pickaxe until the grave is open. There is a body in it, a heap of " +
     "loose earth, and a shovel."),
    ("Morgue_Jar_Cutscene",
     "Something starts hammering on the morgue door from the other side, four blows hard " +
     "enough to shake the whole frame, and then stops."),
    ("Scarecrow_Attack",
     "The scarecrow comes down off its post and opens you up. Deep wounds."),
    ("Graveyard_Scarecrow_Eye",
     "The scarecrow has you. It is going for your eyes."),
    ("Graveyard_Scarecrow_Eye_02",
     "It takes your eyes. You wake up by the grave, blind."),
    ("Graveyard_Crypt_Sign_Cutscene",
     "A small sign rises out of the crypt floor."),
    ("Hurt_Animal",
     "The dog is hurt. It is screaming."),

    // ---- chapter 4, the forest
    ("Forest_Mad_Dog",
     "The dog goes for you."),
    ("Forest_Drink_Energy",
     "The camper drinks what you gave him."),
    ("Forest_Tire_Fix",
     "The tyre goes back on the car."),
    ("Forest_Back_Worm",
     "Something drags you back the way you came."),

    // ---- chapter 5, hospital B
    ("Hospital_B_Bird_Attack_Controller",
     "The bird monster drops on you out of the dark."),
    ("Hospital_B_Bird_Damage",
     "It takes your hand. The screen goes black, and you come round back in room 15, " +
     "badly hurt."),
    ("Hospital_B_Hall_No_Lights",
     "The lights in the corridor go out."),
    ("Nurse_Visit_Scream",
     "The nurse screams."),
    ("Nurse_Visit_Heal",
     "The nurse patches you up. Your wounds are gone."),
    ("Nurse_Heal_Teddy",
     "The nurse works on the teddy. It is alive again."),
    ("Dialogue_Teddy_Masked_Appear",
     "A masked figure walks into room 15 behind you."),
    ("Hospital_B_Park_Nurse_Go",
     "The nurse leads everyone away. The park empties."),
    ("Hospital_B_Canteen_Spider_Enemy_Attack",
     "A spider comes straight at you across the canteen."),

    // ---- the tape in the canteen, and the film inside it
    ("Little_Nightmares_Few",
     "A card fills the screen: a few moments later."),
    ("Little_Nightmares_End_Screen",
     "The tape runs out. The screen fills with colour bars."),
    ("Little_Nightmares_Clown",
     "A clown walks in across the kitchen and goes out the door. Nothing here can be " +
     "used until he has gone."),
    ("Little_Nightmares_Change_Scene",
     "The picture cuts to black and comes back somewhere else."),

    // ---- chapter 6, the flats
    ("Hall_Broken_Mirror_Cutscene",
     "The mirror breaks."),
    ("Hall_Bell_Cutscene",
     "A doorbell rings twice somewhere in the building."),
    ("Board_Bell_Cutscene",
     "A doorbell rings three times, a long way off."),
    ("Casette_Cutscene",
     "A cassette player clicks and rattles four times somewhere in the flat."),

    // ---- chapter 7, the old house
    ("Memories_Night",
     "The moon comes up. It is night."),
    ("Memories_Spider_Creator",
     "The music stops. Spiders start pouring into the room."),
    ("Memories_Spiders_Black",
     "The spiders cover everything and the screen goes dark."),
    ("Memories_Mask_Cutscene",
     "The mask screams. Cover it with a rag to use anything here."),
};

var eventLines = new System.Text.StringBuilder();
int nEvent = 0;
foreach (var row in eventRows)
{
    if (Data.GameObjects.ByName(row.obj) == null)
    {
        Console.WriteLine($"  event row dropped, no object: {row.obj}");
        continue;
    }
    // The map answers "what is this cutscene", the two lists are what the appearance
    // sweep walks. Same rows, two shapes, because GameMaker has no way to iterate a
    // ds_map that this game already calls.
    eventLines.Append("    ds_map_add(a11y_ev, " + row.obj + ", \"" + row.text + "\");\n");
    eventLines.Append("    ds_list_add(a11y_evo, " + row.obj + ");\n");
    eventLines.Append("    ds_list_add(a11y_evt, \"" + row.text + "\");\n");
    eventLines.Append("    ds_list_add(a11y_evon, 0);\n");
    nEvent++;
}
Console.WriteLine($"Story events announced when they appear: {nEvent} of {eventRows.Length}.");

// Flags. Ordered on purpose: at most ONE is announced per frame and the rest are recorded
// silently, so where a single press sets two - smashing the pram doll sets doll_broken and
// device_broken together - the first row wins and the player hears one sentence rather
// than two talking over each other.
var flagRows = new (string obj, string var_, string text)[]
{
    ("Bridge_Controller", "doll_broken",
     "The crowbar goes through the doll. It stops crying, and there is a wire hanging out " +
     "of the pram."),
    ("Bridge_Controller", "device_broken",
     "The casing splits open. There is a lever inside it and a handful of cut wires."),

    ("Hospital_Controller", "fingers",
     "The scissors take the patient's fingers off. They are lying loose on the bed."),
    ("Controller", "hand_fixed",
     "The fingers are glued back onto your hand. It works again."),
    ("Hospital_Controller", "pervert",
     "Somebody saw you do that."),

    ("Graveyard_Controller", "night",
     "The sky goes out. It is night in the graveyard now."),
    ("Graveyard_Controller", "bone",
     "The stone arm closes around the bone and holds it."),
    ("Graveyard_Controller", "skull",
     "The stone arm closes around the skull and holds it."),
    ("Graveyard_Controller", "hidden_open",
     "The pentagram grinds aside. There is a way through behind it."),
    ("Graveyard_Controller", "zombie_cut",
     "The scissors take the hand off at the wrist. You can pick the hand up now."),
    ("Graveyard_Controller", "scarecrow_killed",
     "The eraser rubs the scarecrow out. Nothing is left on the gate but smears."),
    ("Controller", "zombie_erased",
     "The eraser rubs the zombie out. There is a smear on the ground where it stood."),
    ("Graveyard_Controller", "morgue_visited",
     "Whatever was behind the morgue door has stopped."),

    ("Forest_Controller", "man_down",
     "The camper goes down and does not get up."),
    ("Controller", "beehive_destroyed",
     "The match catches the smouldering cigarette and the hive goes up. The bees are gone."),
    ("Forest_Controller", "tire_fixed",
     "The wheel is back on."),

    ("Controller_Hospital_B", "butcher_wc",
     "A butcher walks into the toilets, and the door locks behind him."),
    ("Controller_Hospital_B", "chewed_food",
     "The old man eats it."),
    ("Controller_Hospital_B", "bird_attacked",
     "The bird is finished with you."),

    ("Flat_Controller", "broken_mirror",
     "The mirror breaks and the way through it is gone."),
    ("Flat_Controller", "toilet_repaired",
     "The toilet is fixed."),

    ("Memories_Controller", "room_ereased",
     "Something changes in the parents' room."),
    ("Memories_Controller", "death_get_out",
     "The sign is off the door. The bin in the kid's room can be opened now."),
};

var flagLines = new System.Text.StringBuilder();
var flagSeed  = new System.Text.StringBuilder();
int nFlag = 0;
foreach (var row in flagRows)
{
    var holder = Data.GameObjects.ByName(row.obj);
    if (holder == null)
    {
        Console.WriteLine($"  flag row dropped, no object: {row.obj}");
        continue;
    }
    // Same rule as the switch table: the holder must really set the variable in its own
    // Create, or reading it is the fatal unset-variable case the first time the flag is
    // watched rather than the first time it is set.
    if (!HasSelfVar(holder, row.var_))
    {
        Console.WriteLine($"  flag row dropped, {row.obj} does not set {row.var_}");
        continue;
    }
    // Written out as GML rather than kept in a table, because reading a variable whose
    // NAME is only known at runtime needs variable_instance_get, and a builtin this game
    // does not already call is a failure that only shows up once the patched game runs -
    // see the note on variable_local_exists. Generating the read means the compiler
    // resolves the name here, where a mistake is a build error.
    //
    // The previous value lives at this row's index in a11y_flgl, seeded to 0 below.
    flagLines.Append(
        // instance_exists() and instance_find() do NOT agree while the pause menu is
        // open. Pause's Create calls instance_deactivate_all, and a deactivated instance
        // still answers instance_exists in this runtime while instance_find - which walks
        // the ACTIVE list - returns noone. Reading a field off that is the crash
        // "Variable <unknown_object>.<unknown variable> not set before reading it", with
        // no object and no variable name because there is no instance to name.
        //
        // So the guard has to be on the instance the find returned, never on the object.
        "                    a_fv = 0;\n" +
        "                    a_fi = instance_find(" + row.obj + ", 0);\n" +
        "                    if (instance_exists(a_fi))\n" +
        "                    {\n" +
        "                        if (a_fi." + row.var_ + ")\n" +
        "                            a_fv = 1;\n" +
        "                    }\n" +
        "                    if (a_fv && ds_list_find_value(a11y_flgl, " + nFlag + ") == 0)\n" +
        "                    {\n" +
        "                        if (a11y_ready && a_evsaid == 0)\n" +
        "                        {\n" +
        "                            external_call(a11y_f_speak, \"" + row.text + "\", 1);\n" +
        "                            a_evsaid = 1;\n" +
        "                        }\n" +
        "                    }\n" +
        "                    ds_list_replace(a11y_flgl, " + nFlag + ", a_fv);\n");
    flagSeed.Append("    ds_list_add(a11y_flgl, 0);\n");
    nFlag++;
}
Console.WriteLine($"Story flags watched: {nFlag} of {flagRows.Length}.");

// Which cutscenes a keypress can actually do anything to. Three of the seventy-four, and
// that is the whole of the "Press Enter to carry on" bug: the line was offered for every
// one of them, so seventy-one times out of seventy-four the player pressed a key at a
// cutscene and nothing happened. A Cutscene answers a click only through a global left
// press handler, which is event Mouse 53 - own or inherited, so the parent chain is
// walked the same way the press and hover lookups do it.
bool HasGlobalPress(UndertaleGameObject o)
{
    for (var p = o; p != null; p = p.ParentId)
    {
        var lst = p.Events[(int)EventType.Mouse];
        if (lst == null) continue;
        foreach (var ev in lst)
            if (ev.EventSubtype == 53 && ev.Actions.Count > 0) return true;
    }
    return false;
}

var cutPressLines = new System.Text.StringBuilder();
int nCutPress = 0, nCutTotal = 0;
foreach (var o in Data.GameObjects)
{
    if (o == cutRoot || cutRoot == null) continue;
    bool isCut = false;
    for (var p = o.ParentId; p != null; p = p.ParentId)
        if (p == cutRoot) { isCut = true; break; }
    if (!isCut) continue;
    var cn = o.Name?.Content;
    if (!SpeakableName(cn)) continue;
    nCutTotal++;
    if (!HasGlobalPress(o)) continue;
    cutPressLines.Append("    ds_map_add(a11y_cutkey, " + cn + ", 1);\n");
    nCutPress++;
}
Console.WriteLine($"Cutscenes a key can dismiss: {nCutPress} of {nCutTotal}.");

// Items whose localised name leaves out the one detail the puzzle turns on. There is
// exactly one so far and it is the queue ticket in the hospital: its name in every
// language is just "Number", and the number printed on it - a 3, where the waiting room
// is calling 13 - is drawn on the sprite and written down nowhere. A sighted player reads
// it off the icon; there was nothing to hear at all, and the whole point of the ticket is
// which number it is.
//
// Kept as a table rather than a rename so the game's own localisation still supplies the
// word and this only appends to it.
var itemNameRows = new (string obj, string suffix)[]
{
    ("Item_Doctor_Number_3", " 3"),
};

var itemNameLines = new System.Text.StringBuilder();
int nItemName = 0;
foreach (var row in itemNameRows)
{
    if (Data.GameObjects.ByName(row.obj) == null)
    {
        Console.WriteLine($"  item name row dropped, no object: {row.obj}");
        continue;
    }
    itemNameLines.Append("    ds_map_add(a11y_iname, " + row.obj + ", \"" + row.suffix + "\");\n");
    nItemName++;
}
Console.WriteLine($"Items whose name gains the detail on the sprite: {nItemName}.");

// What a piece of scenery LOOKS like.
//
// Most of what fills a room here is a look-at hotspot whose entire press handler is a
// random rustle or scrape. The name is all there was, and a name is not a description:
// "Graveyard Cementery Gtave" says nothing about a leaning headstone with the letters
// worn off it. These are transcribed from the exported sprite the same way the picture
// close-ups were - see export_closeups.csx - because nothing else in the data file
// records what the art shows.
//
// Read on the first Enter and NOT on the second: pressing the same scenery twice in a row
// gives the short label back, so a player stepping along a wall of debris is not read a
// paragraph each time. Moving to anything else re-arms it.
//
// This table is deliberately partial and is meant to grow a chapter at a time. An object
// with no row here behaves exactly as it did before.
var sceneryRows = new (string obj, string text)[]
{
    ("Bridge_Battery_Item",
     "A small cylindrical battery lying on its side."),
    ("Bridge_Bird",
     "A big black crow standing side on, wings folded."),
    ("Bridge_Bird_02",
     "A black crow, drawn in dense scribbled ink."),
    ("Bridge_Bird_03",
     "A black crow, drawn in dense scribbled ink."),
    ("Bridge_Bird_04",
     "A black crow, drawn in dense scribbled ink."),
    ("Bridge_Bird_05",
     "A black crow, drawn in dense scribbled ink."),
    ("Bridge_Bird_06",
     "A black crow, drawn in dense scribbled ink."),
    ("Bridge_Can",
     "A crushed drinks can on its side."),
    ("Bridge_Can_02",
     "A small can standing upright, dented."),
    ("Bridge_Car",
     "A small hatchback seen from behind and above, its aerial bent over in a zigzag."),
    ("Bridge_Bag_Foil",
     "A crumpled foil crisp packet."),
    ("Bridge_Banana",
     "A banana skin, flattened, its ends splayed out like a star."),
    ("Bridge_Controller_Item",
     "A handheld remote: a rounded case with two buttons, a small joystick and a stubby aerial."),
    ("Bridge_Cup",
     "A paper cup with a bent straw sticking out of it."),
    ("Bridge_Plank",
     "A long sawn plank of wood, the grain drawn in."),
    ("Bridge_Plank_02",
     "A long sawn plank of wood, the grain drawn in."),
    ("Bridge_03_Shoe",
     "A single trainer lying on its side."),
    ("Bridge_Bottle_Plastic",
     "A crushed plastic bottle on its side, the label still on it."),
    ("Bridge_Keys_Car",
     "A car key on a short ring, with a black plastic fob."),
    ("Bridge_Fishing_Rod",
     "A fishing rod with the line hanging slack from the tip."),
    ("Bridge_05_Barier",
     "A long red and white barrier pole."),
    ("Bridge_Clip",
     "A bent paperclip."),
    ("Bridge_Coin_Item",
     "A small coin, seen face on."),
    ("Bridge_Bird_Monster",
     "A bird the size of a man: a crow's head and long beak on a hunched, half-plucked body, with spindly arms and legs that end in hooked claws."),
    ("Bridge_Bear",
     "A teddy bear lying in a red smear, its stuffing coming out."),
    ("Bridge_Controller_Button",
     "A large rounded button standing proud of its base."),
    ("Bridge_Controller_Button_B",
     "A flat rectangular button with a hatched face."),
    ("Bridge_Wire_Item",
     "A length of wire, frayed at both cut ends."),
    ("Bridge_Doll_Crying",
     "A baby doll in a ragged dress, its face screwed up in a cry, arms out."),
    ("Bridge_Crowbar",
     "A long crowbar, hooked and split at one end."),
    ("Bridge_Masked_Man",
     "A figure in a mask, standing still."),
    ("Computer_Clock",
     "A small blank panel on the desktop, the clock."),
    ("Computer_Floppy_Icon",
     "A desktop icon showing a floppy disk."),
    ("Computer_Games_Icon",
     "A desktop icon showing a folder with a corner turned up."),
    ("Computer_Internet_Icon",
     "A desktop icon showing a crescent moon and stars."),
    ("Computer_Trash_Icon",
     "A desktop icon showing a waste bin."),
    ("Hospital_Reception_Trash",
     "A metal waste bin, full of crumpled paper."),
    ("Hospital_Entrance_Trash",
     "A knotted rubbish bag."),
    ("Hospital_Entrance_Trash_02",
     "A knotted rubbish bag."),
    ("Hospital_Park_Trash",
     "A metal waste bin, full to the brim."),
    ("Hospital_Doctor_Key",
     "A small key."),
    ("Hospital_Doctor_Note",
     "A small slip of paper with a couple of lines written on it."),
    ("Hospital_Fish_Eye",
     "A single eyeball."),
    ("Flat_Room_02_Chair",
     "A soft armchair with thin splayed legs."),
    ("Flat_Room_02_Clock",
     "A round alarm clock with two bells on top."),
    ("Flat_Room_02_Table",
     "A low wooden table."),
    ("Flat_Room_02_Window",
     "Blank window panes, three across and one below."),
    ("Flat_Room_02_Curtain",
     "A curtain hanging on rings from a rail."),
    ("Hospital_Flat_Board",
     "A flat card lying on the table with a strip printed across it."),
    ("Hosptital_Hall_Light",
     "A light switch, a small square plate on the wall."),
    ("Hospital_Hall_02_Box",
     "A cardboard box, open at the top."),
    ("Hospital_Bird",
     "A black crow, drawn in dense scribbled ink."),
    ("Hospital_Hall_03_Window",
     "A tall window, empty."),
    ("Hospital_Hall_03_Window_Open",
     "A window pane in its frame."),
    ("Hospital_Hall_Ear_Interactive",
     "A severed ear."),
    ("Hospital_Hand",
     "A hand seen from above, the two middle fingers cut off and bleeding, stitches across the knuckles."),
    ("Hospital_Glue",
     "A tube of glue, half squeezed."),
    ("Hospital_Nightmares_Telephone",
     "An old dial telephone with the handset resting on it."),
    ("Hospital_Park_Balloon",
     "A balloon on a string, drifting."),
    ("Hospital_Reception_FIrst_Aid",
     "A first aid box with a cross on the front."),
    ("Hospital_Reception_Pin",
     "A drawing pin."),
    ("Hospital_Scissors",
     "A pair of scissors, open."),
    ("Hospital_Patient_Room_14",
     "A patient lying in bed under the covers, head bandaged, one arm hanging over the side."),
    ("Hospital_Room_15_Key",
     "A small key."),
    ("Hospital_Room_15_Patient",
     "A gaunt bald patient propped up in bed against a pillow, staring."),
    ("Hospital_Park_Apple_Core",
     "An apple core."),
    ("Hospital_Sink_Hairs",
     "A tangled knot of hair."),
    ("Hospital_Soup_Finger",
     "A severed fingertip, bloody at the cut."),
    ("Hospital_Soup_Finger_02",
     "A severed fingertip, bloody at the cut."),
    ("Hospital_Soup_Finger_03",
     "A severed finger, cut through at the knuckle."),
    ("Hospital_Phone_Handle",
     "The handset of a wall telephone."),
    ("Hospital_WC_Bottle_Cap",
     "A bottle cap."),
    ("Hospital_WC_Flush",
     "A flush handle on a chain."),
    ("Hospital_Cook",
     "A thin young man in a hospital gown, leaning on a walking stick."),
    ("Hospital_Moon",
     "A full moon, pitted with craters."),
    ("Hospital_Canteen_Mug",
     "A mug."),
    ("Hospital_Canteen_Socket",
     "A wall socket."),
    ("Hospital_Canteen_Spider",
     "A fat spider with a pale marking on its back."),
    ("Hospital_Canteen_Spider_02",
     "A small spider."),
    ("Hospital_Canteen_Spider_03",
     "A small spider."),
    ("Hospital_Jar",
     "A small glass jar."),
    ("Doctor_Head",
     "A severed head lying on its side, the mouth open."),
    ("Hospital_Bus_Fish",
     "A small dead fish."),
    ("Hospital_Bus_Stop_Glass",
     "A bus shelter with a curved roof, one side panel broken out."),
    ("Hospital_B_Ruins_Bottle",
     "A bottle lying on its side, half its label torn off."),
    ("Hospital_B_Spider_Chew",
     "A spider with its legs splayed."),
    ("Hospital_B_WC_Fish",
     "A small fish skeleton."),
    ("Hospital_B_WC_Flush",
     "A flush handle hanging on a long chain."),
    ("Hospital_B_Park_Bus_Stop",
     "A bus shelter with a curved roof, its far side panel broken out."),
    ("Hospital_B_Canteen_Socket",
     "A wall socket."),
    ("Hospital_B_Doctor_Box",
     "A cardboard box, flaps open, THIS WAY UP printed upside down on the side."),
    ("Hospital_B_Doctor_Leech",
     "A long black leech."),
    ("Hospital_B_Nurse",
     "A nurse in a white uniform and cap, standing with one hand on her hip."),
    ("Hospital_B_Patient",
     "A man standing with his belly opened, holding his own insides in both hands."),
    ("Hospital_B_Patient_Old",
     "A stooped old man in a jumper, leaning on a walking stick."),
    ("Hospital_B_Patient_02",
     "A gaunt man crouching over a mug, blood at his mouth."),
    ("Hospital_B_Patient_Woman",
     "A heavy woman sitting on the floor, a red wound across her scalp."),
    ("Hospital_B_Patient_02_Waiting",
     "A gaunt man crouching over a mug, blood at his mouth."),
    ("Hospital_B_Patient_Old_Waiting",
     "A stooped old man in a jumper, leaning on a walking stick."),
    ("Hospital_B_Patient_Woman_Waiting",
     "A heavy woman sitting on the floor, a red wound across her scalp."),
    ("Hospital_B_Patient_Buy",
     "A young man in a hospital gown, standing."),
    ("Hospital_B_Hall_Light",
     "A light switch on the wall."),
    ("Hospital_Hall_02_Trash_Can",
     "A metal waste bin, full to the brim."),
    ("HospitalB_Hall_03_Window",
     "A tall window pane in its frame."),
    ("Hospital_B_Hall_03_Vending_Machine_Glass",
     "The glass front of a vending machine."),
    ("Hospital_B_Park_Sewers",
     "A round drain cover set in the ground."),
    ("Hospital_B_Coin_Reception",
     "A coin lying flat."),
    ("Hospital_B_Reception_Firs_Aid_Closed",
     "A first aid box with a cross on the front, shut."),
    ("Hospital_B_Reception_Mouse_Hole",
     "A mouse hole in the skirting board."),
    ("Hospital_B_Reception_Spider",
     "A fat spider."),
    ("Hospital_B_Gate_Key",
     "A small key."),
    ("Hospital_B_Patient_Old_Food",
     "A stooped old man in a jumper, leaning on a walking stick."),
    ("Hospital_B_Teddy_Corpse",
     "A teddy bear lying on its back with its limbs splayed and its stuffing out."),
    ("Hospital_B_Ruins_Dont_Cross",
     "A length of tape stretched across the way, printed DO NOT CROSS over and over."),
    ("Hospital_B_Crowbar_Ruins",
     "A long crowbar."),
    ("Hospital_B_Waiting_Mouse_Trap",
     "A wooden mousetrap, the bar still set."),
    ("HospitalB_Mouse_Trap",
     "A wooden mousetrap seen from above, the bar set and the bait pin standing."),
    ("HospitalNF_Acid_Key",
     "A key lying in a pool of green acid."),
    ("HospitalNF_Room_03_Wood",
     "A short length of broken batten."),
    ("HospitalNF_Surgery_Acid",
     "A bottle on its side with green acid running out of the neck."),
    ("HospitalNF_Surgery_Web_Acid",
     "A cobweb hanging over a patch of green acid."),
    ("Graveyard_Bird",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Bird_02",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Bird_03",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Bird_04",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Bird_05",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Bird_06",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Bird_07",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Bird_08",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Bird_09",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Bird_10",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Bird_11",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Scarecrow_Bird",
     "A black crow, drawn in dense scribbled ink."),
    ("Graveyard_Cementery_Gtave",
     "A grave: a rounded headstone with a cross cut into it, a long slab in front, grass growing up around the edges."),
    ("Graveyard_Spider",
     "A small spider."),
    ("Graveyard_Crypt_Hole",
     "A gap in the crypt wall where the stone has fallen through."),
    ("Graveyard_Star",
     "A small five-pointed star."),
    ("Graveyard_Fly_Small",
     "A tiny fly."),
    ("Graveyard_Jug",
     "A jug lying on its side with a few dark beans spilled out of the mouth."),
    ("Graveyard_Drawing_Gate",
     "A small card with a couple of lines written on it."),
    ("Graveyard_Flower",
     "A flower with curling petals on a twisted, thorny stem."),
    ("Graveyard_Bone",
     "A long bone with knobbed ends, lying on the ground with dirt around it."),
    ("Graveyard_Shovel",
     "A shovel, blade down, the handle running up out of frame."),
    ("Graveyard_Glove",
     "A heavy work glove."),
    ("Graveyard_Gravedigger_Skull",
     "A small human skull."),
    ("Igor_Big",
     "A big heavy man in a jumper, a long scar across his forehead, glaring straight out."),
    ("Graveyard_Lady",
     "A very fat woman in a long coat, arms folded, standing still."),
    ("Graveyard_Candle",
     "A candle, standing, wax running down one side."),
    ("Graveyard_Monument_Memento",
     "A small plaque with MEMENTO MORI cut into it."),
    ("Graveyard_Morgue_Doors",
     "A heavy metal door with a long vertical handle."),
    ("Graveyard_Morgue_Glass_Door",
     "A tall glass-fronted cabinet door."),
    ("Graveyard_Morgue_Jar",
     "A small jar."),
    ("Graveyard_Crypt_Inside_Sign_Small",
     "A small sign standing in the crypt floor."),
    ("Forest_Beehive",
     "A beehive hanging from a branch, banded round like a coil of rope, with a hole in the front."),
    ("Forest_Forest_Berries",
     "A scatter of small dark berries."),
    ("Forest_Cigarette",
     "A cigarette."),
    ("Forest_House_Inside_Bottle_Mask",
     "A tall glass jar with a wide neck and no lid."),
    ("Forest_House_Inside_Cleaver",
     "A meat cleaver with blood on the blade and drops flicked around it."),
    ("Forest_House_Inside_Meat",
     "A thick slab of red meat."),
    ("Forest_House_Inside_Pot",
     "A big round cauldron with a carrying handle."),
    ("Forest_House_Inside_Rung",
     "A short length of wooden rung."),
    ("Forest_House_Inside_Salt",
     "A salt shaker."),
    ("Forest_Fishing_Rod_Standing",
     "A fishing rod propped upright, the line hanging."),
    ("Forest_Lake_Man",
     "A man in shorts and a t-shirt standing by the water, staring, hair on end."),
    ("Forest_Lake_Trash_Pile",
     "A heap of rubbish: bottles, cans, torn paper and a boot."),
    ("Forest_Dog",
     "A thin dog sitting up, ears pricked, a collar round its neck and its tail curled behind it."),
    ("Forest_Egg",
     "A small speckled egg."),
    ("Forest_Ladder",
     "A wooden ladder."),
    ("Forest_Road_Blood",
     "A splash of blood on the road with something caught in it."),
    ("Forest_Road_Grain",
     "A head of wheat on its stalk."),
    ("Forest_Road_Motor_Oil",
     "A black patch of spilt oil."),
    ("Forest_Road_Tire",
     "A car wheel leaning against the ground, its spokes showing."),
    ("Forest_Swamp_Rung",
     "A short wooden peg."),
    ("Forest_Tent_Keys",
     "A bunch of keys on a ring."),
    ("Forest_Flower",
     "A flower with curling petals on a twisted stem."),
    ("Flat_B_Hall_Hat",
     "A hat hanging up."),
    ("Flat_B_Hall_Jacket",
     "A long coat hanging on a hook."),
    ("Flat_B_Hall_Shoes",
     "A boot lying on its side."),
    ("Flat_B_Kitchen_Note",
     "A small note with a couple of lines written on it."),
    ("Flat_B_Kitchen_Piggy",
     "A piggy bank."),
    ("Flat_Kitchen_Piggy",
     "A piggy bank."),
    ("Flat_Kitchen_Frying_Pan",
     "A frying pan with a long handle."),
    ("Flat_Kitchen_Kettle",
     "A stovetop kettle with a spout and a handle over the top."),
    ("Flat_Kitchen_Ladle",
     "Two ladles hanging side by side."),
    ("Flat_Kitchen_Paper_Towel",
     "A roll of kitchen paper."),
    ("Flat_Kitchen_Spices",
     "Two spice jars with dark contents."),
    ("Flat_Kitchen_Thing",
     "Two plastic bottles of cleaner."),
    ("Flat_Room_Pictures",
     "Five framed pictures hung together on the wall, four small and one wider below."),
    ("Flat_Room_Radio",
     "A portable stereo with a speaker at each end and a handle on top."),
    ("Flat_Room_Window_Glass",
     "Two blank window panes."),
    ("Flat_Room_Window",
     "Blank windows on the block opposite, in rows."),
    ("Flat_B_Room_02_Key",
     "A small key."),
    ("Flat_Bathroom_Plunger",
     "A rubber plunger on a wooden handle."),
    ("Flat_Help_Paperclip",
     "A paperclip."),
    ("Flat_Kitchen_Stamps",
     "A sheet of small stamps."),
    ("Flat_Mail",
     "A large envelope with the flap folded open."),
    ("Flat_Room_Book",
     "A closed book."),
    ("Flat_Phone_Info_Small",
     "A scrap of paper with a scribbled note on it."),
    ("Flat_Room_Plate",
     "A dinner plate seen from above."),
    ("Flat_Sink_Teeth",
     "Two teeth lying loose, red at the roots."),
    ("Flat_Staircase_Door_Mat",
     "A doormat."),
    ("Flat_Staircase_Door_Note",
     "A small note pinned to the door."),
    ("Flat_Kitchen_Curtain",
     "A curtain hanging on rings from a rail."),
    ("Flat_B_Kitchen_Curtain",
     "A curtain hanging on rings from a rail."),
    ("Flat_Room_Curtain",
     "A curtain hanging on rings from a rail."),
    ("Flat_B_Room_02_Curtain",
     "A curtain hanging on rings from a rail."),
    ("Ending_Flat_Kitchen_Curtain",
     "A curtain hanging on rings from a rail."),
    ("Ending_Flat_Room_Curtain",
     "A curtain hanging on rings from a rail."),
    ("Board_Bat",
     "A square of the board with a bat drawn on it, labelled BAT, a red ring below."),
    ("Board_Bat_02",
     "A square of the board with a bat drawn on it, labelled BAT, a red ring below."),
    ("Board_Butcher",
     "A square of the board with a stick figure holding a cleaver, labelled BUTCHER, a red ring below."),
    ("Board_Cyclops",
     "A square of the board with a one-eyed stick figure, labelled CYCLOPS, a red ring below."),
    ("Board_Poison_Ivy",
     "A square of the board with a creeping thorned vine, labelled POISON IVY, a red ring below."),
    ("Board_Scarecrow",
     "A square of the board with a scarecrow on a post, labelled SCARECROW, a red ring below."),
    ("Board_Spider",
     "A square of the board with a spider, labelled SPIDER, a red ring below."),
    ("Board_Trap",
     "A square of the board with a lit bomb on it, labelled TRAP, a red ring below."),
    ("Board_Spikes",
     "A square of the board scribbled out in blue pen."),
    ("Board_Spikes_02",
     "A square of the board scribbled out in blue pen."),
    ("Board_Spikes_03",
     "A square of the board scribbled out in blue pen."),
    ("Board_Spikes_04",
     "A square of the board scribbled out in blue pen."),
    ("Board_Spikes_05",
     "A square of the board scribbled out in blue pen."),
    ("Board_Spikes_06",
     "A square of the board scribbled out in blue pen."),
    ("Board_Spikes_07",
     "A square of the board scribbled out in blue pen."),
    ("Board_Spikes_08",
     "A square of the board scribbled out in blue pen."),
    ("Ending_Flat_02_Mirror_Mess",
     "Black blotches and smears scrawled across the glass."),
    ("Ending_Flat_Fridge_Message",
     "A small note stuck to the fridge with a few lines on it."),
    ("Ending_Flat_Basement_Key",
     "A small key."),
    ("Ending_Flat_Kitchen_Hammer",
     "A claw hammer."),
    ("Ending_Flat_Outside_Windows",
     "Blank windows on the block opposite."),
    ("Ending_Hospital_Menu",
     "A blackboard menu with a few handwritten lines and a chicken leg drawn at the bottom."),
    ("Ending_Hospital_Doctor_Office_Bricks",
     "A patch of bare brickwork where the plaster has come away."),
    ("Ending_Hospital_Nurse_Doctor_Office",
     "A nurse in a white uniform and cap, one hand on her hip."),
    ("Ending_Hospital_Nurse_Room",
     "A nurse in a white uniform and cap, one hand on her hip."),
    ("Ending_Plant",
     "A potted plant with broad leaves on a thin stem."),
    ("Ending_Plant_02",
     "A small potted tree clipped into a ball."),
    ("Ending_Plant_03",
     "A potted plant with wide leaves."),
    ("Ending_Plant_04",
     "A potted plant with long narrow leaves."),
    ("Ending_Hospital_Flower",
     "A small flower."),
    ("Ending_Hospital_Flower_03",
     "A small flower."),
    ("Ending_Hospital_Flower_04",
     "A small flower."),
    ("Ending_Hospital_Flower_05",
     "A small flower."),
    ("Endning_Hospital_Flower_02",
     "A small flower."),
    ("Ending_Hospital_Boy",
     "A boy in shorts and a t-shirt with a teddy printed on it, standing."),
    ("EndingB_Hospital_Kid",
     "A boy in shorts and a t-shirt with a teddy printed on it, standing."),
    ("EndingB_Drawing_Baloon_02",
     "A hot air balloon with a basket under it."),
    ("EndingB_Graveyard_Drawing_Baloon",
     "A hot air balloon with a basket under it."),
    ("EndingB_Graveyard_Zombie",
     "A corpse sitting on the ground, the skin torn away over its ribs, legs straight out in front."),
    ("EndingB_Graveyard_Zombie_Head",
     "A severed head, the jaw hanging."),
    ("EndingB_Candle",
     "A candle burnt down low."),
    ("EndingB_Dead_Bee",
     "A dead bee."),
    ("EndingB_Graveyard_Crypt_Inside_Rat",
     "A rat."),
    ("EndingB_Graveyard_House_Camera",
     "An old camera on a long strap."),
    ("EndingB_Graveyard_House_Drawing",
     "A blank sheet of paper lying flat."),
    ("EndingB_Graveyard_House_Teddy_Head",
     "A teddy bear's head, torn off."),
    ("EndingB_Graveyard_House_Teddy_Head_02",
     "A teddy bear's head, torn off."),
    ("EndingB_Graveyard_House_Teddy_Head_03",
     "A teddy bear's head, torn off."),
    ("EndingB_Graveyard_Skull",
     "A small skull."),
    ("EndingB_Graveyard_Lady",
     "A very fat woman in a long coat, arms folded."),
    ("EndingB_Canteen_Woman",
     "A thin woman in a vest, her head bandaged."),
    ("EndingB_Doctor",
     "A severed head lying on its side, the mouth open."),
    ("EndingB_Hospital_Patient_Fat",
     "A man standing with his belly opened, holding his own insides in both hands."),
    ("EndingB_Hospital_Room_14_Patient",
     "A patient lying in bed, bandaged, one arm hanging over the side."),
    ("EndingB_Patient_Eye",
     "A gaunt man crouching over a mug, his mouth bloody."),
    ("EndingB_Patient_Woman",
     "A heavy woman sitting on the floor, a red wound across her scalp."),
    ("EndngB_Hospital_Room_15_Pole",
     "A drip stand."),
    ("Memories_Dog_House_Map",
     "A folded drawing headed Treasure Map, TOP SECRET written across it in red, with a green bush inked on one side."),
    ("Memories_Forest_Beehive",
     "A beehive hanging from a branch, banded round like a coil of rope."),
    ("Memories_Hall_Drawer",
     "A long wooden drawer front, shut."),
    ("Memories_Kitchen_Mask",
     "A pale round face with hollow eyes and an open mouth, hanging on the wall over a black smear."),
    ("Memories_Kitchen_Mask_02",
     "A pale round face with hollow eyes and an open mouth."),
    ("Memories_Kitchen_Mask_03",
     "A pale round face with hollow eyes and a wide open mouth."),
    ("Memories_Kitchen_Rag",
     "A torn scrap of cloth with dark specks on it."),
    ("Memories_Lake_Fishing_Rod",
     "A fishing rod, the line hanging from the tip."),
    ("Memories_Lake_Message",
     "A big sign, hand lettered STOP! in blue."),
    ("Memories_Letter_Pass",
     "A strip of paper with PASS written across it in blue."),
    ("Memories_Outside_Guard",
     "A small figure standing with its mouth open, shouting."),
    ("Memories_Photo",
     "A grey photograph of a boy standing in a forest clearing among cut branches."),
    ("Memories_Carpet",
     "A rug lying flat on the floor, its corners frayed, something small showing at one edge."),
    ("Memories_Tacks",
     "A scatter of tacks, points up."),
    ("Memories_Room_Kid_Broom",
     "A broom standing against the wall."),
    ("Memories_Room_Kid_Paper",
     "A drawing lying flat, a couple of lines on it."),
    ("Memories_Toilet_Door",
     "A plank door with a heart cut into it and a round knob."),
    ("Memories_Trash_Items",
     "A heap of torn paper and small cards, spilled out."),
    ("Little_Nightmares_Door",
     "A plain panelled door in the left-hand wall, with a coat hanging beside it and a mat on the floor in front."),
    ("Little_Nightmares_Fridge",
     "A tall rounded fridge with a long chrome handle and a note stuck to the door."),
    ("Little_Nightmares_Cupboard",
     "A low cupboard door under the worktop."),
    ("Little_Nightmares_Drawer",
     "A run of shallow drawers beside the sink."),
    ("Little_Nightmares_Oven_Closed",
     "The oven under the cooker, its door shut."),
    ("Little_Nightmares_Oven_Opened",
     "The oven, its door hanging open."),
    ("Little_Nightmares_Cutting_Board",
     "A wooden chopping board on the worktop."),
    ("Little_Nightmares_Pan_Sink",
     "A pan sitting in the sink."),
    ("Little_Nightmares_Note",
     "A sheet of paper pinned to the wall."),
    ("Little_Nightmares_Knife",
     "A kitchen knife lying on the board."),
    ("Little_Nightmares_Beans",
     "A tin of beans."),
    ("Little_Nightmares_Poison",
     "A small dark bottle standing in the open drawer."),
    ("Little_Nightmares_Dead_Clown",
     "The other clown, slumped forward over the table, not moving."),
    ("Little_Nightmares_Borken_Window",
     "The window with its glass gone and the roofs of the town beyond it."),
    ("Little_Nightmares_Fish_Fridge",
     "A big pale fish with a black eye and stitch marks along its side, lying on the shelf."),

    // ---- the invisible look-at hotspots -----------------------------------
    //
    // Everything above is an object the game DRAWS. The scene reader's Scenery view is the
    // opposite set by definition - the bare collision shapes - so browsing Scenery found
    // nothing described at all. These have no art of their own; the picture of what they
    // cover is the room background under them, cropped by gscripts/export_hotspots.csx and
    // laid out for transcription by gscripts/hotspot_sheets.py. Each sheet cell shows the
    // object's own sprite beside the crop, because roughly half of these are hit boxes over
    // room art and the rest are real drawings the story reveals later - and only looking at
    // both tells you which.
    ("Bridge_01_Bird_Mask",
     "The top board of the roadside sign, where the crows perch."),
    ("Bridge_01_Crowbar",
     "A crowbar, hooked at one end."),
    ("Bridge_02_Crowbar_B",
     "A crowbar, hooked at one end."),
    ("Bridge_Bird_01_Dead",
     "A dead crow lying on its back, wings splayed."),
    ("Bridge_Bird_03_Dead",
     "A dead crow lying on the ground."),
    ("Bridge_Bird_04_Dead",
     "A dead crow lying on the ground."),
    ("Bridge_Bottle",
     "A glass bottle lying on its side."),
    ("Bridge_Bottle_B",
     "A glass bottle standing upright."),
    ("Bridge_Can_Smashed",
     "A drinks can, crushed flat."),
    ("Bridge_Can_02B",
     "A small can lying on its side."),
    ("Bridge_Cup_B",
     "A paper cup with a bent straw in it."),
    ("Bridge_Closed_Info_Mask",
     "A printed notice lying among the litter on the road."),
    ("Bridge_Controller_Mask",
     "A handheld remote with two buttons, a joystick and a stubby aerial."),
    ("Bridge_Homeless_Cart",
     "A supermarket trolley tipped over on the road."),
    ("Bridge_Hospital_Info_Mask",
     "A printed leaflet lying on the road."),
    ("Bridge_Weather_Info_Mask",
     "A folded sheet of newspaper on the road."),
    ("Bridge_02_News_Mask",
     "A newspaper spread open on the ground."),
    ("Bridge_03_News_Mask",
     "A sheet of newspaper lying on the ground."),
    ("Bridge_Pipe_Mask",
     "A length of ribbed pipe, coiled on the ground."),
    ("Bridge_Sewer_Mask",
     "A drain grate set into the road, papers blown up against it."),
    ("Bridge_Stone_Mask",
     "A broken chunk of concrete on the road."),
    ("Bridge_03_Stone_Mask",
     "Broken chunks of concrete on the roadway."),
    ("Bridge_04_Stone_Mask",
     "Broken chunks of concrete at the edge of the gap."),
    ("Bridge_04_Stone_Mask_02",
     "The torn edge of the roadway where it has fallen away."),
    ("Bridge_05_Stone_Mask",
     "A line of broken concrete along the roadway."),
    ("Bridge_05_Stone_Mask_02",
     "A broken chunk of concrete on the road."),
    ("Bridge_06_Stone_Mask",
     "Chips of broken concrete scattered on the road."),
    ("Bridge_Tire_Mask",
     "A car tyre lying flat on the road."),
    ("Bridge_Wood_Mask",
     "The wooden boards of the roadside WELCOME sign."),
    ("Bridge_02_Box_Mask",
     "A small wooden crate on the broken roadway."),
    ("Bridge_02_Metal_Rods_Mask",
     "Reinforcing rods jutting out of the broken roadway."),
    ("Bridge_04_Metal_Rods_Mask",
     "Reinforcing rods jutting out of the broken roadway."),
    ("Bridge04B_Metal_Rods_Mask",
     "Reinforcing rods jutting out of the broken roadway."),
    ("Bridge_05_Metal_Rod_Mask",
     "A metal rod jutting out of the broken parapet."),
    ("Bridge_Stroller",
     "A pram with its hood up, standing on the road."),
    ("Bridge_Bottle_Plastic_Smashed",
     "A plastic bottle, crushed flat."),
    ("Bridge_04_Bottle_Mask",
     "A bottle lying at the kerb."),
    ("Bridge_04B_Can_Mask",
     "A can lying on the road."),
    ("Bridge_04_Plank",
     "A long plank of wood."),
    ("Bridge_04_Plank_02",
     "A long plank of wood."),
    ("Bridge_05_Barrier_Broken",
     "A red and white barrier pole, snapped."),
    ("Bridge_05_Metal_Box_Mask",
     "A metal box mounted on a post at the roadside."),
    ("Bridge_05_Metal_Box_Wire_Mask",
     "Wires running out of the metal box on the post."),
    ("Bridge_06_Wood_Mask",
     "Wooden planks laid across the roadway."),
    ("Bridge_07_Wood_Mask",
     "Wooden planks laid end to end across the gap."),
    ("Car_Body_Mask",
     "An arm hanging out of the car, blood pooled under it."),
    ("Car_Inside_Sound",
     "The dark inside of the car."),
    ("Car_Window",
     "The car's side window."),
    ("Car_Window_Sound",
     "The car's side window."),
    ("EndingB_Graveyard_Cementery_Broken_Grave_Mask",
     "Broken pieces of a headstone lying in the grass."),
    ("EndingB_Graveyard_Cliff_Bridge_Mask",
     "The planks of the rope bridge across the gorge."),
    ("EndingB_Graveyard_Cliff_Bridge_Mask_02",
     "The far end of the rope bridge and its posts."),
    ("EndingB_Graveyard_Cliff_Mask",
     "The cliff edge, with graves and a bare tree beyond it."),
    ("EndingB_Graveyard_Cliff_Metal_Mask",
     "A scrap of metal in the grass at the cliff edge."),
    ("EndingB_Graveyard_Cliff_Trash_Mask",
     "Litter scattered among the graves."),
    ("EndingB_Crypt_Trash_Mask",
     "A heap of rubbish outside the crypt."),
    ("EndingB_Crypt_Inside_Glass_Mask",
     "Broken glass in the crypt window."),
    ("EndingB_Crypt_Inside_Paper_Mask",
     "The crypt floor, littered with scraps of paper."),
    ("EndingB_Crypt_Inside_Skull_Mask",
     "A skull resting on the crypt slab."),
    ("EndingB_Crypt_Inside_Trash_Mask",
     "Rubbish scattered over the crypt floor."),
    ("EndingB_Crypt_Inside_Stone_Mask",
     "The stone slab inside the crypt."),
    ("EndingB_Flower_Ground_Mask",
     "The bare ground in front of a headstone."),
    ("EndingB_Graveyard_Gate_Mask",
     "The iron gate of the graveyard, barred and arched."),
    ("EndingB_Graveyard_Gate_Wall_Mask",
     "The wall beside the graveyard gate."),
    ("EndingB_Bird_Monster",
     "A bird the size of a man, hunched, with a crow's head and long clawed limbs."),
    ("EndingB_Grave_Shoes_Mask",
     "A pair of shoes in the grass by a fence post."),
    ("EndingB_Grave_Trash_Mask",
     "Litter in the grass by the grave."),
    ("EndingB_Grave_Trash_Mask_02",
     "Litter in the grass by the grave."),
    ("EndingB_Graveyard_Hosue_02_Stone_Mask",
     "A stone in the ground by the house."),
    ("EndingB_Graveyard_House_02_Broom_Mask",
     "A broom leaning against the house wall."),
    ("EndingB_Graveyard_House_02_Bucket_Mask",
     "A bucket standing against the house wall."),
    ("EndingB_Graveyard_House_02_Mailbox_Mask",
     "A mailbox on a post by the house."),
    ("EndingB_Graveyard_House_02_Roof_Mask",
     "The tiled roof of the house."),
    ("EndingB_Graveyard_House_02_Wood_Mask",
     "The boarded wall of the house."),
    ("EndingB_Graveyard_House_Crowbar",
     "A crowbar."),
    ("EndingB_Graveyard_Hosue_Inside_Carpet_Mask",
     "A rug on the floor."),
    ("EndingB_Graveyard_House_Inside_Chair_Mask",
     "An armchair with a table beside it."),
    ("EndingB_Graveyard_House_Inside_Glass_Mask",
     "Bottles standing on a shelf."),
    ("EndingB_Graveyard_House_Inside_Plastic_Mask",
     "Plastic bottles on the shelf."),
    ("EndingB_Graveyard_House_Inside_Wood_Mask",
     "The wooden furniture of the room, doors and a low table."),
    ("EndingB_Monument_Glass_Mask",
     "A glass jar standing on the monument."),
    ("EndingB_Graveyard_Cementery_Crypt_Mask",
     "The crypt, a stone building with a heavy door."),
    ("EndingB_Crypt_Mask",
     "The crypt, a stone building with a heavy door."),
    ("EndingB_Graveyard_Grave_House_Mask",
     "A wooden shed with a lean-to roof."),
    ("Graveyard_Cementery_Brick",
     "A loose brick in the wall."),
    ("Graveyard_Cementery_Brick_02",
     "A loose brick in the wall."),
    ("Graveyard_Cementery_Shovel",
     "A shovel standing against a headstone."),
    ("Graveyard_Cementery_Stone_Mask",
     "A stone lying in the grass."),
    ("Graveyard_Cliff_Bridge",
     "The planks of the rope bridge over the gorge."),
    ("Graveyard_Cliff_Stone_Mask",
     "A stone at the cliff edge."),
    ("Graveyard_Thing_Mask",
     "A knot in the trunk of the dead tree."),
    ("Graveyard_Crypt_Bone",
     "A long bone laid in the stone arms."),
    ("Graveyard_Crypt_Skull",
     "A skull laid in the stone arms."),
    ("Graveyard_Crypt_Stone_Mask",
     "The stone wall of the crypt."),
    ("Graveyard_Crypt_Inside_Paper_Mask",
     "Scraps of paper on the crypt floor."),
    ("Graveyard_Crypt_Inside_Scythe_Blade",
     "The rusty blade of a scythe."),
    ("Graveyard_Crypt_Inside_Scythe_Wood",
     "The wooden shaft of a scythe."),
    ("Graveyard_Crypt_Inside_Skull_Mask",
     "A skull resting on the slab."),
    ("Graveyard_Gate_Sound",
     "The barred iron gate of the graveyard."),
    ("Graveyard_Gate_Stone_Ground",
     "A stone lying by the gate."),
    ("Graveyard_Gate_Stone_Mask",
     "A stone in the ground by the gate."),
    ("Graveyard_Grave_Pipe_Mask",
     "A pipe standing up out of the ground."),
    ("Graveyard_Grave_Igor",
     "A big heavy man with a scarred forehead."),
    ("Graveyard_Gravedigger_Bottles_Mask",
     "Bottles standing on a shelf."),
    ("Graveyard_Gravedigger_Bottles_02_Mask",
     "Bottles standing on a shelf."),
    ("Graveyard_Gravedigger_Box_Mask",
     "A wooden crate."),
    ("Graveyard_Gravedigger_Box_02_Mask",
     "A wooden crate."),
    ("Graveyard_Gravedigger_Box_03_Mask",
     "A wooden crate."),
    ("Graveyard_Gravedigger_Box_04",
     "A wooden crate."),
    ("Graveyard_Gravedigger_Box_04B",
     "A wooden crate."),
    ("Graveyard_Gravedigger_Cans_Mask",
     "Tins standing on a shelf."),
    ("Graveyard_Gravedigger_Coffin",
     "A coffin standing on end against the wall."),
    ("Graveyard_Gravedigger_House_Bucket_Mask",
     "Buckets with tools standing in them."),
    ("Graveyard_Gravedigger_Plank_Mask",
     "A plank leaning against the wall."),
    ("Graveyard_Gravedigger_Plank_02_Mask",
     "A plank leaning against the wall."),
    ("Graveyard_Gravedigger_Plank_Mask_02",
     "A shovel and a plank leaning in the corner."),
    ("Graveyard_Gravedigger_Shelve",
     "A shelf with jars and tins on it."),
    ("Graveyard_Gravedigger_Shovel_Mask",
     "A shovel leaning against the wall."),
    ("Graveyard_Gravedigger_Wood_Mask_02",
     "The wooden furniture of the hut."),
    ("Graveyard_Teddy_Body",
     "A teddy bear's body, torn open, with blood on it."),
    ("Graveyard_Lady_Metal_Mask",
     "A metal bench beside the path."),
    ("Graveyard_Lady_Wood_Mask",
     "A wooden bench by the path."),
    ("Graveyard_Monument_Stone_Mask",
     "The stone monument, a robed figure on a plinth."),
    ("Graveyard_Morgue_Bottle_Mask",
     "A bottle on the morgue shelf."),
    ("Graveyard_Morgue_Bottles_Mask",
     "Bottles standing on the morgue shelf."),
    ("Graveyard_Morgue_Freazer_Mask",
     "A mortuary drawer with a long handle."),
    ("Graveyard_Morgue_Freazer_Mask_02",
     "A mortuary drawer with a long handle."),
    ("Graveyard_Morgue_Freazer_Mask_03",
     "A mortuary drawer with a long handle."),
    ("Graveyard_Morgue_Metal_Mask",
     "A metal trolley and table in the morgue."),
    ("Graveyard_Morgue_Plastic_Bottle_Mask",
     "A plastic container under the sink."),
    ("Graveyard_Morgue_Plastic_Bottles_Mask",
     "Plastic bottles on the shelf."),
    ("Graveyard_Morgue_Plastic_Bottles_02_Mask",
     "Plastic bottles on the shelf."),
    ("Graveyard_Morgue_Shelve_Mask",
     "A shelf of bottles above the bench."),
    ("Graveyard_Morgue_Shelve_02",
     "A shelf of bottles above the bench."),
    ("Graveyard_Morgue_Sink_Mask",
     "A steel sink with blood in it."),
    ("Graveyard_Morgue_Trash_Can",
     "A bin under the morgue bench."),
    ("Graveyard_Star_02",
     "A small five-pointed star."),
    ("Graveyard_Star_03",
     "A small five-pointed star."),
    ("Graveyard_Star_04",
     "A small five-pointed star."),
    ("Graveyard_Star_05",
     "A small five-pointed star."),
    ("Graveyard_Cliff_Branch_Mask",
     "A bare branch of the dead tree."),
    ("Graveyard_Cliff_Branch_Mask_02",
     "A bare branch of the dead tree."),
    ("Graveyard_Cliff_Branch_Mask_03",
     "A bare branch of the dead tree."),
    ("Graveyard_Cliff_Branch_Mask_04",
     "A bare branch of the dead tree."),
    ("Graveyard_Cliff_Branch_Mask_05",
     "A bare branch of the dead tree."),
    ("Graveyard_Cliff_Branch_Mask_06",
     "A bare branch of the dead tree."),
    ("Graveyard_Cliff_Branch_Mask_07",
     "A bare branch of the dead tree."),
    ("Graveyard_Cliff_Branch_Mask_08",
     "A bare branch of the dead tree."),
    ("Graveyard_Cliff_Branch_Mask_09",
     "A bare branch of the dead tree."),
    ("Graveyard_Grave_Branch_Mask",
     "A bare branch of the dead tree."),
    ("Graveyard_Grave_Branch_Mask_02",
     "A bare branch of the dead tree."),
    ("Graveyard_Grave_Branch_Mask_03",
     "A bare branch of the dead tree."),
    ("Graveyard_Grave_Branch_Mask_04",
     "A bare branch of the dead tree."),
    ("Graveyard_Grave_Branch_Mask_05",
     "A bare branch of the dead tree."),
    ("Graveyard_Grave_Branch_Mask_06",
     "A bare branch of the dead tree."),
    ("Forest_Forest_Motor_Oil",
     "A dark patch of spilt oil."),
    ("Forest_Forest_Motor_Oil_B",
     "A dark patch of spilt oil."),
    ("Forest_House_Oil",
     "A dark patch of spilt oil."),
    ("Forest_House_Oil_02",
     "A dark patch of spilt oil."),
    ("Forest_Lake_Motor_Oil",
     "A dark patch of spilt oil."),
    ("Forest_Mountain_Motor_Oil",
     "A dark patch of spilt oil."),
    ("Forest_Swamp_Motor_Oil_C",
     "A dark slick of oil on the water."),
    ("Forest_Wall_Motor_Oil",
     "A dark patch of spilt oil."),
    ("Memories_Forest_Motor_Oil_B",
     "A dark patch of spilt oil."),
    ("Memories_Mountain_Motor_Oil",
     "A dark patch of spilt oil."),
    ("Memories_Road_Motor_Oil",
     "A dark patch of spilt oil."),
    ("Forest_Mushroom_Mask",
     "Mushrooms growing among the trees."),
    ("Forest_House_Mushroom_Mask",
     "Mushrooms growing by the house."),
    ("Forest_Mountain_Mushroom_Mask",
     "Mushrooms growing among the trees."),
    ("Forest_Wall_Mushroom_Mask",
     "Mushrooms growing among the trees."),
    ("Forest_Road_Mushroom_Mask",
     "Mushrooms growing at the roadside."),
    ("Memories_Forest_Mushroom_Mask",
     "Mushrooms growing among the trees."),
    ("Memories_Road_Nasty_Mushroom",
     "A mushroom growing at the roadside."),
    ("Memories_Road_Nasty_Mushroom_02",
     "A mushroom growing at the roadside."),
    ("Forest_Hand_Mask",
     "A severed hand lying on the ground."),
    ("Forest_House_Fence_Mask",
     "The fence around the house."),
    ("Forest_House_Sign_Mask",
     "A wooden signpost with two boards pointing opposite ways."),
    ("Forest_House_Window",
     "A window in the house wall."),
    ("Forest_House_Window_02",
     "A window in the house wall."),
    ("Forest_House_Window_03",
     "A window in the house wall."),
    ("Forest_House_Inside_Broom_Mask",
     "A broom standing against the wall."),
    ("Forest_House_Inside_Broom_Handle_Mask",
     "The handle of the broom standing against the wall."),
    ("Forest_House_Inside_Garlick_Mask",
     "Bulbs of garlic hanging over the hearth."),
    ("Forest_House_Inside_Jars_Mask",
     "Jars and bottles standing on a shelf."),
    ("Forest_House_Inside_Metal",
     "Metal fittings by the hearth."),
    ("Forest_House_Inside_Mortar",
     "A mortar standing on the table."),
    ("Forest_House_Inside_Plate",
     "A plate on the table."),
    ("Forest_House_Inside_Skull",
     "A skull set on the shelf."),
    ("Forest_House_Inside_Table_Mask",
     "The table with the cauldron on it."),
    ("Forest_House_Inside_Wood",
     "A stack of split logs by the hearth."),
    ("Forest_House_Inside_Wood_02",
     "A wooden chest and a broom in the corner."),
    ("Forest_Lake_Bottle_Mask",
     "A bottle lying in the grass."),
    ("Forest_Lake_Garbage_Mask",
     "Rubbish dropped in the grass."),
    ("Forest_Lake_Rod_Mask",
     "A fishing rod propped up at the water's edge."),
    ("Forest_Mountain_Bush_Mask",
     "A bare bush by the path."),
    ("Forest_Mountain_Stone_Mask",
     "A stone on the path."),
    ("Forest_Road_Car_Mask",
     "A car parked at the roadside."),
    ("Forest_Bush_Mask",
     "Reeds growing at the water's edge."),
    ("Forest_Swamp_Bird_Oil",
     "A dead bird lying in a slick of oil."),
    ("Forest_Swamp_Bridge_Mask",
     "The planks of the walkway over the swamp."),
    ("Forest_Swamp_Fishing_Rod",
     "A fishing rod lying on the boards."),
    ("Forest_Swamp_Stone_Mask",
     "A stone at the edge of the walkway."),
    ("Forest_Swamp_Water_02_Mask",
     "The still water under the walkway."),
    ("Forest_Swamp_Water_Mask",
     "The still water beside the path."),
    ("Forest_Swamp_Wood_Log_Mask",
     "A fallen log beside the walkway."),
    ("Forest_Frog_Mask",
     "A frog at the water's edge."),
    ("Forest_Swamp_Road_Stone_Mask",
     "A stone beside the path."),
    ("Forest_Tent_Man_Sleep",
     "A man asleep in a sleeping bag."),
    ("Forest_Twigs_Mask",
     "Twigs and a stone at the foot of a tree."),
    ("Forest_Twig_Mask",
     "A fallen branch on the forest floor."),
    ("Forest_Wall_Junkie",
     "A gaunt man sitting on the ground with his arms round his knees."),
    ("Forest_Wall_Stone",
     "A stone on the ground."),
    ("Forest_Wall_Twig_Mask",
     "A fallen branch on the ground."),
    ("Forest_Stone_Mask",
     "A stone on the forest floor."),
    ("Forest_Road_Stone_Mask",
     "A stone at the roadside."),
    ("Forest_Lake_Bushes_02",
     "Reeds at the water's edge."),
    ("Forest_Lake_Bushes_Mask",
     "Reeds at the water's edge."),
    ("Forest_Lake_Stick",
     "A forked stick pushed into the ground."),
    ("Forest_Lake_Stone_Mask",
     "A stone at the water's edge."),
    ("Forest_Lake_Water_Mask",
     "The still water of the lake."),
    ("Forest_Tent_Bed_Mask",
     "A sleeping bag laid out in the tent."),
    ("Forest_Tent_Sleeping_Bag_Mask",
     "A sleeping bag laid out in the tent."),
    ("Forest_Tent_Can_Mask",
     "A can on the tent floor."),
    ("Forest_Tent_Clothes_Mask",
     "Clothes and a rucksack in the tent."),
    ("Forest_Tent_Flashlight_Mask",
     "A torch lying in the tent."),
    ("Forest_Tent_Metal_Mask",
     "The tent pole."),
    ("Forest_Tent_Plastic_Mask",
     "A plastic bottle in the tent."),
    ("Hospital_Entrance_Sing",
     "A sign reading HOSPITAL over the door."),
    ("Hospital_Entrance_Can_Mask",
     "A crushed can on the ground."),
    ("Hospital_Entrance_Trash_Mask",
     "A rubbish bag on the ground."),
    ("Hospital_Trash_Can_Mask",
     "A bin by the entrance."),
    ("HospitalB_Entrance_Bottle_Mask",
     "A bottle lying in the grass."),
    ("HospitalB_Entrance_Concrete_Mask",
     "The concrete front of the hospital."),
    ("HospitalB_Entrance_Pipe_Mask",
     "A pipe running down the wall."),
    ("HospitalB_Entrance_Window_Mask",
     "The windows of the hospital front."),
    ("Hospital_Hall_Sign",
     "A direction sign with an arrow on it."),
    ("Hospital_Hall_Sign_Mask",
     "A direction sign with an arrow on it."),
    ("HospitalB_Hall_Sign_Mask",
     "A direction sign with an arrow on it."),
    ("HospitalB_Hall_Sign_Ground_Mask",
     "A sign lying on the floor."),
    ("HospitalB_Hall_02_Sign_Mask",
     "A sign with an arrow on it."),
    ("Hospital_Hall_Poster_Mask",
     "A poster on the corridor wall."),
    ("Hospital_Warning_Info_Mask",
     "A notice on the corridor wall."),
    ("Hospital_Hall_Masked",
     "A young man standing with his hands behind his back."),
    ("Hospital_Room_15_Masked_Man",
     "A young man standing with his hands behind his back."),
    ("Hospital_B_Room_15_Masked",
     "A young man standing with his hands behind his back."),
    ("Hospital_B_Bus_Stop_Masked",
     "A young man standing with his hands behind his back."),
    ("Ending_Good_Masked_02",
     "A young man standing with his hands behind his back."),
    ("HospitalB_Hall_Bench_Mask",
     "A bench in the corridor."),
    ("HospitalB_Hall_Bench_02_Mask",
     "A bench in the corridor."),
    ("HospitalB_Hall_Door_Frame",
     "A door frame in the corridor."),
    ("HospitalB_Hall_Lamp_Mask",
     "A bare bulb hanging on a flex."),
    ("HospitalB_Light_Bulb",
     "A bare bulb hanging on a flex."),
    ("Flat_Hall_Lamp_Mask",
     "A bare bulb hanging on a flex."),
    ("Hospital_Fly_04",
     "A fly on the wall."),
    ("Hospital_Fly_04b",
     "A fly on the wall."),
    ("Hospital_Fly_04c",
     "A fly on the wall."),
    ("Hospital_Fly_05",
     "A fly on the wall."),
    ("Hospital_Fly_06",
     "A fly on the wall."),
    ("Hospital_Fly_06c",
     "A fly on the wall."),
    ("HospitalB_Hall_02_Board",
     "A noticeboard with a sign on it."),
    ("HospitalB_Hall_02_Desk_Mask",
     "A desk in the corridor."),
    ("HospitalB_Hall_02_Door_Frame",
     "A door frame."),
    ("HospitalB_Hall_02_Door_Mask",
     "Planks nailed across the door."),
    ("HospitalB_Hall_02_Plank_Mask",
     "Planks nailed across the door."),
    ("HospitalB_Hall_03_Drink_Freazer_Mask",
     "A drinks machine full of bottles."),
    ("HospitalB_Hall_03_Window_Mask",
     "The corridor window."),
    ("Hospital_Hall_Soda_Machine",
     "A vending machine full of bottles."),
    ("Hospital_Bird_Nest",
     "A black crow, drawn in dense scribbled ink."),
    ("Hospital_B_Hall_02_Torment",
     "The word TORMENT daubed on the wall in blood."),
    ("Hospital_B_Hall_02_Trash_Can",
     "A waste bin in the corridor."),
    ("Hospital_B_Hall_Rat",
     "A dead rat on the floor."),
    ("Hospital_B_Canteen_Dead_Rat",
     "A dead rat on the floor."),
    ("Hospital_B_Canteen_Eye",
     "An eyeball on the floor."),
    ("Hospital_B_Hall_Hand",
     "A severed hand lying in a pool of blood."),
    ("HospitalB_Gate_Hand",
     "A severed hand lying in a pool of blood."),
    ("HospitalB_Gate_Cook_Hand",
     "A severed hand lying in a pool of blood."),
    ("HospitalB_Hall_03_Fat_Dead_Hand",
     "A severed hand lying in a pool of blood."),
    ("HospitalB_Janitor_Hand",
     "A severed hand lying in a pool of blood."),
    ("HospitalB_Old_Woman_Hand",
     "A severed hand lying in a pool of blood."),
    ("EndingB_Hospital_Hall_Lake_Man_Hand",
     "A severed hand lying in a pool of blood."),
    ("HospitalB_Janitor_Leg",
     "A severed leg lying in a pool of blood."),
    ("HospitalB_Ruins_Inside_Cook_Leg",
     "A severed leg lying in a pool of blood."),
    ("EndingB_Hospital_Hall_Lake_Man_Leg",
     "A severed leg lying in a pool of blood."),
    ("HospitalB_Ruins_Inside_Foot",
     "A severed foot lying in a pool of blood."),
    ("HospitalB_Fat_Destroyed",
     "A body torn apart on the floor."),
    ("HospitalB_WC_Janitor_Destroyed",
     "A body torn apart on the washroom floor."),
    ("HospitalB_Old_Woman_Killed",
     "A body lying on the floor, torn open."),
    ("HospitalB_Old_Man_Out",
     "A stooped old man leaning on a walking stick."),
    ("HospitalB_Bird",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bird_02",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bird_03",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bird_04",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bird_05",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bird_06",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bird_07",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bird_08",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bird_09",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bird_10",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bird_11",
     "A black crow, drawn in dense scribbled ink."),
    ("HospitalB_Bus_Stop_Mask",
     "The bus shelter with its curved roof."),
    ("HospitalB_Bus_Stop_Sign",
     "A BUS STOP sign on a post."),
    ("HospitalB_Bus_Stop_Bench_Mask",
     "The bench in the bus shelter."),
    ("HospitalB_Bus_Stop_Trash_Mask",
     "A bin at the bus stop."),
    ("HosptialB_Trash_Mask_02",
     "A bin by the bus stop."),
    ("HospitalB_Ruins_Can_Mask",
     "A can lying on the ground."),
    ("HospitalB_Canteen_Broken_TV",
     "A television with its screen smashed."),
    ("HospitalB_Canteen_Door_Frame",
     "A heavy metal door with a round handle."),
    ("HospitalB_Canteen_Wood_Mask",
     "The wooden tables and chairs of the canteen."),
    ("Hospital_Canteen_Cash_Register_Mask",
     "A till on the counter."),
    ("Hospital_Canteen_Chair_Mask",
     "A chair at the canteen table."),
    ("Hospital_Canteen_Dishes",
     "A stack of plates and cups."),
    ("Hospital_Canteen_Paper_Mask",
     "A crumpled paper wrapper on the table."),
    ("Hospital_Canteen_Paper_Mask_02",
     "A crumpled paper wrapper on the table."),
    ("Hospital_Canteen_Salt_Mask",
     "Salt and pepper pots on the table."),
    ("Hospital_Canteen_Salt_Mask_02",
     "Salt and pepper pots on the table."),
    ("Hospital_Canteen_Salt_Mask_03",
     "Salt and pepper pots on the table."),
    ("Hospital_Canteen_Spider_Dead",
     "A dead spider on the floor."),
    ("Hospital_Canteen_Spider_02_Dead",
     "A dead spider on the floor."),
    ("Hospital_Canteen_Spider_03_Dead",
     "A dead spider on the floor."),
    ("Hospital_Canteen_TV_Mask",
     "A television on a bracket."),
    ("Hospital_Canteen_Trash_Can_Mask",
     "A waste bin in the canteen."),
    ("Hospital_Doctor_Paper_Mask",
     "Papers spread on the desk."),
    ("HospitalB_Doctor_Desk_Mask",
     "The doctor's desk and chair."),
    ("HospitalB_Doctor_Metal_Mask",
     "A metal cabinet in the office."),
    ("HospitalB_Doctor_Window_Frame",
     "The office window frame."),
    ("HospitalB_Doctor_Hole_Mask",
     "A hole broken in the wall."),
    ("HospitalB_Doctor_Jar_Mask",
     "A jar standing on the desk."),
    ("Ending_Hospital_Doctor_Mug_Mask",
     "A mug on the desk."),
    ("HospitalB_Gate_Bench_Mask",
     "A bench by the gate."),
    ("HospitalB_Gate_Mask",
     "A tall wooden gate, overgrown."),
    ("HospitalB_Gate_Sign_Mask",
     "A sign fixed to the gate."),
    ("HospitalB_Gate_Wall_Mask",
     "The wall beside the gate."),
    ("HospitalB_Park_Bench",
     "A park bench."),
    ("HospitalB_Reception_Aquarium_Mask",
     "A fish tank on the reception desk."),
    ("HospitalB_Reception_Chair_Mask",
     "A chair at the reception desk."),
    ("HospitalB_Reception_Desk_Mask",
     "The reception desk."),
    ("HospitalB_Reception_Desk_Mask_02",
     "The reception desk."),
    ("HospitalB_Reception_Door_Frame",
     "A door with a nameplate on it."),
    ("HospitalB_Reception_Drawing_Mask",
     "A child's drawing pinned up."),
    ("Hospital_Reception_Help",
     "A note pinned to the wall."),
    ("Hospital_Reception_Paper_Mask",
     "Papers on the reception desk."),
    ("Hospital_Reception_Pencil_Mask",
     "A pencil on the desk."),
    ("Hospital_Reception_Pin_B",
     "A drawing pin."),
    ("Hospital_B_Reception_First_Aid",
     "A first aid box on the wall, open."),
    ("Hospital_B_Reception_Mouse_Trap_B",
     "A mousetrap set on the floor."),
    ("EndingB_Hospital_Reception_Puke",
     "A patch of vomit on the floor."),
    ("EndingB_Reception_Box_Mask",
     "A wooden crate."),
    ("EndingB_Reception_Computer",
     "An old computer on the reception desk."),
    ("EndingB_Reception_Computer_02",
     "An old computer on the reception desk."),
    ("EndingB_Reception_Computer_03",
     "An old computer on the reception desk."),
    ("EndingB_Reception_Computer_04",
     "An old computer on the reception desk."),
    ("EndingB_Reception_Door_Broken",
     "A door hanging off its hinges."),
    ("EndingB_Reception_Door_Ground",
     "A door lying flat on the floor."),
    ("EndingB_Reception_Wood_Mask",
     "The wooden reception desk."),
    ("EndngB_Reception_Trash_Mask",
     "A waste bin by the reception desk."),
    ("EndingB_Hall_Bucket_Mask",
     "A bucket standing in the corridor."),
    ("EndingB_Hall_Poster_Mask",
     "A poster on the corridor wall."),
    ("EndingB_Hall_02_Board_02",
     "A noticeboard with papers pinned to it."),
    ("EndingB_Hall_02_Board_Ground_Mask",
     "A board lying on the floor."),
    ("EndingB_Hall_02_Broken_Desk_Mask",
     "A desk with its top broken away."),
    ("EndingB_Hospital_Kid_Photos",
     "A boy in shorts and a t-shirt with a teddy printed on it."),
    ("EndingB_Doctor_Cabinet_Mask",
     "A glass-fronted cabinet full of bottles."),
    ("EndingB_Doctor_Window_Frame",
     "A window frame with rubble piled outside."),
    ("EndingB_Doctor_Wood_Mask",
     "The wooden desk and shelves of the office."),
    ("EndingB_Canteen_Cash_Register_Mask",
     "A till on the canteen counter."),
    ("EndingB_Hospital_Canteen_Cash_Register_Ground_Mask",
     "A till lying on the floor."),
    ("EndingB_Canteen_Salt_Mask",
     "Salt and pepper pots on the table."),
    ("EndingB_Canteen_TV_Broken_Mask",
     "A television with its screen smashed."),
    ("EndingB_Canteen_TV_Table_Mask",
     "The table the television stands on."),
    ("EndingB_Hospital_Canteen_TV_Mask",
     "A television on a stand."),
    ("EndingB_Hospital_Canteen_TV_Mask_02",
     "A television screen."),
    ("EndingB_Hospital_Canteen_Table_Broken",
     "A broken table."),
    ("EndingB_Hospital_Cook_Head",
     "A severed head lying in a pool of blood."),
    ("HospitalB_Janitor_MetalB_Mask",
     "Metal shelving in the store room."),
    ("HospitalB_Janitor_Metal_Mask",
     "Metal shelving stacked with tins."),
    ("HospitalB_Janitor_Box_Mask",
     "A box on the store room shelf."),
    ("HospitalB_Janitor_Broom_Mask",
     "A broom standing in a bucket."),
    ("HospitalB_Janitor_BrushB_Mask",
     "A brush standing in a bucket."),
    ("HospitalB_Janitor_Brush_Mask",
     "A brush on the shelf."),
    ("HospitalB_Janitor_Brush_Handle_Mask",
     "The handle of a brush."),
    ("HospitalB_Janitor_Chair_Mask",
     "An armchair in the store room."),
    ("HospitalB_Janitor_Glass_Mask",
     "Bottles on the store room shelf."),
    ("HospitalB_Janitor_Plastic_Big_Mask",
     "A big water bottle on a stand."),
    ("HospitalB_Janitor_Plastic_Mask",
     "Plastic bottles on the shelf."),
    ("HospitalB_Janitor_Wood_Mask",
     "Wooden shelving in the store room."),
    ("HospitalB_Janitor_Wood_02_Mask",
     "Wooden shelving in the store room."),
    ("Ending_Hospital_Janitor_Glass_Mask",
     "Bottles on a shelf in the store room."),
    ("Hospital_Nightmares_Box_Mask",
     "Boxes stacked in the kitchen."),
    ("Hospital_Nightmares_Cloth_Mask",
     "Cloths hanging in the kitchen."),
    ("Hospital_Nightmares_Dishes_Mask",
     "Dishes stacked by the sink."),
    ("Hospital_Nightmares_Glass_Mask",
     "Glass jars in the kitchen."),
    ("Hospital_Nightmares_Metal_Mask",
     "Metal pans and fittings in the kitchen."),
    ("Hospital_Nightmares_Paper_Mask",
     "Papers pinned up in the kitchen."),
    ("Hospital_Nightmares_Plastic_Mask",
     "Plastic bottles in the kitchen."),
    ("Hospital_Nightmares_Stove_Mask",
     "The cooker in the kitchen."),
    ("Hospital_Nightmares_Stuff_Mask",
     "A drawer in the kitchen unit."),
    ("Hospital_Nightmares_Window_Frame",
     "The kitchen window frame."),
    ("Hospital_Nightmares_Wood_Mask",
     "The wooden kitchen units."),
    ("Hospital_Nightmares_Ending_Box_Mask",
     "Boxes stacked in the kitchen."),
    ("Hospital_Nightmares_Ending_Glass_Mask",
     "Glass jars and bottles in the kitchen."),
    ("Hospital_Nightmares_Ending_Metal_Mask",
     "Metal pans and fittings in the kitchen."),
    ("Hospital_Nightmares_Ending_Stove_Mask",
     "The cooker in the kitchen."),
    ("Hospital_Nightmares_Ending_Wood_Mask",
     "The wooden kitchen units."),
    ("HospitalNF_Hall_Window_Mask",
     "A window bricked up from the outside."),
    ("HospitalNF_Hall_Window_Wall_Mask",
     "A window bricked up from the outside."),
    ("HospitalNF_Hall_Wood_Mask",
     "Wooden furniture in the room."),
    ("HospitalNF_Room_03_Bed_Mask",
     "A hospital bed with its side rails up."),
    ("HospitalNF_Room_03_Brick_Mask",
     "A loose brick."),
    ("HospitalNF_Room_03_Broom_Mask",
     "A broom standing in a bucket."),
    ("HospitalNF_Room_03_Bucket_Mask",
     "A bucket with a mop in it."),
    ("HospitalNF_Room_03_Cardboard_Mask",
     "A wooden crate."),
    ("HospitalNF_Room_03_Glass",
     "A cabinet with its glass smashed."),
    ("HospitalNF_Room_03_Rag_Mask",
     "A rag hanging over the bucket."),
    ("HospitalNF_Room_03_Rung_02",
     "A short wooden batten."),
    ("HospitalNF_Room_03_Stick_Mask",
     "A mop handle standing in the bucket."),
    ("HospitalNF_Room_03_Window_Mask",
     "A window with the glass gone."),
    ("HospitalNF_Room_03_Wire_Mask",
     "A tangle of wire on the wall."),
    ("HospitalNF_Window_Cover_Mask",
     "A board nailed over the window."),
    ("HospitalNF_Room_14_Plank_Mask",
     "Planks nailed across the window."),
    ("HospitalNF_Room_14_Window_Mask",
     "Planks nailed across the window."),
    ("HospitalNF_Room_15_Cyclops_Poster",
     "A note pinned to the wall."),
    ("HospitalNF_Room_15_Metal_Mask",
     "An operating table under a surgical lamp."),
    ("Hospital_Card_Blank_Mask",
     "A card clipped to the end of the bed."),
    ("Hospital_Doll_Info_Mask",
     "A card clipped to the end of the bed."),
    ("Hospital_Monster_Bird_Info_Mask",
     "A card clipped to the end of the bed."),
    ("Hospital_Patient_14_Card_Mask",
     "A card clipped to the end of the bed."),
    ("Hospital_Player_Card_Mask",
     "A card clipped to the end of the bed."),
    ("Hospital_Patient_Card_Info_Mask",
     "A card clipped to the end of the bed."),
    ("Hospital_Teddy_Card_Mask",
     "A card clipped to the end of the bed."),
    ("Hospital_Room_14_Note",
     "A note pinned to the wall by the bed."),
    ("Hospital_Room_14_Plastic_Mask",
     "Bottles on the bedside table."),
    ("Hospital_Room_14_Thing_Mask",
     "A jug tipped over on the floor."),
    ("Hospital_Room_15_Bed_Mask",
     "A hospital bed with its side rails up."),
    ("Hospital_Room_15_Bed_Mask_02",
     "A hospital bed with its side rails up."),
    ("Hospital_Room_15_Bottle_Mask",
     "A bottle and a cup on the table."),
    ("Hospital_Room_15_Glass_Mask",
     "A cup on the table."),
    ("Hospital_Room_Teddy",
     "A teddy bear tucked into the bed."),
    ("Hospital_Window_Stone",
     "The window over the bed."),
    ("HospitalB_Room_15_Glass_Mask",
     "The window over the bed."),
    ("HospitalB_Room_15_Window_Mask",
     "The window over the bed."),
    ("HospitalB_Room_15_IV_Mask",
     "A drip bag hanging from its stand."),
    ("HospitalB_Room_15_Monitor_Mask",
     "A monitor on a bracket above the bed."),
    ("HospitalB_Room_15_Wood_Mask",
     "A wooden table and stool by the bed."),
    ("HospitalB_Room_15_Metal_Mask",
     "The metal frame of the hospital bed."),
    ("HospitalB_Room_15_Metal_02_Mask",
     "The metal frame of the hospital bed."),
    ("EndingB_Room_14_Metal_Mask",
     "The metal frame of the hospital bed."),
    ("EndingB_Room_15_Metal_Mask",
     "The metal frame of the hospital bed."),
    ("EndingB_Room_15_Metal_Pole_Mask",
     "A drip stand beside the bed."),
    ("EndingB_Room_15_Monitor_Mask",
     "A monitor on a bracket above the bed."),
    ("EndingB_Room_15_Switch_Mask",
     "A light switch on the wall."),
    ("EndingB_Room_14_Switch_Mask",
     "A light switch on the wall."),
    ("EndingB_Room_15_Wood_Mask",
     "A wooden table and stool by the bed."),
    ("EndingB_Room_14_Window_Mask",
     "The room window."),
    ("EndingB_Room_14_Window_Mask_02",
     "The room window."),
    ("HospitalB_Room_15_Table_Mask",
     "The bedside table with a bottle and a cup on it."),
    ("HospitalB_Room_15_Table_Bottle_Mask",
     "A bottle and a cup on the table."),
    ("HospitalB_Room_15_Table_Box_Mask",
     "A box on the table."),
    ("HospitalB_Room_15_Table_Chips_Mask",
     "A packet on the table."),
    ("HospitalB_Ruins_Bottle_Mask",
     "A bottle among the ruins."),
    ("HospitalB_Ruins_Mask",
     "The burnt-out shell of a building."),
    ("HospitalB_Ruins_Trash_Mask",
     "Rubbish among the ruins."),
    ("HospitalB_Ruins_Wall",
     "A broken wall of the ruin."),
    ("HospitalB_Ruins_Wood_Mask",
     "Charred timbers in the ruin."),
    ("HospitalB_Ruins_Inside_Bottle_Mask",
     "A bottle among the rubble."),
    ("HospitalB_Ruins_Inside_Planks",
     "Planks leaning among the rubble."),
    ("HospitalB_Ruins_Inside_Stone_Mask",
     "Broken stone among the rubble."),
    ("HospitalB_Ruins_Inside_Trash_Mask",
     "Rubbish among the rubble."),
    ("HospitalB_Ruins_Inside_Wall_Mask",
     "A broken wall."),
    ("HospitalB_Ruins_Shoe_Mask",
     "A trainer lying among the rubble."),
    ("HospitalB_WC_Curtain_Mask",
     "A shower curtain on its rail."),
    ("HospitalB_WC_Mirror_Mask",
     "A cracked mirror over the basin."),
    ("EndingB_WC_Mirror_Mask",
     "A cracked mirror over the basin."),
    ("HospitalB_WC_Outflow_Mask",
     "A drain set in the washroom floor."),
    ("HospitalB_WC_Pipe_Mask",
     "Pipes running along the washroom wall."),
    ("HospitalB_WC_Plastic_Stuff_Mask",
     "A toilet brush beside the pan."),
    ("HospitalB_WC_Shower_Mask",
     "A shower hose hanging on the wall."),
    ("HospitalB_WC_Shower_02_Mask",
     "A shower head on the wall."),
    ("HospitalB_WC_Sink_Mask",
     "A washbasin with a tap."),
    ("HospitalB_WC_Wall_Mask",
     "The tiled washroom wall."),
    ("HospitalB_WC_Water_Container",
     "A basin of water."),
    ("Hospital_WC_Sink",
     "A washbasin with dark stains in it."),
    ("Hospital_WC_Slime",
     "Slime running down the wall."),
    ("Hospital_B_WC_Leech",
     "A leech clinging to the wall."),
    ("Hospital_B_WC_Leech_02",
     "A leech clinging to the wall."),
    ("Hospital_B_WC_Leech_03",
     "A leech clinging to the wall."),
    ("Hospital_B_WC_Leech_04",
     "A leech clinging to the wall."),
    ("Hospital_B_WC_Leech_05",
     "A leech clinging to the wall."),
    ("Hospital_B_WC_Leech_06",
     "A leech clinging to the wall."),
    ("EndingB_WC_Curtain_Rod",
     "A curtain rail with rings on it."),
    ("EndingB_WC_Curtain_Rod_02",
     "A curtain rail with rings on it."),
    ("EndingB_WC_Metal_Mask",
     "Metal fittings on the washroom wall."),
    ("EndingB_WC_Plastic_Mask",
     "A toilet brush beside the pan."),
    ("EndingB_Patient_Waiting",
     "A gaunt bald patient standing with his arms folded."),
    ("EndingB_Waiting_Wall_Mask",
     "The waiting room wall."),
    ("HospitalB_Waiting_Bench_Mask",
     "A wooden bench in the waiting room."),
    ("HospitalB_Waiting_Door_Mask",
     "The waiting room door."),
    ("HospitalB_Waiting_Wall_Mask",
     "A notice on the waiting room wall."),
    ("Hospital_Waiting_Bottle",
     "A bottle standing on the floor."),
    ("Hospital_Waiting_Note",
     "A note pinned to the wall."),
    ("Hospital_Waiting_Ticket",
     "A ticket dispenser on the wall."),
    ("HospitalB_Reception_Plate_Mask",
     "A plate left on the reception desk."),
    ("Ending_Hospital_Hall_Arrow",
     "A direction sign with an arrow on the corridor wall."),
    ("Ending_Hospital_Hall_Picture",
     "A framed picture on the corridor wall."),
    ("Ending_Hospital_Hall_Leafs",
     "The leaves of a potted plant."),
    ("Ending_Hospital_Hall_Feafs_02",
     "The leaves of a potted plant."),
    ("Ending_Hospital_Hall_02_Leaf",
     "The leaves of a potted plant."),
    ("Ending_Hospital_Hall_Pot",
     "A plant pot standing on the corridor floor."),
    ("Ending_Hospital_Hall_Pot_02",
     "A plant pot standing on the corridor floor."),
    ("Ending_Hospital_Hall_02_Pot",
     "A plant pot standing on the corridor floor."),
    ("Ending_Hospital_Hall_03_Pot",
     "A plant pot standing on the corridor floor."),
    ("Hospital_Hall_Pot_Ground",
     "A plant pot on the floor."),
    ("Hospital_Hall_Pot_Ground_02",
     "A plant pot on the floor."),
    ("Ending_Hospital_Hall_03_Tree",
     "A small potted tree."),
    ("Ending_Hospital_Nurse_Room_Cloth_Mask",
     "A coat hanging on a hook."),
    ("Ending_Hospital_Nurse_Room_Mug",
     "A mug standing on the table."),
    ("Ending_Hospital_Nurse_Room_Plastic_Mask",
     "A plastic bottle on the table."),
    ("Ending_Hospital_Nurse_Room_Plastic_Mask_02",
     "A plastic bottle on the table."),
    ("Ending_Hospital_Nurse_Room_Shoes",
     "A pair of shoes on the floor."),
    ("Ending_Hospital_Nurse_Room_Wood_Mask",
     "A wooden table and chair."),
    ("Ending_Hospital_Room_15_Wood_Mask",
     "Wooden furniture in the room."),
    ("Flat_B_Bathroom_Sink",
     "A washbasin with a tap."),
    ("Flat_Bathroom_Toilet_Sludge",
     "Dark sludge spattered round the pan."),
    ("Flat_Bathroom_Sink_Mask",
     "The washbasin, with the toilet beside it."),
    ("Flat_Bathroom_Toilet_Mask",
     "The toilet, its lid up."),
    ("Flat_Bathroom_Toilet_02_Mask",
     "The toilet, its lid up."),
    ("Flat_Bathroom_Toilet_Paper_Mask",
     "A toilet roll on its holder."),
    ("Flat_Bathroom_Cupboard_Mask",
     "A wall cupboard above the basin."),
    ("Flat_Bathroom_Faucet_Mask",
     "The tap over the basin."),
    ("Flat_Bathroom_Pipes_Mask",
     "Pipes running up the bathroom wall."),
    ("Flat_Bathroom_Plastic_Mask",
     "The washbasin and its pipework."),
    ("Flat_Basement_Pipes_Mask",
     "Pipes running along the cellar wall."),
    ("Flat_WC_Germs",
     "A yellow stain spreading across the floor."),
    ("Flat_WC_Germs_02",
     "A yellow stain on the floor."),
    ("Flat_Entrance_Germs",
     "A yellow stain on the entrance floor."),
    ("FlatB_Hall_Light_Switch_Mask",
     "A light switch on the wall."),
    ("Flat_Hall_Light_Switch_Mask",
     "A light switch on the wall."),
    ("Flat_B_Hall_Door_Note",
     "A note taped to the door."),
    ("Flat_Hall_Carpet_Mask",
     "A rug on the hall floor."),
    ("Flat_Hall_Cupboard_Mask",
     "A cupboard in the hall."),
    ("Flat_Hall_Door_Frame_Mask",
     "A door frame."),
    ("Flat_Hall_Fuse_Box_Mask",
     "A fuse box on the wall."),
    ("Flat_Hall_Lamp_Mask_02",
     "A domed lamp hanging from the ceiling."),
    ("Flat_Hall_Mirror_Broken",
     "A mirror, its glass shattered."),
    ("Flat_Hall_Mirror_Broken_02",
     "A mirror, its glass shattered."),
    ("Flat_Hall_Mirror_Sound_Mask",
     "The mirror in its frame."),
    ("Flat_Kitchen_Cupboard_Door_Mask",
     "A cupboard door."),
    ("Flat_Kitchen_Curtain_Rod_Mask",
     "The curtain rail above the window."),
    ("Flat_Kitchen_Faucet",
     "The kitchen tap over the sink."),
    ("Flat_Kitchen_Freazer_Mask",
     "The fridge freezer."),
    ("Flat_Kitchen_Furniture_Mask",
     "The kitchen units and worktop."),
    ("Flat_Kitchen_Glasses_Mask",
     "Glasses standing in the cupboard."),
    ("Flat_Kitchen_Radiator_Mask",
     "A radiator under the window."),
    ("Flat_Kitchen_Sink_Mask",
     "The kitchen sink and draining board."),
    ("Flat_Kitchen_Stove_Mask",
     "The cooker, with an oven under it."),
    ("Flat_Kitchen_Table_Mask",
     "The kitchen table and its chairs."),
    ("Flat_Kitchen_Thing_Mask",
     "A rack of hooks on the wall."),
    ("Flat_Kitchen_Window_Frame_Mask",
     "The kitchen window frame."),
    ("Flat_Kitchen_Window_Mask",
     "The kitchen window."),
    ("Flat_Outside_Mailbox_Mask",
     "A mailbox on the wall by the door."),
    ("Flat_Outside_Windows_Mask",
     "The windows of the block opposite."),
    ("Flat_Trash_Mask",
     "A dustbin outside the block."),
    ("Flat_Trash_Mask_02",
     "A dustbin outside the block."),
    ("Flat_Room_Carpet_Mask",
     "A rug on the floor."),
    ("Flat_Room_Curtain_Rod",
     "The curtain rail above the window."),
    ("Flat_Room_Furniture_Mask",
     "A tall wardrobe with drawers under it."),
    ("Flat_Room_Pipe_Mask",
     "A pipe running up the wall."),
    ("Flat_Room_Radiator_Mask",
     "A radiator under the window."),
    ("Flat_Room_Socket_Mask",
     "A power socket on the skirting board."),
    ("Flat_Room_Window_Frame_Mask",
     "The window frame."),
    ("Flat_Staircase_Stairs_Mask",
     "The stairs running up through the block."),
    ("Flat_Staircase_Door_Frame_Mask",
     "The frame of the flat door."),
    ("Flat_Staircase_Door_Mat_02",
     "A doormat outside the flat door."),
    ("Flat_Staircase_Door_Spider",
     "A spider on the wall by the door."),
    ("Flat_Entrance_Door_Frame_Mask",
     "The frame of the entrance door."),
    ("Flat_Entrance_Mailbox_Mask",
     "A bank of mailboxes in the entrance."),
    ("Flat_Start_Garbage_Mask",
     "A bag of rubbish on the ground."),
    ("Flat_Start_Tree_Mask",
     "A tree beside the path."),
    ("Flat_Room_02_Carpet_Mask",
     "A rug on the floor."),
    ("Flat_Room_02_Curtain_Rod",
     "The curtain rail above the window."),
    ("Flat_Room_02_Furniutre_Mask",
     "A wall of cupboards and shelves."),
    ("Flat_Room_02_Handle_Mask",
     "A door handle."),
    ("Flat_Room_02_Lamp_Mask",
     "A shaded lamp."),
    ("Flat_Room_02_Lamp_02_Mask",
     "A lamp hanging from the ceiling."),
    ("Flat_Room_02_Radiator_Mask",
     "A radiator under the window."),
    ("Flat_Room_02_Socket_Mask",
     "A power socket on the wall."),
    ("Flat_Room_02_TV_Mask",
     "A television set."),
    ("Flat_Room_02_TV_Mask_02",
     "A television screen."),
    ("Flat_Room_02_Window_Frame_Mask",
     "The window frame."),
    ("Ending_Flat_Hall_Picture_Mask",
     "A framed picture on the hall wall."),
    ("Ending_Flat_Picture_Mask_02",
     "A framed picture on the hall wall."),
    ("Ending_Flat_Room_Drawing_Mask",
     "A child's drawing pinned to the wall."),
    ("Ending_Flat_Room_Window_Broken_Mask",
     "A broken pane in the window."),
    ("Ending_Flat_Room_Window_Broken_Mask_02",
     "A broken pane in the window."),
    ("Ending_Flat_Room_Window_Broken_Mask_03",
     "A broken pane in the window."),
    ("EndingB_Memories_Hall_Picture_Fram",
     "A picture frame on the wall."),
    ("Memories_Basement_Candle_Mask",
     "A candle burning in the cellar."),
    ("Memories_Basement_Coffe_Mask",
     "A tin marked Coffee."),
    ("Memories_Basement_Glass_Mask",
     "Jars standing on the cellar shelf."),
    ("Memories_Basement_Glass_Mask_02",
     "Jars standing on the cellar shelf."),
    ("Memories_Basement_Grave",
     "A mound of earth in the cellar floor."),
    ("Memories_Basement_Metal_Mask",
     "Metal shelving in the cellar."),
    ("Memories_Basement_Wood_Mask",
     "Wooden shelving in the cellar."),
    ("Memories_Hall_Paper",
     "Papers scattered over the hall floor."),
    ("Memories_Hall_Robot",
     "A tin toy robot."),
    ("Memories_Hall_TV_Mask",
     "A television lying smashed on the floor."),
    ("Memories_Hall_TV_Mask_02",
     "A television lying smashed on the floor."),
    ("Memories_Hall_TV_Mask_03",
     "A television lying smashed on the floor."),
    ("Memories_Hall_TV_Mask_04",
     "A television lying smashed on the floor."),
    ("Memories_Hall_Teddy",
     "A teddy bear on the floor with its stuffing coming out."),
    ("Memories_Hall_Wood_Mask",
     "Broken wooden furniture in the hall."),
    ("Memories_Hall_Wood_Mask_02",
     "Broken wooden furniture in the hall."),
    ("Memories_Portrait_Mask",
     "A picture frame hanging crooked on the wall."),
    ("Memories_Kitchen_Doll",
     "A baby doll sitting on the floor."),
    ("Memories_Kitchen_Freazer_Mask",
     "The fridge, its door hanging open."),
    ("Memories_Kitchen_Freazer_Mask_02",
     "The fridge, its door hanging open."),
    ("Memories_Kitchen_Glass_Mask",
     "Broken glass in the kitchen window."),
    ("Memories_Kitchen_Portrait_Mask",
     "A picture frame on the kitchen wall."),
    ("Memories_Kitchen_Wood_Mask",
     "Broken wooden units in the kitchen."),
    ("Memories_Kitchen_Wood_Mask_02",
     "Broken wooden units in the kitchen."),
    ("Memories_Bottle_Broken",
     "A broken bottle in the grass."),
    ("Memories_Outside_Ball_Mask",
     "A ball lying in the grass."),
    ("Memories_Outside_Dog_House",
     "A kennel with a dark opening."),
    ("Memories_Outside_Glass_Mask",
     "The windows of the house."),
    ("Memories_Outside_House_Mask",
     "The front of the house."),
    ("Memories_Outside_Mailbox_Mask",
     "A mailbox on a post."),
    ("Memories_Outside_Roof_Mask",
     "The tiled roof of the house."),
    ("Memories_Outside_Stone_Mask",
     "A stone in the grass."),
    ("Memories_Outside_Tire_Mask",
     "A car tyre lying in the grass."),
    ("Memories_Outside_Trash_Mask",
     "Rubbish dropped in the grass."),
    ("Memories_Outside_Water_Mask",
     "A water trough beside the house."),
    ("Memories_Outside_Wood_Mask",
     "The wooden porch of the house."),
    ("Memories_Outside_Wood_Mask_02",
     "A wooden bench by the house."),
    ("Memories_Outside_Wood_Mask_03",
     "The wooden porch of the house."),
    ("Memories_Lake_Fishing_Rod_Mask",
     "A fishing rod propped at the water's edge."),
    ("Memories_Carpet_02",
     "A rug rolled up on the floor."),
    ("Memories_Room_Clown",
     "A clown doll lying on the floor."),
    ("Memories_Room_Mothers_Day_Mask",
     "A drawing pinned to the wall."),
    ("Memories_Room_Parents_Bed_Mask",
     "The parents' bed."),
    ("Memories_Room_Parents_Curtain_Mask",
     "A curtain hanging at the window."),
    ("Memories_Room_Parents_Curtain_Rod_Mask",
     "The curtain rail."),
    ("Memories_Room_Parents_Lamp_Mask",
     "A shaded lamp."),
    ("Memories_Room_Parents_Lamp_Mask_02",
     "A shaded lamp."),
    ("Memories_Room_Parents_Wood_Mask",
     "Wooden furniture in the parents' room."),
    ("Memories_Room_Teddy",
     "A teddy bear lying on the floor, torn open."),
    ("Memories_Tacks_02",
     "A scatter of tacks on the floor."),
    ("Memories_Room_Books_Mask",
     "Books scattered over the floor."),
    ("Memories_Room_Car_Mask",
     "A toy car on the floor."),
    ("Memories_Room_Glass_Mask",
     "The window of the kid's room."),
    ("Memories_Room_Kid_Doll",
     "A doll lying face down on the floor."),
    ("Memories_Room_Kid_Monkey",
     "A toy monkey."),
    ("Memories_Room_Wheel_Mask",
     "A wheel off a toy, lying on the floor."),
    ("Memories_Room_Wood_Mask",
     "Broken wooden furniture in the room."),
    ("Memories_Toilet_Can_Mask",
     "A can on the outhouse floor."),
    ("Memories_Toilet_Drawing",
     "A drawing pinned up inside the outhouse."),
    ("Memories_Toilet_Stone_Mask",
     "A stone by the outhouse."),
    ("Memories_Toilet_Wood_Mask",
     "The plank walls of the outhouse."),
    ("Memories_Toilet_Wood_Mask_02",
     "The plank walls of the outhouse."),
    ("EndingB_Room_14_Wood_Mask",
     "Wooden furniture in the room."),
    ("HospitalB_Waiting_Ticket_Mask_02",
     "A queue ticket lying on the floor."),
    ("EndingB_Waiting_Ticket_Mask",
     "A queue ticket lying on the floor."),
    ("EndingB_Waiting_Bottle_Mask",
     "A bottle on the waiting room floor."),
    ("Hospital_Room_15_Switch_Mask",
     "A light switch on the wall."),
    ("HospitalB_WC_Toilet_Mask_02",
     "The toilet."),
    ("Hospital_WC_Paper_Mask",
     "A toilet roll on its holder."),
    ("Memories_Kitchen_Pipe_Mask",
     "A pipe running along the kitchen wall."),
    ("Memories_Kitchen_Radiator_Mask",
     "A radiator on the kitchen wall."),
    ("Forest_House_Inside_Knife_Blade_Mask",
     "The blade of a knife."),
    ("Forest_House_Inside_Knife_Handle_Mask",
     "The handle of a knife."),
    ("Flat_Bathroom_Wood_Mask",
     "Wooden fittings in the bathroom."),
    ("Flat_Room_Bed_Mask",
     "A bed against the wall."),
    ("FlatB_Hall_Pictures_Mask",
     "Framed pictures on the hall wall."),
    ("EndingB_Crypt_Inside_Metal_Mask",
     "Metal fittings inside the crypt."),
    ("EndingB_Crypt_Inside_Sponge_Mask",
     "A sponge on the crypt floor."),
    ("Graveyard_Lady_Stone_Mask",
     "A stone beside the path."),
    ("Graveyard_Grave_Stone_Mask",
     "A stone in the grass by the grave."),
    ("Graveyard_Gravedigger_Wood_Mask",
     "Wooden shelving in the hut."),
    ("EndingB_Flower_Metal_Mask",
     "A scrap of metal in the grass."),
    ("Ending_Hosital_Janitor_Metal_Mask",
     "Metal shelving in the store room."),
    ("Ending_Hospital_Janitor_Board_Mask",
     "A board leaning in the store room."),
    ("Ending_Hospital_Nurse_Room_Box_Mask",
     "A box in the nurse's room."),
    ("HospitalNF_Room_14_Plastic_Mask",
     "Plastic bottles in the room."),
};

// Families rather than objects. Roughly half of everything drawn in this game is numbered
// decoration - Graveyard_Gate_Wall_Break_01 to _08, Bridge_02_Walk_Break_01 to _03,
// Crypt_Wall_Part_01 to _09 - and they really are all the same picture of a crack or a
// chip. One sentence each, applied by name to anything the table above does not name
// explicitly, is honest and is what a sighted player gets from them.
var sceneryFamilies = new (string rx, string text)[]
{
    (@"walk_break|wall_break|road_break|staircase_wall_break",
     "A long crack running through the concrete."),
    (@"wall_broken",
     "A hole where a stretch of wall has come down."),
    (@"road_part|walk_part|wall_part|grave_part|sidewalk_stone|wall_thing|wall_element",
     "A broken chip of concrete lying loose."),
    (@"bird_dead|dead_bird",
     "A dead bird lying on its side, wings splayed, drawn in heavy black."),
    (@"candle_night",
     "A candle burning, the flame drawn as a small spike of light."),
    (@"grave_debris",
     "A patch of scratched, scuffed ground."),
};


var sceneryLines = new System.Text.StringBuilder();
var sceneryNamed = new HashSet<string>();
int nScenery = 0;
foreach (var row in sceneryRows)
{
    if (Data.GameObjects.ByName(row.obj) == null)
    {
        Console.WriteLine($"  scenery row dropped, no object: {row.obj}");
        continue;
    }
    if (!sceneryNamed.Add(row.obj)) continue;
    sceneryLines.Append("    ds_map_add(a11y_scn, " + row.obj + ", \"" + row.text + "\");\n");
    nScenery++;
}
int nSceneryFam = 0;
foreach (var fam in sceneryFamilies)
{
    var rx = new System.Text.RegularExpressions.Regex(fam.rx,
                 System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    foreach (var o in Data.GameObjects)
    {
        var fn = o.Name?.Content;
        if (!SpeakableName(fn)) continue;
        if (!DescendsFromInteractive(o)) continue;
        if (sceneryNamed.Contains(fn)) continue;
        if (!rx.IsMatch(fn)) continue;
        sceneryNamed.Add(fn);
        sceneryLines.Append("    ds_map_add(a11y_scn, " + fn + ", \"" + fam.text + "\");\n");
        nSceneryFam++;
    }
}
Console.WriteLine($"Scenery objects with a written description: {nScenery} named, " +
                  $"{nSceneryFam} by family.");




// ---- switches, valves, curtains and drawers ------------------------------
//
// A switch tells you nothing about what it did. Its hover is the same hand cursor whether
// the thing it controls is on or off, and the state itself lives on a DIFFERENT object -
// the hall light switch writes Hospital_Hall_Light_02.active, the shower valve writes
// Hospital_WC_Shower.active - so there is no way to find it from the switch at runtime.
//
// The pairs are derived by gscripts/find_toggles.py, which reads the decompiled dump for
// the shapes a toggle takes here: 'if (V) V = 0; else V = 1;' and 'V = !V;', plain, dotted
// or inside a with block. 21 in the whole game. Re-run it after a game update and diff.
//
// A baked table can go stale, so every row is checked below before it is emitted: both
// objects must exist, and the target must really set the variable in its own Create -
// except 'visible', which is a builtin every instance has. A row that fails is dropped and
// reported rather than compiled into a read that would crash the game.
//
// Wording is per row on purpose. The variable is 'active' for a light, a billboard, a
// curtain and a running shower, and one phrase cannot honestly cover all four.
var switchRows = new (string sw, string tgt, string var_, string on, string off)[]
{
    ("Bridge_Light_Billboard",            "Bridge_01_Sign_Light",     "active",  "lit",     "dark"),
    ("Flat_Hall_Light_Switch_Mask",       "Flat_Hall_Light",          "active",  "on",      "off"),
    ("FlatB_Hall_Light_Switch_Mask",      "Flat_Light",               "active",  "on",      "off"),
    ("HospitalNF_Surgery_Light_Switch",   "HospitalNF_Light_Surgery", "active",  "on",      "off"),
    ("Ending_Hospital_Nurse_Room_Lights", "Ending_Good_Light",        "active",  "on",      "off"),
    ("Flat_Kitchen_Curtain",              "Flat_Light",               "active",  "open",    "closed"),
    ("Flat_B_Kitchen_Curtain",            "Flat_Light",               "active",  "open",    "closed"),
    ("Flat_Room_Curtain",                 "Flat_Light",               "active",  "open",    "closed"),
    ("Flat_Room_02_Curtain",              "Flat_Light",               "active",  "open",    "closed"),
    ("Flat_B_Room_02_Curtain",            "Flat_Light",               "active",  "open",    "closed"),
    ("Ending_Flat_Kitchen_Curtain",       "Ending_Good_Light",        "active",  "open",    "closed"),
    ("Ending_Flat_Room_Curtain",          "Ending_Good_Light",        "active",  "open",    "closed"),
    ("Hospital_WC_Shower_Valve",          "Hospital_WC_Shower",       "active",  "running", "off"),
    ("Hospital_B_WC_Shower_Valve",        "Hospital_WC_Shower",       "active",  "running", "off"),
    ("Hospital_Reception_Drawer",         null,                       "visible", "open",    "closed"),
    ("Hospital_Soda_Ventilator",          null,                       "visible", "open",    "closed"),

    // The two hall windows you can actually open. They keep no variable at all - the
    // press just swaps image_index between the open and the shut frame - so the toggle
    // scan cannot find them and the wording has to be inverted: frame 0 is OPEN. That is
    // not a guess. Hospital_B_Number_Window only lets the ticket blow out of the building
    // while HospitalB_Hall_03_Window.image_index is 0, and the close handler is the branch
    // that stops the city traffic playing.
    ("Hospital_Hall_03_Window_Open",      null,                  "image_index", "closed",  "open"),
    ("HospitalB_Hall_03_Window",          null,                  "image_index", "closed",  "open"),
};

var switchLines = new System.Text.StringBuilder();
int nSwitch = 0;
foreach (var row in switchRows)
{
    var swObj = Data.GameObjects.ByName(row.sw);
    if (swObj == null) { Console.WriteLine($"  switch row dropped, no object: {row.sw}"); continue; }

    var holder = row.tgt == null ? swObj : Data.GameObjects.ByName(row.tgt);
    if (holder == null) { Console.WriteLine($"  switch row dropped, no target: {row.tgt}"); continue; }

    // 'visible' and 'image_index' are builtins that every instance has, so neither can
    // be checked by looking for an assignment in a Create - and neither ever needs to be.
    if (row.var_ != "visible" && row.var_ != "image_index" && !HasSelfVar(holder, row.var_))
    {
        Console.WriteLine($"  switch row dropped, {holder.Name.Content} does not set {row.var_}");
        continue;
    }

    // Read through a11y_sw, an instance the row fetches for itself, so the guard is on the
    // INSTANCE and not on the object - the same rule as everywhere else in this patch,
    // because instance_exists and instance_find disagree about a deactivated instance.
    // These rows only run inside the world reader, which never runs while a Pause exists,
    // so here it is belt and braces; it costs one assignment and closes the whole class.
    string read = row.tgt == null
        ? "a_wt2." + row.var_
        : "a11y_sw." + row.var_;
    string guard = row.tgt == null
        ? "1"
        : "instance_exists(a11y_sw)";
    string fetch = row.tgt == null
        ? ""
        : "                    a11y_sw = instance_find(" + row.tgt + ", 0);\n";

    switchLines.Append(
        fetch +
        "                    if (a_wnr == \"" + row.sw + "\" && " + guard + ")\n" +
        "                    {\n" +
        "                        if (" + read + ")\n" +
        "                            a_wl += \", " + row.on + "\";\n" +
        "                        else\n" +
        "                            a_wl += \", " + row.off + "\";\n" +
        "                    }\n");
    nSwitch++;
}
Console.WriteLine($"Switches that now say their state: {nSwitch} of {switchRows.Length}.");

// Which objects PICK SOMETHING UP. The cursor art cannot tell the difference: the hand
// cursor covers taking an item, pulling a lever, opening a drawer and shaking a scarecrow,
// so everything read as "use". The press handler can - taking an item is the one that calls
// _item_add. Walks the parent chain, since a child with no handler inherits one.
UndertaleCode PressCodeFor(UndertaleGameObject o)
{
    for (var p = o; p != null; p = p.ParentId)
    {
        var lst = p.Events[(int)EventType.Other];
        if (lst == null) continue;
        foreach (var ev in lst)
            if (ev.EventSubtype == 10 && ev.Actions.Count > 0)
                return ev.Actions[0].CodeId;
    }
    return null;
}

var pickLines = new System.Text.StringBuilder();
int nPick = 0;
foreach (var o in Data.GameObjects)
{
    if (o == interactiveObj || !DescendsFromInteractive(o)) continue;
    var pkn = o.Name?.Content;
    if (string.IsNullOrEmpty(pkn)) continue;
    if (!System.Text.RegularExpressions.Regex.IsMatch(pkn, "^[A-Za-z_][A-Za-z0-9_]*$")) continue;

    var press = PressCodeFor(o);
    if (press?.Instructions == null) continue;
    bool adds = false;
    foreach (var ins in press.Instructions)
        if (ins.ValueFunction?.Name?.Content == "_item_add") { adds = true; break; }
    if (!adds) continue;

    pickLines.Append("    ds_map_add(a11y_pick, " + pkn + ", 1);\n");
    nPick++;
}
Console.WriteLine($"Objects that pick something up: {nPick}.");

// Pure hazards: a press that wounds you and gives nothing back. The scythe blade in the
// crypt is the type case - _dmg(0.5) the first time, a scraping noise every time after,
// and no state set anywhere. It has no hover handler either, so it lands in Scenery
// reading as an ordinary name, and a sighted player has the one thing this does not: a
// picture of a rusty blade.
//
// Anything that also calls _item_add is EXCLUDED. Graveyard_Flower wounds you only when
// you pick it bare-handed, and it is a thing you are meant to take - warning about it
// every time would be wrong.
// Hover handlers that DESTROY the instance they were asked about.
//
// The label probe below runs the game's own hover to find out what a thing is for, which
// is normally free of consequences - it sets a cursor and maybe an info string. Not
// always. Bridge_01_Bird_Mask, the birds on the bridge sign, has no press handler
// whatsoever: its hover spawns the birds, plays their wings and destroys itself. Asking
// it what it is makes it cease to exist, and every line of label building after that
// dereferences an instance that has gone.
//
// Found by reading the hover code of the object and its parents, and one level into any
// script it calls, for instance_destroy. Those objects are not probed at all; they read
// out under their own name, and the pointer warp that follows still puts the real mouse
// on them, so the game's own hover fires in its own End Step and the birds still scatter.
// The world keeps working - it just is not this patch that pulls the trigger, in the
// middle of reading a label off the thing.
var noHoverLines = new System.Text.StringBuilder();
int nNoHover = 0;
foreach (var o in Data.GameObjects)
{
    if (o == interactiveObj || !DescendsFromInteractive(o)) continue;
    var dn = o.Name?.Content;
    if (!SpeakableName(dn)) continue;

    var hv = HoverCodeFor(o);
    if (hv?.Instructions == null) continue;

    bool kills = false;
    foreach (var ins in hv.Instructions)
    {
        var f = ins.ValueFunction?.Name?.Content;
        if (f == null) continue;
        if (f == "instance_destroy") { kills = true; break; }

        var sc = Data.Scripts.ByName(f);
        var scc = sc?.Code;
        if (scc?.Instructions == null) continue;
        foreach (var i2 in scc.Instructions)
            if (i2.ValueFunction?.Name?.Content == "instance_destroy") { kills = true; break; }
        if (kills) break;
    }
    if (!kills) continue;

    noHoverLines.Append("    ds_map_add(a11y_nohover, " + dn + ", 1);\n");
    nNoHover++;
}
Console.WriteLine($"Hovers that destroy the object they are asked about: {nNoHover}.");

var hurtLines = new System.Text.StringBuilder();
int nHurt = 0;
foreach (var o in Data.GameObjects)
{
    if (o == interactiveObj || !DescendsFromInteractive(o)) continue;
    var hn = o.Name?.Content;
    if (!SpeakableName(hn)) continue;

    var press = PressCodeFor(o);
    if (press?.Instructions == null) continue;
    bool hurts = false, gives = false;
    foreach (var ins in press.Instructions)
    {
        var f = ins.ValueFunction?.Name?.Content;
        if (f == "_dmg") hurts = true;
        else if (f == "_item_add") gives = true;
    }
    if (!hurts || gives) continue;

    hurtLines.Append("    ds_map_add(a11y_hurt, " + hn + ", 1);\n");
    nHurt++;
}
Console.WriteLine($"Objects that only hurt you: {nHurt}.");

// The pattern has grown well past debris. Three families were reported from play as
// burying the list: the ten Graveyard_Bird_Dead hotspots that appear all at once when the
// second candle is lit, the Graveyard_Candle_Night copies that come with them, and the
// blood, mess and stain hotspots that carpet the later hospital rooms. All of them are
// look-at-only decoration placed in numbered sets, which is exactly what the filter is for.
var clutterRx = new System.Text.RegularExpressions.Regex(
                    "grave_debris|bird_dead|dead_bird|candle_night|blood|stain|" +
                    "_mess|_gore|_piss|_dirt|spider_web|cobweb|_rubble|_debris|" +
                    // The numbered decoration sets. Exporting every drawn hotspot in the
                    // game and looking at it settled what these are: eight to ten copies
                    // per room of one crack in the concrete or one broken-off chip, and
                    // nothing else. They still read out under Everything and they still
                    // have descriptions - see the scenery families - but they have no
                    // business padding the two lists anybody browses.
                    "walk_break|wall_break|road_break|wall_broken|road_part|walk_part|" +
                    "wall_part|grave_part|sidewalk_stone|wall_thing|wall_element",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

// Nothing may be hidden that the player could need. A name matching the pattern is only
// a suggestion; the object is checked the same three ways the rest of this file decides
// what an object is FOR, and any hit disqualifies it:
//
//   * its hover reaches an exit or an action cursor - it is a way out or a usable thing;
//   * its press calls _item_add - it gives you something;
//   * its press calls _check_item - it answers to an item you might be carrying.
//
// Graveyard_Flower_Blood and Bridge_Stain_Blood_Mask both fail that test and stay listed,
// which is the point: 'blood' in a name is a strong hint and never a proof.
bool ClutterSafe(UndertaleGameObject o)
{
    var hover = HoverCodeFor(o);
    if (hover?.Instructions != null)
        foreach (var ins in hover.Instructions)
        {
            var fn = ins.ValueFunction?.Name?.Content;
            if (fn == null) continue;
            if (exitFns.Contains(fn) || actFns.Contains(fn)) return false;
        }
    var press = PressCodeFor(o);
    if (press?.Instructions != null)
        foreach (var ins in press.Instructions)
        {
            var fn = ins.ValueFunction?.Name?.Content;
            if (fn == "_item_add" || fn == "_check_item") return false;
        }
    return true;
}

var clutterLines = new System.Text.StringBuilder();
int nClutter = 0, nClutterKept = 0;
foreach (var o in Data.GameObjects)
{
    var cln = o.Name?.Content;
    if (string.IsNullOrEmpty(cln)) continue;
    if (!System.Text.RegularExpressions.Regex.IsMatch(cln, "^[A-Za-z_][A-Za-z0-9_]*$")) continue;
    if (!clutterRx.IsMatch(cln)) continue;
    if (!DescendsFromInteractive(o)) continue;
    if (!ClutterSafe(o)) { nClutterKept++; continue; }
    clutterLines.Append("    ds_map_add(a11y_clutter, " + cln + ", 1);\n");
    nClutter++;
}
Console.WriteLine($"Clutter objects behind the F2 filter: {nClutter} " +
                  $"({nClutterKept} name matches kept because they are usable).");

// Ambient clutter that fills the scene list without ever being worth visiting. The
// graveyard rooms carry eight Grave_Debris masks each - scratch-mark hotspots whose whole
// handler is a random scrape sound - and there are 60 of them across the chapter.
// Toggleable at runtime rather than removed, because a couple of them do answer to an item.

var stateLines = new System.Text.StringBuilder();
int nState = 0;

var catLines = new System.Text.StringBuilder();
int nExit = 0, nAct = 0, nScen = 0, nJunk = 0;
foreach (var o in Data.GameObjects)
{
    if (o == interactiveObj || !DescendsFromInteractive(o)) continue;
    var oname = o.Name?.Content;
    if (string.IsNullOrEmpty(oname)) continue;
    if (!System.Text.RegularExpressions.Regex.IsMatch(oname, "^[A-Za-z_][A-Za-z0-9_]*$")) continue;

    var hover = HoverCodeFor(o);
    bool isExit = false, isAct = false;
    if (hover?.Instructions != null)
    {
        foreach (var ins in hover.Instructions)
        {
            var fn = ins.ValueFunction?.Name?.Content;
            if (fn == null) continue;
            if (exitFns.Contains(fn)) isExit = true;
            else if (actFns.Contains(fn)) isAct = true;
        }
    }
    if (isExit) { catLines.Append("    ds_map_add(a11y_cat, " + oname + ", 1);\n"); nExit++; }
    else if (isAct) { catLines.Append("    ds_map_add(a11y_cat, " + oname + ", 2);\n"); nAct++; }
    else if (junkRx.IsMatch(oname))
    {
        // Category 3: clutter. Kept out of the Scenery view, still present in Everything
        // so nothing becomes unreachable.
        catLines.Append("    ds_map_add(a11y_cat, " + oname + ", 3);\n");
        nJunk++;
    }
    else nScen++;

    if (KeepsCounter(o))
    {
        stateLines.Append("    ds_map_add(a11y_state, " + oname + ", 1);\n");
        nState++;
    }
}
Console.WriteLine($"Scene categories: {nExit} exits, {nAct} usable, {nScen} scenery, {nJunk} clutter.");
Console.WriteLine($"Objects with a readable counter: {nState}.");

// Shared by Controller's Create AND by the Step's recovery path, because game_load()
// restores instances without re-running Create - see the tick below.
string initBody = @"
    // Clear any previous marker first, so a re-init never leaves two behind.
    with (Worm_February)
        instance_destroy();

    a11y_ready = 0;
    a11y_idx = -1;
    a11y_n = 0;
    a11y_sig = """";
    a11y_last = """";
    a11y_dlg_last = """";
    a11y_chap_last = """";
    a11y_info_last = """";

    // Whether a cutscene was blocking all interaction last frame.
    a11y_cut_last = -1;

    // Inventory reading mode, toggled with I. Off by default so nothing about the
    // game's own controls changes until it is asked for.
    a11y_inv = 0;
    a11y_inv_idx = 0;

    // Scene navigation. The focus is remembered as an INSTANCE, not a position - see the
    // tick for why that matters.
    a11y_w_idx = -1;
    a11y_w_id = 0;
    a11y_w_room = -1;
    a11y_wn = -1;      // how many entries the scene list held last frame
    a11y_sw = -4;      // scratch instance for the generated switch-state reads
    a11y_leech = 0;
    a11y_w_mode = 0;   // 0 everything, 1 exits, 2 usable, 3 scenery

    // What each interactive object is for, decided at patch time - the injector explains
    // at length why asking the objects themselves at runtime is not an option.
    // Anything absent is scenery.
    a11y_cat = ds_map_create();
/*CATEGORIES*/

    // Objects whose 'nbr' is worth reading out - dials and keypad buttons, whose whole
    // state is otherwise conveyed by swapping a sprite.
    a11y_state = ds_map_create();
/*STATEVARS*/

    // Interface buttons to keep out of the scene list, descendants included.
    a11y_skip = ds_map_create();
/*SKIPOBJS*/

    // What to SAY for an object, where its own name reads badly. Generated at patch time;
    // anything absent just has its underscores turned into spaces.
    // Room index -> name. room_get_name has 0 uses in this game, so it is not in the
    // FUNC chunk and cannot be emitted; the table is baked instead.
    // What each picture close-up actually shows, transcribed from the exported sprite.
    a11y_pics = ds_map_create();
/*PICS*/

    a11y_rooms = ds_map_create();
/*ROOMNAMES*/

    a11y_pretty = ds_map_create();
/*PRETTY*/

    // The same names again with the area stripped off the front. Selected by a11y_area,
    // which F1 toggles.
    a11y_short = ds_map_create();
/*SHORTNAMES*/

    // Objects whose press handler calls _item_add, so the hand cursor can be read out as
    // picking something up instead of the catch-all use verb.
    a11y_pick = ds_map_create();
/*PICKOBJS*/

    // Objects whose press wounds you and gives nothing back.
    a11y_hurt = ds_map_create();
/*HURTOBJS*/

    // Objects whose HOVER destroys them, and which therefore must never be probed.
    a11y_nohover = ds_map_create();
/*NOHOVEROBJS*/

    // Ambient clutter, hidden from Objects and Scenery while a11y_junk is on.
    a11y_clutter = ds_map_create();
/*CLUTTEROBJS*/

    // F1: strip the area name from the front of every spoken label. On by default - the
    // area is the same for everything in the room, so it is pure repetition.
    a11y_area = 1;

    // F2: hide the clutter above. On by default.
    a11y_junk = 1;

    // ----- The mod's own settings -----------------------------------------
    //
    // Reachable from a row of our own at the bottom of the title screen and the pause
    // menu - see the @a11y row in the menu scan below.
    //
    // Kept in A11y.ini rather than in the marker, because a preference somebody has set
    // should still be there tomorrow. Its own file rather than a section in the game's
    // Config.ini: GML can only hold one ini file open at a time, and sharing the game's
    // would mean writing to it at moments the game may want it itself.
    a11y_warn = 1;       // say when a thing can hurt you or cost you the good ending
    a11y_hint = 1;       // say what a thing needs, and what to use where
    a11y_set = 0;        // the settings screen is open
    a11y_set_idx = 0;    // which row of it has focus
    a11y_set_hdr = 0;    // say the screen's name before the next row
    a11y_set_wait = 0;   // frames before that row is spoken - see a11y_wpend
    a11y_setsave = 0;    // something changed and the file needs writing
    a11y_autosave = 0;   // whether the autosave caption was up last frame

    ini_open(""A11y.ini"");
    a11y_warn = ini_read_real(""Options"", ""Warnings"", 1);
    a11y_hint = ini_read_real(""Options"", ""Hints"", 1);
    a11y_area = ini_read_real(""Options"", ""AreaNames"", 1);
    a11y_junk = ini_read_real(""Options"", ""HideClutter"", 1);
    ini_close();

    // The fake desktop in Lvl_Computer. Family codes, so the tick knows which variables an
    // object actually has: 1 desktop icon, 2 paint tool, 3 drawing part, 4 colour swatch,
    // 5 save/print/delete. Decided at patch time by scanning each Create - see the injector.
    a11y_comp = ds_map_create();
/*COMPOBJS*/

    // Desktop icons to drop from the list while a window is over them. Games is not in
    // here - it is the way out.
    a11y_desk = ds_map_create();
/*DESKOBJS*/

    // Cutscenes that are just a screen wipe on the way somewhere else.
    a11y_trans = ds_map_create();
/*TRANSOBJS*/

    // What is happening, when something is happening. Object index -> one sentence.
    // The map answers the cutscene reader; the two lists carry the same rows for the
    // sweep that watches for one of them turning up, which a ds_map cannot be walked for.
    //
    // a11y_evon holds whether each row was on screen last frame, one entry per row, in row
    // order. The announcement is the 0-to-1 EDGE, not the presence: an event that sits on
    // screen for a while is therefore heard once, and one that can happen again - the
    // bridge earthquake, the kitchen mask's scream - is heard every time it happens, which
    // is what a sighted player gets.
    //
    // ALL FOUR structures are created BEFORE the generated block, not after. The block
    // fills every one of them, so a create that comes later is a read of an unset variable
    // and kills the game on the Create event - which is exactly what happened when
    // a11y_evon was declared below it.
    a11y_ev = ds_map_create();
    a11y_evo = ds_list_create();
    a11y_evt = ds_list_create();
    a11y_evon = ds_list_create();
/*EVENTOBJS*/

    // The previous value of each watched story flag, one entry per row, in row order.
    // Seeded to 0 rather than to the live value: a save loaded with a flag already set
    // therefore announces it once, which is a reasonable thing to be told on arrival.
    a11y_flgl = ds_list_create();
/*FLAGSEED*/

    // Cutscenes that a keypress can actually dismiss - three of the seventy-four.
    a11y_cutkey = ds_map_create();
/*CUTKEYS*/

    // Item index -> text to append to the game's own localised item name.
    a11y_iname = ds_map_create();
/*ITEMNAMES*/

    // Object index -> what the art shows, for scenery whose name says nothing.
    a11y_scn = ds_map_create();
/*SCENERY*/

    // The last scenery object whose description was read out, so holding Enter on one
    // does not read the same paragraph over and over. Cleared by moving to anything else.
    a11y_scn_id = 0;

    // What a press still has to say, and how many frames until it may say it.
    //
    // A screen reader interrupts itself when Enter is pressed - NVDA has 'Speech
    // interrupt for Enter key' on by default - so anything handed over in the same
    // instant as the keypress is cancelled before it is heard. Arrowing onto an entry
    // was audible and pressing the same entry was silent, which is that rule and nothing
    // in this patch. Held three frames, and past the release of the key.
    a11y_wpend = 0;    // frames remaining, 0 for nothing pending
    a11y_wsay = """";   // a confirmation the press branch wants said first
    a11y_wpost = 0;    // whether the entry itself is re-read after it

    // The film inside the VHS tape: how far the clown has got, and whether the opening
    // titles have been described yet.
    a11y_nm_step = -1;
    a11y_nm_title = 0;

    // How many of the five stars have been collected, so each one can be counted off.
    a11y_stars = -1;

    // Whether the writing on the hospital toilet mirror has shown up yet.
    a11y_mirror = 0;

    // Board-game hazards, which sit on a numbered square and can be crossed out.
    a11y_board = ds_map_create();
/*BOARDOBJS*/

    // Its windows, and whether each carries a second line of body text as well as a title.
    a11y_cwin = ds_map_create();
/*COMPWINS*/

    // Colour names. The paint puzzle is decided entirely by 24-bit colour values and the
    // game never writes one down anywhere, so they get spoken instead. GameMaker packs
    // these as R + G*256 + B*65536, which is why the familiar values look backwards.
    a11y_col = ds_map_create();
    ds_map_add(a11y_col, 16777215, ""white"");
    ds_map_add(a11y_col, 0,        ""black"");
    ds_map_add(a11y_col, 255,      ""red"");
    ds_map_add(a11y_col, 65280,    ""green"");
    ds_map_add(a11y_col, 32768,    ""dark green"");
    ds_map_add(a11y_col, 16711680, ""blue"");
    ds_map_add(a11y_col, 16776960, ""cyan"");
    ds_map_add(a11y_col, 65535,    ""yellow"");
    ds_map_add(a11y_col, 32896,    ""olive"");
    ds_map_add(a11y_col, 4235519,  ""orange"");
    ds_map_add(a11y_col, 16711935, ""magenta"");
    ds_map_add(a11y_col, 8388736,  ""purple"");
    ds_map_add(a11y_col, 4210752,  ""dark grey"");
    ds_map_add(a11y_col, 8421504,  ""grey"");
    ds_map_add(a11y_col, 2111350,  ""brown"");

    // Things listed in the scene that are NOT Interactive_Objects, and so are invisible
    // both to the game's own click dispatch and to the sweep below. The paint palette is
    // the whole of it: fourteen swatches that answer only to a real Mouse_4 press on the
    // instance. Activated with a synthetic one.
    a11y_extra = ds_list_create();
    ds_list_add(a11y_extra, Computer_Paint_Color_Take);

    // And the very last thing in the game. The_End_Controller sits in all three ending
    // rooms and its only handler is a GLOBAL left press - a click anywhere on the screen,
    // once its ten second timer has armed it, creates End_Game and takes you back to the
    // menu. It is not an Interactive_Object, so the click dispatch never offered it and
    // the sweep could not see it; NO_MENU is in those rooms too, so Escape was not a way
    // out either. Without this the ending screen is a room with nothing in it and no way
    // to leave.
    ds_list_add(a11y_extra, The_End_Controller);

    // Last computer window announced, so an open window is read once and not every frame.
    a11y_cw_last = """";

    // Whether the drawing was complete last frame, so finishing it is audible.
    a11y_draw_last = -1;

    // Things that are not interactive at all, but that something in the game watches the
    // MOUSE POSITION against. Bridge_05_Barier only raises the wire objective - and starts
    // the doll crying that leads you to it - while the pointer is within 60 pixels of
    // Bridge_Wire. No click, no Interactive_Object, nothing a keyboard can reach, so the
    // chapter is unfinishable without this. Listed so they can be focused, which puts the
    // real pointer on them.
    a11y_prox = ds_list_create();
    ds_list_add(a11y_prox, Bridge_Wire);

    // Forest_Leech is the same trap as the wire, and on the good route it is fatal to
    // progress. Take the leech in the swamp WITHOUT the glove and it attaches to you and
    // sets Item_Leech.ready = 0, which makes the cauldron refuse it. The only thing that
    // ever sets ready back to 1 is the leech's own Step, and it only runs while the
    // POINTER is within 20 pixels of it - no click, and it is not an Interactive_Object,
    // so there was nothing to focus and no way to finish the recipe.
    ds_list_add(a11y_prox, Forest_Leech);

    // The hospital phone keypad. Its digit keys are typed on the number row instead of
    // being walked to one at a time, so they are kept out of the scene list.
    a11y_phone = ds_map_create();
/*PHONEOBJS*/

    // What each Info popup actually says.
    //
    // These are the notes, posters, newspaper cuttings and patient cards you click in the
    // world, and every one is a single pre-rendered sprite - draw_sprite, no text object,
    // and one frame each rather than the seven the localised art uses, so the game shows
    // these in English to everyone. Without this the entire contents are unreachable, and
    // some of it is not flavour: two of them are phone numbers a puzzle needs.
    //
    // Transcribed from the sprites exported by gscripts\export_info_sprites.csx.
    a11y_info = ds_map_create();
    ds_map_add(a11y_info, Bridge_Nurse_Info,          ""Missing Nurse. Where is she?"");
    ds_map_add(a11y_info, Bridge_Immortal_Info,       ""Why can't we wake up?!!"");
    ds_map_add(a11y_info, Bridge_Weather_Info,        ""Weather today: heavy storm."");
    ds_map_add(a11y_info, Bridge_Closed_Info,         ""Bridge is CLOSED."");
    ds_map_add(a11y_info, Bridge_Hospital_Info,       ""Need any Help? Visit us! We will help you. For FREE!"");
    ds_map_add(a11y_info, Bridge_Hospital_Info_02,    ""Lost? Visit our hospital."");
    ds_map_add(a11y_info, HospitalNF_Eyes_Poster,     ""Examine Your Eyes!! NOW!!"");
    ds_map_add(a11y_info, Hospital_Hall_02_Note,      ""S.O.S."");
    ds_map_add(a11y_info, Hospital_Note_Call,         ""CALL ME!! 555-279"");
    ds_map_add(a11y_info, Hospital_Note_Fake,         ""This Life is FAKE!"");
    ds_map_add(a11y_info, Hospital_Note_Pills,        ""Pills. EAT ME!!"");
    ds_map_add(a11y_info, Hospital_Note_Help,         ""I need help here!"");
    ds_map_add(a11y_info, Hospital_Note_Eyes,         ""Just open your eyes..."");
    ds_map_add(a11y_info, Hospital_Monster_Bird_Info, ""Newspaper. New Monster Spotted! New birdlike creature was seen near the bridge. Who did that and why?!"");
    ds_map_add(a11y_info, Hospital_Doll_Info,         ""Newspaper. Broken Childhood Dream. The situation is getting worse and worse."");
    ds_map_add(a11y_info, Hospital_Warning_Info,      ""Disclaimer! Every action has a reaction! Our minds are capable of creating monsters! We can't leave this dream so we don't want it to become a nightmare! Don't do anything bad or stupid! For the sake of all of us."");
    ds_map_add(a11y_info, Hospital_Earthquake_Info,   ""Newspaper. Strange Earthquakes. What causes them?"");
    ds_map_add(a11y_info, Hospital_Patient_Card,      ""Patient card. Name, x x x. Age, 41. I D, 0000432A. Urgently require an eyes, ears and hair transplant. Stable condition. Found a few days ago near the cemetery."");
    ds_map_add(a11y_info, Hospital_Patient_14_Card,   ""Patient card. I D, 00000081. Several fatal wounds, paralyzed, traumatized. Impossible to cure and rehabilitate. Only solution, find a way to wake up."");
    ds_map_add(a11y_info, Hospital_Player_Card,       ""Patient card. Name, unknown. Age, unknown. Address, unknown. I D, 0084H39M. Lost several fingers. Condition, stable. Found on the bridge."");
    ds_map_add(a11y_info, Hospital_Patient_Card_Blank, ""Patient card. Blank - name, age, address and I D all empty."");
    ds_map_add(a11y_info, Hospital_Teddy_Card,        ""Patient card. Name, Teddy Bear. I D, 00000049. Missing head."");
    ds_map_add(a11y_info, HospitalB_Erie_Sound,       ""Newspaper. What is the Source of Eerie Moans?"");
    ds_map_add(a11y_info, HospitalB_Butcher_Note,     ""Newspaper. Butcher Strikes Again!!"");
    ds_map_add(a11y_info, HospitalB_Spiders_Note,     ""Newspaper. Dangerous Spiders!"");
    ds_map_add(a11y_info, HospitalB_Bird_Note,        ""Newspaper. What's wrong with the birds?!"");
    ds_map_add(a11y_info, Flat_Phone_Info,            ""637-511"");

    // Watches for things gained. Seeded from what is already here so that loading a save
    // does not read out the entire inventory and every status at once.
    a11y_itprev = ds_list_create();
    a11y_stprev = ds_list_create();
    var a_sd = instance_number(Item);
    for (var a_s3 = 0; a_s3 < a_sd; a_s3 += 1)
        ds_list_add(a11y_itprev, instance_find(Item, a_s3));
    a_sd = instance_number(Status);
    for (var a_s4 = 0; a_s4 < a_sd; a_s4 += 1)
        ds_list_add(a11y_stprev, instance_find(Status, a_s4));

    // Status screen.
    a11y_st_idx = 0;
    a11y_st_on = 0;

    // Damage watch. Seeded from the live value so that loading a save, which restores hp
    // along with everything else, does not report the whole game's injuries at once.
    a11y_hp = hp;

    // Board game.
    a11y_die = -1;
    a11y_bpos = -1;
    a11y_bend = -1;

    // Speech bridge. bdcspeech.dll wraps Prism, which routes to whatever screen reader is
    // running. If the DLL is missing these calls just fail and the game plays on normally.
    a11y_f_init  = external_define(""bdcspeech.dll"", ""bdc_init"",  dll_cdecl, ty_real, 0);
    a11y_f_speak = external_define(""bdcspeech.dll"", ""bdc_speak"", dll_cdecl, ty_real, 2, ty_string, ty_real);
    a11y_f_stop  = external_define(""bdcspeech.dll"", ""bdc_stop"",  dll_cdecl, ty_real, 0);
    a11y_ready = external_call(a11y_f_init);

    // Object name -> what to say. Every menu label in this game is a pre-rendered sprite,
    // so the object name is the only identity a control has.
    a11y_lbl = ds_map_create();

    // Our own row. Not an object and nothing draws it, so it is looked up by a name no
    // object could have. Its a11y_act entry is added below, where that map is created -
    // GML has no hoisting, and filling a map a few lines before ds_map_create is the
    // crash the ordering guard exists for.
    ds_map_add(a11y_lbl, ""@a11y"", ""Accessibility settings"");
    // Object name -> how to act on it:
    //   0 = user event 0, 1 = simulated left press, 2 = slider, 3 = toggle
    a11y_act = ds_map_create();
    ds_map_add(a11y_act, ""@a11y"", 0);

    // --- title screen ---
    ds_map_add(a11y_lbl, ""Main_Menu_New_Game"",  ""New Game"");  ds_map_add(a11y_act, ""Main_Menu_New_Game"", 0);
    ds_map_add(a11y_lbl, ""Main_Menu_Load_Game"", ""Load Game""); ds_map_add(a11y_act, ""Main_Menu_Load_Game"", 0);
    ds_map_add(a11y_lbl, ""Main_Menu_Options"",   ""Options"");   ds_map_add(a11y_act, ""Main_Menu_Options"", 0);
    ds_map_add(a11y_lbl, ""Main_Menu_Credits"",   ""Credits"");   ds_map_add(a11y_act, ""Main_Menu_Credits"", 0);
    ds_map_add(a11y_lbl, ""Main_Menu_Exit"",      ""Exit"");      ds_map_add(a11y_act, ""Main_Menu_Exit"", 0);

    // --- save slots. Slot_01..05 are containers whose user event 0 only repopulates
    //     them, so they are NOT focusable - the buttons they spawn are. ---
    ds_map_add(a11y_lbl, ""Slot_New_Game"",   ""New Game"");  ds_map_add(a11y_act, ""Slot_New_Game"", 0);
    ds_map_add(a11y_lbl, ""Slot_Load"",       ""Load"");      ds_map_add(a11y_act, ""Slot_Load"", 0);
    ds_map_add(a11y_lbl, ""Slot_Delete"",     ""Delete"");    ds_map_add(a11y_act, ""Slot_Delete"", 0);
    ds_map_add(a11y_lbl, ""Slot_Back"",       ""Back"");      ds_map_add(a11y_act, ""Slot_Back"", 0);
    ds_map_add(a11y_lbl, ""Slot_Delete_Yes"", ""Yes"");       ds_map_add(a11y_act, ""Slot_Delete_Yes"", 1);
    ds_map_add(a11y_lbl, ""Slot_Delete_No"",  ""No"");        ds_map_add(a11y_act, ""Slot_Delete_No"", 1);

    // --- pause / options: plain buttons ---
    ds_map_add(a11y_lbl, ""Game_Menu_Resume"",   ""Resume"");       ds_map_add(a11y_act, ""Game_Menu_Resume"", 1);
    ds_map_add(a11y_lbl, ""Game_Menu_Restart"",  ""Restart"");      ds_map_add(a11y_act, ""Game_Menu_Restart"", 1);
    ds_map_add(a11y_lbl, ""Game_Menu_Autosave"", ""Autosave"");     ds_map_add(a11y_act, ""Game_Menu_Autosave"", 1);
    ds_map_add(a11y_lbl, ""Game_Menu_Exit"",     ""Exit to Menu""); ds_map_add(a11y_act, ""Game_Menu_Exit"", 1);
    ds_map_add(a11y_lbl, ""Game_Menu_Screen_Set_Window"",       ""Apply Windowed"");    ds_map_add(a11y_act, ""Game_Menu_Screen_Set_Window"", 1);
    ds_map_add(a11y_lbl, ""Game_Menu_Screen_Set_Fullscreen"",   ""Apply Fullscreen"");  ds_map_add(a11y_act, ""Game_Menu_Screen_Set_Fullscreen"", 1);
    ds_map_add(a11y_lbl, ""Game_Menu_Screen_Fullscren_Center"", ""Center Fullscreen""); ds_map_add(a11y_act, ""Game_Menu_Screen_Fullscren_Center"", 1);
    ds_map_add(a11y_lbl, ""Game_Menu_Sure_Yes"", ""Yes"");  ds_map_add(a11y_act, ""Game_Menu_Sure_Yes"", 1);
    ds_map_add(a11y_lbl, ""Game_Menu_Sure_No"",  ""No"");   ds_map_add(a11y_act, ""Game_Menu_Sure_No"", 1);

    // --- options: sliders (left/right adjust) ---
    // Game_Menu_Volume is ONE object hosting THREE sliders, so it becomes three focus
    // entries distinguished by a sub-index.
    ds_map_add(a11y_lbl, ""Game_Menu_Volume"",     ""Volume"");     ds_map_add(a11y_act, ""Game_Menu_Volume"", 2);
    ds_map_add(a11y_lbl, ""Game_Menu_Brightness"", ""Brightness""); ds_map_add(a11y_act, ""Game_Menu_Brightness"", 2);

    // --- options: toggles (left/right or Enter flips them) ---
    ds_map_add(a11y_lbl, ""Game_Menu_Volume_Mute"",        ""Mute"");        ds_map_add(a11y_act, ""Game_Menu_Volume_Mute"", 3);
    ds_map_add(a11y_lbl, ""Game_Menu_Sound_3D"",           ""3D Sound"");    ds_map_add(a11y_act, ""Game_Menu_Sound_3D"", 3);
    ds_map_add(a11y_lbl, ""Game_Menu_Screen_Window_Size"", ""Window Size""); ds_map_add(a11y_act, ""Game_Menu_Screen_Window_Size"", 3);

    // Objects swept each frame. A parent object matches all of its children, which is why
    // one entry covers the five title buttons and one covers the three slot buttons.
    a11y_scan = ds_list_create();
    ds_list_add(a11y_scan, Main_Menu_Button);
    ds_list_add(a11y_scan, SLOT_BTN);
    ds_list_add(a11y_scan, Slot_Back);
    ds_list_add(a11y_scan, Slot_Delete_Yes);
    ds_list_add(a11y_scan, Slot_Delete_No);
    ds_list_add(a11y_scan, Game_Menu_Resume);
    ds_list_add(a11y_scan, Game_Menu_Restart);
    ds_list_add(a11y_scan, Game_Menu_Autosave);
    ds_list_add(a11y_scan, Game_Menu_Exit);
    ds_list_add(a11y_scan, Game_Menu_Volume);
    ds_list_add(a11y_scan, Game_Menu_Volume_Mute);
    ds_list_add(a11y_scan, Game_Menu_Sound_3D);
    ds_list_add(a11y_scan, Game_Menu_Brightness);
    ds_list_add(a11y_scan, Game_Menu_Screen_Window_Size);
    ds_list_add(a11y_scan, Game_Menu_Screen_Set_Window);
    ds_list_add(a11y_scan, Game_Menu_Screen_Set_Fullscreen);
    ds_list_add(a11y_scan, Game_Menu_Screen_Fullscren_Center);
    ds_list_add(a11y_scan, Game_Menu_Sure_Yes);
    ds_list_add(a11y_scan, Game_Menu_Sure_No);

    // Per-frame focus lists, rebuilt every step.
    a11y_ins = ds_list_create();
    a11y_nm  = ds_list_create();
    a11y_cx  = ds_list_create();
    a11y_cy  = ds_list_create();
    a11y_sub = ds_list_create();   // -1 = whole object, 0/1/2 = which volume slider

    // Scene focus lists, kept separate from the menu ones: the two never coexist, but
    // sharing them would make it much harder to see that.
    a11y_wi  = ds_list_create();
    a11y_wnm = ds_list_create();
    a11y_wx  = ds_list_create();

    // Ownership stamp. After a game_load these ids may be stale - or worse, may now
    // belong to one of the game's own data structures - so the tick checks for this
    // key before trusting any of them.
    ds_map_add(a11y_lbl, ""@a11y"", 1);

    // Initialised-marker, created LAST so that its existence means the init ran to
    // completion. Persistent, or a room change would destroy it and force a needless
    // re-init every time; invisible and sprite-less, so it draws nothing and collides
    // with nothing.
    a11y_mk = instance_create(x, y, Worm_February);
    a11y_mk.persistent = 1;
    a11y_mk.visible = 0;

    // The marker carries the authoritative copy of every one of these. Controller's own
    // are a per-frame working cache - see the hydrate/persist note in the tick.
/*SEEDMARKER*/
";

// Every generated block fills structures the init created a few lines above it, and the
// ORDER of those two things is load-bearing: GML has no hoisting, so a ds_list_create()
// that lands BELOW the block that fills it means the very first generated line reads a
// variable that is not set yet, and this runner treats that as fatal - the game dies on
// Controller's Create with "Variable Controller.a11y_evon not set before reading it", which
// is exactly how this guard came to exist.
//
// Nothing about the failure is visible here: the C# runs cleanly, the patch imports, the
// verifier finds every marker, and the decompiled output looks correct because it IS
// correct except for the order. So check it mechanically instead. For each placeholder,
// every a11y_ name the generated block touches must be assigned somewhere in the init
// ABOVE that placeholder.
void CheckOrder(string tag, string body)
{
    int at = initBody.IndexOf(tag, StringComparison.Ordinal);
    if (at < 0) throw new Exception("placeholder missing from the init: " + tag);
    string above = initBody.Substring(0, at);
    var seen = new HashSet<string>();
    foreach (System.Text.RegularExpressions.Match m in
             System.Text.RegularExpressions.Regex.Matches(body, @"\ba11y_[A-Za-z0-9_]+"))
    {
        if (!seen.Add(m.Value)) continue;
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                above, @"(?m)^\s*" + m.Value + @"\s*="))
            throw new Exception(tag + " fills " + m.Value +
                                ", which the init does not create until AFTER it");
    }
}

CheckOrder("/*CATEGORIES*/", catLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*CATEGORIES*/", catLines.ToString().TrimEnd('\n'));
CheckOrder("/*STATEVARS*/", stateLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*STATEVARS*/", stateLines.ToString().TrimEnd('\n'));
CheckOrder("/*SKIPOBJS*/", skipLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*SKIPOBJS*/", skipLines.ToString().TrimEnd('\n'));
CheckOrder("/*PICS*/", picLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*PICS*/", picLines.ToString().TrimEnd('\n'));
CheckOrder("/*ROOMNAMES*/", roomLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*ROOMNAMES*/", roomLines.ToString().TrimEnd('\n'));
CheckOrder("/*PRETTY*/", prettyLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*PRETTY*/", prettyLines.ToString().TrimEnd('\n'));
CheckOrder("/*SHORTNAMES*/", shortLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*SHORTNAMES*/", shortLines.ToString().TrimEnd('\n'));
CheckOrder("/*PICKOBJS*/", pickLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*PICKOBJS*/", pickLines.ToString().TrimEnd('\n'));
CheckOrder("/*HURTOBJS*/", hurtLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*HURTOBJS*/", hurtLines.ToString().TrimEnd('\n'));
CheckOrder("/*NOHOVEROBJS*/", noHoverLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*NOHOVEROBJS*/", noHoverLines.ToString().TrimEnd('\n'));
CheckOrder("/*CLUTTEROBJS*/", clutterLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*CLUTTEROBJS*/", clutterLines.ToString().TrimEnd('\n'));
CheckOrder("/*COMPOBJS*/", compLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*COMPOBJS*/", compLines.ToString().TrimEnd('\n'));
CheckOrder("/*DESKOBJS*/", deskLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*DESKOBJS*/", deskLines.ToString().TrimEnd('\n'));
CheckOrder("/*TRANSOBJS*/", transLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*TRANSOBJS*/", transLines.ToString().TrimEnd('\n'));
CheckOrder("/*EVENTOBJS*/", eventLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*EVENTOBJS*/", eventLines.ToString().TrimEnd('\n'));
CheckOrder("/*FLAGSEED*/", flagSeed.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*FLAGSEED*/", flagSeed.ToString().TrimEnd('\n'));
CheckOrder("/*CUTKEYS*/", cutPressLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*CUTKEYS*/", cutPressLines.ToString().TrimEnd('\n'));
CheckOrder("/*ITEMNAMES*/", itemNameLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*ITEMNAMES*/", itemNameLines.ToString().TrimEnd('\n'));
CheckOrder("/*SCENERY*/", sceneryLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*SCENERY*/", sceneryLines.ToString().TrimEnd('\n'));
CheckOrder("/*BOARDOBJS*/", boardLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*BOARDOBJS*/", boardLines.ToString().TrimEnd('\n'));
CheckOrder("/*COMPWINS*/", winLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*COMPWINS*/", winLines.ToString().TrimEnd('\n'));
CheckOrder("/*PHONEOBJS*/", phoneLines.ToString().TrimEnd('\n'));
initBody = initBody.Replace("/*PHONEOBJS*/", phoneLines.ToString().TrimEnd('\n'));

// Every a11y_ variable the init establishes, read straight back out of the init source so
// the three generated blocks below can never drift out of sync with it. a11y_mk is the
// marker instance itself and is excluded - it would be self-referential.
var stateVars = System.Text.RegularExpressions.Regex
    .Matches(initBody, @"(?m)^\s*(a11y_[A-Za-z0-9_]+)\s*=")
    .Select(m => m.Groups[1].Value)
    .Distinct()
    .Where(v => v != "a11y_mk")
    .ToArray();

// A schema tag derived from the variable names themselves. game_load can restore a marker
// saved by an OLDER build of this patch, whose variable set is not the one the tick now
// expects - which is exactly how 'Worm_February.a11y_lbl not set before reading it'
// happened. Deriving the tag from the names means any change to the state set invalidates
// every marker written by an earlier build, automatically and without anyone remembering to
// bump a version.
int schemaTag = 7;
foreach (var v in stateVars)
    foreach (var ch in v)
        schemaTag = (schemaTag * 31 + ch) % 1000000;
Console.WriteLine($"State schema tag: {schemaTag}.");

// Seed the marker at the end of init; refresh Controller from it at the top of each tick;
// hand it back at the end. See the note on the guard for why the marker, and not
// Controller, has to be the authority.
// friction is a built-in instance variable, so it exists on EVERY instance no matter which
// build wrote it - that is the whole point, since reading anything else can fail. It is
// also inert here: friction only applies while speed is non-zero, and this marker never
// moves, has no sprite and no events. Written last, so the tag can only be present on a
// marker whose seeding completed.
string seedBlock    = string.Join("\n", stateVars.Select(v => "    a11y_mk." + v + " = " + v + ";"))
                    + "\n    a11y_mk.friction = " + schemaTag + ";";
string hydrateBlock = string.Join("\n", stateVars.Select(v => "    " + v + " = a_mk." + v + ";"));
string persistBlock = string.Join("\n", stateVars.Select(v => "        a_mk." + v + " = " + v + ";"));

initBody = initBody.Replace("/*SEEDMARKER*/", seedBlock);
Console.WriteLine($"State carried on the marker: {stateVars.Length} variables.");

// Create only. Re-announcing this every time a save loads would just be noise.
string init = "{" + initBody + @"
    if (a11y_ready)
        external_call(a11y_f_speak, ""Bad Dream accessibility ready"", 1);
}
";

// ---------------------------------------------------------------------------
// Tick - Controller's Step event, every frame.
// ---------------------------------------------------------------------------
string tick = @"
// Controller is persistent AND room-placed, so returning to Lvl_Main_Menu can leave more
// than one around. Only the first does accessibility work, or everything is said twice.
if (id == instance_find(Controller, 0))
{
    // Recover after a game_load. The game saves with GameMaker's built-in game_save(),
    // and game_load() replaces every live instance with the stored one WITHOUT running
    // its Create event - so the Controller that comes back has whatever variables it had
    // when the save was written, and the accessibility state has to be rebuilt here.
    //
    // Two distinct failures, both of which this covers:
    //   * A save written before the mod existed has no a11y_ variables at all. Reading
    //     one is fatal ('a11y_cy not set before reading it').
    //   * A save written WITH the mod restores the ids the data structures had in that
    //     session. ds_* structures are not part of a save file, so those ids are stale;
    //     they may be dangling, or may since have been handed to one of the game's own
    //     lists - which would be silent corruption rather than an error.
    //
    // The check cannot READ any a11y_ variable, because after an old save the whole point
    // is that they are absent and reading one is what crashes. variable_local_exists would
    // be the obvious tool and it is a dead end - see the note by the FUNC registration
    // below. instance_exists is the way out: it is always safe to call, and Worm_February
    // is a leftover object with no events, no sprite, no parent and not one reference in
    // any of this game's 10239 code entries, so an instance of it can only ever be one
    // this patch made. The init creates it last, so its presence means the init finished.
    //
    // The check reads ONLY the marker, never Controller. An earlier version read
    // Controller.a11y_lbl once the marker existed, and that was wrong: the marker is
    // persistent and survives game_load, but game_load REPLACES the Controller with the
    // one from the save file. Loading a save written before this patch therefore left a
    // live marker beside a Controller with none of these variables, the guard passed, and
    // the very next line died on 'Controller.a11y_lbl not set before reading it'.
    //
    // Nor is 'the marker exists' enough on its own, which cost another crash: game_load can
    // restore a marker saved by an OLDER build of this patch, carrying a different set of
    // variables - or, as it turned out, none at all - and reading one of those is just as
    // fatal. So the FIRST thing checked is a built-in variable, which every instance has
    // whichever build wrote it, holding a tag derived from this build's state schema.
    // Only once that matches is anything else on the marker safe to touch.
    var a_live = 0;
    var a_mk = instance_find(Worm_February, 0);

    // Guarded on the INSTANCE, not on the object. instance_deactivate_all - which every
    // pause menu in this game calls - takes the marker off the active list that
    // instance_find walks while instance_exists(Worm_February) still answers true, so the
    // object-level guard let a_mk through as noone and the very next line read a field off
    // it. That is the crash, and being the first thing the tick does every frame it is by
    // far the worst place in the patch to have it.
    if (instance_exists(a_mk))
    {
        if (a_mk.friction == /*SCHEMATAG*/)
        {
            if (ds_exists(a_mk.a11y_lbl, ds_type_map))
            {
                if (ds_map_exists(a_mk.a11y_lbl, ""@a11y""))
                    a_live = 1;
            }
        }
    }
    if (!a_live)
    {
        /*REINIT*/
        a_mk = instance_find(Worm_February, 0);
    }

    // Pull this frame's state off the marker. Everything below works on Controller exactly
    // as before; these copies are just a cache, refreshed here and written back at the end,
    // which is what makes the Controller being swapped out from under us survivable.
/*HYDRATE*/

    // Control silences speech, the screen-reader convention. It sits out here rather
    // than with the menu keys so it also works during dialogue and in the inventory.
    if (keyboard_check_pressed(vk_control) && a11y_ready)
        external_call(a11y_f_stop);

    // F4: where am I, and why is the list empty. A no-entries report in every filter is the one
    // report that cannot be acted on - it is the same sentence whether the room is genuinely
    // bare, whether something invisible is holding interaction off, or whether this patch has
    // filtered the room away by mistake. This separates those three. Read-only.
    //
    // Counted over exactly the population the object list is built from, because anything
    // else is a number the player cannot go and find. Items are the trap: Item is an
    // Interactive_Object child and every item in the game is PERSISTENT, so what you are
    // carrying travels with you and was being counted as furniture. In the ending screens
    // that was the entire count - the room holds no interactive object at all, and F4
    // cheerfully reported nine of them, seven usable, in a room where the list is empty.
    //
    // 'playable' is skipped for the interface buttons, the only Interactive_Object
    // descendants that never set it; reading an unset variable is fatal. They are dropped
    // through a11y_skip here rather than by name, so the Memories_ variants go with them.
    if (keyboard_check_pressed(vk_f4) && a11y_ready)
    {
        var a_dgs = ""Room "" + string(room);
        if (ds_map_exists(a11y_rooms, room))
            a_dgs = ds_map_find_value(a11y_rooms, room);

        var a_dgt = 0;
        var a_dgp = 0;
        var a_dgsk = ds_map_create();
        var a_dgn = instance_number(Item);
        for (var a_dgi = 0; a_dgi < a_dgn; a_dgi += 1)
        {
            var a_dgq = instance_find(Item, a_dgi);
            if (instance_exists(a_dgq))
                ds_map_add(a_dgsk, a_dgq, 1);
        }
        var a_dgc = instance_number(Interactive_Object);
        for (var a_dgj = 0; a_dgj < a_dgc; a_dgj += 1)
        {
            var a_dgx = instance_find(Interactive_Object, a_dgj);
            if (instance_exists(a_dgx))
            {
                if (!ds_map_exists(a11y_skip, a_dgx.object_index) &&
                    !ds_map_exists(a_dgsk, a_dgx))
                {
                    a_dgt += 1;
                    if (a_dgx.sprite_index != -1)
                    {
                        if (a_dgx.playable != 0)
                            a_dgp += 1;
                    }
                }
            }
        }
        ds_map_destroy(a_dgsk);
        if (a_dgt == 0)
            a_dgs += "". Nothing in this room can be used"";
        else if (a_dgt == 1)
            a_dgs += "". 1 interactive object, "" + string(a_dgp) + "" usable"";
        else
            a_dgs += "". "" + string(a_dgt) + "" interactive objects, "" +
                     string(a_dgp) + "" usable"";

        if (instance_exists(Info))
            a_dgs += "", a popup is open"";
        if (instance_exists(Dialogue))
            a_dgs += "", a conversation is running"";
        if (instance_exists(Cutscene))
            a_dgs += "", a cutscene is holding the room"";
        if (instance_exists(Pause))
            a_dgs += "", the pause menu is open"";
        if (instance_exists(Chapter_Screen))
            a_dgs += "", a chapter card is up"";
        if (instance_exists(Room_Translation))
            a_dgs += "", the room is changing"";
        if (instance_exists(NO_INVENTORY))
            a_dgs += "", inventory disabled here"";
        if (a11y_inv)
            a_dgs += "", the inventory reader is open"";
        external_call(a11y_f_speak, a_dgs + ""."", 1);
    }

    // ----- Picture close-ups ----------------------------------------------
    // A dozen rooms in the old house are one drawn image and nothing else: the object list
    // there can only ever say Go back, and the picture - which is the entire content of the
    // room, and in several cases the puzzle - was silent. The text is transcribed from the
    // exported sprite, the same way the Info popups were, and baked at patch time.
    //
    // Built once here so entering the room and F5 cannot drift apart.
    var a_picd = """";
    if (ds_map_exists(a11y_pics, room))
    {
        a_picd = ds_map_find_value(a11y_pics, room);

        // Which of the two endings this room is showing. ending_neutral is set in
        // Controller's own Create, so it is a plain self read here and always safe, and it
        // is the same flag Bed_Scene uses to choose how dark to make the room - which is
        // the only difference a player is ever given between these two.
        if (room == Lvl_Last_Screen_Bad)
        {
            if (ending_neutral)
                a_picd += "" The room is dim and grey."";
            else
                a_picd += "" The room is almost completely dark."";
        }

        // Three of them are cut with the scissors, and the cut is the whole point. The
        // game shows it by making a second sprite visible over the hole.
        if (room == Lvl_Memories_Map)
        {
            if (instance_exists(Memories_Drawing_Map_Cut))
            {
                if (Memories_Drawing_Map_Cut.visible)
                    a_picd += "" The key has been cut out of the corner."";
            }
        }
        if (room == Lvl_Memories_Treasure)
        {
            if (instance_exists(Memories_Drawing_Key_Cut))
            {
                if (Memories_Drawing_Key_Cut.visible)
                    a_picd += "" The key has been cut out of the corner."";
            }
        }
        if (room == Lvl_Memories_Drawing_Happy)
        {
            if (instance_exists(Memories_Happy_Sun_Cut))
            {
                if (Memories_Happy_Sun_Cut.visible)
                    a_picd += "" The sun has been cut out of the corner."";
            }
        }
        if (room == Lvl_Memories_Photo)
        {
            var a_mpi = instance_find(Memories_Photo, 0);
            if (instance_exists(a_mpi))
            {
                if (a_mpi.image_index >= 1)
                    a_picd += "" A piece has been cut from its left edge."";
            }
        }
    }

    // F5 repeats it. Entering the room reads it once, and by the time a player has walked
    // the list they may well want it again - especially on the drawings that are puzzles.
    if (keyboard_check_pressed(vk_f5) && a11y_ready)
    {
        if (a_picd == """")
            external_call(a11y_f_speak, ""No picture to describe here."", 1);
        else
            external_call(a11y_f_speak, a_picd, 1);
    }

    // ----- Autosaves ------------------------------------------------------
    //
    // The game marks one with a caption at the top of the screen that fades out over about
    // a second and a half, and there was nothing at all to hear - so there was no way to
    // know the game had saved, or where it would put you back if you died.
    //
    // Autosave_Info is created by Autosave's own alarm on the line after game_save
    // returns, so its arrival IS the save; nothing else creates it. Watched by count
    // rather than by the object, because it is destroyed by its own Step once it has
    // faded and a second autosave creates a fresh one.
    //
    // QUEUED, not interrupting. An autosave usually fires on arriving somewhere, which is
    // exactly the moment the room and its contents are being read out, and cutting that
    // off to say the game had saved would trade the more useful sentence for the less.
    var a_asv = 0;
    if (instance_number(Autosave_Info) > 0)
        a_asv = 1;
    if (a_asv && a11y_autosave == 0 && a11y_ready)
        external_call(a11y_f_speak, ""Game saved."", 0);
    a11y_autosave = a_asv;

    // ----- The leech ------------------------------------------------------
    // Using the swamp head without the glove leaves Forest_Leech attached to you and sets
    // Item_Leech.ready = 0. That flag is exactly what the cauldron checks, so the recipe
    // is stuck until the leech has drunk its fill - and it only drinks while the pointer
    // rests within 20 pixels of it, which is the wire trap all over again: focus the
    // entry, then do nothing. Nothing said when that was finished either, so the one
    // moment the player is allowed to move again was silent.
    // Guarded on the INSTANCE the find returned, not on the object - see the note on the
    // story-flag sweep. instance_exists(SomeObject) and instance_find(SomeObject, 0) do not
    // agree while the pause menu is up, because instance_deactivate_all takes the instance
    // off the active list that instance_find walks. Every field read in this patch that
    // goes through instance_find is written this way for that reason.
    var a_lch = 0;
    var a_lci = instance_find(Item_Leech, 0);
    if (instance_exists(a_lci))
    {
        if (a_lci.ready)
            a_lch = 2;
        else
            a_lch = 1;
    }
    if (a_lch != a11y_leech)
    {
        if (a_lch == 2 && a11y_leech == 1 && a11y_ready)
            external_call(a11y_f_speak,
                ""The leech has let go. It is ready for the cauldron now."", 1);
        a11y_leech = a_lch;
    }

    // ----- Info popups ----------------------------------------------------
    // The surface crash these used to cause is fixed in Info's own Draw event, not here -
    // see the note by that prepend at the bottom of this script. Guarding from Controller
    // could not work: the popup is created LATER in the same frame than this runs.
    //
    // The popup is a single pre-rendered sprite, so its contents come from the transcription
    // table rather than from the game. Anything not transcribed falls back to its object
    // name, which is at least descriptive. While one is up the game refuses every other
    // interaction (_interactive_get_type returns -4), so say how to shut it too.
    var a_info_on = 0;
    if (instance_exists(Info))
    {
        var a_ifi = instance_find(Info, 0);
        if (instance_exists(a_ifi))
        {
            a_info_on = 1;
            var a_ifo = a_ifi.object_index;
            var a_ifn = string_replace_all(object_get_name(a_ifo), ""_"", "" "");
            if (ds_map_exists(a11y_pretty, a_ifo))
                a_ifn = ds_map_find_value(a11y_pretty, a_ifo);
            a_ifn += "", a picture"";
            if (ds_map_exists(a11y_info, a_ifo))
                a_ifn = ds_map_find_value(a11y_info, a_ifo);
            if (a_ifn != a11y_info_last)
            {
                a11y_info_last = a_ifn;
                if (a11y_ready)
                    external_call(a11y_f_speak, a_ifn + "" Press Enter to close."", 1);
            }

            // Its own global-left-press handler, so the appear/destroy guard still applies
            // and Enter does nothing while it is still fading in - just as a click would.
            if (keyboard_check_pressed(vk_enter))
            {
                with (a_ifi)
                    event_perform(ev_mouse, 53);
            }
        }
    }
    else
    {
        a11y_info_last = """";
    }

    // ----- The computer's windows -----------------------------------------
    //
    // Lvl_Computer's windows and dialogs are where the chapter actually talks to you -
    // the computer being infected, the printer not being connected, the virus being
    // healed - and every word of it is drawn straight to the screen by the window's own
    // Draw event. There is no object to focus and nothing in the scene list, so all of it
    // was silent.
    //
    // The newest window is the one on top. Both variables are read only for objects the
    // injector confirmed set them in their own Create.
    var a_cwn = """";
    if (instance_exists(Computer_Window))
    {
        var a_cwi = instance_find(Computer_Window, instance_number(Computer_Window) - 1);
        if (instance_exists(a_cwi))
        {
            if (ds_map_exists(a11y_cwin, a_cwi.object_index))
            {
                a_cwn = a_cwi.window_name;
                if (ds_map_find_value(a11y_cwin, a_cwi.object_index))
                    a_cwn += "". "" + a_cwi.text;
                a_cwn += "". Press Escape to close."";
            }
        }
    }
    if (a_cwn != a11y_cw_last)
    {
        a11y_cw_last = a_cwn;
        if (a_cwn != """" && a11y_ready)
            external_call(a11y_f_speak, a_cwn, 1);
    }

    // ----- Paint tools on the number row ----------------------------------
    //
    // Which tool is armed decides what a press on the picture does, and the two of them
    // are otherwise two more entries to arrow past every time the colour changes - which
    // on this puzzle is constantly. 1 and 2 press them directly through the same user
    // event 0 a click runs, so the toggle, the deselect of the other tool and the click
    // sound all still happen.
    //
    // Guarded on the paint window existing, so the digits stay free everywhere else - the
    // hospital phone keypad reads the same keys.
    if (instance_exists(Computer_Paint_Window))
    {
        var a_pt = -4;
        if (keyboard_check_pressed(ord(""1"")) && instance_exists(Computer_Paint_Icon_FIll))
            a_pt = instance_find(Computer_Paint_Icon_FIll, 0);
        if (keyboard_check_pressed(ord(""2"")) && instance_exists(Computer_Paint_Take_Color))
            a_pt = instance_find(Computer_Paint_Take_Color, 0);
        if (a_pt != -4)
        {
            if (instance_exists(a_pt))
            {
                with (a_pt)
                    event_user(0);

                // Re-checked, not assumed: the line above ran the tool's own code, and
                // object code is free to destroy the object it belongs to.
                if (a11y_ready && instance_exists(a_pt))
                {
                    if (a_pt.active)
                        external_call(a11y_f_speak, a_pt.name + "" selected."", 1);
                    else
                        external_call(a11y_f_speak, a_pt.name + "" off."", 1);
                }
            }
        }
    }

    // Finishing the drawing. Hospital_Controller.drawing is recomputed every frame by the
    // paint window's Step - it is 1 only while EVERY element matches its target colour -
    // and the sole cue the game gives is that Save, Print and Delete quietly start
    // working. Announced once, and only while the paint window is actually open, so
    // walking back into the room later does not re-announce it.
    var a_hci = instance_find(Hospital_Controller, 0);
    if (instance_exists(Computer_Paint_Window) && instance_exists(a_hci))
    {
        var a_dnow = a_hci.drawing;
        if (a_dnow != a11y_draw_last)
        {
            if (a_dnow && a11y_draw_last != -1 && a11y_ready)
                external_call(a11y_f_speak, ""The drawing is finished."", 1);
            a11y_draw_last = a_dnow;
        }
    }
    else
    {
        a11y_draw_last = -1;
    }

    // ----- Health ---------------------------------------------------------
    // Controller.hp is accumulated DAMAGE, not remaining life - it only rises, and _dmg
    // converts it into one of five wound statuses as it crosses 5, 10, 15, 20 and 25.
    // Those Status objects are the game's own health display and carry properly localised
    // names, so they are what gets read out rather than a raw number the game never shows
    // anyone. Listed worst-last so the most severe wins.
    var a_wound = ""unhurt"";
    if (instance_exists(Status_Wounds))
        a_wound = Status_Wounds.name;
    if (instance_exists(Status_Wounds_Deep))
        a_wound = Status_Wounds_Deep.name;
    if (instance_exists(Status_Wounds_Critical))
        a_wound = Status_Wounds_Critical.name;
    if (instance_exists(Status_Wounds_Deadly))
        a_wound = Status_Wounds_Deadly.name;
    if (instance_exists(Status_Wounds_Agony))
        a_wound = Status_Wounds_Agony.name;

    // Taking a hit is otherwise conveyed by a wince, a stain and a sound - nothing a
    // screen reader can pass on, and nothing that says how bad it now is.
    if (hp != a11y_hp)
    {
        var a_hurt = (hp > a11y_hp);
        a11y_hp = hp;
        if (a11y_ready)
        {
            if (a_hurt)
                external_call(a11y_f_speak, ""Hurt. "" + a_wound + ""."", 1);
            else
                external_call(a11y_f_speak, ""Healed. "" + a_wound + ""."", 1);
        }
    }

    // H reads it back on demand, along with everything else the game is tracking about
    // you - venom, blindness, and the story flags - which is exactly what the S status
    // screen shows, from the same Status instances.
    if (keyboard_check_pressed(72) && a11y_ready)
    {
        var a_ht = ""Health, "" + a_wound;
        var a_extra = """";
        var a_sn = instance_number(Status);
        for (var a_s2 = 0; a_s2 < a_sn; a_s2 += 1)
        {
            var a_si = instance_find(Status, a_s2);
            if (instance_exists(a_si))
            {
                var a_so = a_si.object_index;
                if (a_so != Status_Wounds && a_so != Status_Wounds_Deep &&
                    a_so != Status_Wounds_Critical && a_so != Status_Wounds_Deadly &&
                    a_so != Status_Wounds_Agony)
                {
                    a_extra += "", "" + a_si.name;
                }
            }
        }
        if (a_extra != """")
            a_ht += "". Also"" + a_extra;

        // What you are holding, since an item stays active across rooms and there is
        // otherwise no way to be reminded without opening the inventory and walking it.
        if (active_item != -4)
        {
            if (instance_exists(active_item))
                a_ht += "". Holding "" + active_item.name;
        }
        external_call(a11y_f_speak, a_ht + ""."", 1);
    }

    // ----- Things gained --------------------------------------------------
    // Picking something up and gaining a condition are both silent to a screen reader: the
    // item simply appears in the strip down the side of the screen, and a status is an
    // icon that only names itself while the pointer is on it. Both are caught by diffing
    // the live instances against last frame's.
    //
    // Skipped entirely while a Pause exists. Pause's Create calls instance_deactivate_all
    // and explicitly reactivates only Controller, Music, Light and Screen - so Items and
    // Statuses genuinely disappear for the duration of the pause menu, the options screen
    // and the status screen, all three of which descend from Pause. Diffing through that
    // would announce the whole inventory again every time one of them closed.
    if (!instance_exists(Pause))
    {
        var a_icur = ds_list_create();
        var a_icn = instance_number(Item);
        for (var a_p = 0; a_p < a_icn; a_p += 1)
        {
            var a_pi = instance_find(Item, a_p);
            if (instance_exists(a_pi))
            {
                ds_list_add(a_icur, a_pi);
                // Queued rather than interrupting: picking something up often happens in
                // the same breath as a line of dialogue, and neither should cut the other.
                if (ds_list_find_index(a11y_itprev, a_pi) < 0 && a11y_ready)
                {
                    var a_pin = a_pi.name;
                    if (ds_map_exists(a11y_iname, a_pi.object_index))
                        a_pin += ds_map_find_value(a11y_iname, a_pi.object_index);
                    external_call(a11y_f_speak, a_pin + "" added to your inventory."", 0);
                }
            }
        }
        ds_list_copy(a11y_itprev, a_icur);
        ds_list_destroy(a_icur);

        var a_scur = ds_list_create();
        var a_scn = instance_number(Status);
        for (var a_p2 = 0; a_p2 < a_scn; a_p2 += 1)
        {
            var a_pi2 = instance_find(Status, a_p2);
            if (instance_exists(a_pi2))
            {
                ds_list_add(a_scur, a_pi2);
                if (ds_list_find_index(a11y_stprev, a_pi2) < 0 && a11y_ready)
                    external_call(a11y_f_speak, a_pi2.name + "" gained."", 0);
            }
        }
        ds_list_copy(a11y_stprev, a_scur);
        ds_list_destroy(a_scur);
    }

    // ----- Dialogue -------------------------------------------------------
    // Unlike the menus, the story text is REAL text: the Dialogue parent keeps its
    // lines in a ds_list and exposes the visible one as current_text, advancing via
    // its user event 0. So there is nothing to scrape - just watch current_text.
    var a_dlg_on = 0;
    if (instance_exists(Dialogue))
    {
        var a_dlg = instance_find(Dialogue, 0);
        if (instance_exists(a_dlg))
        {
            a_dlg_on = 1;
            var a_ct = a_dlg.current_text;
            if (a_ct != """" && a_ct != a11y_dlg_last)
            {
                a11y_dlg_last = a_ct;
                a11y_last = """";
                if (a11y_ready)
                    external_call(a11y_f_speak, a_ct, 1);
            }

            // Any arrow key repeats the line. Between lines current_text is blank for
            // a few frames (Mouse_53 clears it, alarm 0 fetches the next one), so the
            // repeat has to come from the remembered line - reading current_text here
            // would say nothing at all if the key landed in that gap.
            if (keyboard_check_pressed(vk_up) || keyboard_check_pressed(vk_down) ||
                keyboard_check_pressed(vk_left) || keyboard_check_pressed(vk_right))
            {
                if (a11y_dlg_last != """" && a11y_ready)
                    external_call(a11y_f_speak, a11y_dlg_last, 1);
            }

            // Enter advances one line. Event 53 is the global left press the game
            // already uses for this, so its own guard (only advance once the line has
            // settled) still applies.
            //
            // Space is deliberately NOT bound here. Dialogue has its own space keypress
            // event that destroys the box outright, skipping the rest of the
            // conversation, and Escape routes to the same thing through Controller. So
            // space and escape already mean 'skip'; binding them here as well would
            // advance and skip in the same frame.
            if (keyboard_check_pressed(vk_enter))
            {
                with (a_dlg)
                    event_perform(ev_mouse, 53);
            }
        }
    }
    else
    {
        a11y_dlg_last = """";
    }

    // ----- Board game -----------------------------------------------------
    // The dice game reached by phoning 637-511. Mechanically it already worked - roll the
    // die, then click your piece to move - but every piece of information in it is drawn
    // and nothing else: the roll is a sprite frame on Board_Dice, your position is where a
    // token sits on the board, and dying just moves that token back. Board_Controller holds
    // all three as plain numbers, and its Create sets them, so they are safe to read.
    if (instance_exists(Board_Controller))
    {
        var a_bc = instance_find(Board_Controller, 0);

        // The roll lands a moment after the die is clicked, when Board_Dice_Roll's
        // animation finishes and sets die_result - so watch for it rather than trying to
        // say anything at the moment of the press.
        if (a_bc.die_result != a11y_die)
        {
            a11y_die = a_bc.die_result;
            if (a11y_die > 0 && a11y_ready)
            {
                // Say where the roll lands and what is waiting there, not just the
                // number. A sighted player reads the destination straight off the board
                // the moment the die settles, and with Dishonesty it is the whole basis
                // for deciding whether to reroll - by which point they must already know.
                var a_rdest = a_bc.position + a11y_die;
                var a_rt = ""Rolled "" + string(a11y_die) + "". Square "" +
                           string(a_bc.position) + "", moving to "" + string(a_rdest);
                var a_rn = instance_number(Position_Pointer);
                for (var a_ri = 0; a_ri < a_rn; a_ri += 1)
                {
                    var a_rpi = instance_find(Position_Pointer, a_ri);
                    if (instance_exists(a_rpi))
                    {
                        if (a_rpi.nbr == a_rdest)
                        {
                            if (a_rpi.object_index == Dead_Pointer)
                            {
                                if (a_rpi.active)
                                    a_rt += "", DEADLY"";
                                else
                                    a_rt += "", safe, crossed out"";
                            }
                            if (a_rpi.object_index == Checkpoint_Pointer)
                                a_rt += "", a checkpoint"";
                        }
                    }
                }
                // The two boards end on different squares - 72 in the flat, 25 in the
                // hospital - so ask the room rather than assuming.
                if (room == Lvl_Flat_Board)
                {
                    if (a_rdest > 72)
                        a_rt += "", the end of the board"";
                }
                else if (a_rdest > 25)
                {
                    a_rt += "", the end of the board"";
                }
                a_rt += "". Press Space to move"";

                // Board_Reroll_Dishonesty has nothing but a Create event - it is a hint
                // sprite, not a button. The reroll itself is pressing the die a second
                // time, which its own handler allows only while Board_Controller.reroll
                // is set. Its visibility is exactly the cue a sighted player gets.
                if (instance_exists(Board_Reroll_Dishonesty))
                {
                    if (Board_Reroll_Dishonesty.visible)
                    {
                        a_rt += "", or Enter on the die to reroll"";
                        if (a11y_warn && !instance_exists(Status_Cheater))
                            a_rt += "", which costs the good ending"";
                    }
                }
                external_call(a11y_f_speak, a_rt + ""."", 1);
            }
        }

        // Whether the game is over. Board_Dice's handler simply 'exit's once this is set,
        // so the die stops responding with no cue whatsoever - it just silently does
        // nothing, which reads exactly like a broken control. The hospital board ends past
        // square 25 (Board_Controller.end_game), the flat one past 72
        // (Flat_Controller.board_game_ended); both flags are set in their own Creates, so
        // both are safe to read.
        var a_bover = a_bc.end_game;
        if (instance_exists(Flat_Controller))
        {
            if (Flat_Controller.board_game_ended)
                a_bover = 1;
        }
        if (a_bover != a11y_bend)
        {
            a11y_bend = a_bover;
            if (a_bover && a11y_ready)
            {
                external_call(a11y_f_speak,
                    ""The board game is over. The die will not respond now - choose Go back to leave."", 1);
            }
        }

        // Space plays a turn from wherever the reader happens to be. A turn is press the
        // die, then press your piece, and those are two entries a long way apart in a
        // room list of over fifty - the board alone is 57 numbered squares - for a game
        // that runs 30-odd turns. Space does whichever of the two the game is waiting
        // for; Enter on either entry still works, and Enter on the die is still how a
        // reroll is taken.
        //
        // Both handlers are plain logic with no mouse test in them, so event_user(0)
        // reaches them exactly as a click would. Board_Dice.playable is NOT bypassed,
        // though: it is 0 until the die and the pawn have been put on the board, and
        // rolling before that would run a turn the mouse game cannot.
        if (keyboard_check_pressed(vk_space) && !instance_exists(Info) &&
            !instance_exists(Dialogue) && !instance_exists(Cutscene) &&
            !instance_exists(Pause) && !instance_exists(Board_Dice_Roll) && !a11y_inv)
        {
            if (a_bover)
            {
                if (a11y_ready)
                    external_call(a11y_f_speak,
                        ""The board game is over. Choose Go back to leave."", 1);
            }
            else if (a_bc.death)
            {
                if (a11y_ready)
                    external_call(a11y_f_speak, ""Back to the checkpoint."", 1);
                with (Board_Button)
                    event_user(0);
            }
            else if (a_bc.die_result > 0)
            {
                with (Board_Button)
                    event_user(0);
            }
            else if (instance_exists(Board_Dice))
            {
                if (Board_Dice.playable)
                {
                    with (Board_Dice)
                        event_user(0);
                }
                else if (a11y_ready)
                {
                    external_call(a11y_f_speak,
                        ""The board is not set up. Use the die and the pawn on it first."", 1);
                }
            }
        }

        if (a_bc.position != a11y_bpos)
        {
            a11y_bpos = a_bc.position;
            if (a11y_ready)
            {
                var a_bt = ""Square "" + string(a11y_bpos);
                if (a_bc.death)
                    a_bt += "", killed. Press Space to go back to the checkpoint"";
                external_call(a11y_f_speak, a_bt + ""."", 0);
            }
        }
    }
    else
    {
        a11y_die = -1;
        a11y_bpos = -1;
    }

    // ----- Phone keypad ---------------------------------------------------
    // Twelve keys is a miserable thing to arrow through, and a phone number is something
    // you already know how to type. The number row and the numpad both press the matching
    // key directly, through its own user event 0 - the same handler a click runs, so the
    // key's press animation, its cooldown and the dial tone all still happen.
    //
    // Hospital_Telephone_Number.number is the dialled string, which it inserts a dash into
    // after three digits, so reading it back is the exact display a sighted player sees.
    if (instance_number(Hospital_Phone_Btn) > 0)
    {
        var a_dig = -1;
        for (var a_d = 0; a_d <= 9; a_d += 1)
        {
            if (keyboard_check_pressed(48 + a_d) || keyboard_check_pressed(96 + a_d))
                a_dig = a_d;
        }
        if (a_dig >= 0)
        {
            var a_ds = string(a_dig);
            var a_pcount = instance_number(Hospital_Phone_Btn);
            for (var a_p4 = 0; a_p4 < a_pcount; a_p4 += 1)
            {
                var a_pb = instance_find(Hospital_Phone_Btn, a_p4);
                if (instance_exists(a_pb))
                {
                    // Checked against the baked set, not just the family: instance_find is
                    // parent-aware and Hospital_Fish_Tank descends from this button too.
                    if (ds_map_exists(a11y_phone, a_pb.object_index))
                    {
                        if (a_pb.nbr == a_ds)
                        {
                            with (a_pb)
                                event_user(0);
                        }
                    }
                }
            }
            if (a11y_ready)
            {
                var a_ptxt = a_ds;
                var a_tni = instance_find(Hospital_Telephone_Number, 0);
                if (instance_exists(a_tni))
                    a_ptxt = a_ds + "". "" + a_tni.number;
                external_call(a11y_f_speak, a_ptxt, 1);
            }
        }
    }

    // ----- Chapter title cards --------------------------------------------
    // The card that comes up after loading a save, and between chapters. Chapter_Screen
    // parents all 15 of them, and every one carries real localised text: 'chapter'
    // ('Chapter I:') and 'description' ('BRIDGE'), set from its Create event by
    // _chapter_names_set or, for the end-of-chapter cards, _chapter_end_names_set. So
    // like the dialogue and unlike the menus there is nothing to scrape.
    var a_chap_on = 0;
    if (instance_exists(Chapter_Screen))
    {
        var a_cs = instance_find(Chapter_Screen, 0);
        if (instance_exists(a_cs))
        {
            a_chap_on = 1;
            var a_ctx = a_cs.chapter + "" "" + a_cs.description;

            // End-of-chapter cards also show what was just unlocked, as one
            // Status_Chapter_Info per item. Those are normally readable only by hovering
            // each icon in turn, so they would otherwise be lost entirely.
            var a_un = instance_number(Status_Chapter_Info);
            if (a_un > 0)
            {
                a_ctx += "", "" + _text_chapter_end_unlocked();
                for (var a_u = 0; a_u < a_un; a_u += 1)
                {
                    var a_ui = instance_find(Status_Chapter_Info, a_u);
                    if (instance_exists(a_ui))
                        a_ctx += "" "" + a_ui.name + "","";
                }
            }

            // These cards end in silence over a loop of birdsong, with nothing saying
            // the game is waiting rather than finished - and the end-of-chapter one is
            // the single most alarming place for that, since it looks like the run just
            // stopped. Say what the card wants.
            a_ctx += "". Press Enter to carry on."";

            if (a_ctx != a11y_chap_last)
            {
                a11y_chap_last = a_ctx;
                a11y_last = """";
                if (a11y_ready)
                    external_call(a11y_f_speak, a_ctx, 1);
            }

            // These cards ignore input for their first 30 frames - alarm 0 is what sets
            // 'active' - so Enter really does nothing at first, exactly as a click would.
            // Going through the card's own space keypress keeps that guard, and keeps
            // whichever room transition this particular card wants. Space and Escape
            // already work here on their own; Enter is the only addition.
            if (keyboard_check_pressed(vk_enter))
            {
                with (a_cs)
                    event_perform(ev_keypress, vk_space);
            }
            else if (keyboard_check_pressed(vk_up) || keyboard_check_pressed(vk_down) ||
                     keyboard_check_pressed(vk_left) || keyboard_check_pressed(vk_right) ||
                     keyboard_check_pressed(vk_f3))
            {
                if (a11y_ready)
                    external_call(a11y_f_speak, a_ctx, 1);
            }
        }
    }
    else
    {
        a11y_chap_last = """";
    }

    // ----- Status screen (S) ----------------------------------------------
    // Status_Menu is a report, not a menu: a grid of Status icons laid out six to a row by
    // its own user event 1, three ending indicators, and a Back button. Each Status
    // carries a properly localised 'name' that the game only reveals by hovering the icon,
    // and the endings are conveyed purely by a padlock sprite drawn over them.
    //
    // Its contents are positional rather than listed, so the focus indexes them
    // arithmetically instead of building a list: statuses first, then Good, Neutral and
    // Bad, then Back.
    var a_stat_on = 0;
    if (instance_exists(Status_Menu))
    {
        a_stat_on = 1;
        var a_stn = instance_number(Status);
        var a_stc = a_stn + 4;

        var a_stspeak = 0;
        var a_stpre = """";
        if (!a11y_st_on)
        {
            a11y_st_on = 1;
            a11y_st_idx = 0;
            a_stpre = ""Status. "" + string(a_stn) + "" carried. "";
            a_stspeak = 1;
        }
        if (a11y_st_idx >= a_stc)
        {
            a11y_st_idx = a_stc - 1;
            a_stspeak = 1;
        }

        var a_stm = 0;
        if (keyboard_check_pressed(vk_down) || keyboard_check_pressed(vk_right))
            a_stm = 1;
        if (keyboard_check_pressed(vk_up) || keyboard_check_pressed(vk_left))
            a_stm = -1;
        if (a_stm != 0)
        {
            a11y_st_idx = ((a11y_st_idx + a_stm) + a_stc) mod a_stc;
            a_stspeak = 1;
        }
        if (keyboard_check_pressed(vk_f3))
            a_stspeak = 1;

        if (keyboard_check_pressed(vk_enter))
        {
            // Only Back does anything - the rest of the screen is a report. Escape and a
            // right click already close it, both handled by the game itself.
            if (a11y_st_idx == (a_stc - 1) && instance_exists(Status_Menu_Resume))
            {
                with (Status_Menu_Resume)
                    event_perform(ev_mouse, ev_left_press);
                a_stspeak = 0;
            }
            else
            {
                a_stspeak = 1;
            }
        }

        if (a_stspeak && a11y_ready)
        {
            var a_stt = ""Back"";
            if (a11y_st_idx < a_stn)
            {
                var a_sti2 = instance_find(Status, a11y_st_idx);
                a_stt = """";
                if (instance_exists(a_sti2))
                    a_stt = a_sti2.name;
            }
            else if (a11y_st_idx == a_stn)
            {
                a_stt = ""Good ending"";
                if (ending_good)
                    a_stt += "", still possible"";
                else
                    a_stt += "", lost"";
            }
            else if (a11y_st_idx == (a_stn + 1))
            {
                a_stt = ""Neutral ending"";
                if (ending_neutral)
                    a_stt += "", still possible"";
                else
                    a_stt += "", lost"";
            }
            else if (a11y_st_idx == (a_stn + 2))
            {
                a_stt = ""Bad ending"";
                if (ending_bad)
                    a_stt += "", still possible"";
                else
                    a_stt += "", lost"";
            }
            a_stt = a_stpre + string(a11y_st_idx + 1) + "" of "" + string(a_stc) + "", "" + a_stt;
            external_call(a11y_f_speak, a_stt, 1);
        }
    }
    else
    {
        a11y_st_on = 0;
    }

    // ----- Inventory ------------------------------------------------------
    // The inventory is not a screen you open: Inventory_Controller simply draws up to
    // eight carried items down one side of the play area, and clicking one makes it the
    // active item. I therefore toggles a reading mode that walks every item the player
    // is carrying - not just the eight on show - scrolling the game's own window to
    // follow so the display keeps matching what is being read.
    var a_inv_ok = 0;
    if (instance_exists(Inventory_Controller) && !a_dlg_on && !a_stat_on && !a_info_on &&
        !instance_exists(Pause) && !instance_exists(BLOCK_EXIT) &&
        !instance_exists(Chapter_Screen) && !instance_exists(NO_INVENTORY) &&
        !instance_exists(Item_Destroyed))
    {
        a_inv_ok = 1;
    }

    // Drop out on our own if the inventory stops being available - a dialogue starts,
    // the pause menu opens, the last item is used - rather than trapping the arrows.
    if (a11y_inv && (!a_inv_ok || instance_number(Item) == 0))
        a11y_inv = 0;

    var a_inv_say = 0;
    var a_inv_pre = """";
    if (keyboard_check_pressed(73))   // I. ord() is absent from this game's FUNC chunk.
    {
        if (a11y_inv)
        {
            a11y_inv = 0;
            if (a11y_ready)
                external_call(a11y_f_speak, ""Inventory closed."", 1);
        }
        else if (a_inv_ok && instance_number(Item) > 0)
        {
            a11y_inv = 1;
            a11y_inv_idx = 0;
            // A right click hides the whole interface, items included. Opening the
            // reader's inventory with the items invisible would be a lie, so put it back.
            show_gui = 1;
            a_inv_pre = ""Inventory, "" + string(instance_number(Item)) + "" items. "";
            a_inv_say = 1;
        }
        else if (a11y_ready)
        {
            if (a_inv_ok)
                external_call(a11y_f_speak, ""You are not carrying anything."", 1);
            else
                external_call(a11y_f_speak, ""Inventory not available here."", 1);
        }
    }

    if (a11y_inv)
    {
        var a_icount = instance_number(Item);
        if (a11y_inv_idx >= a_icount)
        {
            // Using an item destroys it, so the focus can outrun the list.
            a11y_inv_idx = a_icount - 1;
            a_inv_say = 1;
        }

        var a_imove = 0;
        if (keyboard_check_pressed(vk_down) || keyboard_check_pressed(vk_right))
            a_imove = 1;
        if (keyboard_check_pressed(vk_up) || keyboard_check_pressed(vk_left))
            a_imove = -1;
        if (a_imove != 0)
        {
            a11y_inv_idx = ((a11y_inv_idx + a_imove) + a_icount) mod a_icount;
            a_inv_say = 1;
        }
        if (keyboard_check_pressed(vk_f3))
            a_inv_say = 1;

        // instance_find over Item gives the same order Inventory_Controller draws in,
        // so index N here really is the Nth row on screen.
        var a_item = instance_find(Item, a11y_inv_idx);
        if (instance_exists(a_item))
        {
            var a_ivc = instance_find(Inventory_Controller, 0);
            if (instance_exists(a_ivc))
            {
                if (a11y_inv_idx < a_ivc.shift)
                    a_ivc.shift = a11y_inv_idx;
                if (a11y_inv_idx > (a_ivc.shift + 7))
                    a_ivc.shift = a11y_inv_idx - 7;
            }

            // Enter is a click. Controller's End Step fires user event 0 on whatever the
            // mouse is over when the left button goes down, so calling it directly is
            // the genuine article: it makes this the active item, or clears it if it
            // already was, and plays the game's own click sound.
            if (keyboard_check_pressed(vk_enter))
            {
                with (a_item)
                    event_user(0);
                a_inv_say = 1;
            }

            // The instance test is repeated because Enter above ran the item's own user
            // event, and object code can destroy the object.
            if (a_inv_say && a11y_ready && instance_exists(a_item))
            {
                // Items, unlike the menus, carry real localised text.
                var a_inm = a_item.name;
                if (ds_map_exists(a11y_iname, a_item.object_index))
                    a_inm += ds_map_find_value(a11y_iname, a_item.object_index);
                var a_itxt = a_inv_pre + string(a11y_inv_idx + 1) + "" of "" +
                             string(a_icount) + "", "" + a_inm;
                if (a_item.active)
                    a_itxt += a_item.active_txt;
                external_call(a11y_f_speak, a_itxt, 1);
            }
        }
    }

    ds_list_clear(a11y_ins);
    ds_list_clear(a11y_nm);
    ds_list_clear(a11y_cx);
    ds_list_clear(a11y_cy);
    ds_list_clear(a11y_sub);

    // Opening the slot screen or the options panel does NOT destroy the title buttons -
    // Main_Menu_Button.Step_0 just early-exits while BLOCK_EXIT (or a Pause) is around, so
    // they sit there visible and dead. Including them mixed five unreachable items into
    // the slot list and scrambled the reading order. Mirror the game's own test.
    var a_block = (instance_exists(BLOCK_EXIT) || instance_exists(Pause));

    for (var a_s = 0; a_s < ds_list_size(a11y_scan); a_s += 1)
    {
        var a_ob = ds_list_find_value(a11y_scan, a_s);
        if (a_ob == Main_Menu_Button && a_block)
            continue;
        var a_num = instance_number(a_ob);
        for (var a_k = 0; a_k < a_num; a_k += 1)
        {
            var a_in = instance_find(a_ob, a_k);
            if (!instance_exists(a_in))
                continue;
            if (a_in.visible == 0)
                continue;
            var a_nm = object_get_name(a_in.object_index);
            if (!ds_map_exists(a11y_lbl, a_nm))
                continue;

            if (a_nm == ""Game_Menu_Volume"")
            {
                // One object, three sliders. Their rows come from Volume's Create event:
                // sound at y-15, music at y+40, effects at y+95.
                ds_list_add(a11y_ins, a_in); ds_list_add(a11y_nm, a_nm);
                ds_list_add(a11y_cy, a_in.y - 15); ds_list_add(a11y_cx, a_in.x); ds_list_add(a11y_sub, 0);
                ds_list_add(a11y_ins, a_in); ds_list_add(a11y_nm, a_nm);
                ds_list_add(a11y_cy, a_in.y + 40); ds_list_add(a11y_cx, a_in.x); ds_list_add(a11y_sub, 1);
                ds_list_add(a11y_ins, a_in); ds_list_add(a11y_nm, a_nm);
                ds_list_add(a11y_cy, a_in.y + 95); ds_list_add(a11y_cx, a_in.x); ds_list_add(a11y_sub, 2);
            }
            else
            {
                ds_list_add(a11y_ins, a_in);
                ds_list_add(a11y_nm, a_nm);
                ds_list_add(a11y_cy, a_in.bbox_top + ((a_in.bbox_bottom - a_in.bbox_top) / 2));
                ds_list_add(a11y_cx, a_in.bbox_left + ((a_in.bbox_right - a_in.bbox_left) / 2));
                ds_list_add(a11y_sub, -1);
            }
        }
    }
    // One row of ours, at the bottom of the title screen and the pause menu. A sighted
    // player sees the menu they always saw; this only exists in the reader, which is where
    // somebody using the reader would go looking for the reader's own settings. Its cy is
    // past the bottom of any real button, so the sort below always leaves it last.
    if (instance_exists(Game_Menu) || (room == Lvl_Main_Menu && !a_block))
    {
        if (ds_list_size(a11y_ins) > 0)
        {
            ds_list_add(a11y_ins, -4);
            ds_list_add(a11y_nm, ""@a11y"");
            ds_list_add(a11y_cy, 99999);
            ds_list_add(a11y_cx, 0);
            ds_list_add(a11y_sub, -1);
        }
    }
    else if (a11y_set)
    {
        // The menu it hangs off has gone - the game was resumed with a click, or the
        // title screen moved on. Leaving the screen open would leave the world reader
        // standing aside for a screen that is no longer reachable.
        a11y_set = 0;
    }
    a11y_n = ds_list_size(a11y_ins);

    // Reading order: down the screen, then across. Rows are fuzzy, so buttons within 24
    // units of each other count as one row and are ordered left to right.
    for (var a_i = 1; a_i < a11y_n; a_i += 1)
    {
        var a_j = a_i;
        while (a_j > 0)
        {
            var a_cy0 = ds_list_find_value(a11y_cy, a_j);
            var a_cy1 = ds_list_find_value(a11y_cy, a_j - 1);
            var a_swap = 0;
            if (abs(a_cy0 - a_cy1) > 24)
            {
                if (a_cy0 < a_cy1)
                    a_swap = 1;
            }
            else if (ds_list_find_value(a11y_cx, a_j) < ds_list_find_value(a11y_cx, a_j - 1))
            {
                a_swap = 1;
            }
            if (a_swap == 0)
                break;

            var a_t;
            a_t = ds_list_find_value(a11y_ins, a_j);
            ds_list_replace(a11y_ins, a_j, ds_list_find_value(a11y_ins, a_j - 1));
            ds_list_replace(a11y_ins, a_j - 1, a_t);
            a_t = ds_list_find_value(a11y_nm, a_j);
            ds_list_replace(a11y_nm, a_j, ds_list_find_value(a11y_nm, a_j - 1));
            ds_list_replace(a11y_nm, a_j - 1, a_t);
            a_t = ds_list_find_value(a11y_cy, a_j);
            ds_list_replace(a11y_cy, a_j, ds_list_find_value(a11y_cy, a_j - 1));
            ds_list_replace(a11y_cy, a_j - 1, a_t);
            a_t = ds_list_find_value(a11y_cx, a_j);
            ds_list_replace(a11y_cx, a_j, ds_list_find_value(a11y_cx, a_j - 1));
            ds_list_replace(a11y_cx, a_j - 1, a_t);
            a_t = ds_list_find_value(a11y_sub, a_j);
            ds_list_replace(a11y_sub, a_j, ds_list_find_value(a11y_sub, a_j - 1));
            ds_list_replace(a11y_sub, a_j - 1, a_t);
            a_j -= 1;
        }
    }

    var a_speak = 0;
    var a_pre = """";

    var a_sig = """";
    if (instance_exists(Credits))
    {
        // The credits are a whole room whose text is a scrolling picture, so there is
        // nothing to enumerate and nothing readable. Say what the screen is and how to
        // leave it - any key, a click, or simply waiting all return to the menu.
        a_sig = ""@credits"";
    }
    else
    {
        for (var a_i = 0; a_i < a11y_n; a_i += 1)
            a_sig += ds_list_find_value(a11y_nm, a_i) + string(ds_list_find_value(a11y_sub, a_i)) + ""|"";
    }

    if (a_sig != a11y_sig)
    {
        a11y_sig = a_sig;
        a11y_idx = -1;
        if (a_sig == ""@credits"")
        {
            a11y_last = """";
            if (a11y_ready)
                external_call(a11y_f_speak, ""Credits. Press any key to return."", 1);
        }
        else if (a11y_n > 0)
        {
            a11y_idx = 0;
            a_pre = string(a11y_n) + "" items. "";
            a_speak = 1;
        }
    }

    // ----- The settings screen --------------------------------------------
    //
    // Audio only: nothing is drawn and nothing on screen changes. It holds the arrow keys
    // and Enter for as long as it is open, so the menu reader below stands aside; Escape
    // closes it, from the prepend on Controller's KeyPress 27.
    //
    // Left and Right flip a setting as well as Enter, because a screen reader cancels its
    // own speech when Enter goes down and the arrow keys are the ones that never do - the
    // wait below is the same delay the world reader uses, and for the same reason.
    if (a11y_set)
    {
        var a_stmv = 0;
        if (keyboard_check_pressed(vk_down))
            a_stmv = 1;
        if (keyboard_check_pressed(vk_up))
            a_stmv = -1;

        var a_stsay = 0;
        if (a_stmv != 0)
        {
            a11y_set_idx = ((a11y_set_idx + a_stmv) + 5) mod 5;
            a_stsay = 1;
        }
        if (keyboard_check_pressed(vk_f3))
            a_stsay = 1;

        if (keyboard_check_pressed(vk_enter) || keyboard_check_pressed(vk_space) ||
            keyboard_check_pressed(vk_left) || keyboard_check_pressed(vk_right))
        {
            if (a11y_set_idx == 0)
            {
                if (a11y_warn)
                    a11y_warn = 0;
                else
                    a11y_warn = 1;
                a11y_setsave = 1;
                a_stsay = 1;
            }
            else if (a11y_set_idx == 1)
            {
                if (a11y_hint)
                    a11y_hint = 0;
                else
                    a11y_hint = 1;
                a11y_setsave = 1;
                a_stsay = 1;
            }
            else if (a11y_set_idx == 2)
            {
                if (a11y_area)
                    a11y_area = 0;
                else
                    a11y_area = 1;
                a11y_setsave = 1;
                a_stsay = 1;
            }
            else if (a11y_set_idx == 3)
            {
                if (a11y_junk)
                    a11y_junk = 0;
                else
                    a11y_junk = 1;
                a11y_setsave = 1;
                a_stsay = 1;
            }
            else
            {
                // Back. Nothing is said here on purpose: clearing the signature makes the
                // menu underneath read itself out again on the NEXT frame, which is both
                // the right answer and safely past the Enter that asked for it.
                a11y_set = 0;
                a11y_sig = """";
            }
        }

        if (a_stsay)
            a11y_set_wait = 3;

        if (a11y_set_wait > 0 && a11y_set)
        {
            a11y_set_wait -= 1;
            if (a11y_set_wait == 0)
            {
                if (keyboard_check(vk_enter) || keyboard_check(vk_space))
                {
                    a11y_set_wait = 1;
                }
                else if (a11y_ready)
                {
                    var a_stt = """";
                    if (a11y_set_idx == 0)
                    {
                        a_stt = ""Warn about danger"";
                        if (a11y_warn)
                            a_stt += "", on"";
                        else
                            a_stt += "", off"";
                    }
                    else if (a11y_set_idx == 1)
                    {
                        a_stt = ""Hints about what to use"";
                        if (a11y_hint)
                            a_stt += "", on"";
                        else
                            a_stt += "", off"";
                    }
                    else if (a11y_set_idx == 2)
                    {
                        // a11y_area is the strip, so it reads the other way round.
                        a_stt = ""Say the area before every name"";
                        if (a11y_area)
                            a_stt += "", off"";
                        else
                            a_stt += "", on"";
                    }
                    else if (a11y_set_idx == 3)
                    {
                        a_stt = ""Hide blood, mess and rubble"";
                        if (a11y_junk)
                            a_stt += "", on"";
                        else
                            a_stt += "", off"";
                    }
                    else
                    {
                        a_stt = ""Back"";
                    }

                    var a_stp = """";
                    if (a11y_set_hdr)
                    {
                        a_stp = ""Accessibility settings. 5 items. "";
                        a11y_set_hdr = 0;
                    }
                    external_call(a11y_f_speak,
                        a_stp + string(a11y_set_idx + 1) + "" of 5, "" + a_stt, 1);
                }
            }
        }
    }

    // Dialogue and the inventory own the arrow keys and Enter while they are up, so the
    // menu navigation stands aside rather than acting on the same keypress twice. So does
    // the settings screen above, which is holding the same keys.
    if (a11y_n > 0 && !a11y_inv && !a_dlg_on && !a_chap_on && !a11y_set)
    {
        var a_kind = 0;
        if (a11y_idx >= 0)
            a_kind = ds_map_find_value(a11y_act, ds_list_find_value(a11y_nm, a11y_idx));

        // Up/Down always move. Left/Right adjust a slider or flip a toggle, and only fall
        // back to moving when the focused item is an ordinary button - otherwise there
        // would be no way to change a value from the keyboard at all.
        var a_move = 0;
        var a_adjust = 0;
        if (keyboard_check_pressed(vk_down))
            a_move = 1;
        if (keyboard_check_pressed(vk_up))
            a_move = -1;
        if (keyboard_check_pressed(vk_right))
        {
            if (a_kind == 2 || a_kind == 3)
                a_adjust = 1;
            else
                a_move = 1;
        }
        if (keyboard_check_pressed(vk_left))
        {
            if (a_kind == 2 || a_kind == 3)
                a_adjust = -1;
            else
                a_move = -1;
        }

        if (a_move != 0)
        {
            if (a11y_idx < 0)
                a11y_idx = 0;
            else
                a11y_idx = ((a11y_idx + a_move) + a11y_n) mod a11y_n;
            a_speak = 1;
        }

        if (a_adjust != 0 && a11y_idx >= 0)
        {
            var a_in = ds_list_find_value(a11y_ins, a11y_idx);
            var a_nm = ds_list_find_value(a11y_nm, a11y_idx);
            var a_sb = ds_list_find_value(a11y_sub, a11y_idx);
            if (instance_exists(a_in))
            {
                if (a_kind == 3)
                {
                    // Toggles are flipped by their own mouse handler, so let the game do it.
                    with (a_in)
                        event_perform(ev_mouse, ev_left_press);
                }
                else
                {
                    // Sliders run 0..140. Step by 10%, then apply through the object's own
                    // user event 0 - the same call the game makes when you drag it.
                    // Volume's user event only repositions the SOUND slider, so the music
                    // and effects handles have to be moved here or they visibly stick.
                    var a_stp = a_adjust * 14;
                    if (a_nm == ""Game_Menu_Volume"")
                    {
                        if (a_sb == 0)
                        {
                            a_in.length = min(max(a_in.length + a_stp, 0), 140);
                            Game_Menu_Volume_Slider.x = (a_in.x - 65) + a_in.length;
                        }
                        else if (a_sb == 1)
                        {
                            a_in.length_mus = min(max(a_in.length_mus + a_stp, 0), 140);
                            Game_Menu_Volume_Music_Slider.x = (a_in.x - 65) + a_in.length_mus;
                        }
                        else
                        {
                            a_in.length_sfx = min(max(a_in.length_sfx + a_stp, 0), 140);
                            Game_Menu_Volume_SFX_Slider.x = (a_in.x - 65) + a_in.length_sfx;
                        }
                    }
                    else
                    {
                        a_in.length = min(max(a_in.length + a_stp, 0), 140);
                        Game_Menu_Brightness_Slider.x = (a_in.x - 65) + a_in.length;
                    }
                    with (a_in)
                        event_user(0);
                }
                a11y_last = """";   // the value changed, so re-announce even if the name did not
                a_speak = 1;
            }
        }

        if (keyboard_check_pressed(vk_f3))
        {
            a_speak = 1;
            a11y_last = """";
        }

        if ((keyboard_check_pressed(vk_enter) || keyboard_check_pressed(vk_space)) && a11y_idx >= 0)
        {
            var a_in = ds_list_find_value(a11y_ins, a11y_idx);
            if (ds_list_find_value(a11y_nm, a11y_idx) == ""@a11y"")
            {
                // Our own row, and the only one in any menu with no instance behind it.
                // The settings screen takes the keys from here until it is closed.
                a11y_set = 1;
                a11y_set_idx = 0;
                a11y_set_hdr = 1;
                a11y_set_wait = 3;
            }
            else if (instance_exists(a_in))
            {
                if (a_kind == 2)
                {
                    // Enter on a slider does nothing useful; say the value instead.
                    a11y_last = """";
                    a_speak = 1;
                }
                else
                {
                    // Two button mechanisms in this game: the title screen and the slot
                    // buttons act on user event 0, the pause menu on a real mouse press.
                    if (a_kind == 0)
                    {
                        with (a_in)
                            event_user(0);
                    }
                    else
                    {
                        with (a_in)
                            event_perform(ev_mouse, ev_left_press);
                    }
                    if (a_kind == 3)
                    {
                        // A toggle stays put, so report its new state rather than falling
                        // silent as if nothing had happened.
                        a11y_last = """";
                        a_speak = 1;
                    }
                    else
                    {
                        a11y_sig = """";   // the screen probably changed; re-read it
                        a_speak = 0;
                    }
                }
            }
        }
    }

    // ----- Story events ---------------------------------------------------
    //
    // The game tells its story in pictures and screen shakes, and none of that was ever
    // going to reach a player who cannot see it. Two sweeps, both baked - see the injector
    // for the tables and for why the flag reads are generated GML rather than a lookup.
    //
    // At most ONE event is announced per frame, and everything else that fired on the same
    // frame is recorded silently. Several of these arrive in pairs - smashing the pram doll
    // sets doll_broken and device_broken together, the scarecrow's attack creates a
    // cutscene and a screen shake in the same instant - and two sentences fighting over
    // the speech channel is worse than the second one being dropped.
    //
    // Skipped entirely while a Pause exists, for the reason the pick-up diff further up is:
    // Pause's Create calls instance_deactivate_all and reactivates only Controller, Music,
    // Light and Screen. Every chapter controller and every cutscene therefore genuinely
    // stops existing for as long as the pause menu, the options screen or the status
    // screen is open - so both sweeps would read every flag back as 0, record that, and
    // then replay the whole story one line at a time as soon as the menu closed.
    var a_evsaid = 0;
    if (!instance_exists(Pause))
    {
    for (var a_ei = 0; a_ei < ds_list_size(a11y_evo); a_ei += 1)
    {
        var a_enow = 0;
        if (instance_number(ds_list_find_value(a11y_evo, a_ei)) > 0)
            a_enow = 1;
        if (a_enow && ds_list_find_value(a11y_evon, a_ei) == 0)
        {
            if (a11y_ready && a_evsaid == 0)
            {
                external_call(a11y_f_speak, ds_list_find_value(a11y_evt, a_ei), 1);
                a_evsaid = 1;
            }
        }
        ds_list_replace(a11y_evon, a_ei, a_enow);
    }

    // The chapter controllers keep the whole story as a set of flags, and setting one is
    // exactly what something happened means. Watched on the 0-to-set edge.
    var a_fv = 0;
    var a_fi = -4;
/*FLAGSTATE*/

    // The five stars in the crypt. Graveyard_Controller.stars counts up one at a time and
    // nothing says so - the only feedback is a star appearing on a sign in another room -
    // so this one is counted off rather than announced once like the flags above.
    var a_gci = instance_find(Graveyard_Controller, 0);
    if (instance_exists(a_gci))
    {
        var a_str = a_gci.stars;
        if (a_str != a11y_stars)
        {
            if (a11y_ready && a11y_stars >= 0 && a_str > a11y_stars && a_evsaid == 0)
            {
                if (a_str >= 5)
                {
                    external_call(a11y_f_speak,
                        ""All five stars are lit. Something has opened."", 1);
                }
                else
                {
                    external_call(a11y_f_speak,
                        ""Star "" + string(a_str) + "" of five."", 1);
                }
                a_evsaid = 1;
            }
            a11y_stars = a_str;
        }
    }
    else
    {
        a11y_stars = -1;
    }
    }

    // Words appearing on the hospital toilet mirror. Not an event-table row, because
    // Hospital_WC_Mirror_Text is in the room from the start and only becomes VISIBLE when
    // the eye comes out of the plughole - and the table watches for an instance turning
    // up, which for this one is the wrong edge entirely.
    var a_mri = instance_find(Hospital_WC_Mirror_Text, 0);
    if (instance_exists(a_mri) && !instance_exists(Pause))
    {
        if (a_mri.visible)
        {
            if (a11y_mirror == 0)
            {
                a11y_mirror = 1;
                if (a11y_ready && a_evsaid == 0)
                {
                    external_call(a11y_f_speak,
                        ""Words have appeared on the mirror, in blood: I am glad I could HELP."", 1);
                    a_evsaid = 1;
                }
            }
        }
        else
        {
            a11y_mirror = 0;
        }
    }
    else if (!instance_exists(Pause))
    {
        a11y_mirror = 0;
    }

    // ----- The film inside the VHS tape -----------------------------------
    //
    // Putting the tape into the canteen television does not play a video: it drops the
    // player into a fourteen-step point-and-click of its own, in a kitchen, as the clown.
    // Mechanically it is fine from the keyboard - every object in it is an Interactive
    // _Object that sets playable, and only the one thing you are meant to press is ever
    // playable, so the scene list is short and correct. What it has none of is words.
    // Nothing in the whole film is text: no dialogue but one note, no interface, and each
    // press starts the clown WALKING for two or three seconds before the next thing
    // becomes usable. From the keyboard that reads as a control that did nothing, twice
    // over - once when the press is silent and again when the list refuses to change.
    //
    // Little_Nightmares_Clown_02.step is the film's own progress counter, 0 to 14,
    // incremented the instant he arrives and immediately used to reveal whatever comes
    // next. Reading it off is therefore exactly the beat a sighted player is watching for.
    // Only an INCREASE is announced: the room is persistent, but the close-ups are
    // separate rooms and this costs nothing to be careful about.
    var a_nmc = instance_find(Little_Nightmares_Clown_02, 0);
    if (instance_exists(a_nmc) && !instance_exists(Pause))
    {
        if (a_nmc.step > a11y_nm_step)
        {
            var a_nms = """";
            if (a_nmc.step == 1)
                a_nms = ""He crosses to the wall. There is a note pinned to it."";
            if (a_nmc.step == 2)
                a_nms = ""He reads the note."";
            if (a_nmc.step == 3)
                a_nms = ""He opens the oven."";
            if (a_nmc.step == 4)
                a_nms = ""He opens the cupboard. There is a tin of beans in it."";
            if (a_nmc.step == 5)
                a_nms = ""He sets the pan on the cooker."";
            if (a_nmc.step == 6)
                a_nms = ""He drops the fish on the chopping board and picks up the knife."";
            if (a_nmc.step == 7)
                a_nms = ""He cuts the fish open. There is something inside it."";
            if (a_nmc.step == 8)
                a_nms = ""He carries it over to the worktop."";
            if (a_nmc.step == 9)
                a_nms = ""He tips the worms into the pan."";
            if (a_nmc.step == 10)
                a_nms = ""He pulls the drawer open. There is a bottle in it."";
            if (a_nmc.step == 11)
                a_nms = ""He stirs the bottle into the pan."";
            if (a_nmc.step == 12)
                a_nms = ""He goes out and shuts the door behind him."";
            if (a_nmc.step == 13)
                a_nms = ""The other clown is slumped over the table, not moving."";
            if (a_nmc.step == 14)
                a_nms = ""The window goes out. That is the end of the film."";
            a11y_nm_step = a_nmc.step;
            if (a_nms != """" && a11y_ready && a_evsaid == 0)
            {
                external_call(a11y_f_speak, a_nms, 1);
                a_evsaid = 1;
            }
        }
    }
    else if (instance_number(Little_Nightmares_Effects) == 0 && !instance_exists(Pause))
    {
        // Only when the film is over, not merely when the clown is off screen. Two of the
        // film's four rooms are close-ups he does not stand in, and resetting there made
        // stepping back into the kitchen repeat the last thing he did.
        // Little_Nightmares_Effects is the persistent television-static object and lives
        // for exactly as long as the film does.
        a11y_nm_step = -1;
    }

    // ----- The scene ------------------------------------------------------
    // Everything clickable in a room is a child of Interactive_Object - asset 18, which is
    // literally what _interactive_get_type returns. Controller's End Step finds the one
    // under the pointer with a collision_point sweep, fires user event 1 on it to set the
    // cursor, and user event 0 on a click. Neither needs the pointer to be anywhere in
    // particular, so this walks the same set from the keyboard.
    //
    // The game refuses all interaction while an Info popup, a Dialogue or a Cutscene is up
    // - _interactive_get_type returns -4 then - so mirror that exactly rather than offering
    // the player things the game will ignore.
    var a_world = 0;
    if (!a_dlg_on && !a_chap_on && !a11y_inv && !a11y_set && a11y_n == 0 &&
        !instance_exists(Info) && !instance_exists(Cutscene) &&
        !instance_exists(Pause) && !instance_exists(Room_Translation))
    {
        a_world = 1;
    }

    // ----- Blocked by a cutscene ------------------------------------------
    //
    // Mirroring the game exactly is right for Info, Dialogue, Pause and the rest: each of
    // those has its own reader here, so going quiet hands over cleanly. A Cutscene has no
    // reader, and that turned out to be a trap rather than a handover.
    //
    // Lvl_Flat_Bathroom is the case that showed it up. Bathroom_Dark_Back is a Cutscene
    // that draws a near-opaque black rectangle over the screen and destroys itself only
    // when Flat_Controller.bathroom_light is set, so in an unlit bathroom it is there for
    // as long as you are. While it exists _interactive_get_type returns -4, so the game
    // refuses every object in the room - and the one thing that still works is
    // Bathroom_Dark_Back's OWN global-left-press handler, which walks you back out.
    //
    // A sighted player sees a black screen and clicks. With nothing announced and no key
    // that produces a click, this was a silent dead end you could only leave by reloading.
    //
    // So: say that the room is blocked, and make Enter do what the click does. Scoped to
    // Cutscene instances rather than fired globally - that is the object class holding
    // everything up, and a real broadcast would reach handlers that have nothing to do
    // with it.
    var a_cut = 0;
    var a_cutd = """";   // this cutscene's own description, where the table has one
    var a_cutk = 0;    // whether a key can actually dismiss it
    if (instance_exists(Cutscene) && !instance_exists(Info) && !instance_exists(Dialogue) &&
        !instance_exists(Pause) && !a_chap_on && !a11y_inv)
    {
        a_cut = 1;

        // A transition is not something to wait through, it is the thing you just asked
        // for working. The mirror in the flats spends about half a second in one, and
        // saying nothing can be used yet to a player who has just used the mirror is
        // exactly backwards. Any translation instance present makes the whole state one.
        var a_ccn = instance_number(Cutscene);
        for (var a_cq = 0; a_cq < a_ccn; a_cq += 1)
        {
            var a_cqi = instance_find(Cutscene, a_cq);
            if (instance_exists(a_cqi))
            {
                if (ds_map_exists(a11y_trans, a_cqi.object_index))
                    a_cut = 2;

                // What this one IS, where the story-event table knows. The sweep above has
                // already said it on the frame the instance appeared, so nothing is said
                // again here - it is kept so F3 can repeat it while the scene is still up.
                if (ds_map_exists(a11y_ev, a_cqi.object_index))
                    a_cutd = ds_map_find_value(a11y_ev, a_cqi.object_index);

                // And whether a keypress does anything to it. THREE of the seventy-four
                // cutscenes in this game have a global left-press handler; the other
                // seventy-one ignore a click entirely and end on their own timer. Offering
                // Enter for all of them was the whole of the bug: the mod told the player
                // to press a key at exactly the moments where no key does anything, over
                // and over, through every scripted scene in the game. Baked by looking for
                // an actual Mouse 53 handler - see the injector.
                if (ds_map_exists(a11y_cutkey, a_cqi.object_index))
                    a_cutk = 1;
            }
        }
    }
    if (a_cut != a11y_cut_last)
    {
        a11y_cut_last = a_cut;
        if (a11y_ready)
        {
            if (a_cut == 2)
            {
                external_call(a11y_f_speak, ""Transporting."", 1);
            }
            else if (a_cut == 1 && a_cutk)
            {
                external_call(a11y_f_speak,
                    ""Nothing here can be used yet. Press Enter to carry on."", 1);
            }
            else if (a_cut == 1 && a_cutd == """" && a_evsaid == 0)
            {
                // Nothing is known about this one and no key will help. Say that the room
                // is busy rather than that it is broken, and do not offer a control.
                external_call(a11y_f_speak, ""Something is happening. Wait."", 1);
            }
        }
    }

    // Only the three that have a handler take a key. Pressing Enter at any of the others
    // did nothing, which is exactly what the offer of it made the mod look like.
    if (a_cut == 1 && a_cutk)
    {
        if (keyboard_check_pressed(vk_enter) || keyboard_check_pressed(vk_space))
        {
            with (Cutscene)
                event_perform(ev_mouse, 53);
        }
    }
    if (a_cut == 1 && keyboard_check_pressed(vk_f3) && a11y_ready)
    {
        // F3 during a scene repeats what the scene is, which is the one thing worth
        // hearing twice while waiting for it to finish.
        if (a_cutd != """")
            external_call(a11y_f_speak, a_cutd, 1);
        else if (a_cutk)
            external_call(a11y_f_speak,
                ""Nothing here can be used yet. Press Enter to carry on."", 1);
        else
            external_call(a11y_f_speak, ""Something is happening. Wait."", 1);
    }

    if (!a_world)
    {
        // Deliberately keeps the remembered room and instance. Coming back from a
        // conversation, the menu or the status screen should drop the player where they
        // left off, in silence - not re-read the whole room at them.
        a_world = 0;
    }
    else
    {
        // A and D step through what the list is showing. A room holds well over thirty
        // interactive objects and most of them are scenery you can only look at, which
        // buries the handful that matter.
        var a_modechg = 0;
        if (keyboard_check_pressed(68))   // D, forwards
        {
            a11y_w_mode = (a11y_w_mode + 1) mod 4;
            a_modechg = 1;
        }
        if (keyboard_check_pressed(65))   // A, backwards
        {
            a11y_w_mode = ((a11y_w_mode - 1) + 4) mod 4;
            a_modechg = 1;
        }

        // GameMaker runs collision events AFTER Step and before End Step. The game's own
        // clicks happen in Controller's End STEP, so by then the paperclip's collision with
        // Bridge_06_Mask has already cleared Bridge_Coin_Item.lightning. This tick lives in
        // the normal Step - one phase earlier - so it sees the value the coin's own End Step
        // reset to 1 on the previous frame. The coin therefore read as electrified and
        // taking it shocked the player even with the clip correctly placed.
        //
        // Re-evaluate that one collision here so both the readout and Enter agree with what
        // the game is about to do a moment later in the same frame.
        if (instance_exists(Bridge_Clip) && instance_exists(Bridge_Coin_Item) &&
            instance_exists(Bridge_06_Mask))
        {
            with (Bridge_Clip)
            {
                if (place_meeting(x, y, Bridge_06_Mask))
                {
                    with (Bridge_Coin_Item)
                        lightning = 0;
                }
            }
        }

        ds_list_clear(a11y_wi);
        ds_list_clear(a11y_wnm);
        ds_list_clear(a11y_wx);

        // Items are Interactive_Object children too, but they are the inventory strip and
        // belong to the I reader, not the scene.
        var a_skip = ds_map_create();
        var a_qn = instance_number(Item);
        for (var a_q = 0; a_q < a_qn; a_q += 1)
        {
            var a_qi = instance_find(Item, a_q);
            if (instance_exists(a_qi))
                ds_map_add(a_skip, a_qi, 1);
        }

        var a_tot = instance_number(Interactive_Object);
        for (var a_w = 0; a_w < a_tot; a_w += 1)
        {
            var a_wi2 = instance_find(Interactive_Object, a_w);
            if (instance_exists(a_wi2))
            {
                var a_wo = a_wi2.object_index;
                var a_wok = 1;

                // The interface buttons all have their own keys already: Escape for the
                // menu, S for status, I for the inventory and its scrolling. Looked up in
                // a baked set rather than compared directly, because object comparison here
                // is exact - Menu_Btn and Status_Btn each have Memories_* variants that a
                // direct test would let straight through.
                if (ds_map_exists(a11y_skip, a_wo))
                    a_wok = 0;

                // Phone digit keys are typed, not walked to. Their 'nbr' is a STRING here -
                // the character the key enters - and every one of them sets it in Create,
                // so it is safe to read. The star has no number key, so it stays listed
                // alongside the receiver.
                if (a_wok && ds_map_exists(a11y_phone, a_wo))
                {
                    if (string_length(a_wi2.nbr) == 1)
                    {
                        if (a_wi2.nbr >= ""0"" && a_wi2.nbr <= ""9"")
                            a_wok = 0;
                    }
                }
                if (ds_map_exists(a_skip, a_wi2))
                    a_wok = 0;

                // NOT filtered on 'visible', which was the bug that made the game
                // unplayable: 1465 of the 2124 interactive objects are invisible by
                // design. Every room exit and nearly every look-at hotspot is an
                // unrendered collision shape drawn from a placeholder sprite (S_Test,
                // S_Test_02, *_Mask), and the game's own hit test - a collision_point
                // sweep - ignores visibility entirely. Filtering on it threw away roughly
                // two thirds of the game, including every way out of every room.
                //
                // A missing sprite does mean no mask, so nothing the mouse could ever hit
                // either. Checked at instance level, because a few objects get their
                // sprite assigned at runtime.
                //
                // 'playable' is checked LAST on purpose. Every one of Interactive_Object's
                // 2123 descendants sets it except Interface_Inventory_Up/Down, which are
                // already excluded above - so by here it is always safe to read, and
                // reading a variable that is not set is fatal.
                if (a_wok)
                {
                    if (a_wi2.sprite_index == -1)
                        a_wok = 0;
                    else if (a_wi2.playable == 0)
                        a_wok = 0;
                }

                // Category filter.
                //
                // 'Objects' is deliberately the baked category OR simply being visible.
                // The baked category comes from the hover cursor, and plenty of real
                // props set no cursor at all - Bridge_Can is a can you can kick, with a
                // click handler and no hover handler, so the cursor test alone files it
                // as scenery. Anything the game actually draws is a thing, not scenery.
                // Conversely a few genuinely usable hotspots are invisible, so the
                // category still has to count. Scenery is then what is left: unrendered,
                // not an exit, and with no cursor of its own - the look-at masks.
                // Desktop icons with a window open on top of them. They are behind it,
                // they are not what you are working on, and in the paint program they
                // padded the list out with five entries you never want. Games stays, so
                // leaving the computer is always one entry away.
                if (a_wok && instance_exists(Computer_Window))
                {
                    if (ds_map_exists(a11y_desk, a_wo))
                        a_wok = 0;
                }

                // Ambient clutter. Kept in Everything so nothing is ever unreachable -
                // a couple of the grave-debris masks do answer to an item - but out of the
                // two lists anyone actually browses.
                if (a_wok && a11y_junk && a11y_w_mode != 0)
                {
                    if (ds_map_exists(a11y_clutter, a_wo))
                        a_wok = 0;
                }

                if (a_wok && a11y_w_mode != 0)
                {
                    var a_cat = 0;
                    if (ds_map_exists(a11y_cat, a_wo))
                        a_cat = ds_map_find_value(a11y_cat, a_wo);
                    var a_vis = (a_wi2.visible != 0);
                    if (a11y_w_mode == 1 && a_cat != 1)
                        a_wok = 0;
                    if (a11y_w_mode == 2 && a_cat != 2 && !a_vis)
                        a_wok = 0;
                    if (a11y_w_mode == 3 && (a_cat != 0 || a_vis))
                        a_wok = 0;
                }

                if (a_wok)
                {
                    ds_list_add(a11y_wi, a_wi2);
                    ds_list_add(a11y_wnm, object_get_name(a_wo));
                    ds_list_add(a11y_wx, a_wi2.bbox_left + ((a_wi2.bbox_right - a_wi2.bbox_left) / 2));
                }
            }
        }
        ds_map_destroy(a_skip);

        // Going back is not an Interactive_Object at all, which is why it never appeared.
        // View_Back is a bare object holding the destination in lvl_back, and the game's
        // way of using it is pure mouse geometry: its Step watches for the pointer
        // straying more than 'distance' pixels from it, and only then does a click on
        // empty space walk you back. There is nothing there for a keyboard to aim at, so
        // it becomes an explicit entry - forced to the front of the list, because one
        // predictable place for the only way out beats spatial fidelity.
        // Listed in EVERY filter, not just Everything and Exits. It is one entry and it is
        // the way out of a close-up; hiding it behind a filter mode strands the player with
        // no indication that switching mode would give them a way back.
        // Mouse-proximity targets. ONE entry per object, not per instance: Lvl_Bridge_05
        // holds two Bridge_Wire instances 12 pixels apart, which listed the same thing
        // twice. They sit close enough together that the pointer being on one is inside the
        // 60px radius of the other, so the first is always sufficient.
        //
        // Listed in Everything and Objects only. They are not exits and they are not
        // scenery, and putting them in all four modes made every filter look broken.
        // Non-Interactive_Object entries. The paint palette answers only to a real
        // Mouse_4 press on the instance, so the game's own dispatch never offers it and
        // the sweep above cannot see it - it has to be added by hand. Every instance is
        // listed, unlike the proximity targets, because the fourteen swatches are
        // fourteen different colours and collapsing them would defeat the point.
        // Everything and Objects only: these are neither exits nor scenery.
        if (a11y_w_mode == 0 || a11y_w_mode == 2)
        {
            for (var a_xj = 0; a_xj < ds_list_size(a11y_extra); a_xj += 1)
            {
                var a_xob = ds_list_find_value(a11y_extra, a_xj);
                var a_xn = instance_number(a_xob);
                for (var a_xk = 0; a_xk < a_xn; a_xk += 1)
                {
                    var a_xi = instance_find(a_xob, a_xk);
                    if (instance_exists(a_xi))
                    {
                        ds_list_add(a11y_wi, a_xi);
                        ds_list_add(a11y_wnm, object_get_name(a_xob));
                        ds_list_add(a11y_wx, a_xi.x);
                    }
                }
            }
        }

        if (a11y_w_mode == 0 || a11y_w_mode == 2)
        {
            for (var a_pj = 0; a_pj < ds_list_size(a11y_prox); a_pj += 1)
            {
                var a_pob = ds_list_find_value(a11y_prox, a_pj);
                if (instance_number(a_pob) > 0)
                {
                    var a_pi3 = instance_find(a_pob, 0);
                    // Nested rather than an && with the dereference in the same
                    // expression: GML's short-circuit evaluation is a compiler option, so
                    // a condition of the form instance_exists(x) && x.field is only safe
                    // if that option happens to be on.
                    if (instance_exists(a_pi3))
                    {
                        if (a_pi3.visible)
                        {
                            ds_list_add(a11y_wi, a_pi3);
                            ds_list_add(a11y_wnm, ""@prox"");
                            ds_list_add(a11y_wx, a_pi3.x);
                        }
                    }
                }
            }
        }

        var a_vbn = instance_number(View_Back);
        for (var a_v = 0; a_v < a_vbn; a_v += 1)
        {
            var a_vbi = instance_find(View_Back, a_v);
            if (instance_exists(a_vbi))
            {
                ds_list_add(a11y_wi, a_vbi);
                ds_list_add(a11y_wnm, ""View_Back"");
                ds_list_add(a11y_wx, -100000);
            }
        }

        var a_wc = ds_list_size(a11y_wi);

        // Left to right across the scene. These rooms are wide and shallow, so one
        // horizontal sweep matches how the place is actually laid out.
        for (var a_i2 = 1; a_i2 < a_wc; a_i2 += 1)
        {
            var a_j2 = a_i2;
            while (a_j2 > 0)
            {
                if (ds_list_find_value(a11y_wx, a_j2) >= ds_list_find_value(a11y_wx, a_j2 - 1))
                    break;
                var a_t2;
                a_t2 = ds_list_find_value(a11y_wi, a_j2);
                ds_list_replace(a11y_wi, a_j2, ds_list_find_value(a11y_wi, a_j2 - 1));
                ds_list_replace(a11y_wi, a_j2 - 1, a_t2);
                a_t2 = ds_list_find_value(a11y_wnm, a_j2);
                ds_list_replace(a11y_wnm, a_j2, ds_list_find_value(a11y_wnm, a_j2 - 1));
                ds_list_replace(a11y_wnm, a_j2 - 1, a_t2);
                a_t2 = ds_list_find_value(a11y_wx, a_j2);
                ds_list_replace(a11y_wx, a_j2, ds_list_find_value(a11y_wx, a_j2 - 1));
                ds_list_replace(a11y_wx, a_j2 - 1, a_t2);
                a_j2 -= 1;
            }
        }

        // Hold the player's place across rebuilds.
        //
        // The list is rebuilt every frame, and in this game its CONTENTS change constantly
        // - birds, rain, stains and spawner objects are all Interactive_Object children,
        // so things appear and vanish several times a second. Keying the focus off a
        // signature of that list, as this first did, meant any of them reset the cursor to
        // the first entry and re-announced it: '1 of 35, 1 of 35, 1 of 35'.
        //
        // So remember the focused INSTANCE and find where it moved to. Only an actual room
        // change announces anything; a scene that merely reshuffles stays silent and keeps
        // the player exactly where they were.
        var a_wspeak = 0;

        // Whether this announcement may cut off whatever is already being spoken. Almost
        // everything here should: the player pressed a key and wants an answer now.
        var a_wint = 1;
        if (room != a11y_w_room)
        {
            a11y_w_room = room;
            a11y_w_idx = -1;

            // A press that took us through a door has nothing left to say about the room
            // we have left, and the arrival announcement below is the better answer.
            a11y_wpend = 0;
            a11y_wsay = """";
            a11y_wpost = 0;

            // The picture first, then the list behind it. Queued rather than interrupting,
            // or the first entry cuts the description off at the second word.
            if (a_picd != """")
            {
                if (a11y_ready)
                    external_call(a11y_f_speak, a_picd, 1);
                a_wint = 0;
            }
            if (a_wc > 0)
            {
                a11y_w_idx = 0;
                a_wspeak = 1;
            }
        }
        else
        {
            var a_at = -1;
            for (var a_f = 0; a_f < a_wc; a_f += 1)
            {
                if (ds_list_find_value(a11y_wi, a_f) == a11y_w_id)
                {
                    a_at = a_f;
                    break;
                }
            }
            if (a_at >= 0)
            {
                // Still here, just at a different position. Say nothing.
                a11y_w_idx = a_at;
            }
            else if (a_wc > 0)
            {
                // What we were on has gone - picked up, opened, walked away. Clamp and say
                // where that left us. Rare enough not to chatter.
                if (a11y_w_idx < 0)
                    a11y_w_idx = 0;
                if (a11y_w_idx >= a_wc)
                    a11y_w_idx = a_wc - 1;
                if (a11y_w_id != 0)
                {
                    a_wspeak = 1;

                    // QUEUED, not interrupting. Nobody asked for this one - the focus
                    // moved because the thing under it disappeared - and the commonest
                    // way for that to happen is picking it up, which queues an
                    // announcement of its own on this very frame. Interrupting cut
                    // '<item> added to your inventory' off mid-word every single time.
                    a_wint = 0;
                }
            }
            else
            {
                a11y_w_idx = -1;
            }
        }

        // Something became usable while the player was waiting.
        //
        // The film in the VHS tape is the case that showed this up. Its kitchen opens with
        // every single object set to playable = 0 - the scene is a clown walking across the
        // room - and only when he has gone does the door become usable. For several seconds
        // the room genuinely contains nothing, which from the keyboard is indistinguishable
        // from a room that is empty for good, so the player stops waiting and starts
        // looking for a bug. That is exactly what was reported.
        //
        // ONLY the nothing-to-something edge is announced. Anything looser chatters without
        // stopping: birds, rain and stains are all Interactive_Object children, so the list
        // gains and loses entries several times a second in the outdoor rooms. Suppressed
        // on a filter change, which announces its own count already.
        if (a11y_wn == 0 && a_wc > 0 && !a_modechg && a11y_ready)
            external_call(a11y_f_speak, ""Something can be used here now."", 1);
        a11y_wn = a_wc;

        var a_wpre = """";
        if (a_modechg)
        {
            var a_mn = ""Everything"";
            if (a11y_w_mode == 1)
                a_mn = ""Exits"";
            if (a11y_w_mode == 2)
                a_mn = ""Objects"";
            if (a11y_w_mode == 3)
                a_mn = ""Scenery"";

            a11y_w_idx = -1;
            if (a_wc > 0)
            {
                a11y_w_idx = 0;
                a_wpre = a_mn + "", "" + string(a_wc) + "" found. "";
                a_wspeak = 1;
            }
            else
            {
                if (a11y_ready)
                    external_call(a11y_f_speak, a_mn + "", nothing here."", 1);
                a_wspeak = 0;
            }
        }

        if (a_wc > 0)
        {
            var a_wmv = 0;
            if (keyboard_check_pressed(vk_right) || keyboard_check_pressed(vk_down))
                a_wmv = 1;
            if (keyboard_check_pressed(vk_left) || keyboard_check_pressed(vk_up))
                a_wmv = -1;
            if (a_wmv != 0)
            {
                if (a11y_w_idx < 0)
                    a11y_w_idx = 0;
                else
                    a11y_w_idx = ((a11y_w_idx + a_wmv) + a_wc) mod a_wc;
                a_wspeak = 1;
                a_wint = 1;
            }
            if (keyboard_check_pressed(vk_f3))
            {
                a_wspeak = 1;
                a_wint = 1;
            }

            // F1 - the area name. Every object in a room is named after the room it is in,
            // so hearing it in front of all thirty-odd entries is pure repetition. Stripped
            // by matching the room's own name, which is why it is a toggle: the match is
            // mechanical and a name it gets wrong is better heard in full.
            if (keyboard_check_pressed(vk_f1))
            {
                if (a11y_area)
                    a11y_area = 0;
                else
                    a11y_area = 1;
                a11y_setsave = 1;
                if (a11y_ready)
                {
                    if (a11y_area)
                        external_call(a11y_f_speak, ""Area names off."", 1);
                    else
                        external_call(a11y_f_speak, ""Area names on."", 1);
                }
                a_wspeak = 1;
                // Queued: the toggle just announced itself, and the entry that
                // follows must not cut that announcement off.
                a_wint = 0;
            }

            // F2 - the clutter. Rebuilt next frame, so the announcement carries the new
            // count rather than the old one.
            if (keyboard_check_pressed(vk_f2))
            {
                if (a11y_junk)
                    a11y_junk = 0;
                else
                    a11y_junk = 1;
                a11y_setsave = 1;
                if (a11y_ready)
                {
                    if (a11y_junk)
                        external_call(a11y_f_speak, ""Clutter hidden."", 1);
                    else
                        external_call(a11y_f_speak, ""Clutter shown."", 1);
                }
                a_wspeak = 1;
                // Queued: the toggle just announced itself, and the entry that
                // follows must not cut that announcement off.
                a_wint = 0;
            }
            if (a11y_w_idx >= a_wc)
            {
                a11y_w_idx = a_wc - 1;
                a_wspeak = 1;
            }

            if (keyboard_check_pressed(vk_enter) && a11y_w_idx >= 0)
            {
                var a_wt = ds_list_find_value(a11y_wi, a11y_w_idx);
                var a_wtn = ds_list_find_value(a11y_wnm, a11y_w_idx);
                var a_wdone = 0;   // set when the branch already said something of its own
                if (instance_exists(a_wt))
                {
                    // Identified by the name this patch stored in the list, NOT by
                    // object_index. Every close-up uses its OWN View_Back child -
                    // Bridge_Controller_View is object 1526, View_Back is 1580 - and
                    // object comparison in GML is exact, never parent-aware. So
                    // 'object_index == View_Back' was false for every real close-up in the
                    // game, and Go back fell through to the ordinary click path, which
                    // does nothing without a genuine mouse press. instance_find IS
                    // parent-aware, which is why the entry appeared but did nothing.
                    if (a_wtn == ""View_Back"")
                    {
                        // View_Back's own user event 0 ends in mouse_check_button_pressed,
                        // so calling it does nothing from here. Do what that handler does.
                        //
                        // Its 'active' flag is deliberately NOT checked. That flag exists
                        // to stop an accidental click the instant a close-up opens, and it
                        // oscillates: View_Back's Step fires user event 0 whenever the
                        // pointer is further than 'distance' away, which then calls user
                        // event 1 - 'active = 0; alarm[0] = 2;' - if the pointer happens to
                        // be over any interactive object. A keyboard player never moves the
                        // pointer, so wherever it was left it can hold active at 0 almost
                        // permanently, and honouring it made Go back silently do nothing.
                        // A deliberate Enter on an entry that says 'Go back' is not
                        // ambiguous, so just go back.
                        _sound_play_simple(a_wt.sound, 90, 0);
                        room_goto(a_wt.lvl_back);
                    }
                    else if (a_wtn == ""Memories_Trash_Items"")
                    {
                        // Acting on it destroys it, so the usual re-read says nothing and
                        // the press lands in silence - which after a pick-up promise reads
                        // as a control that did not work. Say the outcome instead. What
                        // changed is elsewhere: room_ereased makes Memories_Handle
                        // playable in the parents' room, so that turns up in the list on
                        // its own, exactly as the handle turns up on screen.
                        with (a_wt)
                            event_user(0);
                        if (!ending_good && neutral_bad)
                            a11y_wsay = ""Rubbish cleared out."";
                        else
                            a11y_wsay = ""Rubbish cleared out. There was nothing in it to take."";
                        a_wdone = 1;
                    }
                    else if (a_wtn == ""The_End_Controller"")
                    {
                        // 'active' is the ten second hold that stops the ending being
                        // clicked away before it has been seen, and unlike View_Back's
                        // flag it is honoured: it is set once by an alarm and never
                        // flickers, so refusing while it is 0 is telling the truth
                        // rather than losing a press.
                        //
                        // Read through a local of its own rather than off a_wt directly.
                        // The verifier asserts that 'a_wt.active' appears nowhere in the
                        // patched Step, which is how it stops the View_Back regression -
                        // honouring a flag that oscillates against the pointer, and
                        // silently swallowing Go back - from creeping back in.
                        var a_endc = a_wt;
                        if (a_endc.active)
                        {
                            with (a_wt)
                                event_perform(ev_mouse, 53);
                            a11y_wsay = ""Ending the game."";
                        }
                        else
                        {
                            a11y_wsay = ""Not yet. Let the ending play out first."";
                        }
                        a_wdone = 1;
                    }
                    else if (a_wtn == ""Computer_Paint_Color_Take"")
                    {
                        // A palette swatch. Its only handler is Mouse_4 - a real left
                        // press ON the instance - which Controller's dispatch never
                        // raises and event_user(0) does not reach. event_perform runs
                        // that handler directly; the pointer has already been warped onto
                        // the swatch anyway, so this is what a click would have done.
                        with (a_wt)
                            event_perform(ev_mouse, 4);
                    }
                    else if (a_wtn == ""Bridge_Clip"" && instance_exists(Bridge_06_Mask))
                    {
                        // The one drag-and-drop puzzle in the game, and it is pure mouse.
                        // Bridge_Clip follows the pointer only while the left button is
                        // HELD - its own Step drops it the instant the button is not down
                        // - and while it overlaps Bridge_06_Mask its collision event
                        // clears Bridge_Coin_Item.lightning, which is what makes the coin
                        // safe to pick up. Calling its user event 0 only sets active = 1,
                        // which that same Step cancels one frame later, so from the
                        // keyboard pressing it did nothing whatsoever.
                        //
                        // So put it where a mouse player would have dropped it. Aimed at
                        // the target's bounding-box centre rather than its origin, since
                        // the mask is a plain rectangle whose origin may sit at a corner.
                        var a_tg = instance_find(Bridge_06_Mask, 0);
                        a_wt.x = a_tg.bbox_left + ((a_tg.bbox_right - a_tg.bbox_left) / 2);
                        a_wt.y = a_tg.bbox_top + ((a_tg.bbox_bottom - a_tg.bbox_top) / 2);
                        _sound_play_simple(206, 90, 0);
                        a11y_wsay = ""Paperclip moved into place."";
                        a_wdone = 1;
                    }
                    else
                    {
                        // Exactly what a left click does. Handlers re-check the active
                        // item themselves - _check_item is called inside user event 0,
                        // not carried over from the hover - so no hover pass is needed.
                        with (a_wt)
                            event_user(0);

                        // And say what it looks like, where that is written down. Only
                        // once per visit to the object: a11y_scn_id holds whichever
                        // instance was last described, so a second Enter on the same
                        // thing falls through to the ordinary short re-read and moving
                        // to anything else arms it again.
                        //
                        // The instance test is nested rather than an && with the
                        // dereference beside it, and here the instance really can be
                        // gone: the press just above is what took it.
                        if (instance_exists(a_wt))
                        {
                            if (ds_map_exists(a11y_scn, a_wt.object_index))
                            {
                                if (a_wt != a11y_scn_id)
                                {
                                    a11y_scn_id = a_wt;

                                    // The label still follows, QUEUED behind the
                                    // description rather than interrupting it. Plenty of
                                    // these are things whose press also changed something
                                    // - a switch, a drawer - and swallowing the state
                                    // readout to make room for the picture would trade one
                                    // silence for another. Both go out together when the
                                    // pending announcement fires.
                                    a11y_wsay = ds_map_find_value(a11y_scn, a_wt.object_index);
                                }
                            }
                        }
                    }
                    // Read the thing back after acting on it, so a press is audible at all.
                    // Plenty of these change nothing but a sprite - the remote's dial, the
                    // phone keypad - and were completely silent to press. Re-reading also
                    // picks up the new position and any change of verb.
                    //
                    // Only if it survived: if acting on it destroyed it - picked up, opened,
                    // walked through - stay quiet and let the focus tracking above report
                    // next frame where that left us. And not when the branch already said
                    // something of its own, or the re-read cuts its confirmation off
                    // mid-word, which is exactly what happened to the paperclip.
                    if (instance_exists(a_wt) && a_wdone == 0)
                        a11y_wpost = 1;
                    else
                        a11y_wpost = 0;

                    // ARMED, not spoken. See the note on a11y_wpend: an utterance handed
                    // over on the same frame as the Enter that asked for it is cancelled
                    // by the screen reader's own interrupt-on-Enter before anyone hears
                    // it, which is why pressing an entry did the thing and said nothing
                    // while arrowing onto it read out fine.
                    a11y_wpend = 3;
                }
            }

            // Whatever the last press had to say, said now rather than then.
            if (a11y_wpend > 0)
            {
                if (a_wspeak)
                {
                    // Something newer is already being announced - an arrow key, a mode
                    // change, the thing under the focus vanishing. That is what the
                    // player last asked for, so the press is stale and gets dropped.
                    a11y_wpend = 0;
                    a11y_wsay = """";
                    a11y_wpost = 0;
                }
                else
                {
                    a11y_wpend -= 1;
                    if (a11y_wpend == 0)
                    {
                        if (keyboard_check(vk_enter))
                        {
                            // Still held. The interrupt is tied to the key, not to the
                            // frame it went down on, so wait for it to come back up.
                            a11y_wpend = 1;
                        }
                        else
                        {
                            if (a11y_wsay != """" && a11y_ready)
                                external_call(a11y_f_speak, a11y_wsay, 1);
                            if (a11y_wpost)
                            {
                                a_wspeak = 1;
                                a_wint = 1;
                                if (a11y_wsay != """")
                                    a_wint = 0;
                            }
                            a11y_wsay = """";
                            a11y_wpost = 0;
                        }
                    }
                }
            }

            if (a_wspeak && a11y_w_idx >= 0 && a11y_w_idx < a_wc && a11y_ready)
            {
                var a_wt2 = ds_list_find_value(a11y_wi, a11y_w_idx);
                var a_wnr = ds_list_find_value(a11y_wnm, a11y_w_idx);
                var a_wl = string_replace_all(a_wnr, ""_"", "" "");

                // Most of the world is named <thing>_Mask, and reading that word out
                // after every hotspot in the game is noise. The trimmed name is
                // baked at patch time. Not applied to the two synthetic entries
                // below, whose stored name is a marker the branches test for.
                if (a_wnr != ""@prox"" && a_wnr != ""View_Back"")
                {
                    if (instance_exists(a_wt2))
                    {
                        if (ds_map_exists(a11y_pretty, a_wt2.object_index))
                            a_wl = ds_map_find_value(a11y_pretty, a_wt2.object_index);

                        // And without the area in front of it, unless F1 turned that off.
                        // Checked second so it wins: the short form is built from the same
                        // trimmed name, not from the raw one.
                        if (a11y_area)
                        {
                            if (ds_map_exists(a11y_short, a_wt2.object_index))
                                a_wl = ds_map_find_value(a11y_short, a_wt2.object_index);
                        }
                    }
                }

                if (a_wl == ""@prox"")
                {
                    // Not probed - these are not interactive objects and have no hover.
                    // Focusing one is the whole point: it moves the pointer onto it.
                    a_wl = string_replace_all(object_get_name(a_wt2.object_index), ""_"", "" "");
                    if (ds_map_exists(a11y_pretty, a_wt2.object_index))
                        a_wl = ds_map_find_value(a11y_pretty, a_wt2.object_index);
                    if (a11y_area)
                    {
                        if (ds_map_exists(a11y_short, a_wt2.object_index))
                            a_wl = ds_map_find_value(a11y_short, a_wt2.object_index);
                    }
                    if (a_wt2.object_index == Forest_Leech)
                    {
                        // It grows from 0.5 to 1.0 at 0.005 a step, and only while the
                        // pointer is on it - so the scale IS the progress bar, and it is
                        // the only feedback the screen gives.
                        a_wl = ""Leech on you, stay here until it lets go"";
                        a_wl += "", "" +
                                string(round(((a_wt2.image_xscale - 0.5) / 0.5) * 100)) +
                                "" percent full"";
                    }
                    else
                        a_wl += "", look at this"";
                }
                else if (a_wl == ""View Back"")
                {
                    // Not probed: View_Back's user event 1 deactivates it.
                    a_wl = ""Go back"";
                }
                else if (instance_exists(a_wt2))
                {
                    // Ask the object what it is the way the game does. User event 1 is the
                    // hover, and the cursor it sets is the ONLY place the kind of
                    // interaction is recorded - there is no verb stored anywhere.
                    //
                    // text_info is saved and restored because Controller's End Step reads
                    // it before clearing it, so a stray value here would flicker the
                    // on-screen tooltip for anyone watching. The cursor fields it also
                    // touches are all reset at the top of that same End Step.
                    // Except on the handful whose hover DESTROYS them - see the note by
                    // a11y_nohover in the injector. 36 is the neutral cursor and an empty
                    // info string, so skipping the probe reads exactly as an object that
                    // set nothing, which is the honest answer: nothing was asked.
                    var a_cur = 36;
                    var a_inf = """";
                    var a_itc = 0;
                    var a_wrg = 0;
                    if (!ds_map_exists(a11y_nohover, a_wt2.object_index))
                    {
                        var a_sti = text_info;
                        var a_stb = text_info_bold;
                        var a_sci = cursor_image;
                        var a_sic = item_cursor;
                        var a_swi = wrong_item;
                        text_info = """";
                        cursor_image = 36;
                        item_cursor = 0;
                        wrong_item = 0;
                        with (a_wt2)
                            event_user(1);
                        a_cur = cursor_image;
                        a_inf = text_info;
                        a_itc = item_cursor;
                        a_wrg = wrong_item;
                        text_info = a_sti;
                        text_info_bold = a_stb;
                        cursor_image = a_sci;
                        item_cursor = a_sic;
                        wrong_item = a_swi;
                    }

                    // Everything below reads fields off a_wt2, and the probe above ran
                    // the object's OWN code to get here. Object code can destroy the
                    // object: two hovers in this game do exactly that, and they are
                    // skipped above - but the forty dereferences that follow should not
                    // rest on that list being complete. Checked once, here.
                    if (instance_exists(a_wt2))
                    {

                        // Only 8 of the 972 hover handlers set a text label, but where one
                        // does it beats the object name.
                        if (a_inf != """")
                            a_wl = a_inf;

                        // Cursor sprite -> what the thing is for. The variants are the same
                        // action drawn for an injured, left-handed or blinded player.
                        var a_vb = """";
                        if (a_cur == 43 || a_cur == 44 || a_cur == 49 || a_cur == 50 || a_cur == 408)
                            a_vb = ""use"";
                        if (a_cur == 39 || a_cur == 55 || a_cur == 52)
                            a_vb = ""look"";
                        if (a_cur == 40)
                            a_vb = ""go"";
                        if (a_cur == 46)
                            a_vb = ""enter"";
                        if (a_cur == 45)
                            a_vb = ""locked"";
                        if (a_cur == 41 || a_cur == 42)
                            a_vb = ""hit"";
                        if (a_cur == 47)
                            a_vb = ""back"";
                        if (a_cur == 38)
                            a_vb = ""needs an item"";
                        // The hand cursor is the game's catch-all: taking an item, pulling
                        // a lever and opening a drawer all draw the same hand, so everything
                        // all read as the use verb. Whether a press actually takes
                        // something is settled at patch time by looking for _item_add
                        // in its handler.
                        if (a_vb == ""use"" && ds_map_exists(a11y_pick, a_wt2.object_index))
                            a_vb = ""pick up"";

                        // The game's SECOND way of saying an item works here, and the one that
                        // matters for anything you must not touch bare-handed. Rather than
                        // _activate_item_cursor and the wrong_item flag handled further down,
                        // the hover simply sets the cursor to the HELD item's own cursor
                        // image - Graveyard_Flower does exactly this with the glove. A sighted
                        // player sees the glove icon appear over the flower; there was nothing
                        // at all to hear, and picking it bare-handed costs a wound and a
                        // status.
                        //
                        // 36 is the neutral cursor this probe sets before asking, so it means
                        // the object set nothing rather than matching anything.
                        if (a_vb == """" && a_cur != 36 && active_item != -4)
                        {
                            if (instance_exists(active_item))
                            {
                                if (a_cur == active_item.cursor_image && a11y_hint)
                                {
                                    var a_hn2 = active_item.name;
                                    if (ds_map_exists(a11y_iname, active_item.object_index))
                                        a_hn2 += ds_map_find_value(a11y_iname,
                                                                   active_item.object_index);
                                    a_wl += "", USE "" + a_hn2 + "" HERE"";
                                }
                            }
                        }

                        // Last resort: something whose press takes an item, but whose hover
                        // draws a cursor this does not recognise, would otherwise have read
                        // out as a bare name with no indication it could be taken at all.
                        if (a_vb == """" && ds_map_exists(a11y_pick, a_wt2.object_index))
                            a_vb = ""pick up"";

                        if (a_vb != """")
                            a_wl += "", "" + a_vb;

                        // A press that only ever wounds you. Said last so it is the part left
                        // ringing in your ear.
                        if (a11y_warn && ds_map_exists(a11y_hurt, a_wt2.object_index))
                            a_wl += "", hurts you"";

                        // Using an item ON something is the one mechanic with no textual
                        // feedback whatsoever. The whole flow - pick up the Controller, use it
                        // on the car - is conveyed only by the cursor: the object's hover calls
                        // _activate_item_cursor (which raises 'wrong_item') and then
                        // _check_item, which clears it again if what you are holding is right.
                        // A sighted player sees the 'wrong item' mark disappear; there was
                        // nothing at all to hear, which made every item puzzle unsolvable.
                        if (a_itc && a11y_hint)
                        {
                            if (a_wrg == 0)
                            {
                                var a_hn = ""this"";
                                if (active_item != -4)
                                {
                                    if (instance_exists(active_item))
                                    {
                                        a_hn = active_item.name;
                                        if (ds_map_exists(a11y_iname, active_item.object_index))
                                            a_hn += ds_map_find_value(a11y_iname,
                                                                      active_item.object_index);
                                    }
                                }
                                a_wl += "", USE "" + a_hn + "" HERE"";
                            }
                            else
                            {
                                a_wl += "", takes an item, but not this one"";
                            }
                        }

                        // Dials and keypad buttons keep their whole state in 'nbr' and show it
                        // by swapping a sprite, so pressing one reads as doing nothing at all.
                        // The car remote is exactly this: one button cycles a dial 0 to 3, the
                        // other transmits, and it only works with the dial on 3.
                        if (ds_map_exists(a11y_state, a_wt2.object_index))
                        {
                            // Same variable, different meaning: on the phone it is the
                            // character the key types, not a dial position.
                            if (ds_map_exists(a11y_phone, a_wt2.object_index))
                                a_wl += "", key "" + string(a_wt2.nbr);
                            else
                                a_wl += "", position "" + string(a_wt2.nbr);
                        }

                        // Whether the coin is still live. Its Other_10 shocks you instead of
                        // giving you the coin while 'lightning' is set, and the only cue is
                        // the sparks - so without this there is no way to tell that grounding
                        // it with the paperclip has worked. Safe to read: Bridge_Coin_Item's
                        // Create always sets it, and the name check keeps it to that object.
                        // Tested against the STORED name, not the label. By this point a verb
                        // has already been appended, so comparing the label only matched on the
                        // frames where the coin happened to have no cursor - which is why it
                        // sometimes read as 'Bridge Coin Item, use' instead.
                        // The two things you actually press in the board game, both of which
                        // are otherwise nameless invisible hotspots.
                        // A board hazard. Which square it stands on decides everything and
                        // is only ever drawn, so it is found by nearest pointer - Dead_Pointer
                        // and Checkpoint_Pointer are both Position_Pointer children, so one
                        // parent-aware sweep covers all three. 40 is comfortably above the
                        // worst real gap in the room data (33) and far below the square
                        // spacing, so it cannot pick a neighbour.
                        if (ds_map_exists(a11y_board, a_wt2.object_index) &&
                            instance_number(Position_Pointer) > 0)
                        {
                            var a_bp = -4;
                            var a_bd = 100000;
                            var a_bpn = instance_number(Position_Pointer);
                            for (var a_bi = 0; a_bi < a_bpn; a_bi += 1)
                            {
                                var a_bpi = instance_find(Position_Pointer, a_bi);
                                if (instance_exists(a_bpi))
                                {
                                    var a_bdd = point_distance(a_bpi.x, a_bpi.y, a_wt2.x, a_wt2.y);
                                    if (a_bdd < a_bd)
                                    {
                                        a_bd = a_bdd;
                                        a_bp = a_bpi;
                                    }
                                }
                            }
                            if (a_bp != -4 && a_bd < 40)
                            {
                                a_wl += "", square "" + string(a_bp.nbr);
                                if (a_bp.object_index == Dead_Pointer)
                                {
                                    if (a_bp.active)
                                        a_wl += "", deadly"";
                                    else
                                        a_wl += "", crossed out"";
                                }
                            }
                            var a_bci2 = instance_find(Board_Controller, 0);
                            if (instance_exists(a_bci2))
                            {
                                var a_bcr = a_bci2.correct;
                                if (a_bcr > 0)
                                {
                                    a_wl += "", "" + string(a_bcr) + "" corrections left"";

                                    // Status_Corrector's Create sets Controller.ending_good = 0
                                    // outright, so the very first crossing-out is permanent and
                                    // silent - the board just draws an X. Only worth warning
                                    // about while the ending is still there to lose.
                                    if (a11y_warn && !instance_exists(Status_Corrector))
                                        a_wl += "", the first one costs the good ending"";
                                }
                                else
                                    a_wl += "", no corrections left"";
                            }
                        }

                        if (instance_exists(Board_Controller))
                        {
                            var a_bc2 = instance_find(Board_Controller, 0);
                            if (ds_list_find_value(a11y_wnm, a11y_w_idx) == ""Board_Dice"")
                            {
                                // The die stays listed and looks perfectly usable after the
                                // game ends or after a death, but its handler exits early in
                                // both cases, so say which state it is actually in.
                                var a_bo2 = a_bc2.end_game;
                                if (instance_exists(Flat_Controller))
                                {
                                    if (Flat_Controller.board_game_ended)
                                        a_bo2 = 1;
                                }
                                a_wl = ""Die"";
                                if (a_bo2)
                                    a_wl += "", game over"";
                                else if (a_bc2.death)
                                    a_wl += "", press to return to the checkpoint"";
                                else if (a_bc2.die_result > 0)
                                {
                                    a_wl += "", showing "" + string(a_bc2.die_result);
                                    if (a_bc2.reroll)
                                    {
                                        a_wl += "", press to reroll"";
                                        if (a11y_warn && !instance_exists(Status_Cheater))
                                            a_wl += "", which costs the good ending"";
                                    }
                                }
                                else
                                    a_wl += "", ready to roll, or press Space"";
                            }
                            if (ds_list_find_value(a11y_wnm, a11y_w_idx) == ""Board_Button"")
                            {
                                a_wl = ""Your piece, square "" + string(a_bc2.position);
                                if (a_bc2.die_result > 0)
                                {
                                    var a_dest = a_bc2.position + a_bc2.die_result;
                                    a_wl += "", moves to "" + string(a_dest);

                                    // What is waiting there. A sighted player reads this
                                    // straight off the board, and it is the whole basis for
                                    // deciding whether to reroll.
                                    var a_dn = instance_number(Position_Pointer);
                                    for (var a_di = 0; a_di < a_dn; a_di += 1)
                                    {
                                        var a_dpi = instance_find(Position_Pointer, a_di);
                                        if (instance_exists(a_dpi))
                                        {
                                            if (a_dpi.nbr == a_dest)
                                            {
                                                if (a_dpi.object_index == Dead_Pointer)
                                                {
                                                    if (a_dpi.active)
                                                        a_wl += "", DEADLY"";
                                                    else
                                                        a_wl += "", safe, crossed out"";
                                                }
                                                if (a_dpi.object_index == Checkpoint_Pointer)
                                                    a_wl += "", a checkpoint"";
                                            }
                                        }
                                    }
                                    if (room == Lvl_Flat_Board)
                                    {
                                        if (a_dest > 72)
                                            a_wl += "", the end of the board"";
                                    }
                                    else if (a_dest > 25)
                                    {
                                        a_wl += "", the end of the board"";
                                    }
                                    a_wl += "", press Enter or Space"";
                                }
                            }
                        }

                        // ----- The old house: three things whose entry lies -------------
                        //
                        // The kitchen masks. Each holds its own 'alive', 'rag' and
                        // 'wanted_item' from its Create, and the whole puzzle is in those
                        // three: an uncovered mask screams when anything nearby is used, and
                        // it wants the DIRTY rag (1188) normally but the CLEAN one (1189)
                        // while the good ending is still alive and Status_Germs is not - a
                        // swap decided silently, in Create, per visit to the room. Presenting
                        // the dirty rag on a good run simply does nothing.
                        if (a_wnr == ""Memories_Kitchen_Mask"" ||
                            a_wnr == ""Memories_Kitchen_Mask_02"" ||
                            a_wnr == ""Memories_Kitchen_Mask_03"")
                        {
                            if (instance_exists(a_wt2))
                            {
                                // The state is always said; what it wants is the hint.
                                if (a_wt2.rag)
                                {
                                    a_wl += "", covered"";
                                    if (a11y_hint)
                                        a_wl += "", press to take the rag back"";
                                }
                                else
                                {
                                    a_wl += "", uncovered and screaming"";
                                    if (a11y_hint)
                                    {
                                        if (a_wt2.wanted_item == 1189)
                                            a_wl += "", needs the CLEAN rag"";
                                        else
                                            a_wl += "", needs the rag"";
                                    }
                                }
                            }
                        }

                        // The portrait is not what refuses. Its handler hands the press
                        // straight to Memories_Kitchen_Mask_03 while that mask is alive, and
                        // the mask screams instead - so the portrait reads as a look-at that
                        // simply does not work, and the object that actually said no is
                        // somewhere else in the list.
                        if (a_wnr == ""Memories_Kitchen_Portrait_Mask"")
                        {
                            var a_km3 = instance_find(Memories_Kitchen_Mask_03, 0);
                            if (instance_exists(a_km3))
                            {
                                if (a_km3.alive)
                                    if (a11y_hint)
                                a_wl += "", blocked by the screaming mask below it"";
                            }
                        }

                        // The pile inside the bin. The pick-up verb is baked from a flat
                        // scan for _item_add in the press handler, and here that call sits
                        // behind 'if (!Controller.ending_good && Controller.neutral_bad)' -
                        // so on a good run the pile holds nothing at all and the promise is
                        // false. What the press is really for is Memories_Controller
                        // .room_ereased, which is set unconditionally. Mirrors the game's own
                        // condition rather than guessing.
                        if (a_wnr == ""Memories_Trash_Items"")
                        {
                            if (!ending_good && neutral_bad)
                                a_wl = ""Pile of rubbish, pick it up"";
                            else
                                a_wl = ""Pile of rubbish, clear it out, nothing in it to take"";
                        }

                        // The bin in the kid's room. Two objects sit on the same few pixels:
                        // Memories_Room_Trash_Mask is the drawn bin and does nothing but
                        // rustle, while the way INTO it is Memories_Room_Kid_Paper_03 - the
                        // third drawing on the wall, an invisible S_Test_03 hotspot at depth 0
                        // over the bin at depth 30, so the mouse always hits the drawing. Once
                        // Memories_Controller.death_get_out is set that same object stops
                        // opening a drawing and opens the bin instead. Nothing about the
                        // entry changes, so the list names the door after the wrong thing.
                        if (a_wnr == ""Memories_Room_Kid_Paper_03"")
                        {
                            if (instance_exists(Memories_Controller))
                            {
                                if (Memories_Controller.death_get_out)
                                    a_wl = ""Bin, look inside"";
                            }
                        }
                        if (a_wnr == ""Memories_Room_Trash_Mask"")
                            a_wl = ""Bin, rustle it"";

                        // The mirror over the sink in the hospital toilets. Taking the eye
                        // out of the plughole makes Hospital_WC_Mirror_Text visible - a
                        // sprite, not an object you can press - and what it shows is three
                        // words finger-painted on the glass in blood. That is the whole of
                        // the scene, it is what the Steam achievement called BUTCHER is for,
                        // and there was nothing there to hear.
                        var a_mrt = instance_find(Hospital_WC_Mirror_Text, 0);
                        if (a_wnr == ""Hospital_Nose_Mirror"" && instance_exists(a_mrt))
                        {
                            if (a_mrt.visible)
                                a_wl += "", written on it in blood, I am glad I could HELP"";
                        }

                        // The queue ticket, on the ground and blowing about. Both hotspots
                        // are named after the number they carry and nothing says it out loud,
                        // and the number is the puzzle: the waiting room is calling 13.
                        if (a_wnr == ""Hospital_B_Number_3_Ground"")
                            a_wl = ""Ticket, number 3, on the floor"";
                        if (a_wnr == ""Hospital_B_Number_Window"")
                            a_wl = ""Ticket, number 3, on the windowsill"";

                        // The last entry in the game. It has no hover handler of its own, so
                        // the probe above leaves it as the bare object name, and its ten
                        // second arming timer is the difference between a control that works
                        // and one that will not answer yet.
                        if (a_wnr == ""The_End_Controller"")
                        {
                            if (a_wt2.active)
                                a_wl = ""Finish, end the game and go back to the menu"";
                            else
                                a_wl = ""The ending is still playing, wait"";
                        }

                        // The fake desktop. Family code says which variables the object
                        // really has - see the injector, which decides membership by reading
                        // each Create rather than trusting the parent chain, because an
                        // inherited variable is not a variable this instance owns.
                        if (ds_map_exists(a11y_comp, a_wt2.object_index))
                        {
                            var a_cf = ds_map_find_value(a11y_comp, a_wt2.object_index);

                            // Icons and tools carry a properly localised 'name'. Far better
                            // than the object name, and the only translated text in the room.
                            if (a_cf == 1)
                            {
                                // Two presses: the first selects, the second opens. That is
                                // the game's own double-click, and the first press was
                                // completely silent.
                                a_wl = a_wt2.name;
                                if (a_wt2.active)
                                    a_wl += "", selected, press again to open"";
                                else
                                    a_wl += "", icon"";
                            }
                            if (a_cf == 2)
                            {
                                // Fill and Take Colour. Which one is armed decides what a
                                // press on the picture does, and it is shown only by a
                                // slightly greyer sprite.
                                a_wl = a_wt2.name;
                                if (a_wt2.active)
                                    a_wl += "", selected"";
                            }
                            if (a_cf == 3)
                            {
                                // A part of the drawing. Its colour is the puzzle, so say it -
                                // and say when it is right, which is the only feedback a
                                // sighted player gets from the picture looking correct. The
                                // TARGET is deliberately not read out: working out which
                                // colour belongs where is the puzzle itself.
                                var a_c1 = ""colour "" + string(a_wt2.color);
                                if (ds_map_exists(a11y_col, a_wt2.color))
                                    a_c1 = ds_map_find_value(a11y_col, a_wt2.color);
                                a_wl += "", "" + a_c1;
                                if (a_wt2.color == a_wt2.color_target)
                                    a_wl += "", correct"";
                            }
                            if (a_cf == 4)
                            {
                                // A swatch. Five example swatches and fourteen palette ones,
                                // all instances of two objects, so without the colour they
                                // read as nineteen identical entries.
                                var a_c2 = ""colour "" + string(a_wt2.color);
                                if (ds_map_exists(a11y_col, a_wt2.color))
                                    a_c2 = ds_map_find_value(a11y_col, a_wt2.color);
                                a_wl = a_c2;
                            }
                            if (a_cf == 5)
                            {
                                // Save, Print and Delete. Their hover only sets the info text
                                // once the drawing is finished, so until then they were three
                                // nameless entries that did nothing.
                                a_wl = a_wt2.name;
                                var a_hc2 = instance_find(Hospital_Controller, 0);
                                if (instance_exists(a_hc2))
                                {
                                    if (!a_hc2.drawing)
                                        a_wl += "", not yet"";
                                }
                            }
                        }

                        // The forest cauldron. How far along the recipe is exists only as two
                        // numbers on Forest_Controller, and the one cue the game gives is that
                        // the bubbling animation changes object when the last ingredient goes
                        // in. Until then the can silently does nothing: its branch is an
                        // 'else if' on done_cooking, so a press produces no sound, no message
                        // and no wrong-item mark - indistinguishable from a broken control.
                        //
                        // The count is worth saying rather than just ready/not ready, because
                        // the recipe is CONDITIONAL - blood and salt only if you had bloody
                        // hands when you read it, berries and grain only on the good path -
                        // and it is fixed at the moment the recipe was read, so counting the
                        // list you remember can honestly disagree with what the pot wants.
                        if (a_wnr == ""Forest_House_Inside_Cooking"" && instance_exists(Forest_Controller))
                        {
                            var a_fc = instance_find(Forest_Controller, 0);
                            if (a_wt2.done_cooking)
                                a_wl += "", ready, use the can on it"";
                            else
                                a_wl += "", "" + string(a_fc.cooking_progress) + "" of "" +
                                        string(a_fc.ingredients) + "" ingredients in"";
                        }

                        // The letter in apartment 1. Two objects and four separate things to
                        // put in, and every wrong press is silent: each stage falls through its
                        // if-chain and does nothing at all when a piece is missing. The final
                        // press has to be made with NOTHING selected, because holding the stamp
                        // re-runs the stamp branch and exits before the finish branch is
                        // reached - which looks exactly like the letter refusing to be picked
                        // up. coin, adress and stamp are all set in the objects' own Creates.
                        if (a_wnr == ""Flat_Mail"")
                        {
                            a_wl = ""Envelope"";
                            if (a_wt2.image_index == 0)
                                a_wl += "", no bill in it"";
                            if (!a_wt2.coin)
                                a_wl += "", no coin in it"";
                            if (a_wt2.image_index != 0 && a_wt2.coin)
                                a_wl += "", ready, press it with nothing selected"";
                        }
                        if (a_wnr == ""Flat_Mail_02"")
                        {
                            a_wl = ""Envelope"";
                            if (!a_wt2.adress)
                                a_wl += "", not addressed"";
                            if (!a_wt2.stamp)
                                a_wl += "", not stamped"";
                            if (a_wt2.adress && a_wt2.stamp)
                                a_wl += "", ready, press it with nothing selected"";
                        }

                        if (ds_list_find_value(a11y_wnm, a11y_w_idx) == ""Bridge_Coin_Item"")
                        {
                            a_wl = ""Coin"";
                            if (a11y_warn)
                            {
                                if (a_wt2.lightning)
                                    a_wl += "", electrified"";
                                else
                                    a_wl += "", safe to take"";
                            }
                        }

                        // The hospital hall lights, and the elevator they gate.
                        //
                        // Hospital_Elevator_Button's hover shows the locked cursor whenever
                        // Hospital_Controller.hall_light is 0, and says nothing about why.
                        // The switch that controls it is a room away, in Lvl_Hospital_Hall,
                        // where it reads identically whether the lights are on or off - the
                        // only cue either place is how bright the screen is. Between them
                        // that is a dead end with nothing to hear at either end of it.
                        //
                        // Hospital_Controller is persistent and its Create always sets
                        // hall_light, so both reads are safe once it exists at all.
                        // Switches, valves, curtains and drawers. Generated - see the
                        // injector for how the pairs are found and validated.
    /*SWITCHSTATE*/

                        if (instance_exists(Hospital_Controller))
                        {
                            var a_hlc = instance_find(Hospital_Controller, 0);
                            if (a_wnr == ""Hosptital_Hall_Light"")
                            {
                                // The object name is the game's own misspelling.
                                a_wl = ""Hall light switch"";
                                if (a_hlc.hall_light)
                                    a_wl += "", lights on"";
                                else
                                    a_wl += "", lights off"";
                            }
                            if (a_wnr == ""Hospital_Elevator_Button"" && a11y_hint)
                            {
                                if (!a_hlc.hall_light)
                                    a_wl += "", the hall lights are off"";
                            }

                            // Same shape again, in the reception. The computer only works
                            // while its cable is plugged in, and the cable is a plain toggle
                            // whose hover is _cursor_hand() either way. Worse, the computer's
                            // own hover sets NO cursor at all while the cable is out, so it
                            // read as a bare name with no verb and no hint that it was
                            // waiting on anything.
                            if (a_wnr == ""Hospital_Reception_Wire_Mask"")
                            {
                                a_wl = ""Computer cable"";
                                if (a_hlc.computer_wire)
                                    a_wl += "", plugged in"";
                                else
                                    a_wl += "", unplugged"";
                            }
                            if (a_wnr == ""Hospital_Reception_Computer"" && a11y_hint)
                            {
                                if (!a_hlc.computer_wire)
                                    a_wl += "", the cable is unplugged"";
                            }
                        }
                    }
                }

                // Put the REAL pointer on whatever now has focus.
                //
                // Several things here are gated purely on where the mouse physically is,
                // with no click involved - Bridge_05_Barier raises the whole wire objective
                // only while the pointer is within 60px of Bridge_Wire - and none of that
                // is reachable from a keyboard otherwise. It also keeps the game's own
                // hover state, info text and cursor art correct for anyone watching, and
                // stops View_Back's 'active' flag flickering off against a stale pointer.
                //
                // Converted THROUGH the current pointer position rather than by absolute
                // view arithmetic, so whatever constant offset exists between window space
                // and room space cancels out and only the scale has to be right. Unindexed
                // view_ variables, exactly as the game's own Screen_Controller writes them.
                if (instance_exists(a_wt2) && view_wview > 0 && view_hview > 0)
                {
                    var a_px = a_wt2.x;
                    var a_py = a_wt2.y;
                    if (a_wt2.sprite_index != -1)
                    {
                        a_px = a_wt2.bbox_left + ((a_wt2.bbox_right - a_wt2.bbox_left) / 2);
                        a_py = a_wt2.bbox_top + ((a_wt2.bbox_bottom - a_wt2.bbox_top) / 2);
                    }
                    window_mouse_set(
                        window_mouse_get_x() + (((a_px - mouse_x) * window_get_width()) / view_wview),
                        window_mouse_get_y() + (((a_py - mouse_y) * window_get_height()) / view_hview));
                }

                a_wl = a_wpre + string(a11y_w_idx + 1) + "" of "" + string(a_wc) + "", "" + a_wl;
                external_call(a11y_f_speak, a_wl, a_wint);
            }

            // Remember WHICH THING we are on, so next frame's rebuild can find it again
            // wherever it has moved to in the list.
            //
            // Moving off something also RE-ARMS its description. The anti-repeat rule is
            // not-twice-in-a-row-on-the-same-thing, and keying that off the last object
            // PRESSED got it wrong: press a bin, walk along the wall, come back to the bin
            // and press it again, and the description stayed silent because nothing else
            // had been pressed in between. Keying it off the focus instead means leaving
            // the entry is what clears it, which is what the rule was always meant to say.
            var a_wprev = a11y_w_id;
            if (a11y_w_idx >= 0 && a11y_w_idx < a_wc)
                a11y_w_id = ds_list_find_value(a11y_wi, a11y_w_idx);
            else
                a11y_w_id = 0;
            if (a11y_w_id != a_wprev)
                a11y_scn_id = 0;
        }
        else
        {
            a11y_w_id = 0;
            a11y_wpend = 0;
            a11y_wsay = """";
            a11y_wpost = 0;
        }
    }

    if (a_speak && a11y_idx >= 0 && a11y_idx < a11y_n && a11y_ready)
    {
        var a_nm2 = ds_list_find_value(a11y_nm, a11y_idx);
        var a_in2 = ds_list_find_value(a11y_ins, a11y_idx);
        var a_sb2 = ds_list_find_value(a11y_sub, a11y_idx);
        var a_txt = ds_map_find_value(a11y_lbl, a_nm2);
        var a_kd2 = ds_map_find_value(a11y_act, a_nm2);

        // Options on the title screen opens the very same Game_Menu object as the in-game
        // pause menu, so there this button closes the panel rather than resuming anything.
        if (a_nm2 == ""Game_Menu_Resume"" && room == Lvl_Main_Menu)
            a_txt = ""Back"";

        if (instance_exists(a_in2))
        {
            // Slot buttons carry a 'par' pointing at their slot, which is where the slot
            // number and whether it is empty come from.
            if (a_nm2 == ""Slot_New_Game"" || a_nm2 == ""Slot_Load"" || a_nm2 == ""Slot_Delete"")
            {
                var a_p = a_in2.par;
                if (a_p != -4 && instance_exists(a_p))
                {
                    var a_sn = string_replace(a_p.name, ""Slot"", ""Slot "");
                    if (a_p.empty)
                        a_txt = a_sn + "", empty, "" + a_txt;
                    else
                        a_txt = a_sn + "", chapter "" + string(a_p.chapter) + "", "" + a_txt;
                }
            }

            // Sliders: name the row and read the value as a percentage. Without the value
            // these rows are unusable - 'Volume' on its own tells you nothing.
            if (a_kd2 == 2)
            {
                var a_v = 0;
                if (a_nm2 == ""Game_Menu_Volume"")
                {
                    if (a_sb2 == 0)
                    {
                        a_txt = ""Sound Volume"";
                        a_v = a_in2.length;
                    }
                    else if (a_sb2 == 1)
                    {
                        a_txt = ""Music Volume"";
                        a_v = a_in2.length_mus;
                    }
                    else
                    {
                        a_txt = ""Effects Volume"";
                        a_v = a_in2.length_sfx;
                    }
                }
                else
                {
                    a_v = a_in2.length;
                }
                a_txt += "", "" + string(round((a_v / 140) * 100)) + "" percent"";
            }

            // Toggles: say which way they are set.
            if (a_kd2 == 3)
            {
                if (a_nm2 == ""Game_Menu_Sound_3D"")
                {
                    if (Controller.sound_3D)
                        a_txt += "", on"";
                    else
                        a_txt += "", off"";
                }
                else if (a_nm2 == ""Game_Menu_Screen_Window_Size"")
                {
                    if (Controller.window_small)
                        a_txt += "", small"";
                    else
                        a_txt += "", large"";
                }
                else
                {
                    // Mute is not its own flag - the game treats a zero sound slider as
                    // muted, which is exactly what its own sprite does.
                    if (instance_exists(Game_Menu_Volume))
                    {
                        if (Game_Menu_Volume.length == 0)
                            a_txt += "", on"";
                        else
                            a_txt += "", off"";
                    }
                }
            }
        }

        a_txt = a_pre + a_txt;
        if (a_txt != a11y_last)
        {
            a11y_last = a_txt;
            external_call(a11y_f_speak, a_txt, 1);
        }
    }

    // A setting changed. Written now rather than at shutdown, because a game closed from
    // the desktop never gets a shutdown, and losing somebody's preferences to that would
    // be a poor reward for setting them. One frame's file write, only on the frames a
    // setting actually moved.
    if (a11y_setsave)
    {
        a11y_setsave = 0;
        ini_open(""A11y.ini"");
        ini_write_real(""Options"", ""Warnings"", a11y_warn);
        ini_write_real(""Options"", ""Hints"", a11y_hint);
        ini_write_real(""Options"", ""AreaNames"", a11y_area);
        ini_write_real(""Options"", ""HideClutter"", a11y_junk);
        ini_close();
    }

    // Hand this frame's state back to the marker, which outlives any Controller.
    if (instance_exists(a_mk))
    {
/*PERSIST*/
    }
}
";

// ---------------------------------------------------------------------------
// Apply
// ---------------------------------------------------------------------------
// The recovery path runs the very same init, so the two can never drift apart.
// Deliberately NOT destroying the old structures first: after a load we cannot prove the
// ids are still ours, and freeing one of the game's lists by mistake would be far worse
// than leaking a handful of ours. A load happens a few times a session at most.
tick = tick.Replace("/*REINIT*/", initBody);
tick = tick.Replace("/*FLAGSTATE*/", flagLines.ToString().TrimEnd('\n'));
tick = tick.Replace("/*HYDRATE*/", hydrateBlock);
tick = tick.Replace("/*PERSIST*/", persistBlock);
tick = tick.Replace("/*SCHEMATAG*/", schemaTag.ToString());

// DO NOT try to use variable_local_exists here. It is the obvious tool for the recovery
// check - in GameMaker 1.4 'local' means instance scope, and it is the only builtin that
// asks whether a variable is set without reading it - and it does not work in this game.
// Recorded in full because it looks like it should, twice over:
//
//   * Getting it to COMPILE takes two registrations, not one. Adding it to the FUNC chunk
//     alone still fails with 'Failed to find function "variable_local_exists"', because
//     UTMT validates against its own Data.BuiltinList.Functions dictionary, which is
//     separate from the data file and ships variable_global_exists but not this.
//   * With both registrations it compiles cleanly, decompiles correctly, and the runner
//     does export it (builtin table entry at 0x4fcad0, one argument) - and the game then
//     dies at once with 'trying to index a variable which is not an array', the same
//     misleading message this bytecode gives for adding a global. Everything else in the
//     recovery check was ruled out first: ds_type_map compiles to a real constant, and
//     ds_exists / ds_map_exists were already in use elsewhere in this patch.
//
// The recovery therefore uses instance_exists on a marker object instead, which needs
// nothing added to the data file at all.

// ---------------------------------------------------------------------------
// The one thing outside Controller.
//
// Info's Draw calls draw_surface(info_surface, ...), but the ONLY code that ever creates
// that surface is Info's Step:
//
//     Step:  if (!surface_exists(info_surface)) info_surface = surface_create(1024, 768);
//     Draw:  draw_surface(info_surface, 0, 0);
//
// An instance born partway through a frame therefore reaches its first Draw with nothing
// to draw, and the game dies with 'Trying to use non-existing surface'. This is the game's
// own latent bug - opening a note from the keyboard just hits it every time.
//
// It has to be fixed HERE, immediately before the surface is used. Guarding it from
// Controller does not work and was tried: Controller's Step runs before the click that
// creates the popup, so by the time the instance exists the guard has already run for
// that frame. This is the only place that cannot be outrun by whoever created it.
//
// All 28 Info children inherit both this Draw and Info's Create, so one prepend covers
// every popup and info_surface is always defined.
string infoFix = @"
if (!surface_exists(info_surface))
{
    info_surface = surface_create(1024, 768);
    surface_set_target(info_surface);
    draw_clear_alpha(c_black, 0);
    surface_reset_target();
}
";

// ---------------------------------------------------------------------------
// Escape should back out one level before it reaches for the pause menu.
//
// This has to be PREPENDED to Controller's own Escape handler, not handled in the tick:
// GameMaker runs Key Press events BEFORE Step, so by the time the tick sees the key the
// pause menu already exists. Exiting early here stops the game's handler running at all.
//
// Reads only the marker, which is the authority for this state - see the tick guard.
// patch the Controller has none of the a11y_ variables until the tick rebuilds them, and
// reading one that is not set is fatal. Everything is read off Controller instance 0,
// which is the only one that does accessibility work - this event fires on every instance.
string escapeFix = @"
var a_mk = instance_find(Worm_February, 0);
if (instance_exists(a_mk))
{
    if (a_mk.friction == /*SCHEMATAG*/)
    {
    // The mod's own settings screen. First, because while it is open it owns the keys -
    // and Escape here must close it rather than the pause menu underneath it.
    //
    // Nothing is said: clearing the signature makes the menu behind it read itself out
    // again on the next frame, which is the better answer anyway.
    if (a_mk.a11y_set)
    {
        a_mk.a11y_set = 0;
        a_mk.a11y_sig = """";
        exit;
    }
    if (a_mk.a11y_inv)
    {
        a_mk.a11y_inv = 0;
        if (a_mk.a11y_ready)
            external_call(a_mk.a11y_f_speak, ""Inventory closed."", 1);
        exit;
    }
    if (instance_exists(Info))
    {
        with (instance_find(Info, 0))
            event_perform(ev_mouse, 53);
        exit;
    }
    // The computer's windows have no keyboard way out at all. Both the X
    // (Computer_Exit_Window) and the dialogs' OK (Computer_OK_Button) answer only to a
    // real Mouse_4 press on the instance, and neither is an Interactive_Object, so the
    // game's click dispatch never raises them and they never reach the scene list.
    // Escape does what those buttons do: destroy the window they belong to.
    //
    // Guarded on Computer_Window_Opener exactly as they are - a window is still animating
    // open while that exists, and both buttons refuse to act until it is gone.
    //
    // The orphan sweep matters: Computer_Virus_Alert has no Destroy event, so its OK
    // button is not cleaned up with it the way every other window cleans up its own.
    if (instance_exists(Computer_Window) && instance_number(Computer_Window_Opener) == 0)
    {
        with (instance_find(Computer_Window, instance_number(Computer_Window) - 1))
            instance_destroy();
        with (Computer_OK_Button)
            if (!instance_exists(parent))
                instance_destroy();
        with (Computer_Exit_Window)
            if (!instance_exists(parent))
                instance_destroy();
        _sound_play_simple(471, 100, 0);
        exit;
    }
    }
}
";

var infoObj = Data.GameObjects.ByName("Info");
var infoDraw = infoObj?.EventHandlerFor(EventType.Draw, EventSubtypeDraw.Draw, Data);
var escKey = controller.EventHandlerFor(EventType.KeyPress, (uint)27, Data);

// The switch table is generated, so it is spliced into the tick after the fact rather
// than living inside the literal above.
tick = tick.Replace("/*SWITCHSTATE*/", switchLines.ToString().TrimEnd('\n'));

// The whole scene reader is the ELSE of 'if (!a_world)', so anything inserted between
// that closing brace and its else silently re-binds the else to the new statement - which
// is how the scene list came to run while the inventory was open. Cheap to check, and the
// failure is invisible until someone plays that exact case.
if (!tick.Contains("        a_world = 0;\n    }\n    else\n    {"))
    throw new Exception("The scene reader is no longer the else of 'if (!a_world)' - "
                      + "something was inserted between that block and its else.");

UndertaleModLib.Compiler.CodeImportGroup importGroup = new(Data, globalCtx, settings);
importGroup.QueueAppend(controller.EventHandlerFor(EventType.Create, Data), init);
importGroup.QueueAppend(controller.EventHandlerFor(EventType.Step, EventSubtypeStep.Step, Data), tick);
if (infoDraw != null)
{
    importGroup.QueuePrepend(infoDraw, infoFix);
    Console.WriteLine("Guarded Info's Draw against its missing surface.");
}
else
{
    Console.WriteLine("WARNING: Info's Draw event not found - the surface crash is NOT fixed.");
}
if (escKey != null)
{
    importGroup.QueuePrepend(escKey, escapeFix.Replace("/*SCHEMATAG*/", schemaTag.ToString()));
    Console.WriteLine("Escape now backs out of the inventory and info popups first.");
}
else
{
    Console.WriteLine("WARNING: Controller's Escape handler not found - Escape unchanged.");
}
importGroup.Import();

Console.WriteLine("Injected accessibility into Controller (Create + Step). No other assets changed.");
