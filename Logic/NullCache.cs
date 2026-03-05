public class NullCache : ICache
{
    public string? Get(string key, string hash) => null;

    public string? GetLast(string key) => null;

    public void Set(string key, string hash, string value)
    {
    }
}
