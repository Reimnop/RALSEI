using System.Globalization;
using Ralsei.Platform.Input;
using RalseiMouseButton = Ralsei.Platform.Input.MouseButton;

namespace Ralsei.Desktop;

/// <summary>Input from a file (<c>RALSEI_INPUT_SCRIPT</c>) for looking at an interface without a person at the window. Each line is a frame and what happens then: <c>move 400 300</c>, <c>down left</c>, <c>up left</c>, <c>click left</c>, <c>scroll 0 -1</c>, <c>key LeftControl+Z</c>, <c>hold LeftShift</c>, <c>release LeftShift</c>, <c>text abc</c>. Lines starting with # are skipped.</summary>
public sealed class ScriptedInput
{
    private readonly List<(int Frame, string[] Words)> steps = [];
    private int next;

    public ScriptedInput(string path)
    {
        foreach (var line in File.ReadAllLines(path))
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length < 2 || words[0].StartsWith('#') || !int.TryParse(words[0], out var frame))
                continue;

            steps.Add((frame, words[1..]));
        }

        steps.Sort((a, b) => a.Frame.CompareTo(b.Frame));
    }

    /// <summary>Does what the file says for this frame.</summary>
    public void Play(int frame, Action<Keycode, bool> key, Action<float, float> move, Action<RalseiMouseButton, bool> button, Action<float, float> scroll, Action<string> text)
    {
        while (next < steps.Count && steps[next].Frame <= frame)
        {
            var words = steps[next++].Words;
            Console.WriteLine($"Script, frame {frame}: {string.Join(' ', words)}");
            var args = words[1..];
            switch (words[0])
            {
                case "move":
                    move(Number(args[0]), Number(args[1]));
                    break;
                case "down":
                    button(Button(args[0]), true);
                    break;
                case "up":
                    button(Button(args[0]), false);
                    break;
                case "click":
                    button(Button(args[0]), true);
                    button(Button(args[0]), false);
                    break;
                case "scroll":
                    scroll(Number(args[0]), Number(args[1]));
                    break;
                case "hold":
                    key(Key(args[0]), true);
                    break;
                case "release":
                    key(Key(args[0]), false);
                    break;
                case "key":
                {
                    var keys = args[0].Split('+').Select(Key).ToArray();
                    foreach (var pressed in keys)
                        key(pressed, true);
                    foreach (var released in keys.Reverse())
                        key(released, false);

                    break;
                }
                case "text":
                    text(string.Join(' ', args));
                    break;
            }
        }
    }

    private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    private static Keycode Key(string name) => Enum.Parse<Keycode>(name, true);

    private static RalseiMouseButton Button(string name) => Enum.Parse<RalseiMouseButton>(name, true);
}
