using System.Collections.Generic;

namespace DCSMissionReader;

public class WeatherInfo
{
    public int WindSpeedGround { get; set; }
    public int WindDirGround { get; set; }
    public int WindSpeed2000 { get; set; }
    public int WindDir2000 { get; set; }
    public int WindSpeed8000 { get; set; }
    public int WindDir8000 { get; set; }
    public int QNH { get; set; }
    public double Temperature { get; set; }
}

public class FlightSlot
{
    public string Coalition { get; set; } = "";
    public string Country { get; set; } = "";
    public string GroupType { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string Task { get; set; } = "";
    public string UnitName { get; set; } = "";
    public string Type { get; set; } = "";
    public string Skill { get; set; } = "";
    public string CallSign { get; set; } = "";
    public string UnitId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Alt { get; set; }
    public double Speed { get; set; }
    public double Heading { get; set; }
}

public class UnitGroup
{
    public string Coalition { get; set; } = "";
    public string Country { get; set; } = "";
    public string GroupType { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string Task { get; set; } = "";
    public List<Unit> Units { get; set; } = new();
    public List<Waypoint> Route { get; set; } = new();
}

public class Unit
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Skill { get; set; } = "";
    public string UnitId { get; set; } = "";
    public string CallSign { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Alt { get; set; }
    public double Speed { get; set; }
    public double Heading { get; set; }
    public bool IsPlayer { get; set; }
}

public class Waypoint
{
    public string Name { get; set; } = "";
    public string Action { get; set; } = "";
    public string Type { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Alt { get; set; }
    public double Speed { get; set; }
}
