using ProfileDeck.Core.Models;

namespace ProfileDeck.Core.Persistence;

public sealed class ProfileRepository<T> where T : class
{
    private readonly string _dir;
    private readonly Func<T, string> _getName;

    public ProfileRepository(string dir, Func<T, string> getName)
    {
        _dir = dir;
        _getName = getName;
        Directory.CreateDirectory(_dir);
    }

    public IReadOnlyList<T> GetAll()
    {
        var list = new List<T>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json").OrderBy(f => f))
        {
            var p = JsonStore.Load<T>(file);
            if (p != null) list.Add(p);
        }
        return list;
    }

    public T? GetByName(string name)
        => JsonStore.Load<T>(PathFor(name));

    public void Save(T profile)
        => JsonStore.Save(PathFor(_getName(profile)), profile);

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    public bool Rename(string oldName, T updatedProfile)
    {
        var newName = _getName(updatedProfile);
        Save(updatedProfile);
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            Delete(oldName);
        return true;
    }

    private string PathFor(string name)
        => Path.Combine(_dir, Sanitize(name) + ".json");

    private static string Sanitize(string name)
        => string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
}
