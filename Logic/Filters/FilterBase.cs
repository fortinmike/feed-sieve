using System.Xml.Linq;

public class FilterBase
{
    protected string GetValue(XElement parent, string localName, string? ns = null) =>
        (string?)parent.Element(ns is null ? localName : XName.Get(localName, ns)) ?? "";

    protected string GetTitle(XElement item)
    {
        var title = GetValue(item, "title");
        if (!string.IsNullOrWhiteSpace(title))
            return title;

        return GetValue(item, "title", Constants.AtomNamespace);
    }

    protected string GetContent(XElement item)
    {
        // RSS 2.0 content:encoded
        var content = GetValue(item, "encoded", "http://purl.org/rss/1.0/modules/content/");
        if (!string.IsNullOrWhiteSpace(content))
            return content;

        // RSS 2.0 description
        content = GetValue(item, "description");
        if (!string.IsNullOrWhiteSpace(content))
            return content;

        // Atom content
        content = GetValue(item, "content", Constants.AtomNamespace);
        if (!string.IsNullOrWhiteSpace(content))
            return content;

        // Atom summary
        return GetValue(item, "summary", Constants.AtomNamespace);
    }
}
