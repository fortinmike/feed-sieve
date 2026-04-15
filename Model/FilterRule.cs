using YamlDotNet.Serialization;

public class FilterRule
{
    [YamlMember(Alias = "rule", Order = 1)]
    public string Name { get; set; } = "";

    [YamlMember(Order = 2)]
    public string Match { get; set; } = "title";

    [YamlMember(Order = 3)]
    public string Regex { get; set; } = "";

    [YamlMember(Order = 4)]
    public bool CaseSensitive { get; set; }

    [YamlMember(Order = 5)]
    public string? Sample { get; set; }
}
