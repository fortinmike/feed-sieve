using YamlDotNet.Serialization;

public class Rule
{
    [YamlMember(Alias = "rule", Order = 1)]
    public required string Name { get; set; }

    [YamlMember(Order = 2)]
    public required string? Feed { get; set; } = null;

    [YamlMember(Order = 3)]
    public required string Match { get; set; }

    [YamlMember(Order = 4)]
    public required string Regex { get; set; }

    [YamlMember(Order = 5)]
    public bool CaseSensitive { get; set; } = false;

    [YamlMember(Order = 6)]
    public string? Sample { get; set; }
}
