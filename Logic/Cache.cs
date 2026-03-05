public class Cache : ICache
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

    private DirectoryInfo GetSubDirectory(string key)
    {
        var safeName = key.ToSafeFileName();
        return new DirectoryInfo(Path.Combine(_directory.FullName, safeName));
    }

    private string GetValueFile(string key) => Path.Combine(GetSubDirectory(key).FullName, "value.txt");

    private string GetHashFile(string key) => Path.Combine(GetSubDirectory(key).FullName, "hash.txt");
}
