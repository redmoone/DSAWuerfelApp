using System.Xml.Serialization;

namespace DsaWuerfelApp.Core.Dtos;

public class EigenschaftWertDto
{
    [XmlElement("akt")] public int Akt { get; set; }
}