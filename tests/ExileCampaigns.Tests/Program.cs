using System;
using System.Collections.Generic;
using ExileCampaigns.Guide;

var tests = new (string Name, Action Body)[]
{
    ("route metadata and nested guidance round-trip", RouteMetadataAndGuidanceRoundTrip),
    ("legacy v1 tiles reconstruct guidance", LegacyV1TilesReconstructGuidance),
    ("malformed route fails closed", MalformedRouteFailsClosed),
    ("missing optional fields use safe defaults", MissingOptionalFieldsUseSafeDefaults),
};

var failures = 0;
foreach (var (name, body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

if (failures != 0)
    throw new InvalidOperationException($"{failures} ExileCampaigns route test(s) failed.");

Console.WriteLine($"{tests.Length} ExileCampaigns route tests passed.");

static void RouteMetadataAndGuidanceRoundTrip()
{
    var target = new Target(TargetKind.Entity, new Pattern("Metadata/Quest/Boss", true), MatchKind.Path, true);
    var step = new RouteStep(
        "act1-boss", 1, "Forest", "The Forest", "Defeat the boss", "keep the waypoint", false,
        CompleteWhen.All,
        [new Objective(
            ObjectiveType.Kill,
            Entities: [new EntityMatcher(new Pattern("The Boss"))],
            Count: 1,
            Paths: [new GuidePath(target)],
            Indicators: [new Indicator(target)],
            MinimapIcons: [new MinimapIcon("Boss", target, 0xFF00FF00, 24f)],
            Mode: PathMode.All)],
        "abc123");
    var source = new RouteDocument(2, [step], new RouteMetadata("poe2", "0.5.4e", "Runes of Aldur", "fixture", "2026-08-09T00:00:00Z"));

    var parsed = RouteJson.Read(RouteJson.Write(source));
    Assert(parsed.Version == 2, "version");
    Assert(parsed.Metadata?.League == "Runes of Aldur", "metadata league");
    Assert(parsed.Steps.Count == 1 && parsed.Steps[0].ImportFp == "abc123", "step identity");
    var objective = parsed.Steps[0].Objectives[0];
    Assert(objective.Paths?[0].Target.MatchBy == MatchKind.Path, "path match kind");
    Assert(objective.Paths?[0].Target.LivingOnly == true, "living-only target");
    Assert(objective.MinimapIcons?[0].Size == 24f, "icon size");
}

static void LegacyV1TilesReconstructGuidance()
{
    const string json = """
    { "version": 1, "steps": [ { "id": "legacy", "act": 2, "areaId": "Cave", "areaName": "The Cave", "text": "Find it", "note": "", "objectives": [
      { "type": "Interact", "entities": [ { "match": { "value": "Door" }, "matchBy": "Name" } ],
        "tiles": [ { "value": "room-a" }, { "value": "room-b", "regex": true } ] }
    ] } ] }
    """;

    var parsed = RouteJson.Read(json);
    var objective = parsed.Steps[0].Objectives[0];
    Assert(objective.Paths?.Count == 3, "legacy paths count");
    Assert(objective.Paths?[0].Target.Kind == TargetKind.Entity, "legacy entity path");
    Assert(objective.Paths?[1].Target.Kind == TargetKind.Room, "legacy interact tile becomes room");
    Assert(objective.Paths?[2].Target.Match.Regex == true, "legacy regex preserved");
    Assert(objective.Indicators?.Count == 1, "legacy entity indicator");
}

static void MalformedRouteFailsClosed()
{
    var parsed = RouteJson.Read("{not json");
    Assert(parsed == RouteDocument.Empty, "malformed route should return empty document");
}

static void MissingOptionalFieldsUseSafeDefaults()
{
    var parsed = RouteJson.Read("{ \"version\": 2, \"steps\": [ { \"id\": \"safe\", \"objectives\": [ { \"type\": \"Unknown\" } ] } ] }");
    var step = parsed.Steps[0];
    Assert(step.Act == 0 && step.AreaId == "" && !step.Optional, "step defaults");
    Assert(step.CompleteWhen == CompleteWhen.All, "completion default");
    Assert(step.Objectives[0].Type == ObjectiveType.Manual, "objective enum fallback");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
