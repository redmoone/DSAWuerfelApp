using System.Xml.Serialization;

namespace DsaWuerfelApp.Core.Dtos;

public class TalentDto
{
    [XmlElement("name")] public string Name { get; set; } = string.Empty;

    [XmlElement("wert")] public int Wert { get; set; }

    [XmlElement("probe")] public string Probe { get; set; } = string.Empty;

    [XmlElement("spezialisierungen")] public string Specializations { get; set; } = string.Empty;

    [XmlElement("bereich")] public List<string> Bereiche { get; set; } = [];
}
