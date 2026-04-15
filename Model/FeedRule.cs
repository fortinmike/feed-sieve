using YamlDotNet.Serialization;

public class FeedRule
{
    [YamlMember(Order = 1)]
    public string Name { get; set; } = "";

    [YamlMember(Order = 2)]
    public string Feed { get; set; } = "";

    [YamlMember(Order = 3)]
    public List<FilterRule> Filters { get; set; } = [];

    [YamlMember(Order = 4)]
    public SummaryRule? Summary { get; set; }
}
