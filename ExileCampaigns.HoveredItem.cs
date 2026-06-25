using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.Shared.Enums;

namespace ExileCampaigns;

// reads the item under the cursor (inventory/stash/ground) into a small snapshot. everything is wrapped so
// a stale/invalid read just yields Valid=false.
public partial class ExileCampaigns
{
    internal readonly struct HoveredItemSnapshot
    {
        public readonly string Name;
        public readonly string BaseType;
        public readonly string ItemClass;
        public readonly string Rarity;
        public readonly int RequiredLevel;
        public readonly bool IsGem;
        public readonly bool Valid;

        public HoveredItemSnapshot(string name, string baseType, string itemClass, string rarity,
            int requiredLevel, bool isGem)
        {
            Name = name;
            BaseType = baseType;
            ItemClass = itemClass;
            Rarity = rarity;
            RequiredLevel = requiredLevel;
            IsGem = isGem;
            Valid = true;
        }
    }

    private HoveredItemSnapshot TryCaptureHoveredItem()
    {
        try
        {
            var uiHover = GameController?.Game?.IngameState?.UIHover;
            if (uiHover == null || uiHover.Address == 0) return default;

            var icon = uiHover.AsObject<HoverItemIcon>();
            if (icon == null || icon.Address == 0) return default;
            if (icon.ToolTipType is ToolTipType.ItemInChat or ToolTipType.None) return default;

            var entity = uiHover.AsObject<NormalInventoryItem>()?.Item;
            return ReadItemSnapshot(entity);
        }
        catch { return default; }
    }

    // build a snapshot from an item Entity (works for hovered, inventory, or equipped items).
    private HoveredItemSnapshot ReadItemSnapshot(Entity? entity)
    {
        try
        {
            if (entity == null || entity.Address == 0 || !entity.IsValid) return default;

            var bit = GameController!.Files.BaseItemTypes.Translate(entity.Path);
            var baseType = bit?.BaseName ?? "";
            var itemClass = bit?.ClassName ?? "";

            // name preference: unique title -> Base.Name -> base type
            string name = baseType;
            int reqLvl = 0;
            string rarity = "";
            if (entity.TryGetComponent<Mods>(out var mods) && mods != null)
            {
                reqLvl = mods.RequiredLevel;
                rarity = mods.ItemRarity.ToString();
                if (!string.IsNullOrEmpty(mods.UniqueName)) name = mods.UniqueName;
            }
            if (name == baseType && entity.TryGetComponent<Base>(out var b) && b != null
                && !string.IsNullOrEmpty(b.Name))
                name = b.Name;

            bool isGem = entity.TryGetComponent<SkillGem>(out _)
                || itemClass.Contains("Skill Gem")
                || itemClass.StartsWith("Uncut");

            return new HoveredItemSnapshot(name, baseType, itemClass, rarity, reqLvl, isGem);
        }
        catch { return default; }
    }
}
