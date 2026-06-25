using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ExileCampaigns.Build;

namespace ExileCampaigns;

// build helper overlay: from the build's gear list, surfaces what you can equip now and what unlocks at the
// next level. reads the build directly, ignoring route steps. used items drop off.
public partial class ExileCampaigns
{
    private List<PanelLine> BuildHelperLines(OverlayStyle s)
    {
        var lines = new List<PanelLine> { new PanelLine("Build Helper", s.HeaderColor.Value, isHeader: true) };

        if (_build.Gear.Count == 0)
        {
            lines.Add(new PanelLine("  (no build items - hover an item and press the add key)", s.OptionalColor.Value));
            return lines;
        }

        var green = Color.FromArgb(255, 120, 210, 120);
        var yellow = Color.FromArgb(255, 230, 200, 70);
        int nextLevel = _playerLevel + 1;

        string Label(BuildGearItem g) => $"{g.Name}{(g.Kind == BuildItemKind.Gem ? " (gem)" : "")}";

        var pending = _build.Gear.Where(g => !g.Used).OrderBy(g => g.TargetLevel).ToList();
        var now = pending.Where(g => g.TargetLevel <= _playerLevel).ToList();
        var next = pending.Where(g => g.TargetLevel == nextLevel).ToList();

        foreach (var g in now)
            lines.Add(new PanelLine($"  now   {Label(g)}", green));
        foreach (var g in next)
            lines.Add(new PanelLine($"  Lvl {g.TargetLevel}  {Label(g)}", yellow));

        // nothing actionable: hint the soonest upcoming so the panel still helps.
        if (now.Count == 0 && next.Count == 0)
        {
            var upcoming = pending.FirstOrDefault(g => g.TargetLevel > _playerLevel);
            lines.Add(upcoming != null
                ? new PanelLine($"  next at Lvl {upcoming.TargetLevel}: {Label(upcoming)}", s.OptionalColor.Value)
                : new PanelLine("  (all build items equipped)", s.OptionalColor.Value));
        }

        return lines;
    }
}
