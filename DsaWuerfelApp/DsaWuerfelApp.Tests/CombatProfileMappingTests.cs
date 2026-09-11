using System.Text;

using DsaWuerfelApp.Core.Mappers;
using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Tests;

public sealed class CombatProfileMappingTests
{
    [Fact]
    public void Maps_darian_values_and_keeps_zero_separate_from_missing()
    {
        var profile = Map(DarianXml, "Darian Falkenstein", new Dictionary<string, TalentData>
        {
            ["Kriegskunst"] = new TalentData { Wert = 7 }
        });

        Assert.Equal(22, profile.Resources.LeP);
        Assert.Equal(28, profile.Resources.AuP);
        Assert.Equal(8, profile.Attributes.Single(attribute => attribute.Key == "KO").Value);
        Assert.Equal(4, profile.WoundThreshold);
        Assert.Equal(3, profile.Sets.Select(set => set.Number).Distinct().Count());

        var zoneSet = Assert.Single(profile.Sets, set => set.Number == 1 && set.ArmorModel == CombatArmorModel.Zone);
        Assert.Equal(19, zoneSet.Weapons.Single().Attack);
        Assert.Equal(14, zoneSet.Weapons.Single().Parry);
        Assert.Equal("1W+4", zoneSet.Weapons.Single().BaseDamage);
        Assert.Equal("1W+3", zoneSet.Weapons.Single().CalculatedDamage);
        Assert.Equal(13, zoneSet.Dodge);
        Assert.NotNull(zoneSet.ArmorZones);
        Assert.Equal(0, zoneSet.ArmorZones!.Head);
        Assert.Equal(0, zoneSet.ArmorZones.Chest);
        Assert.Equal(0, zoneSet.ArmorZones.Back);
        Assert.Equal(0, zoneSet.ArmorZones.LeftArm);
        Assert.Contains(profile.Specializations, specialization => specialization.Name == "Aufmerksamkeit" && specialization.IsLearned);
        Assert.Contains(profile.Specializations, specialization => specialization.Name == "Ausweichen II" && specialization.IsLearned);
        Assert.Contains(profile.Specializations, specialization => specialization.Name == "Binden" && !specialization.IsLearned);
        Assert.True(profile.HasAttention);
        Assert.False(profile.HasKlingentaenzer);
        Assert.Equal(7, profile.KriegskunstValue);
    }

    [Fact]
    public void Keeps_ardors_duplicate_weapons_shield_type_and_raw_armor_values()
    {
        var profile = Map(ArdorXml, "Ardor Collen");
        var zoneSet = Assert.Single(profile.Sets, set => set.Number == 1 && set.ArmorModel == CombatArmorModel.Zone);
        var swords = zoneSet.Weapons.Where(weapon => weapon.Category == CombatWeaponCategory.Melee).ToArray();

        Assert.Equal(2, swords.Length);
        Assert.Equal([1, 2], swords.Select(sword => sword.Number).OrderBy(number => number).ToArray());
        Assert.NotEqual(swords[0].Id, swords[1].Id);
        Assert.All(swords, sword => Assert.Equal(21, sword.Attack));
        Assert.Equal(5, zoneSet.ArmorZones!.Head);
        Assert.Equal(3, zoneSet.ArmorZones.LeftLeg);
        Assert.Equal("*", zoneSet.ArmorPieces.Single(piece => piece.Number == 1).RawRs);
        Assert.Equal("*", zoneSet.ArmorPieces.Single(piece => piece.Number == 1).RawBe);

        var setTwo = Assert.Single(profile.Sets, set => set.Number == 2 && set.ArmorModel == CombatArmorModel.Zone);
        var buckler = Assert.Single(setTwo.Weapons, weapon => weapon.Category == CombatWeaponCategory.Shield);
        Assert.Equal("Paradewaffe", buckler.ShieldType);
        Assert.Equal(14, buckler.Parry);
        Assert.Equal(9, profile.WoundThreshold);
    }

