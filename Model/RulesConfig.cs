using YamlDotNet.Serialization;

public class RulesConfig
{
    [YamlMember(Order = 1)]
    public List<FilterRule> GlobalFilters { get; set; } = [];

    [YamlMember(Order = 2)]
    public List<FeedRule> Feeds { get; set; } = [];
}
