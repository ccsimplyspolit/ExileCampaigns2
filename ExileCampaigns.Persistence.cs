using System;
using System.IO;
using ExileCampaigns.Build;
using ExileCampaigns.Guide;
using Newtonsoft.Json.Linq;

namespace ExileCampaigns;

// persists route position per character (reload/char swap keeps your place). one profile file per character
// under ConfigDirectory\profiles, active one follows the logged-in name
public partial class ExileCampaigns
{
    private int _lastSavedStep = -1;
    private string _charName = "";     // active profile = character name; "" before a char is loaded
    private CharacterBuild _build = new();   // active character's build (gear list); banked/loaded with the profile

    private string ProfilesDir => Path.Combine(ConfigDirectory, "profiles");
    private string LegacyProgressPath => Path.Combine(ConfigDirectory, "progress.json");
    private string ProgressPath => string.IsNullOrEmpty(_charName)
        ? LegacyProgressPath                                              // pre-login fallback
        : Path.Combine(ProfilesDir, ProfileNameSanitizer.Sanitize(_charName) + ".json");

    // switch active profile on character change. banks the outgoing one, then loads (or starts fresh) the
    // incoming one. no-op if name is unchanged/empty
    private void SwitchProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == _charName) return;

        SaveProgress();   // bank current profile (under the old name) before switching away

        var wasDefault = string.IsNullOrEmpty(_charName);
        _charName = name;

        try { Directory.CreateDirectory(ProfilesDir); } catch { /* config dir not writable */ }

        // one-time migration: first real character inherits the pre-profiles progress.json
        if (wasDefault && !File.Exists(ProgressPath) && File.Exists(LegacyProgressPath))
        {
            try { File.Move(LegacyProgressPath, ProgressPath); } catch { /* leave legacy in place */ }
        }

        LoadProgress();
        InitStats();      // new char -> fresh run timer/splits
        // masked, not the raw name: this lands in ExileCore2's shared Verbose log
        LogMessage($"ExileCampaigns -> profile {ProfileMask.Mask(_charName)} active (step {_route.Current + 1}).");
    }

    private void LoadProgress()
    {
        try
        {
            if (!File.Exists(ProgressPath))
            {
                _route.SetCurrent(0);     // no saved progress -> start at the top
                _lastSavedStep = _route.Current;
                _build = new CharacterBuild();
                return;
            }
            var o = JObject.Parse(File.ReadAllText(ProgressPath));
            var savedId = (string?)o["stepId"];
            int savedIndex = (int?)o["step"] ?? 0;
            if (!string.IsNullOrEmpty(savedId))
            {
                // prefer id match: survives route reshuffles and step inserts
                int found = -1;
                for (int i = 0; i < _route.Steps.Count; i++)
                    if (_route.Steps[i].Model?.Id == savedId) { found = i; break; }
                if (found >= 0)
                {
                    _route.SetCurrent(found);
                    _lastSavedStep = _route.Current;
                    _build = o["build"]?.ToObject<CharacterBuild>() ?? new CharacterBuild();
                    return;
                }
            }
            _route.SetCurrent(savedIndex);   // SetCurrent already clamps + snaps off headers
            _lastSavedStep = _route.Current;
            _build = o["build"]?.ToObject<CharacterBuild>() ?? new CharacterBuild();
        }
        catch
        {
            // Never carry the previous character's cursor into a profile whose
            // progress file is corrupt or unreadable.
            _route.SetCurrent(0);
            _lastSavedStep = _route.Current;
            _build = new CharacterBuild();
        }
    }

    // write current step on change (called each Tick; cheap, only writes when changed)
    private void MaybeSaveProgress()
    {
        if (_route.Current == _lastSavedStep) return;
        SaveProgress();
    }

    private void SaveProgress()
    {
        var step = _route.Current;
        var path = ProgressPath;
        string? temporaryPath = null;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // stepId goes alongside step index so id-based restore can take over once the route is stable.
            // Replace atomically so a crash cannot leave a half-written progress profile.
            var document = new JObject
            {
                ["character"] = _charName,
                ["area"] = _areaId,
                ["step"] = step,
                ["stepId"] = _route.CurrentStep?.Model?.Id ?? "",
                ["build"] = JObject.FromObject(_build),
            };
            temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, document.ToString());
            File.Move(temporaryPath, path, true);
            _lastSavedStep = step;
        }
        catch (Exception ex)
        {
            LogError($"ExileCampaigns -> progress save failed: {ex.Message}");
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch { /* failed cleanup must not hide the original save error */ }
            }
        }
    }

    // reset a profile back to step 1. if it's active, reset the live route too; else just rewrite its file on disk
    private void ResetProfileProgress(string name)
    {
        if (name == _charName)
        {
            _route.SetCurrent(0);
            _build = new CharacterBuild();
            SaveProgress();
            return;
        }
        try
        {
            Directory.CreateDirectory(ProfilesDir);
            var path = Path.Combine(ProfilesDir, ProfileNameSanitizer.Sanitize(name) + ".json");
            File.WriteAllText(path, new JObject { ["character"] = name, ["area"] = "", ["step"] = 0 }.ToString());
        }
        catch (Exception ex) { LogError($"ExileCampaigns -> reset progress failed: {ex.Message}"); }
    }

    private static string SanitizeProfile(string name) => ProfileNameSanitizer.Sanitize(name);

}
