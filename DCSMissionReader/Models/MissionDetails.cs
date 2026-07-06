using System.Collections.Generic;

namespace DCSMissionReader;

public class MissionDetails
{
    public string Briefing { get; set; } = "";
    public string BriefingSituation { get; set; } = "";
    public string BriefingRedTask { get; set; } = "";
    public string BriefingBlueTask { get; set; } = "";
    public string BriefingNeutralsTask { get; set; } = "";
    public string Theatre { get; set; } = "";
    public string Sortie { get; set; } = "";
    public string Date { get; set; } = "";
    public string StartTime { get; set; } = "";
    public WeatherInfo Weather { get; set; } = new();
    public List<string> RequiredModules { get; set; } = new();
    public List<byte[]> Images { get; set; } = new();
    public List<byte[]> KneeboardImages { get; set; } = new();
    public List<FlightSlot> FlightSlots { get; set; } = new();
    public List<UnitGroup> AllGroups { get; set; } = new();
    public string DebugInfo { get; set; } = "";
}

public class BriefingKeys
{
    public string? SituationKey { get; set; }
    public string? RedTaskKey { get; set; }
    public string? BlueTaskKey { get; set; }
    public string? NeutralsTaskKey { get; set; }
    public string? SortieKey { get; set; }
}
