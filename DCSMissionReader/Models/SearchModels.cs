using System.Collections.Generic;

namespace DCSMissionReader;

public class SearchRequest
{
    public string Text { get; set; } = "";
    public string? TagFilter { get; set; }
    public string? AircraftFilter { get; set; }
    public string? TimeFilter { get; set; }
    public HashSet<string>? LimitToPaths { get; set; }
}

public class SearchResult
{
    public string Path { get; set; } = "";
    public double Score { get; set; }
    public string Snippet { get; set; } = "";
    public string Tags { get; set; } = "";
}