    [Fact]
    public void Maps_cordulas_ranged_value_ranges_and_asymmetric_zones()
    {
        var profile = Map(CordulaXmlForEndpoint, "Cordula");
        var zoneSet = Assert.Single(profile.Sets, set => set.ArmorModel == CombatArmorModel.Zone);
        var bow = Assert.Single(zoneSet.Weapons, weapon => weapon.Category == CombatWeaponCategory.Ranged);

        Assert.Equal(27, bow.RangedValue);
        Assert.Null(bow.Attack);
        Assert.Equal("1W+5", bow.BaseDamage);
        Assert.Null(bow.CalculatedDamage);
        Assert.Equal([10, 25, 50, 100, 200], bow.RangeBands);
        Assert.Equal([3, 2, 1, 1, 0], bow.RangeDamageModifiers);
        Assert.Equal(3, bow.ReloadTime);
        Assert.Equal("Bogen", bow.WeaponTalent);
        Assert.Equal("10 / 25 / 50 / 100 / 200", bow.RangeText);

        Assert.Equal(0, zoneSet.ArmorZones!.Total);
        Assert.Equal(0, zoneSet.ArmorZones.Head);
        Assert.Equal(1, zoneSet.ArmorZones.LeftArm);
        Assert.Equal(1, zoneSet.ArmorZones.RightLeg);
        var armor = Assert.Single(zoneSet.ArmorPieces);
        Assert.Equal("Lederzeug", armor.Name);
        Assert.Equal("Lederhelm", armor.Basis);
    }

    private static CombatProfileDto Map(
        string xml,
        string name,
        Dictionary<string, TalentData>? talents = null)
    {
        var deserializer = new XmlHeroDeserializer();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var source = Assert.Single(deserializer.DeserializeCombat(stream));
        return new HeroCombatMapper().Map(new Hero
        {
            Id = Guid.NewGuid(),
            Name = name,
            ImportVersion = 3,
            Talente = talents ?? new Dictionary<string, TalentData>(),
            SourceXml = Encoding.UTF8.GetBytes(xml)
        }, source);
    }

    private const string DarianXml = """
        <daten>
          <config><rsmodell>zone</rsmodell></config>
          <angaben><name>Darian Falkenstein</name><wundschwelle>4</wundschwelle></angaben>
          <eigenschaften>
            <konstitution><akt>8</akt></konstitution><koerperkraft><akt>8</akt></koerperkraft>
            <lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer>
          </eigenschaften>
          <kampfsets>
            <kampfset nr="1" tzm="true" inbenutzung="true" defaultrsmodel="true">
              <ausweichen>13</ausweichen><geschwindigkeitinklbe>8</geschwindigkeitinklbe><ini>11</ini>
              <ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein><gesamt>0</gesamt><gesamtschutz>0</gesamtschutz><gesamtzonenschutz>0</gesamtzonenschutz><behinderung>0</behinderung></ruestungzonen>
              <nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><möglich>true</möglich><name>Magierstab als Stab</name><at>19</at><pa>14</pa><tp>1W+4</tp><tpinkl>1W+3</tpinkl><wm>2 / 2</wm><ini>1</ini><dk>N S</dk><tpkk>12 / 4</tpkk><waffentalent>Stäbe</waffentalent><be>BE-2</be></nahkampfwaffe></nahkampfwaffen>
            </kampfset>
            <kampfset nr="1" tzm="false" inbenutzung="true" defaultrsmodel="false"><ruestungeinfach><gesamt>0</gesamt><behinderung>0</behinderung></ruestungeinfach></kampfset>
            <kampfset nr="2" tzm="true" inbenutzung="false" defaultrsmodel="true"><ruestungzonen><kopf>0</kopf></ruestungzonen></kampfset>
            <kampfset nr="2" tzm="false" inbenutzung="false" defaultrsmodel="false"><ruestungeinfach><gesamt>0</gesamt></ruestungeinfach></kampfset>
            <kampfset nr="3" tzm="true" inbenutzung="false" defaultrsmodel="true"><ruestungzonen><kopf>0</kopf></ruestungzonen></kampfset>
            <kampfset nr="3" tzm="false" inbenutzung="false" defaultrsmodel="false"><ruestungeinfach><gesamt>0</gesamt></ruestungeinfach></kampfset>
          </kampfsets>
          <sonderfertigkeiten><sonderfertigkeit><name>Aufmerksamkeit</name><bezeichner>Aufmerksamkeit</bezeichner><bereich>Nahkampf</bereich><bereich>Kampf</bereich></sonderfertigkeit><sonderfertigkeit><name>Ausweichen II</name><bezeichner>Ausweichen II</bezeichner></sonderfertigkeit></sonderfertigkeiten>
          <verbilligtesonderfertigkeiten><sonderfertigkeit><name>Binden</name><bezeichner>Binden</bezeichner></sonderfertigkeit></verbilligtesonderfertigkeiten>
        </daten>
        """;

