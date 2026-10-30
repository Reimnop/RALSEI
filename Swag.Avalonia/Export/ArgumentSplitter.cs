using System.Collections.Generic;
using System.Text;

namespace Swag.Avalonia.Export;

/// <summary>Splits a line of arguments the way a shell would, for the arguments that go to FFmpeg.</summary>
public static class ArgumentSplitter
{
    public static List<string> Split(string? line)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(line))
            return arguments;

        var current = new StringBuilder();
        var inArgument = false;
        var quote = '\0';

        foreach (var c in line)
        {
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                else
                    current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inArgument = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inArgument)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    inArgument = false;
                }
            }
            else
            {
                current.Append(c);
                inArgument = true;
            }
        }

        if (inArgument)
            arguments.Add(current.ToString());

        return arguments;
    }
}
