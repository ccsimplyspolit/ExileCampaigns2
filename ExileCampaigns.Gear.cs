using System;
using System.Numerics;
using ExileCampaigns.Build;
using ImGuiNET;

namespace ExileCampaigns;

// "add hovered item to build" dialog. AddGearKey captures the item under the cursor in Tick, then a modal
// popup (drawn in Render) asks for a target level + short note and appends it to the active build.
public partial class ExileCampaigns
{
    private HoveredItemSnapshot _pendingItem;   // snapshot taken at keypress (hover changes while dialog open)
    private bool _openGearPopup;                 // request to open the popup this frame
    private int _gearDialogLevel;
    private string _gearDialogDesc = "";
    private bool _focusGearLevel;                // grab keyboard focus on the open frame
    private const string GearPopupId = "Add to Build##ec_addgear";

    // hotkey: snapshot the hovered item and request the popup. prefill level from the item's requirement,
    // falling back to current level when the item has none.
    private void OnAddGearPressed()
    {
        var snap = TryCaptureHoveredItem();
        if (!snap.Valid)
        {
            ShowToast("No item hovered to add to build", ToastLevel.Warning);
            return;
        }
        _pendingItem = snap;
        _gearDialogLevel = snap.RequiredLevel > 0 ? snap.RequiredLevel : Math.Max(1, _playerLevel);
        _gearDialogDesc = "";
        _openGearPopup = true;
    }

    private void DrawGearDialog()
    {
        if (_openGearPopup)
        {
            ImGui.OpenPopup(GearPopupId);
            _openGearPopup = false;
            _focusGearLevel = true;
        }

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        bool open = true;
        if (!ImGui.BeginPopupModal(GearPopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.Text(_pendingItem.Name);
        var sub = $"{_pendingItem.BaseType}  |  {_pendingItem.ItemClass}" + (_pendingItem.IsGem ? "  |  gem" : "");
        ImGui.TextDisabled(sub);
        ImGui.Separator();

        if (_focusGearLevel) { ImGui.SetKeyboardFocusHere(); _focusGearLevel = false; }
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("Target level", ref _gearDialogLevel);
        if (_gearDialogLevel < 1) _gearDialogLevel = 1;

        ImGui.SetNextItemWidth(320);
        ImGui.InputText("Description", ref _gearDialogDesc, 128);

        ImGui.Separator();
        if (ImGui.Button("Add", new Vector2(90, 0)))
        {
            AddPendingItemToBuild();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(90, 0)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void AddPendingItemToBuild()
    {
        if (!_pendingItem.Valid) return;
        _build.Gear.Add(new BuildGearItem
        {
            Name = _pendingItem.Name,
            BaseType = _pendingItem.BaseType,
            ItemClass = _pendingItem.ItemClass,
            Rarity = _pendingItem.Rarity,
            TargetLevel = _gearDialogLevel,
            RequiredLevel = _pendingItem.RequiredLevel,
            Description = _gearDialogDesc.Trim(),
            Kind = _pendingItem.IsGem ? BuildItemKind.Gem : BuildItemKind.Equipment,
            CapturedAt = DateTime.Now,
        });
        SaveProgress();   // persist immediately
        ShowToast($"Added {_pendingItem.Name} @ Lvl {_gearDialogLevel}", ToastLevel.Success);
    }
}
