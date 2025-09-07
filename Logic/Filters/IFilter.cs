using System.Xml.Linq;

public interface IFilter
{
    public bool Keep(XElement item, Rule rule);
}
