using System;
using System.Collections.Generic;
using System.Linq;

namespace ExileCampaigns.Guide;

// flattened step + its act and 1-based position within that act (StepInAct drives the tracker number column
// + act headers). Model is the RouteStep for advance evaluation, null for synth headers.
public sealed record FlatStep(int Act, int StepInAct, ParsedStep Step, RouteStep? Model = null)
{
    // display string for overlay/banner: the model's clean Text. synth PlainText repeats the kill line
    // twice, so prefer Model.Text. headers have no model -> fall back to synth PlainText (the area label).
    public string DisplayText =>
        !string.IsNullOrEmpty(Model?.Text) ? Model!.Text : Step.PlainText();
}

// flattens the route to one ordered sequence, tracks the current step. drives auto-advance: each zone-header
// step carries an area id mapped to its position so an AreaChange jumps straight to it. pure C#.
public sealed class RouteRepository
{
    private readonly List<FlatStep> _steps = new();
    private readonly Dictionary<string, List<int>> _areaToIndices = new(StringComparer.OrdinalIgnoreCase);
    // effective area id per step: most recent zone-header AreaId at or before that step, so sub-steps with no
    // AreaId of their own still know their zone. quest-flag auto-advance confines its search to the area block.
    private readonly List<string> _stepArea = new();
    // cleaned zone label parallel to _stepArea, set on zone headers and carried forward so every sub-step
    // knows the readable name without re-scanning headers.
    private readonly List<string> _stepAreaName = new();

    public IReadOnlyList<FlatStep> Steps => _steps;
    public int Current { get; private set; }
    public string? Status { get; private set; }

    // mirrors Settings.ShowOptional. when false, navigation skips optional steps so advance/back land only on
    // visible ones. default true.
    public bool IncludeOptional { get; set; } = true;
    public FlatStep? CurrentStep => _steps.Count > 0 && Current < _steps.Count ? _steps[Current] : null;

    // effective area id of the current step: its own AreaId (zone header) or the most recent header above it.
    // the path resolver keys into the authored area->tile map with this.
    public string CurrentAreaId => Current >= 0 && Current < _stepArea.Count ? _stepArea[Current] : "";

    // readable zone label of the current step's area (cleaned of [tags] and (level)), for the header top-right.
    public string CurrentAreaName => Current >= 0 && Current < _stepAreaName.Count ? _stepAreaName[Current] : "";

    // count of real (numbered) steps in an act, for the route-progress stat. headers carry StepInAct 0.
    public int StepsInAct(int act) => _steps.Count(s => s.Act == act && s.StepInAct > 0);

