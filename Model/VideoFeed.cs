using System.Xml.Serialization;

namespace AstroGoblinVideoBot.Model;

[XmlRoot(ElementName = "feed", Namespace = "http://www.w3.org/2005/Atom")]
public readonly struct VideoFeed
{
    [XmlElement(ElementName = "link")]
    public Link[] Links { get; init; }
    
    [XmlElement(ElementName = "title")]
    public string Title { get; init; }
    
    [XmlElement(ElementName = "updated")]
    public DateTimeOffset Updated { get; init; }
    
    [XmlElement(ElementName = "entry")]
    public Entry Entry { get; init; }
}

public readonly struct Entry
{
    [XmlElement(ElementName = "id")]
    public string Id { get; init; }

    [XmlElement(ElementName = "videoId", Namespace = "http://www.youtube.com/xml/schemas/2015")]
    public string VideoId { get; init; }

    [XmlElement(ElementName = "channelId", Namespace = "http://www.youtube.com/xml/schemas/2015")]
    public string ChannelId { get; init; }

    [XmlElement(ElementName = "title")]
    public string Title { get; init; }

    [XmlElement(ElementName = "link")]
    public Link Link { get; init; }

    [XmlElement(ElementName = "author")]
    public Author Author { get; init; }

    [XmlElement(ElementName = "published")]
    public DateTimeOffset Published { get; init; }

    [XmlElement(ElementName = "updated")]
    public DateTimeOffset Updated { get; init; }
}

public readonly struct Link
{
    [XmlAttribute(AttributeName = "rel")]
    public string Rel { get; init; }

    [XmlAttribute(AttributeName = "href")]
    public string Href { get; init; }
}

public readonly struct Author
{
    [XmlElement(ElementName = "name")]
    public string Name { get; init; }

    [XmlElement(ElementName = "uri")]
    public string Uri { get; init; }
}