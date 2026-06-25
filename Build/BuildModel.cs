using System;
using System.Collections.Generic;

namespace ExileCampaigns.Build;

// per-character build data. pure C# (no ExileCore2 refs) so the test project can compile it.
// gear list holds both equipment and gems, told apart by the Kind flag.

public enum BuildItemKind { Equipment, Gem }

// one planned item to equip/socket at a target level. captured from a hovered in-game item.
public sealed class BuildGearItem
{
    public string Name { get; set; } = "";
    public string BaseType { get; set; } = "";
    public string ItemClass { get; set; } = "";   // slot hint, e.g. "Helmet", "Skill Gem"
    public string Rarity { get; set; } = "";       // ItemRarity.ToString(); string keeps this ExileCore2-free
    public int TargetLevel { get; set; }           // user-entered, prefilled from RequiredLevel
    public int RequiredLevel { get; set; }         // captured min req
    public string Description { get; set; } = "";
    public BuildItemKind Kind { get; set; } = BuildItemKind.Equipment;
    public bool Used { get; set; }                  // set sticky once detected equipped; greys the indicator/alert
    public DateTime CapturedAt { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");   // stable handle for edit/remove/reorder
}

// container persisted inside each profile json. Version lets later shapes migrate.
public sealed class CharacterBuild
{
    public int Version { get; set; } = 1;
    public List<BuildGearItem> Gear { get; set; } = new();
}
