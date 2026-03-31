using System.Xml.Serialization;

namespace DsaWuerfelApp.Core.Dtos;

public class EigenschaftenDto
{
    [XmlElement("mut")] public EigenschaftWertDto Mut { get; set; } = new();

    [XmlElement("klugheit")] public EigenschaftWertDto Klugheit { get; set; } = new();

    [XmlElement("intuition")] public EigenschaftWertDto Intuition { get; set; } = new();

    [XmlElement("charisma")] public EigenschaftWertDto Charisma { get; set; } = new();

    [XmlElement("fingerfertigkeit")] public EigenschaftWertDto Fingerfertigkeit { get; set; } = new();

    [XmlElement("gewandtheit")] public EigenschaftWertDto Gewandtheit { get; set; } = new();

    [XmlElement("konstitution")] public EigenschaftWertDto Konstitution { get; set; } = new();

    [XmlElement("koerperkraft")] public EigenschaftWertDto Koerperkraft { get; set; } = new();
}