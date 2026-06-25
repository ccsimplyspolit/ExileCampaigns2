using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using ExileCampaigns.Build;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Enums;
using ImGuiNET;

namespace ExileCampaigns;

// corner markers on inventory items that belong to the build, coloured by distance to the planned level,
// grey once equipped. equipped detection runs off ServerData (no UI needed) and marks the entry Used
public partial class ExileCampaigns
{
    private DateTime _lastUsedScan;

    // worn-gear slots we treat as equipped for used-detection. flasks/jewels/swap-only excluded
    private static readonly HashSet<InventoryNameE> EquipSlots = new()
    {
        InventoryNameE.BodyArmour1, InventoryNameE.Weapon1, InventoryNameE.Offhand1,
        InventoryNameE.Helm1, InventoryNameE.Amulet1, InventoryNameE.Ring1, InventoryNameE.Ring2,
        InventoryNameE.Gloves1, InventoryNameE.Boots1, InventoryNameE.Belt1,
        InventoryNameE.Weapon2, InventoryNameE.Offhand2,
    };

    // same item by unique/base name, or by base type + class for plain bases
    private static bool SameItem(BuildGearItem g, in HoveredItemSnapshot s)
    {
        if (!string.IsNullOrEmpty(g.Name) && g.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrEmpty(g.BaseType)
            && g.BaseType.Equals(s.BaseType, StringComparison.OrdinalIgnoreCase)
            && g.ItemClass.Equals(s.ItemClass, StringComparison.OrdinalIgnoreCase);
    }

    // first matching build entry, preferring a not-yet-used one (so a spare copy still greys out)
    private BuildGearItem? MatchBuildGear(in HoveredItemSnapshot s)
    {
        BuildGearItem? used = null;
        foreach (var g in _build.Gear)
        {
            if (!SameItem(g, s)) continue;
            if (!g.Used) return g;
            used ??= g;
        }
        return used;
    }

    // poll worn gear (~1 Hz), sticky-mark any matching build entry Used and persist on change
    private void DetectEquippedUsed()
    {
        if (_build.Gear.Count == 0) return;
        if ((DateTime.Now - _lastUsedScan).TotalSeconds < 1.0) return;
        _lastUsedScan = DateTime.Now;

        try
        {
            var holders = GameController?.IngameState?.ServerData?.PlayerInventories;
            if (holders == null) return;

            bool changed = false;
            foreach (var holder in holders)
            {
                if (holder == null || !EquipSlots.Contains(holder.TypeId)) continue;
                var items = holder.Inventory?.Items;
                if (items == null) continue;
                foreach (var e in items)
                {
                    if (e == null || e.Address == 0 || !e.IsValid) continue;
                    var snap = ReadItemSnapshot(e);
                    if (!snap.Valid) continue;
                    foreach (var g in _build.Gear)
                        if (!g.Used && g.Kind == BuildItemKind.Equipment && SameItem(g, snap))
                            { g.Used = true; changed = true; }
                }
            }

            changed |= DetectSocketedGems();
            if (changed) SaveProgress();
        }
        catch { /* server data not ready */ }
    }

    // equipped gems live in the SkillSlots1 server inventory, not as ActorSkills: each item there is a skill
    // gem whose Sockets component holds its support gems. ActorSkills only ever surfaces active skills, never
    // supports, so read these directly and sticky-mark matching build gems Used. returns true on change.
    private bool DetectSocketedGems()
    {
        bool anyGem = false;
        foreach (var g in _build.Gear)
            if (g.Kind == BuildItemKind.Gem && !g.Used) { anyGem = true; break; }
        if (!anyGem) return false;

        var holders = GameController?.IngameState?.ServerData?.PlayerInventories;
        if (holders == null) return false;

        var active = new HashSet<string>();
        foreach (var holder in holders)
        {
            var inv = holder?.Inventory;
            if (inv == null || inv.InventSlot != InventorySlotE.SkillSlots1) continue;
            var items = inv.Items;
            if (items == null) continue;
            foreach (var gem in items)
                CollectGemAndSupports(gem, active);
        }
        if (active.Count == 0) return false;

        bool changed = false;
        foreach (var g in _build.Gear)
        {
            if (g.Kind != BuildItemKind.Gem || g.Used) continue;
            if (active.Contains(Normalize(g.Name))) { g.Used = true; changed = true; }
        }
        return changed;
    }

    // add a skill gem's own name plus every support gem socketed into it, through the shared item snapshot so the
    // name matches whatever add-to-build stored. these are server-side inventory entities: IsAlive/IsHostile are
    // junk, only IsValid/Address are meaningful
    private void CollectGemAndSupports(Entity? gem, HashSet<string> active)
    {
        if (gem == null || gem.Address == 0 || !gem.IsValid) return;

        var snap = ReadItemSnapshot(gem);
        if (snap.Valid && !string.IsNullOrEmpty(snap.Name)) active.Add(Normalize(snap.Name));

        if (!gem.TryGetComponent<Sockets>(out var sockets) || sockets?.SocketedItems == null) return;
        foreach (var si in sockets.SocketedItems)
        {
            var support = si?.ItemEntity;
            if (support == null || support.Address == 0 || !support.IsValid) continue;
            var ss = ReadItemSnapshot(support);
            if (ss.Valid && !string.IsNullOrEmpty(ss.Name)) active.Add(Normalize(ss.Name));
        }
    }

    // lowercase, strip everything but a-z0-9, so "Falling Thunder" == "falling_thunder"
    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private Color IndicatorColor(BuildGearItem g)
    {
        var s = Settings.GearIndicators;
        if (g.Used) return s.UsedColor.Value;
        int delta = g.TargetLevel - _playerLevel;
        if (delta <= 0) return s.EquippableColor.Value;
        if (delta <= s.SoonWindow.Value) return s.SoonColor.Value;
        return s.LaterColor.Value;
    }

    private void DrawBuildIndicators()
    {
        if (!Settings.GearIndicators.Enable || _build.Gear.Count == 0) return;

        var panel = GameController?.IngameState?.IngameUi?.InventoryPanel;
        if (panel is not { IsVisible: true }) return;

        var items = panel[InventoryIndex.PlayerInventory]?.VisibleInventoryItems;
        if (items == null) return;

        var dl = ImGui.GetForegroundDrawList();
        float sz = Settings.GearIndicators.Size.Value;
        uint outline = U32(Color.FromArgb(220, 10, 10, 12));

        foreach (var item in items)
        {
            var e = item?.Item;
            if (e == null || e.Address == 0 || !e.IsValid) continue;

            var snap = ReadItemSnapshot(e);
            if (!snap.Valid) continue;
            var g = MatchBuildGear(snap);
            if (g == null) continue;

            var rect = item!.GetClientRectCache;
            float right = rect.X + rect.Width;
            float top = rect.Y;

            // top-right corner triangle
            var p1 = new Vector2(right - sz, top);
            var p2 = new Vector2(right, top);
            var p3 = new Vector2(right, top + sz);
            dl.AddTriangleFilled(p1, p2, p3, U32(IndicatorColor(g)));
            dl.AddTriangle(p1, p2, p3, outline, 1.5f);
        }
    }
}
