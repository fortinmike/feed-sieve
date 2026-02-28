using YamlDotNet.Serialization;

public class Rule
{
    [YamlMember(Alias = "rule")]
    public required string Name { get; set; }

    public required string? Feed { get; set; } = null;

    public required string Match { get; set; }

    public required string Regex { get; set; }

    public string? Sample { get; set; }
}
