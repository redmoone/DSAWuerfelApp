using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

using DsaWuerfelApp.Core.Dtos;

namespace DsaWuerfelApp.Services;

public class XmlHeroDeserializer
{
    public IReadOnlyList<(HeldenDatenDto Dto, byte[] RawXml)> DeserializeMultiple(Stream xmlStream)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);

        var document = LoadDocument(xmlStream);
        var serializer = new XmlSerializer(typeof(HeldenDatenDto));

        var result = new List<(HeldenDatenDto, byte[])>();

        foreach (var datenNode in document.Descendants("daten"))
        {
            using var nodeReader = datenNode.CreateReader();
            if (serializer.Deserialize(nodeReader) is HeldenDatenDto dto)
            {
                dto.SchlechteEigenschaften = ExtractSchlechteEigenschaften(datenNode);
                var rawXml = Encoding.UTF8.GetBytes(datenNode.ToString());
                result.Add((dto, rawXml));
            }
        }

        return result;
    }

    public IReadOnlyList<CombatXmlDatenDto> DeserializeCombat(Stream xmlStream)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);

        var document = LoadDocument(xmlStream);
        var serializer = new XmlSerializer(typeof(CombatXmlDatenDto));
        var result = new List<CombatXmlDatenDto>();

        foreach (var datenNode in document.Descendants("daten"))
        {
            using var nodeReader = datenNode.CreateReader();
            if (serializer.Deserialize(nodeReader) is CombatXmlDatenDto dto)
            {
                result.Add(dto);
            }
        }

        return result;
    }

    private static XDocument LoadDocument(Stream xmlStream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024 * 10
        };

        using var reader = XmlReader.Create(xmlStream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static List<SchlechteEigenschaftDto> ExtractSchlechteEigenschaften(XElement document)
    {
        return document.Descendants("vorteil")
            .Where(IsSchlechteEigenschaft)
            .Select(vorteil => new SchlechteEigenschaftDto
            {
                Bezeichner = GetElementValue(vorteil, "bezeichner"),
                Name = GetElementValue(vorteil, "name"),
                Wert = ParseInt(GetElementValue(vorteil, "wert"))
            })
            .Where(vorteil => !string.IsNullOrWhiteSpace(vorteil.Bezeichner) ||
                              !string.IsNullOrWhiteSpace(vorteil.Name))
            .ToList();
    }

    private static bool IsSchlechteEigenschaft(XElement vorteil)
    {
        return bool.TryParse(GetElementValue(vorteil, "istschlechteeigenschaft"), out var isSchlechteEigenschaft) &&
               isSchlechteEigenschaft;
    }

    private static string GetElementValue(XElement element, string name)
    {
        return element.Element(name)?.Value?.Trim() ?? string.Empty;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out var parsedValue) ? parsedValue : 0;
    }
}
