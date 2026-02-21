public class BasicAuthOptions
{
    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public List<string> ProtectedPathPrefixes { get; set; } = [];
}
