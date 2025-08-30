using System.ServiceModel.Syndication;
using System.Xml;

public class Rss
{
    public static SyndicationFeed Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml));
        return SyndicationFeed.Load(reader);
    }

    public static string Serialize(SyndicationFeed feed)
    {
        using var stringWriter = new StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter);
        feed.SaveAsRss20(xmlWriter);
        return stringWriter.ToString();
    }
}
