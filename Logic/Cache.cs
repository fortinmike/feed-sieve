using System.Text.Json;

public class Cache
{
    private readonly DirectoryInfo _directory;

    public Cache(DirectoryInfo directory)
    {
        _directory = directory;

        if (!_directory.Exists)
            _directory.Create();
    }

    public string? Get(string key, string hash)
    {
        var valueFile = GetValueFile(key);
        var hashFile = GetHashFile(key);

        if (!File.Exists(valueFile) || !File.Exists(hashFile))
            return null;

        var storedHash = File.ReadAllText(hashFile);
        if (!string.Equals(storedHash, hash, StringComparison.Ordinal))
            return null;

        return File.ReadAllText(valueFile);
    }

    public string? GetLast(string key)
    {
        var valueFile = GetValueFile(key);

        if (!File.Exists(valueFile))
            return null;

        return File.ReadAllText(valueFile);
    }

    public void Set(string key, string hash, string value)
    {
        var subDir = GetSubDirectory(key);
        var valueFile = GetValueFile(key);
        var hashFile = GetHashFile(key);

        if (!subDir.Exists)
            subDir.Create();

        File.WriteAllText(valueFile, value);
        File.WriteAllText(hashFile, hash);
    }

    public DateTimeOffset? GetDoNotUpdateBeforeUtc(string key)
    {
        var stateFile = GetStateFile(key);

        if (!File.Exists(stateFile))
            return null;

        var state = JsonSerializer.Deserialize<CacheState>(File.ReadAllText(stateFile));
        return state?.DoNotUpdateBeforeUtc;
    }

    public void SetDoNotUpdateBeforeUtc(string key, DateTimeOffset doNotUpdateBeforeUtc)
    {
        var subDir = GetSubDirectory(key);
        var stateFile = GetStateFile(key);

        if (!subDir.Exists)
            subDir.Create();

        var json = JsonSerializer.Serialize(new CacheState(doNotUpdateBeforeUtc));
        File.WriteAllText(stateFile, json);
    }

    public void ClearDoNotUpdateBeforeUtc(string key)
    {
        var stateFile = GetStateFile(key);

        if (File.Exists(stateFile))
            File.Delete(stateFile);
    }

    private DirectoryInfo GetSubDirectory(string key)
    {
        var safeName = key.ToSafeFileName();
        return new DirectoryInfo(Path.Combine(_directory.FullName, safeName));
    }

    private string GetValueFile(string key) => Path.Combine(GetSubDirectory(key).FullName, "value.txt");

    private string GetHashFile(string key) => Path.Combine(GetSubDirectory(key).FullName, "hash.txt");

    private string GetStateFile(string key) => Path.Combine(GetSubDirectory(key).FullName, "state.json");

    private sealed record CacheState(DateTimeOffset DoNotUpdateBeforeUtc);
}
