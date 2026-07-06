using Xunit;

namespace DCSMissionReader.Tests;

public class DcsSynonymsTests
{
    [Theory]
    [InlineData("F/A-18C", "fa18c")]
    [InlineData("F-16C_50", "f16c50")]
    [InlineData("MiG-29A", "mig29a")]
    [InlineData("AH-64D", "ah64d")]
    [InlineData("Hello World", "helloworld")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("F-4E Phantom", "f4ephantom")]
    public void Collapse_RemovesNonAlphaNumeric(string? input, string expected)
    {
        Assert.Equal(expected, DcsSynonyms.Collapse(input));
    }

    [Theory]
    [InlineData("f16", true)]
    [InlineData("viper", true)]
    [InlineData("hornet", true)]
    [InlineData("caucasus", true)]
    [InlineData("syria", true)]
    [InlineData("tank", false)] // not in any token set
    [InlineData("", true)]
    public void CanAutoDetect_AircraftAndMapTokens(string word, bool expected)
    {
        Assert.Equal(expected, DcsSynonyms.CanAutoDetect(word));
    }

    [Theory]
    [InlineData("cap", true)]
    [InlineData("intercept", true)]
    [InlineData("sead", true)]
    [InlineData("cas", true)]
    [InlineData("night", true)]
    [InlineData("blue", true)]
    [InlineData("the", true)] // stop word
    [InlineData("randomword", false)]
    public void CanAutoDetect_MissionTypeAndStopWords(string word, bool expected)
    {
        Assert.Equal(expected, DcsSynonyms.CanAutoDetect(word));
    }

    [Theory]
    [InlineData("f16", "viper")]
    [InlineData("viper", "viper")]
    [InlineData("a10", "warthog")]
    [InlineData("warthog", "warthog")]
    [InlineData("hornet", "hornet")]
    [InlineData("fa18", "hornet")]
    public void QueryAliases_ContainExpectedKeys(string key, string expectedAlias)
    {
        Assert.True(DcsSynonyms.QueryAliases.ContainsKey(key));
        Assert.Contains(expectedAlias, DcsSynonyms.QueryAliases[key]);
    }

    [Theory]
    [InlineData("F-16C_50", true)]
    [InlineData("FA-18C_hornet", true)]
    [InlineData("A-10C", true)]
    [InlineData("F-4E", true)]
    [InlineData("NonExistentType", false)]
    [InlineData("", false)]
    public void FriendlyTokens_ContainsKnownTypes(string typeName, bool expected)
    {
        Assert.Equal(expected, DcsSynonyms.FriendlyTokens.ContainsKey(typeName));
    }

    [Theory]
    [InlineData("F-16C_50")]
    [InlineData("FA-18C_hornet")]
    [InlineData("A-10C_2")]
    [InlineData("AH-64D_BLK_II")]
    public void DisplayNames_ContainsKnownTypes(string typeName)
    {
        Assert.True(DcsSynonyms.DisplayNames.ContainsKey(typeName));
        Assert.NotEmpty(DcsSynonyms.DisplayNames[typeName]);
    }

    [Theory]
    [InlineData("F-16C_50", "F-16C Viper")]
    [InlineData("FA-18C_hornet", "F/A-18C Hornet")]
    [InlineData("A-10C", "A-10C Warthog")]
    [InlineData("MiG-29A", "MiG-29A")]
    public void DisplayName_ReturnsExpected(string typeName, string expectedDisplay)
    {
        Assert.Equal(expectedDisplay, DcsSynonyms.DisplayName(typeName));
    }

    [Theory]
    [InlineData("F-16C Viper", "F-16C Viper")] // unknown type returns itself
    public void DisplayName_UnknownType_ReturnsItself(string typeName, string expected)
    {
        Assert.Equal(expected, DcsSynonyms.DisplayName(typeName));
    }

    [Theory]
    [InlineData("air to air", "a2a")]
    [InlineData("close air support", "cas")]
    [InlineData("combat air patrol", "cap")]
    [InlineData("wild weasel", "sead")]
    [InlineData("search and rescue", "csar")]
    public void FoldPhrases_CollapsesMultiWordTerms(string input, string expected)
    {
        Assert.Equal(expected, DcsSynonyms.FoldPhrases(input));
    }

    [Theory]
    [InlineData("CAP", "CAP")]
    [InlineData("sead", "SEAD")]
    [InlineData("dead", "SEAD")] // dead aliases to SEAD
    [InlineData("cas", "CAS")]
    [InlineData("barcap", "CAP")]
    public void CanonicalTag_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, DcsSynonyms.CanonicalTag(input));
    }

    [Theory]
    [InlineData("F-4E Phantom", "F-4E Phantom")] // display name is itself
    public void DisplayName_Consistency(string typeName, string expectedDisplayName)
    {
        string display = DcsSynonyms.DisplayName(typeName);
        Assert.Equal(expectedDisplayName, display);
    }

    [Theory]
    [InlineData("Caucasus", "Caucasus", true)]
    [InlineData("Caucasus", "cauc", true)]
    [InlineData("Syria", "syria", true)]
    [InlineData("PersianGulf", "pg", true)]
    [InlineData("Caucasus", "Syria", false)]
    [InlineData("Nevada", "caucasus", false)]
    public void TheatreMatches_VariousCombinations(string missionTheatre, string filterValue, bool expected)
    {
        Assert.Equal(expected, DcsSynonyms.TheatreMatches(filterValue, missionTheatre));
    }

    [Fact]
    public void Tokenize_SplitsCorrectly()
    {
        var tokens = DcsSynonyms.Tokenize("F-16C Viper");
        Assert.Contains("f", tokens);
        Assert.Contains("16c", tokens);
        Assert.Contains("viper", tokens);
    }

    [Fact]
    public void Tokenize_Empty_ReturnsEmpty()
    {
        var tokens = DcsSynonyms.Tokenize("");
        Assert.Empty(tokens);
    }

    [Theory]
    [InlineData("viper", "F-16C_50")]
    [InlineData("hornet", "FA-18C_hornet")]
    [InlineData("flanker", "Su-27")]
    [InlineData("fulcrum", "MiG-29A")]
    public void AircraftMatches_CanonicalAndAliases(string planeFilter, string playerType)
    {
        // playerAircraftCsv format: |type1|type2| with real DCS type IDs
        string csv = $"|{playerType}|";
        Assert.True(DcsSynonyms.AircraftMatches(planeFilter, csv));
    }
}
