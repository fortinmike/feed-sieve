using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class Rules
{
    public static List<Rule> Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    public static List<Rule> Parse(string str)
    {
        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        return deserializer.Deserialize<List<Rule>>(str);
    }

    public static void Save(string path, List<Rule> rules)
    {
        File.WriteAllText(path, Serialize(rules));
    }

    public static string Serialize(List<Rule> rules)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
        var yaml = serializer.Serialize(rules);

        // Keep one blank line between top-level rules for readability
        return Regex.Replace(yaml, @"(\r?\n)(?=- rule:)", "$1$1");
    }
}
