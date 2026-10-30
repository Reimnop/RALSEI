namespace Ralsei.Platform.Window;

/// <summary>The system clipboard, for text.</summary>
public interface IClipboard
{
    string? GetText();

    void SetText(string text);
}

/// <summary>A clipboard that lives in the process only, for hosts without a system one.</summary>
public sealed class MemoryClipboard : IClipboard
{
    private string? text;

    public string? GetText() => text;

    public void SetText(string text) => this.text = text;
}
