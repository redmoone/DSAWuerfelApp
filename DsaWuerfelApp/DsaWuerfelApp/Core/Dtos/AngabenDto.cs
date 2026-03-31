using System.Xml.Serialization;

namespace DsaWuerfelApp.Core.Dtos;

public class AngabenDto
{
    [XmlElement("name")] public string Name { get; set; } = string.Empty;

    [XmlElement("geschlecht")] public string Geschlecht { get; set; } = string.Empty;

    [XmlElement("alter")] public int Alter { get; set; }
}