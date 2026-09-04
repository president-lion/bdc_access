using System;
using System.Linq;
EnsureDataLoaded();
string[] want = { "Graveyard_Ars_Moriendi", "Graveyard_Crypt_Arms", "Graveyard_Crypt_Door",
                  "Graveyard_Bone", "Graveyard_Cliff_Bone", "Graveyard_Gravedigger_Skull",
                  "Graveyard_Crypt_Inside_Scythe_Blade", "Graveyard_Crypt_Skull",
                  "Graveyard_Crypt_Bone", "Graveyard_Crypt_Bone_02" };
foreach (var r in Data.Rooms)
{
    var hits = r.GameObjects.Select(g => g.ObjectDefinition?.Name?.Content)
                .Where(n => n != null && want.Contains(n)).Distinct().ToList();
    if (hits.Count > 0) Console.WriteLine(r.Name.Content + ": " + string.Join(", ", hits));
}
