using System.Globalization;

namespace Ralsei.Desktop;

/// <summary>The command line: <c>--name value</c> (the last one given counts) and bare switches (<c>--name</c>). Unknown ones are never asked for.</summary>
public sealed class Arguments(IEnumerable<string> args)
{
    private readonly string[] args = args.ToArray();

    /// <summary>Whether the option was given at all, with or without a value.</summary>
    public bool Has(string name) => IndexesOf(name).Any();

    /// <summary>The value of the last time the option was given, or null if it was not or had none.</summary>
    public string? Value(string name)
    {
        string? value = null;
        foreach (var index in IndexesOf(name))
        {
            if (index + 1 < args.Length && !IsOption(args[index + 1]))
                value = args[index + 1];
        }

        return value;
    }

    private IEnumerable<int> IndexesOf(string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--" + name)
                yield return i;
        }
    }

    private static bool IsOption(string text)
        => text.Length > 1 && text[0] == '-' && !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
