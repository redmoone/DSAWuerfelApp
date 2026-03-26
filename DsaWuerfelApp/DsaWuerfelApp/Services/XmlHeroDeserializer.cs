using System.Xml;
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
        var serializer = new XmlSerializer(typeof(HeldenDatenDto));

        return serializer.Deserialize(reader) is not HeldenDatenDto dto
            ? throw new InvalidOperationException()
            : dto;
    }
}