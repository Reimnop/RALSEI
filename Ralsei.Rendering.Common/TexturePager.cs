using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;

namespace Ralsei.Rendering.Common;

/// <summary>A run of draw commands that read from the same few textures, which a backend can have bound all at once.</summary>
/// <param name="FirstCommand">The index of the run's first draw command in the list that was planned.</param>
/// <param name="CommandCount">How many commands are in the run.</param>
/// <param name="FirstTexture">Where the page's textures start in <see cref="TexturePager.Textures"/>.</param>
/// <param name="TextureCount">How many textures the page has: at most the page size.</param>
public readonly record struct TexturePage(int FirstCommand, int CommandCount, int FirstTexture, int TextureCount);

/// <summary>
/// For the backends that cannot have every texture bound at once (no bindless textures): divides the draw commands of a pass into pages of
/// at most N textures, N being how many the hardware can have bound together. Going through the commands in order, a command whose
/// texture the current page does not have, and has no room for, starts the next page; a draw batch (instances of a mesh, a multi-draw)
/// must not go past the end of its page. Each command gets the slot its texture has in its page.
/// </summary>
public sealed class TexturePager
{
    private readonly List<TexturePage> pages = [];
    private readonly List<int> textures = [];
    private int[] slots = new int[1024];

    /// <summary>The pages, in the order of the commands.</summary>
    public IReadOnlyList<TexturePage> Pages => pages;

    /// <summary>The textures (the ids of the handles) of every page, one page after the other.</summary>
    public ReadOnlySpan<int> Textures => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(textures);

    /// <summary>The textures of one page: the texture in slot i of the page is at index i.</summary>
    public ReadOnlySpan<int> TexturesOf(in TexturePage page) => Textures.Slice(page.FirstTexture, page.TextureCount);

    /// <summary>The slot in its page that the texture of a command is in, or -1 if the command has no texture.</summary>
    public int SlotOf(int commandIndex) => slots[commandIndex];

    /// <summary>Divides the commands into pages.</summary>
    /// <param name="pageSize">How many textures can be bound at the same time, at least 1.</param>
    public void Plan(in DrawData drawData, ReadOnlySpan<DrawCommandWithDepth> commands, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        pages.Clear();
        textures.Clear();
        if (slots.Length < commands.Length)
            slots = new int[Math.Max(commands.Length, slots.Length * 2)];

        var pageFirstCommand = 0;
        var pageFirstTexture = 0;
        for (var i = 0; i < commands.Length; i++)
        {
            var texture = TextureOf(drawData, commands[i]);
            if (texture.IsNone)
            {
                slots[i] = -1;
                continue;
            }

            var slot = IndexOfTexture(pageFirstTexture, texture.Id);
            if (slot < 0 && textures.Count - pageFirstTexture >= pageSize)
            {
                // No room for this texture, so this command is where the next page starts
                pages.Add(new TexturePage(pageFirstCommand, i - pageFirstCommand, pageFirstTexture, textures.Count - pageFirstTexture));
                pageFirstCommand = i;
                pageFirstTexture = textures.Count;
            }

            if (slot < 0)
            {
                slot = textures.Count - pageFirstTexture;
                textures.Add(texture.Id);
            }

            slots[i] = slot;
        }

        if (commands.Length > 0)
            pages.Add(new TexturePage(pageFirstCommand, commands.Length - pageFirstCommand, pageFirstTexture, textures.Count - pageFirstTexture));
    }

    /// <summary>The texture a draw command samples, or <see cref="TextureHandle.None"/>.</summary>
    public static TextureHandle TextureOf(in DrawData drawData, in DrawCommandWithDepth command)
    {
        if (command.DrawType != DrawType.Mesh)
            return TextureHandle.None;

        ref var meshDrawItem = ref drawData.MeshDrawItems[command.DrawId];
        return meshDrawItem.RenderMode == RenderMode.Normal ? meshDrawItem.Texture : TextureHandle.None;
    }

    private int IndexOfTexture(int pageFirstTexture, int textureId)
    {
        for (var i = pageFirstTexture; i < textures.Count; i++)
        {
            if (textures[i] == textureId)
                return i - pageFirstTexture;
        }

        return -1;
    }
}
