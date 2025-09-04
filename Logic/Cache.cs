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
        var (subDir, valueFile, hashFile) = GetPaths(key);

        if (!File.Exists(valueFile) || !File.Exists(hashFile))
            return null;

        var storedHash = File.ReadAllText(hashFile);
        if (!string.Equals(storedHash, hash, StringComparison.Ordinal))
            return null;

        return File.ReadAllText(valueFile);
    }

    public void Set(string key, string hash, string value)
    {
        var (subDir, valueFile, hashFile) = GetPaths(key);
        if (!subDir.Exists)
            subDir.Create();

        File.WriteAllText(valueFile, value);
        File.WriteAllText(hashFile, hash);
    }

    private (DirectoryInfo subDir, string valueFile, string hashFile) GetPaths(string name)
    {
        var safeName = name.ToSafeFileName();
        var subDir = new DirectoryInfo(Path.Combine(_directory.FullName, safeName));
        var valueFile = Path.Combine(subDir.FullName, "value.txt");
        var hashFile = Path.Combine(subDir.FullName, "hash.txt");
        return (subDir, valueFile, hashFile);
    }
}
