using YamlDotNet.Serialization;

public class Rule
{
  [YamlMember(Alias = "rule")]
  public required string Name { get; set; }

  public required string Match { get; set; }

  public required ExcludeCriteria Exclude { get; set; }
}

public class ExcludeCriteria
{
  public bool MatchTitle { get; set; } = true;
  public bool MatchContent { get; set; } = true;
  public required List<string> Regexes { get; set; }
}
