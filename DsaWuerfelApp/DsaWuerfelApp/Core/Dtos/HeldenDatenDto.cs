using System.Xml.Serialization;

namespace DsaWuerfelApp.Core.Dtos;

[XmlRoot("daten")]
public class HeldenDatenDto
{
    [XmlElement("angaben")] public AngabenDto Angaben { get; set; } = new();

    [XmlElement("eigenschaften")] public EigenschaftenDto Eigenschaften { get; set; } = new();

    [XmlArray("talentliste")]
    [XmlArrayItem("talent")]
    public List<TalentDto> Talentliste { get; set; } = new();

    [XmlIgnore] public List<SchlechteEigenschaftDto> SchlechteEigenschaften { get; set; } = new();
}

public class AngabenDto
{
    [XmlElement("name")] public string Name { get; set; } = string.Empty;

    [XmlElement("geschlecht")] public string Geschlecht { get; set; } = string.Empty;

    [XmlElement("alter")] public int Alter { get; set; }
}

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

public class EigenschaftWertDto
{
    [XmlElement("akt")] public int Akt { get; set; }
}

public class TalentDto
{
    [XmlElement("name")] public string Name { get; set; } = string.Empty;

    [XmlElement("wert")] public int Wert { get; set; }

    [XmlElement("probe")] public string Probe { get; set; } = string.Empty;
}

public class SchlechteEigenschaftDto
{
    public string Bezeichner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Wert { get; set; }
}