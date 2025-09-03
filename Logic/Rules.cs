using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class Rules
{
    public static List<Rule> Load(string path)
    {
        return Rules.Parse(File.ReadAllText(path));
    }

    public static List<Rule> Parse(string str)
    {
        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        return deserializer.Deserialize<List<Rule>>(str);
    }
}
