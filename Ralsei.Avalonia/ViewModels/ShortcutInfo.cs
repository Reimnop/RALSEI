using System;

namespace Ralsei.Avalonia.ViewModels;

/// <summary>
/// A keyboard shortcut, as it is listed in the settings.
/// </summary>
/// <param name="Keys">The keys, with " / " between the alternatives.</param>
public sealed record ShortcutInfo(string Keys, string Description)
{
    public string[] KeyList => Keys.Split(" / ", StringSplitOptions.RemoveEmptyEntries);
}

public sealed record ShortcutGroup(string Title, ShortcutInfo[] Shortcuts)
{
    public static readonly ShortcutGroup[] All =
    [
        new("Playback",
        [
            new("Space / K", "Play or pause"),
            new("Left / Right", "Back or forward 5 seconds"),
            new("J / L", "Back or forward 10 seconds"),
            new("Home", "Start over"),
            new("[ / ]", "Slower or faster"),
            new("Ctrl+L", "Loop the song"),
            new("R", "Draw the randomness of the level again")
        ]),
        new("Sound",
        [
            new("Up / Down", "Louder or quieter"),
            new("M", "Mute")
        ]),
        new("View",
        [
            new("F / F11", "Full screen"),
            new("Escape", "Leave full screen, or close what is open"),
            new("I", "Performance stats"),
            new("P", "Post-processing on or off"),
            new("Double click", "Full screen")
        ]),
        new("App",
        [
            new("Ctrl+O", "Open a folder"),
            new("Ctrl+Shift+O", "Open files"),
            new("Ctrl+E", "Export a video"),
            new("Ctrl+,", "Settings"),
            new("F1", "This list")
        ])
    ];
}
