using YamlDotNet.Serialization;

public class SummaryRule
{
    [YamlMember(Order = 1)]
    public string? Prompt { get; set; }
}
