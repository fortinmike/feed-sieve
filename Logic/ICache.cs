public interface ICache
{
    string? Get(string key, string hash);
    string? GetLast(string key);
    void Set(string key, string hash, string value);
}
