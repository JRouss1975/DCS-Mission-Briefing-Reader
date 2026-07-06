using Xunit;

namespace DCSMissionReader.Tests;

public class MapHelperTests
{
    private const double Tolerance = 0.01; // ~1km accuracy

    [Theory]
    [InlineData("Caucasus", 42.5, 42.0)]
    [InlineData("Syria", 35.0, 37.0)]
    [InlineData("PersianGulf", 26.0, 56.0)]
    [InlineData("Nevada", 36.5, -115.5)]
    [InlineData("MarianaIslands", 15.0, 145.5)]
    [InlineData("SouthAtlantic", -52.0, -59.0)]
    [InlineData("Falklands", -52.0, -59.0)]
    [InlineData("Normandy", 49.0, -1.0)]
    [InlineData("Sinai", 30.0, 33.5)]
    [InlineData("Kola", 68.5, 33.0)]
    [InlineData("Afghanistan", 34.5, 69.0)]
    public void GetTheaterCenter_ReturnsExpectedCoordinates(string theater, double expectedLat, double expectedLon)
    {
        var (lat, lon) = MapHelper.GetTheaterCenter(theater);
        Assert.Equal(expectedLat, lat, Tolerance);
        Assert.Equal(expectedLon, lon, Tolerance);
    }

    [Fact]
    public void GetTheaterCenter_UnknownTheater_FallsBackToCaucasus()
    {
        var (lat, lon) = MapHelper.GetTheaterCenter("NonExistentMap");
        var (caucLat, caucLon) = MapHelper.GetTheaterCenter("Caucasus");
        Assert.Equal(caucLat, lat, Tolerance);
        Assert.Equal(caucLon, lon, Tolerance);
    }

    [Fact]
    public void GetTheaterCenter_NullTheater_FallsBackToCaucasus()
    {
        var (lat, lon) = MapHelper.GetTheaterCenter(null!);
        var (caucLat, caucLon) = MapHelper.GetTheaterCenter("Caucasus");
        Assert.Equal(caucLat, lat, Tolerance);
        Assert.Equal(caucLon, lon, Tolerance);
    }

    [Fact]
    public void Caucasus_KutaisiAirport_ReturnsReasonableLatLon()
    {
        // Kutaisi airport approximate DCS coordinates (from known mission data)
        var (lat, lon) = MapHelper.DcsToLatLon("Caucasus", 288160, -337340);
        Assert.False(double.IsNaN(lat));
        Assert.False(double.IsNaN(lon));
        Assert.InRange(lat, 0.0, 50.0);
        Assert.InRange(lon, 0.0, 90.0);
    }

    [Fact]
    public void Syria_AleppoAirport_ReturnsReasonableLatLon()
    {
        var (lat, lon) = MapHelper.DcsToLatLon("Syria", 280000, -420000);
        Assert.False(double.IsNaN(lat));
        Assert.False(double.IsNaN(lon));
        Assert.InRange(lat, 0.0, 50.0);
        Assert.InRange(lon, 0.0, 90.0);
    }

    [Fact]
    public void PersianGulf_ApproximateCenter_ReturnsReasonableLatLon()
    {
        var (lat, lon) = MapHelper.DcsToLatLon("PersianGulf", 0, 0);
        Assert.InRange(lat, 24.0, 28.0);
        Assert.InRange(lon, 54.0, 58.0);
    }

    [Fact]
    public void DcsToLatLon_Origin_DoesNotReturnNaN()
    {
        var (lat, lon) = MapHelper.DcsToLatLon("Caucasus", 0, 0);
        Assert.False(double.IsNaN(lat));
        Assert.False(double.IsNaN(lon));
        Assert.False(double.IsInfinity(lat));
        Assert.False(double.IsInfinity(lon));
    }

    [Fact]
    public void DcsToLatLon_LargeCoordinates_DoesNotReturnNaN()
    {
        var (lat, lon) = MapHelper.DcsToLatLon("Caucasus", 1000000, 1000000);
        Assert.False(double.IsNaN(lat));
        Assert.False(double.IsNaN(lon));
        Assert.InRange(lat, -90, 90);
        Assert.InRange(lon, -180, 180);
    }

    [Fact]
    public void DcsToLatLon_NegativeCoordinates_DoesNotReturnNaN()
    {
        var (lat, lon) = MapHelper.DcsToLatLon("Caucasus", -500000, -500000);
        Assert.False(double.IsNaN(lat));
        Assert.False(double.IsNaN(lon));
        Assert.InRange(lat, -90, 90);
        Assert.InRange(lon, -180, 180);
    }

    [Fact]
    public void DcsToLatLon_UnknownTheater_UsesCaucasusProjection()
    {
        var (lat1, lon1) = MapHelper.DcsToLatLon("Unknown", 100000, 200000);
        var (lat2, lon2) = MapHelper.DcsToLatLon("Caucasus", 100000, 200000);
        Assert.Equal(lat1, lat2, Tolerance);
        Assert.Equal(lon1, lon2, Tolerance);
    }

    [Theory]
    [InlineData("Caucasus")]
    [InlineData("Syria")]
    [InlineData("PersianGulf")]
    [InlineData("Nevada")]
    [InlineData("Normandy")]
    public void DcsToLatLon_AllTheaters_ReturnsValidCoordinates(string theater)
    {
        var (lat, lon) = MapHelper.DcsToLatLon(theater, 0, 0);
        Assert.InRange(lat, -90, 90);
        Assert.InRange(lon, -180, 180);
    }
}