    // strip a zone header label to the bare name: "The Grelwood [WAYPOINT] (Lvl 3-5)" -> "The Grelwood".
    // cut at the first " [" or " (", whichever comes first.
    private static string CleanAreaLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return "";
        int br = label.IndexOf(" [", StringComparison.Ordinal);
        int pa = label.IndexOf(" (", StringComparison.Ordinal);
        int cut = br < 0 ? pa : pa < 0 ? br : Math.Min(br, pa);
        return (cut < 0 ? label : label.Substring(0, cut)).Trim();
    }

    // a zone-label line: kept for the area->index map but never an objective, navigation never rests on one.
    // narrower than "has an AreaId" since waypoint-use/logout/crafting steps carry an AreaId yet are real tasks.
    private static bool IsHeaderStep(ParsedStep s) => s.IsHeader;

    // a step navigation must never rest on: a zone-label header always, plus an optional step when optional
    // is hidden. both are stepped over together by advance/back.
    private bool IsSkippable(int i) =>
        IsHeaderStep(_steps[i].Step) || (!IncludeOptional && _steps[i].Step.IsOptional);

    // after an advance landed Current on a skippable row, step forward to the next visible objective.
    // falls back to the nearest visible step behind when nothing visible lies ahead.
    private void SnapForwardVisible()
    {
        if (_steps.Count == 0) { Current = 0; return; }
        if (!IsSkippable(Current)) return;
        for (int i = Current; i < _steps.Count; i++)
            if (!IsSkippable(i)) { Current = i; return; }
        for (int i = Current - 1; i >= 0; i--)
            if (!IsSkippable(i)) { Current = i; return; }
    }

    // if Current landed on a zone-label header, move to the first real step in that zone (forward preferred,
    // fall back to the last real step behind).
    private void SnapOffHeader()
    {
        if (_steps.Count == 0) { Current = 0; return; }
        if (!IsHeaderStep(_steps[Current].Step)) return;
        for (int i = Current; i < _steps.Count; i++)
            if (!IsHeaderStep(_steps[i].Step)) { Current = i; return; }
        for (int i = Current - 1; i >= 0; i--)
            if (!IsHeaderStep(_steps[i].Step)) { Current = i; return; }
    }

    // test helper: flatten pre-built sections directly. mirrors LoadFromDocument's flatten logic.
    internal void LoadSections(IReadOnlyList<RouteSection> sections)
    {
        _steps.Clear(); _areaToIndices.Clear(); _stepArea.Clear(); _stepAreaName.Clear();
        Current = 0;
        foreach (var section in sections)
        {
            int stepInAct = 0; string curArea = ""; string curAreaName = "";
            foreach (var step in section.Steps)
            {
                var index = _steps.Count;
                int num = IsHeaderStep(step) ? 0 : ++stepInAct;
                _steps.Add(new FlatStep(section.Act, num, step));
                var areaId = step.AreaId;
                if (!string.IsNullOrEmpty(areaId))
                {
                    curArea = areaId;
                    if (!_areaToIndices.TryGetValue(areaId, out var list))
                        _areaToIndices[areaId] = list = new List<int>();
                    list.Add(index);
                }
                if (IsHeaderStep(step))
                    curAreaName = CleanAreaLabel(step.PlainText());
                _stepArea.Add(curArea);
                _stepAreaName.Add(curAreaName);
            }
        }
        SnapOffHeader();
    }

    // load the unified RouteDocument: one synth header per area boundary, then each model step as a
    // FlatStep carrying both a synth ParsedStep (for guidance/render) and its RouteStep (for advance).
    public bool LoadFromDocument(RouteDocument doc)
    {
        _steps.Clear(); _areaToIndices.Clear(); _stepArea.Clear(); _stepAreaName.Clear();
        Current = 0;
        if (doc == null) { Status = "no route document"; return false; }

        int act = -1, stepInAct = 0;
        string curArea = "";
        foreach (var model in doc.Steps)
        {
            // act split resets the per-act objective number.
            if (model.Act != act) { act = model.Act; stepInAct = 0; }

            // area boundary: emit a synth header row (StepInAct 0, no model).
            if (!string.Equals(model.AreaId, curArea, System.StringComparison.OrdinalIgnoreCase))
            {
                curArea = model.AreaId ?? "";
                var header = LegacyView.HeaderFor(model.AreaId ?? "", model.AreaName ?? "");
                int hidx = _steps.Count;
                _steps.Add(new FlatStep(model.Act, 0, header, null));
                if (!string.IsNullOrEmpty(curArea))
                {
                    if (!_areaToIndices.TryGetValue(curArea, out var hlist))
                        _areaToIndices[curArea] = hlist = new List<int>();
                    hlist.Add(hidx);
                }
                _stepArea.Add(curArea);
                _stepAreaName.Add(model.AreaName ?? "");
            }

            int idx = _steps.Count;
            _steps.Add(new FlatStep(model.Act, ++stepInAct, LegacyView.ForStep(model), model));
            _stepArea.Add(curArea);
            _stepAreaName.Add(model.AreaName ?? "");
        }

        SnapOffHeader();
        Status = $"{doc.Steps.Count} steps ({_steps.Count} rows)";
        return _steps.Count > 0;
    }

    // how far ahead auto-advance may jump. early arrival at a zone that next appears far ahead
    // shouldn't skip a chunk of the campaign.
    private const int AdvanceWindow = 5;

    // entered a new area: jump to the route step for that area id. forward-only: nearest occurrence at or
    // ahead of current, within AdvanceWindow. a revisit whose steps are all behind us (e.g. TP to town),
    // or whose next step is beyond the window, leaves current unchanged.
    public void OnAreaChanged(string? areaIdLower)
    {
        if (string.IsNullOrEmpty(areaIdLower)) return;
        if (!_areaToIndices.TryGetValue(areaIdLower, out var idxs) || idxs.Count == 0) return;
        int next = int.MaxValue;
        foreach (var i in idxs)
            if (i >= Current && i - Current <= AdvanceWindow && i < next) next = i;
        if (next == int.MaxValue) return;

        // matched step is the zone-label header. rest on the zone's first visible task.
        Current = next;
        SnapForwardVisible();
    }

    // advance past the current step iff it itself matches, area-agnostic. for a global flag that should only
    // complete the step you're on. e.g. any waypoint flips AchievementCheckWaypoints but should only clear
    // the "Take waypoint" step you're on, never scan ahead.
    public bool AdvanceCurrentStepIfMatch(Func<string, bool> matches)
    {
        if (_steps.Count == 0 || Current < 0 || Current >= _steps.Count) return false;
        if (IsHeaderStep(_steps[Current].Step)) return false;
        if (!matches(_steps[Current].Step.PlainText())) return false;
        int target = Math.Min(Current + 1, _steps.Count - 1);
        if (target <= Current) return false;
        Current = target;
        SnapForwardVisible();
        return true;
    }

    // advance past the current step iff its meta says "complete on entering <area>" and we just entered it.
    // current-step-only, never scans ahead. e.g. the optional Mausoleum cache clears when you walk back into
    // the Cemetery, grabbed or not.
    public bool AdvanceCurrentStepIfEnteredArea(string? areaIdLower)
    {
        if (string.IsNullOrEmpty(areaIdLower) || _steps.Count == 0 || Current < 0 || Current >= _steps.Count) return false;
        var step = _steps[Current].Step;
        if (IsHeaderStep(step)) return false;
        var want = step.Meta?.CompleteOnEnterArea;
        if (string.IsNullOrEmpty(want) || !string.Equals(want, areaIdLower, StringComparison.OrdinalIgnoreCase)) return false;
        int target = Math.Min(Current + 1, _steps.Count - 1);
        if (target <= Current) return false;
        Current = target;
        SnapForwardVisible();
        return true;
    }

    // cursor parked on a "dead" reminder step (no possible auto-advance, e.g. "TP back to start of zone").
    // find the first live step ahead so completing it pulls the cursor past the reminder. forward-only,
    // within AdvanceWindow, over consecutive dead reminders. returns -1 when current is itself live or no
    // live step is in reach. stops at a zone header (crossing a boundary is the area-change advance's job).
    public int FirstLiveStepAheadIndex(Func<ParsedStep, bool> isDead)
    {
        if (isDead == null || _steps.Count == 0 || Current < 0 || Current >= _steps.Count) return -1;
        if (IsHeaderStep(_steps[Current].Step) || !isDead(_steps[Current].Step)) return -1;
        for (int i = Current + 1; i < _steps.Count && i - Current <= AdvanceWindow; i++)
        {
            if (IsHeaderStep(_steps[i].Step)) return -1;   // don't skip across a zone label
            if (isDead(_steps[i].Step)) continue;          // skip a further dead reminder
            return i;                                       // first live step in reach
        }
        return -1;
    }

    // how many visible steps ahead the quest-flag catch-up may scan and jump. kept small so auto-advance
    // never leaps a big chunk of the route; manual sync handles a real long-distance catch-up.
    private const int FlagLookaheadWindow = 3;

    // catch-up advance: when a quest flag for a step ahead has already flipped (player skipped content or
    // flags tripped out of order), jump to the proper step. scans the next visible steps within the window,
    // takes the furthest whose QuestFlag objective is satisfied, and lands on the first visible step after it.
    // forward-only; returns true when the cursor moved.
    public bool AdvanceToSatisfiedFlagAhead(Func<Pattern, bool> isFlagTrue)
    {
        if (isFlagTrue == null || _steps.Count == 0 || Current < 0 || Current >= _steps.Count) return false;

        int floor = -1;   // furthest upcoming visible step proven done by a satisfied quest flag
        int seen = 0;
        for (int i = Current + 1; i < _steps.Count && seen < FlagLookaheadWindow; i++)
        {
            if (IsSkippable(i)) continue;   // headers + hidden optionals are never landing spots
            seen++;
            var m = _steps[i].Model;
            if (m?.Objectives == null) continue;
            foreach (var o in m.Objectives)
                if (o.Type == ObjectiveType.QuestFlag && o.Flag != null && isFlagTrue(o.Flag)) { floor = i; break; }
        }
        if (floor < 0) return false;

        int target = floor;
        for (int i = floor + 1; i < _steps.Count; i++)
            if (!IsSkippable(i)) { target = i; break; }
        if (target <= Current) return false;

        Current = target;
        SnapForwardVisible();
        return true;
    }

    // read-only: does any step in `area`'s block satisfy the predicate? doesn't mutate position.
    public bool AnyStepInArea(string area, Func<string, bool> matches)
    {
        if (string.IsNullOrEmpty(area)) return false;
        for (int i = 0; i < _steps.Count; i++)
            if (string.Equals(_stepArea[i], area, StringComparison.OrdinalIgnoreCase)
                && matches(_steps[i].Step.PlainText()))
                return true;
        return false;
    }

    // step over zone-label headers (and hidden optional steps) so manual/auto next/prev lands only on
    // visible objectives.
    public void Next()
    {
        if (_steps.Count == 0) return;
        int i = Current + 1;
        while (i < _steps.Count && IsSkippable(i)) i++;
        if (i < _steps.Count) Current = i;
    }

    public void Prev()
    {
        if (_steps.Count == 0) return;
        int i = Current - 1;
        while (i >= 0 && IsSkippable(i)) i--;
        if (i >= 0) Current = i;
    }

    // restore a saved position (clamped to the loaded route, snapped off any zone label).
    public void SetCurrent(int index)
    {
        if (_steps.Count == 0) { Current = 0; return; }
        Current = Math.Clamp(index, 0, _steps.Count - 1);
        SnapOffHeader();
    }

    // upcoming steps after current (optionally hiding optional), up to `count`.
    public IEnumerable<FlatStep> Upcoming(int count, bool includeOptional)
    {
        int taken = 0;
        for (int i = Current + 1; i < _steps.Count && taken < count; i++)
        {
            if (IsHeaderStep(_steps[i].Step)) continue;        // zone labels aren't objectives
            if (!includeOptional && _steps[i].Step.IsOptional) continue;
            taken++;
            yield return _steps[i];
        }
    }

    // `count` steps before current (optionally hiding optional), oldest first so they read
    // top-to-bottom above the current step.
    public IEnumerable<FlatStep> Previous(int count, bool includeOptional)
    {
        var result = new List<FlatStep>();
        for (int i = Current - 1; i >= 0 && result.Count < count; i--)
        {
            if (IsHeaderStep(_steps[i].Step)) continue;        // zone labels aren't objectives
            if (!includeOptional && _steps[i].Step.IsOptional) continue;
            result.Add(_steps[i]);
        }
        result.Reverse();
        return result;
    }
}
