using BeatmapTheme = Pamx.Themes.BeatmapTheme;
using System.Drawing;
using System.Text.Json;
using Pamx;
using Pamx.Keyframes;
using Pamx.Objects;
using Pamx.Serialization;
using Pamx.Themes;

namespace Swag;

public class VgdMigration(ResourceLoader loader)
{
    public void MigrateBeatmap(Beatmap beatmap)
    {
        var themes = LoadThemes();
        
        beatmap.Themes.AddRange(themes);
        
        foreach (var theme in beatmap.Themes)
            FixTheme(theme);
        
        foreach (var prefabObject in beatmap.PrefabObjects)
        {
            if (prefabObject.Scale == System.Numerics.Vector2.Zero)
                prefabObject.Scale = System.Numerics.Vector2.One;
        }

        foreach (var o in beatmap.Objects.Concat(beatmap.Prefabs.SelectMany(x => x.Objects)))
        {
            if (o.Type == ObjectType.LegacyHelper)
            {
                for (var i = 0; i < o.ColorEvents.Count; i++)
                {
                    var oldColorKeyframe = o.ColorEvents[i];
                    oldColorKeyframe.Value = new ObjectColorValue
                    {
                        Index = oldColorKeyframe.Value.Index,
                        EndIndex = oldColorKeyframe.Value.EndIndex,
                        Opacity = 0.35f
                    };
                    o.ColorEvents[i] = oldColorKeyframe;
                }
            }
        }
        
        var events = beatmap.Events;
        
        for (var i = 0; i < events.Theme.Count; i++)
        {
            var themeKeyframe = events.Theme[i];
            events.Theme[i] = new FixedKeyframe<string>
            {
                Time = themeKeyframe.Time,
                Value = themeKeyframe.Value,
                Ease = themeKeyframe.Ease,
            };
        }
        
        for (var i = 0; i < events.Chromatic.Count; i++)
        {
            var chromaticAberrationKeyframe = events.Chromatic[i];
            chromaticAberrationKeyframe.Value /= 80.0f;
            events.Chromatic[i] = chromaticAberrationKeyframe;
        }
    }

    private List<BeatmapTheme> LoadThemes()
    {
        var themeNames = new[]
        {
            "Anarchy.vgt",
            "BlackWhite.vgt",
            "Classic.vgt",
            "Dark.vgt",
            "DayNight.vgt",
            "DesertHeat.vgt",
            "Donuts.vgt",
            "EmberStones.vgt",
            "FireArmour.vgt",
            "HotPanda.vgt",
            "JungleWaterway.vgt",
            "Lure.vgt",
            "Machine.vgt",
            "New.vgt",
            "Poison.vgt",
            "Shiver.vgt",
            "Starlight.vgt",
            "StoneField.vgt",
            "ViciousGoop.vgt",
            "WhiteBlack.vgt",
            "Wonderland.vgt"
        };
        
        var themes = new List<BeatmapTheme>();
        
        foreach (var themeName in themeNames)
        {
            var themeData = loader.ReadResourceString($"Themes/{themeName}");
            if (themeData is null)
                throw new InvalidOperationException($"Could not load theme data for '{themeName}'");

            var theme = JsonSerializer.Deserialize<BeatmapTheme>(themeData, PamxSerialization.Options)!;
            themes.Add(theme);
        }
        
        return themes;
    }
    
    private static void FixTheme(ExternalTheme theme)
    {
        // Make sure theme has correct amount of colors
        for (var i = theme.Players.Count; i < 4; i++)
        {
            theme.Players.Add(Color.Black);
        }
        
        for (var i = theme.Objects.Count; i < 9; i++)
        {
            theme.Objects.Add(Color.Black);
        }
        
        for (var i = theme.ParallaxObjects.Count; i < 9; i++)
        {
            theme.ParallaxObjects.Add(Color.Black);
        }
        
        for (var i = theme.Effects.Count; i < 9; i++)
        {
            theme.Effects.Add(Color.Black);
        }
    }
}