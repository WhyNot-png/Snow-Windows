using System.IO;
using System.Text.Json;

namespace SnowWindows;

public sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public bool FallingSnow { get; set; } = true;
    public bool WindowInteraction { get; set; } = true;
    public bool MouseInteraction { get; set; } = true;
    public bool Snowdrifts { get; set; } = true;
    public bool Snowplow { get; set; } = true;
    public bool PlowSpray { get; set; } = true;
    public bool PlowGetsStuck { get; set; } = true;
    public bool Snowman { get; set; } = true;
    public bool SweepDrifts { get; set; } = false;
    public bool EcoMode { get; set; } = false;
    public bool UiAnimations { get; set; } = true;
    public bool MiniWorld { get; set; } = true;
    public bool FrostEdges { get; set; } = true;
    // Show the intro on each manual launch unless the user disables it.
    public bool StartupAnimation { get; set; } = true;
    public bool IntroPolicyV18 { get; set; } = true;
    public bool IntroPolicyV19 { get; set; } = true;
    public bool PanelTopmost { get; set; } = false;
    public int Density { get; set; } = 120;
    public int Speed { get; set; } = 45;
    public int Opacity { get; set; } = 55;
    public int FlakeSize { get; set; } = 100;
    public int DriftHeight { get; set; } = 28;
    public int DriftOpacity { get; set; } = 90;
    public int PlowSpeed { get; set; } = 60;
    public int PlowInterval { get; set; } = 25;
    public int Wind { get; set; } = 0;
    public string SelectedPreset { get; set; } = "Свой";
    public int? CustomDensity { get; set; }
    public int? CustomSpeed { get; set; }
    public int? CustomOpacity { get; set; }
    public int? CustomFlakeSize { get; set; }
    public int? CustomWind { get; set; }
    public DateTimeOffset? PauseUntilUtc { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsPaused => PauseUntilUtc is { } until && until > DateTimeOffset.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore] public bool EffectiveEnabled => Enabled && !IsPaused;
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnowWindows");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public void EnsureCustom()
    {
        CustomDensity ??= Density; CustomSpeed ??= Speed; CustomOpacity ??= Opacity;
        CustomFlakeSize ??= FlakeSize; CustomWind ??= Wind;
    }
    public void CaptureCustom()
    {
        CustomDensity = Density; CustomSpeed = Speed; CustomOpacity = Opacity;
        CustomFlakeSize = FlakeSize; CustomWind = Wind; SelectedPreset = "Свой";
    }
    public void SelectPreset(string name)
    {
        EnsureCustom();
        switch (name)
        {
            case "Лёгкий": Density = 123; Speed = 70; FlakeSize = 90; Wind = 0; break;
            case "Тихий вечер": Density = 70; Speed = 50; FlakeSize = 100; Wind = 0; break;
            case "Метель": Density = 350; Speed = 160; FlakeSize = 100; Wind = 30; break;
            case "Свой": Density = CustomDensity!.Value; Speed = CustomSpeed!.Value; Opacity = CustomOpacity!.Value; FlakeSize = CustomFlakeSize!.Value; Wind = CustomWind!.Value; break;
            default: throw new ArgumentException("Unknown preset", nameof(name));
        }
        SelectedPreset = name;
    }
    public void ResetTab(int tab)
    {
        var d = new Settings();
        switch (tab)
        {
            case 0:
                FallingSnow = d.FallingSnow; MouseInteraction = d.MouseInteraction; WindowInteraction = d.WindowInteraction;
                Density = d.Density; Speed = d.Speed; FlakeSize = d.FlakeSize; Opacity = d.Opacity; Wind = d.Wind; CaptureCustom(); break;
            case 1:
                Snowdrifts = d.Snowdrifts; SweepDrifts = d.SweepDrifts; Snowman = d.Snowman; DriftHeight = d.DriftHeight; DriftOpacity = d.DriftOpacity; break;
            case 2:
                Snowplow = d.Snowplow; PlowSpray = d.PlowSpray; PlowGetsStuck = d.PlowGetsStuck; PlowSpeed = d.PlowSpeed; PlowInterval = d.PlowInterval; break;
            case 3:
                EcoMode = d.EcoMode; UiAnimations = d.UiAnimations; MiniWorld = d.MiniWorld; FrostEdges = d.FrostEdges; StartupAnimation = d.StartupAnimation; break;
            default: throw new ArgumentOutOfRangeException(nameof(tab));
        }
    }
    public void ResetAll()
    {
        ResetTab(0); ResetTab(1); ResetTab(2); ResetTab(3);
        Enabled = true; PauseUntilUtc = null; PanelTopmost = false;
    }
    public void Normalize()
    {
        Density = Math.Clamp(Density, 0, 350); Speed = Math.Clamp(Speed, 10, 200);
        Opacity = Math.Clamp(Opacity, 10, 100); FlakeSize = Math.Clamp(FlakeSize, 50, 200);
        DriftHeight = Math.Clamp(DriftHeight, 5, 60); DriftOpacity = Math.Clamp(DriftOpacity, 10, 100);
        PlowSpeed = Math.Clamp(PlowSpeed, 20, 160); PlowInterval = Math.Clamp(PlowInterval, 5, 120); Wind = Math.Clamp(Wind, -30, 30);
        EnsureCustom();
        CustomDensity = Math.Clamp(CustomDensity!.Value, 0, 350); CustomSpeed = Math.Clamp(CustomSpeed!.Value, 10, 200);
        CustomOpacity = Math.Clamp(CustomOpacity!.Value, 10, 100); CustomFlakeSize = Math.Clamp(CustomFlakeSize!.Value, 50, 200);
        CustomWind = Math.Clamp(CustomWind!.Value, -30, 30);
        if (SelectedPreset is not ("Свой" or "Лёгкий" or "Метель" or "Тихий вечер")) SelectedPreset = "Свой";
        if (PauseUntilUtc is { } until && until <= DateTimeOffset.UtcNow) PauseUntilUtc = null;
    }
    public static Settings Load()
    {
        try
        {
            string json = File.ReadAllText(FilePath);
            var result = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            // Version 18 made the intro one-shot, leaving the flag off after it
            // ran. Restore it once on upgrade; subsequent user choices persist.
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(nameof(IntroPolicyV19), out _)) result.StartupAnimation = true;
            result.IntroPolicyV18 = true; result.IntroPolicyV19 = true;
            result.Normalize(); return result;
        }
        catch { var result = new Settings(); result.Normalize(); return result; }
    }
    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
