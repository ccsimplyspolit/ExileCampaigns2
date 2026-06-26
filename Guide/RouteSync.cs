using System;
using System.Collections.Generic;

namespace ExileCampaigns.Guide;

// manual "sync to character": pick the tracker index from live state. anchor on the first route occurrence
// of the player's current area, then resume just past the latest satisfied quest flag from that anchor on.
// quest flags are the only reliable retroactive signal (kills/talks/proximity are momentary). pure.
public static class RouteSync
{
    // returns the FlatStep index to SetCurrent to, or -1 when there are no steps.
    public static int ResolveSyncTarget(
        IReadOnlyList<FlatStep> steps, Func<Pattern, bool> isFlagTrue, string? currentAreaLower)
    {
        if (steps == null || steps.Count == 0) return -1;

        // anchor: first route step in the player's current area. flags before it are out of scope (a stale
        // earlier-act flag can't pull the cursor back). area not in the route -> scan from the start.
        int anchor = 0;
        if (!string.IsNullOrEmpty(currentAreaLower))
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var m = steps[i].Model;
                if (m != null && string.Equals(m.AreaId, currentAreaLower, StringComparison.OrdinalIgnoreCase))
                { anchor = i; break; }
            }
        }

        // from the anchor on, the furthest step whose QuestFlag objective is satisfied -- but only WITHIN the
        // anchor's act. PoE2 flags aren't monotonic (some later-act / interlude flags already read true in an
        // earlier act), so an unbounded scan would jump the cursor forward across acts. the area anchor pins
        // the act we're actually in; never pull past it on a stray future flag.
        int anchorAct = steps[anchor].Act;
        int lastTrue = -1;
        for (int i = anchor; i < steps.Count && steps[i].Act == anchorAct; i++)
        {
            var m = steps[i].Model;
            if (m?.Objectives == null) continue;
            foreach (var o in m.Objectives)
                if (o.Type == ObjectiveType.QuestFlag && o.Flag != null && isFlagTrue(o.Flag)) { lastTrue = i; break; }
        }

        // resume on the step after the latest satisfied flag; no flag yet -> the area's first visible step.
        return lastTrue >= 0 ? NextNonHeader(steps, lastTrue + 1) : NextNonHeader(steps, anchor);
    }

    // first non-header (Model != null) at or after `from`; falls back to the last non-header, else 0.
    private static int NextNonHeader(IReadOnlyList<FlatStep> steps, int from)
    {
        for (int i = Math.Max(0, from); i < steps.Count; i++)
            if (steps[i].Model != null) return i;
        for (int i = steps.Count - 1; i >= 0; i--)
            if (steps[i].Model != null) return i;
        return 0;
    }
}
