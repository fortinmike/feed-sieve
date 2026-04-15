public class SummaryCache
{
    private readonly DirectoryInfo _directory;

    public SummaryCache(DirectoryInfo directory)
    {
        _directory = directory;

        if (!_directory.Exists)
            _directory.Create();
    }

    public string? Get(string feedUrl, string itemKey, string hash)
    {
        var valueFile = GetValueFile(feedUrl, itemKey);
        var hashFile = GetHashFile(feedUrl, itemKey);
        if (!File.Exists(valueFile) || !File.Exists(hashFile))
            return null;

        var storedHash = File.ReadAllText(hashFile);
        if (!string.Equals(storedHash, hash, StringComparison.Ordinal))
            return null;

        return File.ReadAllText(valueFile);
    }

    public void Set(string feedUrl, string itemKey, string hash, string summary)
    {
        var itemDirectory = GetItemDirectory(feedUrl, itemKey);
        if (!itemDirectory.Exists)
            itemDirectory.Create();

        File.WriteAllText(GetValueFile(feedUrl, itemKey), summary);
        File.WriteAllText(GetHashFile(feedUrl, itemKey), hash);
    }

    private DirectoryInfo GetItemDirectory(string feedUrl, string itemKey)
    {
        return new DirectoryInfo(Path.Combine(_directory.FullName, feedUrl.Hash(), itemKey.Hash()));
    }

    private string GetValueFile(string feedUrl, string itemKey)
    {
        return Path.Combine(GetItemDirectory(feedUrl, itemKey).FullName, "value.txt");
    }

    private string GetHashFile(string feedUrl, string itemKey)
    {
        return Path.Combine(GetItemDirectory(feedUrl, itemKey).FullName, "hash.txt");
    }
}
