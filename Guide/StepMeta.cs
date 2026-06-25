// ExileCampaigns/Guide/StepMeta.cs
using System.Collections.Generic;

namespace ExileCampaigns.Guide;

// multi-sub-objective info for a step. EntityPath set => entity-based (match live entities whose metadata
// Path contains this fragment), else room-based.
public sealed record ObjectiveMeta(
    string? Label = null,
    IReadOnlyList<string>? Rooms = null,
    int Count = 0,
    IReadOnlyList<string>? ProgressFlags = null,
    string? EntityPath = null);

// consolidated per-step metadata, built fresh each load by LegacyView.ForStep from the step's objectives.
public sealed record StepMeta(
    string? CompletionFlag = null,
    ObjectiveMeta? Objective = null,
    string? PathTarget = null,
    string? InteractKind = null,       // dialog|chest|proximity|kill
    bool? Optional = null,
    string? Note = null,
    // advance past this step when the player (re)enters this area id. for an optional sub-zone step (e.g. the
    // Mausoleum's Forgotten Riches cache) that should clear on return to the parent zone, where no quest flag
    // re-fires (VisitedG1_7 is permanent) and OnAreaChanged is forward-only.
    string? CompleteOnEnterArea = null);
