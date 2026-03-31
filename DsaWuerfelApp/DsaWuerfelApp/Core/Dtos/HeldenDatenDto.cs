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