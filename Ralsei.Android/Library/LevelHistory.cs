using Android.Content;
using Org.Json;
using Uri = Android.Net.Uri;

namespace Ralsei.Android.Library;

/// <summary>
/// The level that is selected on the home screen, and the levels played before it. Both are kept in the app's
/// shared preferences, along with the read access that was granted to their files.
/// </summary>
public sealed class LevelHistory(Context context)
{
    private const string PreferencesName = "levels";
    private const string KeyCurrent = "current";
    private const string KeyRecents = "recents";
    private const int MaxRecents = 8;

    private readonly Context context = context;
    private readonly ISharedPreferences preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public LevelInfo? Current
    {
        get => ParseLevel(preferences.GetString(KeyCurrent, null));
        set => preferences.Edit()!.PutString(KeyCurrent, value?.ToJson().ToString())!.Apply();
    }

    public IReadOnlyList<LevelInfo> GetRecents()
    {
        var recents = new List<LevelInfo>();

        try
        {
            var array = new JSONArray(preferences.GetString(KeyRecents, "[]"));
            for (var i = 0; i < array.Length(); i++)
            {
                var json = array.OptJSONObject(i);
                if (json is not null && LevelInfo.FromJson(json) is { } level)
                    recents.Add(level);
            }
        }
        catch (JSONException)
        {
            // Unreadable history is the same as no history
        }

        return recents;
    }

    /// <summary>
    /// Puts the level on top of the recents.
    /// </summary>
    public void AddRecent(LevelInfo level)
    {
        var recents = GetRecents().Where(x => !x.IsSameLevel(level)).ToList();
        recents.Insert(0, level);

        var dropped = recents.Skip(MaxRecents).ToList();
        if (recents.Count > MaxRecents)
            recents.RemoveRange(MaxRecents, recents.Count - MaxRecents);

        SaveRecents(recents);
        ReleaseUnused(dropped, recents);
    }

    public void RemoveRecent(LevelInfo level)
    {
        var recents = GetRecents().ToList();
        var removed = recents.Where(x => x.IsSameLevel(level)).ToList();
        recents.RemoveAll(x => x.IsSameLevel(level));

        SaveRecents(recents);
        ReleaseUnused(removed, recents);
    }

    /// <summary>
    /// Keeps read access to the given uri across restarts of the app. Not every provider allows that.
    /// </summary>
    public bool TryKeepAccess(Uri uri)
    {
        try
        {
            context.ContentResolver!.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
            return true;
        }
        catch (Exception)
        {
            // Not a document uri, or the provider does not hand out persistable permissions
            return false;
        }
    }

    private void SaveRecents(List<LevelInfo> recents)
    {
        var array = new JSONArray();
        foreach (var level in recents)
            array.Put(level.ToJson());

        preferences.Edit()!.PutString(KeyRecents, array.ToString())!.Apply();
    }

    // Access to a folder or file is only given back once nothing in the history uses it any more
    private void ReleaseUnused(List<LevelInfo> forgotten, List<LevelInfo> remaining)
    {
        var inUse = remaining.SelectMany(x => x.GrantedUris).ToHashSet();
        if (Current is { } current)
            inUse.UnionWith(current.GrantedUris);

        foreach (var uri in forgotten.SelectMany(x => x.GrantedUris).Distinct())
        {
            if (inUse.Contains(uri))
                continue;

            try
            {
                context.ContentResolver!.ReleasePersistableUriPermission(Uri.Parse(uri)!, ActivityFlags.GrantReadUriPermission);
            }
            catch (Exception)
            {
                // It was never kept, or is gone already
            }
        }
    }

    private static LevelInfo? ParseLevel(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return LevelInfo.FromJson(new JSONObject(json));
        }
        catch (JSONException)
        {
            return null;
        }
    }
}
