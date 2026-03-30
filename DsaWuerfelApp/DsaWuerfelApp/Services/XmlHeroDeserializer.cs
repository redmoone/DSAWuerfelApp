using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

using DsaWuerfelApp.Core.Dtos;

namespace DsaWuerfelApp.Services;

public class XmlHeroDeserializer
{
    public HeldenDatenDto Deserialize(Stream xmlStream)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 * 2
        };

        using var reader = XmlReader.Create(xmlStream, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var serializer = new XmlSerializer(typeof(HeldenDatenDto));

        if (serializer.Deserialize(document.CreateReader()) is not HeldenDatenDto dto)
        {
            throw new InvalidOperationException();
        }

        dto.SchlechteEigenschaften = ExtractSchlechteEigenschaften(document);
        return dto;
    }

    private static List<SchlechteEigenschaftDto> ExtractSchlechteEigenschaften(XDocument document)
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