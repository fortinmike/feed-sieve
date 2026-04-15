using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class Rules
{
    public static RulesConfig Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    public static RulesConfig Parse(string str)
    {
        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        return deserializer.Deserialize<RulesConfig>(str) ?? new RulesConfig();
    }

    public static void Save(string path, RulesConfig rules)
    {
        File.WriteAllText(path, Serialize(rules));
    }

    public static string Serialize(RulesConfig rules)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
        return serializer.Serialize(rules);
    }
}
