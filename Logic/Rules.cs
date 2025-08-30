using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class Rules
{
    public static List<Rule> Load(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        return deserializer.Deserialize<List<Rule>>(yaml);
    }
}