    private const string ArdorXml = """
        <daten>
          <angaben><name>Ardor Collen</name><wundschwelle>9</wundschwelle></angaben>
          <eigenschaften><lebensenergie><akt>40</akt></lebensenergie><ausdauer><akt>35</akt></ausdauer></eigenschaften>
          <vorteile><vorteil><name>Eisern</name><bezeichner>Eisern</bezeichner></vorteil></vorteile>
          <kampfsets>
            <kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>11</ausweichen><geschwindigkeitinklbe>7</geschwindigkeitinklbe><ruestungzonen><kopf>5</kopf><brust>5</brust><ruecken>5</ruecken><bauch>5</bauch><linkerarm>5</linkerarm><rechterarm>5</rechterarm><linkesbein>3</linkesbein><rechtesbein>3</rechtesbein><gesamt>5</gesamt><gesamtschutz>5</gesamtschutz><gesamtzonenschutz>5</gesamtzonenschutz><behinderung>1</behinderung></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>(Lang-)Schwert</name><at>21</at><pa>16</pa><tpinkl>1W+5</tpinkl></nahkampfwaffe><nahkampfwaffe><nummer>2</nummer><name>(Lang-)Schwert</name><at>21</at><pa>16</pa><tpinkl>1W+5</tpinkl></nahkampfwaffe></nahkampfwaffen><ruestungen><ruestung><nummer>1</nummer><name>Armschienen</name><grundlage>Armschienen</grundlage><rs>*</rs><be>*</be><kopf>0</kopf></ruestung></ruestungen></kampfset>
            <kampfset nr="2" tzm="true" inbenutzung="true"><ruestungzonen><kopf>5</kopf><brust>5</brust><ruecken>5</ruecken><bauch>5</bauch><linkerarm>5</linkerarm><rechterarm>5</rechterarm><linkesbein>3</linkesbein><rechtesbein>3</rechtesbein></ruestungzonen><schilder><schild><nummer>1</nummer><name>Buckler (Vollmetall)</name><at>0</at><pa>14</pa><typ>Paradewaffe</typ></schild></schilder></kampfset>
          </kampfsets>
        </daten>
        """;

    public const string CordulaXmlForEndpoint = """
        <daten>
          <angaben><name>Cordula</name><wundschwelle>9</wundschwelle></angaben>
          <kampfsets>
            <kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>14</ausweichen><geschwindigkeitinklbe>8</geschwindigkeitinklbe><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>1</linkerarm><rechterarm>1</rechterarm><linkesbein>1</linkesbein><rechtesbein>1</rechtesbein><gesamt>0</gesamt><behinderung>0</behinderung></ruestungzonen><fernkampfwaffen><fernkampfwaffe><nummer>1</nummer><möglich>true</möglich><name>Elfenbogen</name><at>27</at><tp>1W+5</tp><reichweite>10 / 25 / 50 / 100 / 200</reichweite><tpmod>3 / 2 / 1 / 1 / 0</tpmod><ladezeit>3</ladezeit><kampftalent>Bogen</kampftalent><spalte2>Bo/BE-3</spalte2></fernkampfwaffe></fernkampfwaffen><ruestungen><ruestung><nummer>1</nummer><name>Lederzeug</name><grundlage>Lederhelm</grundlage><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>1</linkerarm><rechterarm>1</rechterarm><linkesbein>1</linkesbein><rechtesbein>1</rechtesbein><gesamt>0</gesamt><gesamtzonenschutz>0</gesamtzonenschutz><behinderung>0</behinderung></ruestung></ruestungen></kampfset>
          </kampfsets>
        </daten>
        """;
}
