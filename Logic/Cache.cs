public class Cache
{
    private readonly DirectoryInfo _directory;

    public Cache(DirectoryInfo directory)
    {
        _directory = directory;
    }

    public string Get(string name, string hash)
    {
        return null; // TODO: Implement!
    }

    public void Set(string name, string hash, string value)
    {
        // TODO: Implement!
    }
}
