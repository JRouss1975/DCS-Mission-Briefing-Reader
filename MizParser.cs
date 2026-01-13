using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DCSMissionReader
{
    public class MissionDetails
    {
        public string Briefing { get; set; }
        public string Theatre { get; set; }
        public string Sortie { get; set; }
        public string Date { get; set; }
        public string StartTime { get; set; }
        public WeatherInfo Weather { get; set; } = new WeatherInfo();
        public List<string> RequiredModules { get; set; } = new List<string>();
        public List<byte[]> Images { get; set; } = new List<byte[]>();
        public List<byte[]> KneeboardImages { get; set; } = new List<byte[]>();
        public List<FlightSlot> FlightSlots { get; set; } = new List<FlightSlot>();
        public List<UnitGroup> AllGroups { get; set; } = new List<UnitGroup>();
        public string DebugInfo { get; set; } = "";
    }

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
        public string Coalition { get; set; }
        public string Country { get; set; }
        public string GroupType { get; set; }
        public string GroupName { get; set; }
        public string Task { get; set; }
        public string UnitName { get; set; }
        public string Type { get; set; }
        public string Skill { get; set; }
        public string CallSign { get; set; }
        public string UnitId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Alt { get; set; }
        public double Speed { get; set; }
        public double Heading { get; set; }
    }

    public class UnitGroup
    {
        public string Coalition { get; set; }
        public string Country { get; set; }
        public string GroupType { get; set; }
        public string GroupName { get; set; }
        public string Task { get; set; }
        public List<Unit> Units { get; set; } = new List<Unit>();
        public List<Waypoint> Route { get; set; } = new List<Waypoint>();
    }

    public class Unit
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Skill { get; set; }
        public string UnitId { get; set; }
        public string CallSign { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Alt { get; set; }
        public double Speed { get; set; }
        public double Heading { get; set; }
        public bool IsPlayer { get; set; }
    }

    public class Waypoint
    {
        public string Name { get; set; }
        public string Action { get; set; }
        public string Type { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Alt { get; set; }
        public double Speed { get; set; }
    }

    public class MizParser
    {
        public static async Task<string> GetTheatreAsync(string mizFilePath)
        {
            try
            {
                using (var fs = new FileStream(mizFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    var entry = archive.GetEntry("mission");
                    if (entry == null) return "Unknown";
                    
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        // Read line by line until theater is found, no artificial limit
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            var theatre = ExtractLuaField(line, "theatre") ?? ExtractLuaField(line, "theater") ?? ExtractLuaField(line, "map");
                            if (theatre != null) return theatre;
                        }
                    }
                }
                return "Unknown";
            }
            catch { return "Unknown"; }
        }

        public static async Task<MissionDetails> ParseMissionAsync(string mizFilePath)
        {
            var details = new MissionDetails();
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(mizFilePath))
                {
                    string missionFileContent = await ReadEntryAsync(archive, "mission");
                    string dictionaryContent = await ReadEntryAsync(archive, "l10n/DEFAULT/dictionary");

                    if (string.IsNullOrEmpty(missionFileContent))
                    {
                        details.Briefing = "Error: 'mission' file not found in archive.";
                        return details;
                    }

                    details.Theatre = ExtractLuaField(missionFileContent, "theatre") ?? ExtractLuaField(missionFileContent, "theater") ?? ExtractLuaField(missionFileContent, "map");
                    details.Sortie = ExtractLuaField(missionFileContent, "sortie");
                    details.RequiredModules = ExtractRequiredModules(missionFileContent);
                    details.Date = ExtractDate(missionFileContent);
                    details.StartTime = ExtractStartTime(missionFileContent);

                    string briefingKey = ExtractBriefingKey(missionFileContent);
                    if (!string.IsNullOrEmpty(briefingKey) && !string.IsNullOrEmpty(dictionaryContent))
                    {
                        details.Briefing = ExtractDictionaryValue(dictionaryContent, briefingKey);
                    }
                    if (string.IsNullOrEmpty(details.Briefing)) details.Briefing = "No briefing available.";

                    if (!string.IsNullOrEmpty(details.Sortie) && details.Sortie.StartsWith("DictKey_"))
                    {
                        string sortieText = ExtractDictionaryValue(dictionaryContent, details.Sortie);
                        if (!string.IsNullOrEmpty(sortieText)) details.Sortie = sortieText;
                    }

                    details.Weather = ExtractWeather(missionFileContent);
                    
                    // Extract ALL groups with the new robust method
                    details.AllGroups = ExtractAllGroupsRobust(missionFileContent);

                    // Populate FlightSlots by flattening AllGroups
                    details.FlightSlots = details.AllGroups.SelectMany(g => g.Units.Select(u => new FlightSlot
                    {
                        Coalition = g.Coalition,
                        Country = g.Country,
                        GroupType = g.GroupType,
                        GroupName = g.GroupName,
                        Task = g.Task,
                        UnitName = u.Name,
                        Type = u.Type,
                        Skill = u.Skill,
                        CallSign = u.CallSign,
                        UnitId = u.UnitId,
                        X = u.X,
                        Y = u.Y,
                        Alt = u.Alt,
                        Speed = u.Speed,
                        Heading = u.Heading
                    })).ToList();

                            details.DebugInfo = $"Found {details.AllGroups.Count} groups, {details.AllGroups.Sum(g => g.Units.Count)} units, {details.AllGroups.Sum(g => g.Route.Count)} waypoints";

                    // Images from l10n/DEFAULT folder
                    string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.StartsWith("l10n/DEFAULT/", StringComparison.OrdinalIgnoreCase))
                        {
                            string ext = Path.GetExtension(entry.FullName).ToLower();
                            if (imageExtensions.Contains(ext))
                            {
                                using (var stream = entry.Open())
                                using (var ms = new MemoryStream())
                                {
                                    await stream.CopyToAsync(ms);
                                    details.Images.Add(ms.ToArray());
                                }
                            }
                        }
                    }
                    
                    // Kneeboard images from KNEEBOARD folder
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.StartsWith("KNEEBOARD/", StringComparison.OrdinalIgnoreCase))
                        {
                            string ext = Path.GetExtension(entry.FullName).ToLower();
                            if (imageExtensions.Contains(ext))
                            {
                                using (var stream = entry.Open())
                                using (var ms = new MemoryStream())
                                {
                                    await stream.CopyToAsync(ms);
                                    details.KneeboardImages.Add(ms.ToArray());
                                }
                            }
                        }
                    }

                    return details;
                }
            }
            catch (Exception ex)
            {
                details.Briefing = $"Error parsing mission file: {ex.Message}";
                return details;
            }
        }

        /// <summary>
        /// Robust extraction of all groups by directly finding unit patterns
        /// </summary>
        private static List<UnitGroup> ExtractAllGroupsRobust(string content)
        {
            var groups = new List<UnitGroup>();

            // The mission file structure has ["coalition"] containing ["blue"] and ["red"]
            // Each contains ["country"] array with group types

            // Step 1: Find the coalition block
            var coalitionBlockMatch = Regex.Match(content, @"\[""coalition""\]\s*=\s*\{", RegexOptions.Singleline);
            if (!coalitionBlockMatch.Success) return groups;

            string coalitionBlock = ExtractBalancedBlock(content, coalitionBlockMatch.Index + coalitionBlockMatch.Length - 1);
            if (string.IsNullOrEmpty(coalitionBlock)) return groups;

            // Step 2: Process each side (blue, red)
            foreach (var side in new[] { "blue", "red" })
            {
                var sideMatch = Regex.Match(coalitionBlock, @"\[""" + side + @"""\]\s*=\s*\{", RegexOptions.Singleline);
                if (!sideMatch.Success) continue;

                string sideBlock = ExtractBalancedBlock(coalitionBlock, sideMatch.Index + sideMatch.Length - 1);
                if (string.IsNullOrEmpty(sideBlock)) continue;

                // Step 3: Find country blocks within side
                var countryArrayMatch = Regex.Match(sideBlock, @"\[""country""\]\s*=\s*\{", RegexOptions.Singleline);
                if (!countryArrayMatch.Success) continue;

                string countryArray = ExtractBalancedBlock(sideBlock, countryArrayMatch.Index + countryArrayMatch.Length - 1);
                if (string.IsNullOrEmpty(countryArray)) continue;

                // Step 4: Find each numbered country entry
                var countryEntries = Regex.Matches(countryArray, @"\[(\d+)\]\s*=\s*\{", RegexOptions.Singleline);
                foreach (Match ce in countryEntries)
                {
                    string countryBlock = ExtractBalancedBlock(countryArray, ce.Index + ce.Length - 1);
                    if (string.IsNullOrEmpty(countryBlock)) continue;

                    // Get country name
                    var countryNameMatch = Regex.Match(countryBlock, @"\[""name""\]\s*=\s*""([^""]+)""");
                    string countryName = countryNameMatch.Success ? countryNameMatch.Groups[1].Value : "Unknown";

                    // Step 5: Process each group type in this country
                    foreach (var groupType in new[] { "plane", "helicopter", "vehicle", "ship", "static" })
                    {
                        var groupTypeMatch = Regex.Match(countryBlock, @"\[""" + groupType + @"""\]\s*=\s*\{", RegexOptions.Singleline);
                        if (!groupTypeMatch.Success) continue;

                        string groupTypeBlock = ExtractBalancedBlock(countryBlock, groupTypeMatch.Index + groupTypeMatch.Length - 1);
                        if (string.IsNullOrEmpty(groupTypeBlock)) continue;

                        // Find "group" array
                        var groupArrayMatch = Regex.Match(groupTypeBlock, @"\[""group""\]\s*=\s*\{", RegexOptions.Singleline);
                        if (!groupArrayMatch.Success) continue;

                        string groupArray = ExtractBalancedBlock(groupTypeBlock, groupArrayMatch.Index + groupArrayMatch.Length - 1);
                        if (string.IsNullOrEmpty(groupArray)) continue;

                        // Step 6: Parse each individual group
                        var groupEntries = Regex.Matches(groupArray, @"\[(\d+)\]\s*=\s*\{", RegexOptions.Singleline);
                        foreach (Match ge in groupEntries)
                        {
                            string singleGroupBlock = ExtractBalancedBlock(groupArray, ge.Index + ge.Length - 1);
                            if (string.IsNullOrEmpty(singleGroupBlock)) continue;

                            var group = new UnitGroup
                            {
                                Coalition = side,
                                Country = countryName,
                                GroupType = groupType
                            };

                            // Group name
                            var nameMatch = Regex.Match(singleGroupBlock, @"\[""name""\]\s*=\s*""([^""]+)""");
                            if (nameMatch.Success) group.GroupName = nameMatch.Groups[1].Value;

                            // Task
                            var taskMatch = Regex.Match(singleGroupBlock, @"\[""task""\]\s*=\s*""([^""]+)""");
                            if (taskMatch.Success) group.Task = taskMatch.Groups[1].Value;

                            // Parse units
                            var unitsMatch = Regex.Match(singleGroupBlock, @"\[""units""\]\s*=\s*\{", RegexOptions.Singleline);
                            if (unitsMatch.Success)
                            {
                                string unitsBlock = ExtractBalancedBlock(singleGroupBlock, unitsMatch.Index + unitsMatch.Length - 1);
                                if (!string.IsNullOrEmpty(unitsBlock))
                                {
                                    var unitEntries = Regex.Matches(unitsBlock, @"\[(\d+)\]\s*=\s*\{", RegexOptions.Singleline);
                                    foreach (Match ue in unitEntries)
                                    {
                                        string unitBlock = ExtractBalancedBlock(unitsBlock, ue.Index + ue.Length - 1);
                                        if (!string.IsNullOrEmpty(unitBlock))
                                        {
                                            var unit = ParseUnit(unitBlock);
                                            if (unit != null) group.Units.Add(unit);
                                        }
                                    }
                                }
                            }

                            // Parse route
                            var routeMatch = Regex.Match(singleGroupBlock, @"\[""route""\]\s*=\s*\{", RegexOptions.Singleline);
                            if (routeMatch.Success)
                            {
                                string routeBlock = ExtractBalancedBlock(singleGroupBlock, routeMatch.Index + routeMatch.Length - 1);
                                if (!string.IsNullOrEmpty(routeBlock))
                                {
                                    var pointsMatch = Regex.Match(routeBlock, @"\[""points""\]\s*=\s*\{", RegexOptions.Singleline);
                                    if (pointsMatch.Success)
                                    {
                                        string pointsBlock = ExtractBalancedBlock(routeBlock, pointsMatch.Index + pointsMatch.Length - 1);
                                        if (!string.IsNullOrEmpty(pointsBlock))
                                        {
                                            var wpEntries = Regex.Matches(pointsBlock, @"\[(\d+)\]\s*=\s*\{", RegexOptions.Singleline);
                                            foreach (Match we in wpEntries)
                                            {
                                                string wpBlock = ExtractBalancedBlock(pointsBlock, we.Index + we.Length - 1);
                                                if (!string.IsNullOrEmpty(wpBlock))
                                                {
                                                    var wp = ParseWaypoint(wpBlock);
                                                    if (wp != null) group.Route.Add(wp);
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (group.Units.Count > 0) groups.Add(group);
                        }
                    }
                }
            }

            return groups;
        }

        private static Unit ParseUnit(string unitBlock)
        {
            var unit = new Unit();

            var nameMatch = Regex.Match(unitBlock, @"\[""name""\]\s*=\s*""([^""]+)""");
            if (nameMatch.Success) unit.Name = nameMatch.Groups[1].Value;

            var typeMatch = Regex.Match(unitBlock, @"\[""type""\]\s*=\s*""([^""]+)""");
            if (typeMatch.Success) unit.Type = typeMatch.Groups[1].Value;

            var unitIdMatch = Regex.Match(unitBlock, @"\[""unitId""\]\s*=\s*(\d+)");
            if (unitIdMatch.Success) unit.UnitId = unitIdMatch.Groups[1].Value;

            var skillMatch = Regex.Match(unitBlock, @"\[""skill""\]\s*=\s*""([^""]+)""");
            if (skillMatch.Success)
            {
                unit.Skill = skillMatch.Groups[1].Value;
                unit.IsPlayer = unit.Skill == "Client" || unit.Skill == "Player";
            }
            else
            {
                unit.Skill = "AI";
            }

            // Extract callsign name if it exists (usually for aircraft)
            var callsignNameMatch = Regex.Match(unitBlock, @"\[""callsign""\].*?\[""name""\]\s*=\s*""([^""]+)""", RegexOptions.Singleline);
            if (callsignNameMatch.Success)
            {
                unit.CallSign = callsignNameMatch.Groups[1].Value;
            }
            else
            {
                // Try simple callsign field
                var simpleCallsignMatch = Regex.Match(unitBlock, @"\[""callsign""\]\s*=\s*""([^""]+)""");
                if (simpleCallsignMatch.Success) unit.CallSign = simpleCallsignMatch.Groups[1].Value;
            }

            var xMatch = Regex.Match(unitBlock, @"\[""x""\]\s*=\s*([-\d\.eE+]+)");
            if (xMatch.Success && double.TryParse(xMatch.Groups[1].Value, out double xVal)) unit.X = xVal;

            var yMatch = Regex.Match(unitBlock, @"\[""y""\]\s*=\s*([-\d\.eE+]+)");
            if (yMatch.Success && double.TryParse(yMatch.Groups[1].Value, out double yVal)) unit.Y = yVal;

            var altMatch = Regex.Match(unitBlock, @"\[""alt""\]\s*=\s*([-\d\.eE+]+)");
            if (altMatch.Success && double.TryParse(altMatch.Groups[1].Value, out double altVal)) unit.Alt = altVal;

            var speedMatch = Regex.Match(unitBlock, @"\[""speed""\]\s*=\s*([-\d\.eE+]+)");
            if (speedMatch.Success && double.TryParse(speedMatch.Groups[1].Value, out double speedVal)) unit.Speed = speedVal;

            var headingMatch = Regex.Match(unitBlock, @"\[""heading""\]\s*=\s*([-\d\.eE+]+)");
            if (headingMatch.Success && double.TryParse(headingMatch.Groups[1].Value, out double hVal)) unit.Heading = hVal;

            return !string.IsNullOrEmpty(unit.Type) ? unit : null;
        }

        private static Waypoint ParseWaypoint(string wpBlock)
        {
            var wp = new Waypoint();

            var nameMatch = Regex.Match(wpBlock, @"\[""name""\]\s*=\s*""([^""]+)""");
            if (nameMatch.Success) wp.Name = nameMatch.Groups[1].Value;

            var actionMatch = Regex.Match(wpBlock, @"\[""action""\]\s*=\s*""([^""]+)""");
            if (actionMatch.Success) wp.Action = actionMatch.Groups[1].Value;

            var typeMatch = Regex.Match(wpBlock, @"\[""type""\]\s*=\s*""([^""]+)""");
            if (typeMatch.Success) wp.Type = typeMatch.Groups[1].Value;

            var xMatch = Regex.Match(wpBlock, @"\[""x""\]\s*=\s*([-\d\.eE+]+)");
            if (xMatch.Success && double.TryParse(xMatch.Groups[1].Value, out double wpX)) wp.X = wpX;

            var yMatch = Regex.Match(wpBlock, @"\[""y""\]\s*=\s*([-\d\.eE+]+)");
            if (yMatch.Success && double.TryParse(yMatch.Groups[1].Value, out double wpY)) wp.Y = wpY;

            var altMatch = Regex.Match(wpBlock, @"\[""alt""\]\s*=\s*([-\d\.eE+]+)");
            if (altMatch.Success && double.TryParse(altMatch.Groups[1].Value, out double wpAlt)) wp.Alt = wpAlt;

            var speedMatch = Regex.Match(wpBlock, @"\[""speed""\]\s*=\s*([-\d\.eE+]+)");
            if (speedMatch.Success && double.TryParse(speedMatch.Groups[1].Value, out double wpSpd)) wp.Speed = wpSpd;

            return wp;
        }

        private static string ExtractBalancedBlock(string content, int startIndex)
        {
            if (startIndex >= content.Length || content[startIndex] != '{') return null;

            int openBraces = 0;
            for (int i = startIndex; i < content.Length; i++)
            {
                if (content[i] == '{') openBraces++;
                else if (content[i] == '}')
                {
                    openBraces--;
                    if (openBraces == 0) return content.Substring(startIndex, i - startIndex + 1);
                }
            }
            return null;
        }

        // Keep all existing helper methods
        private static async Task<string> ReadEntryAsync(ZipArchive archive, string entryName)
        {
            var entry = archive.GetEntry(entryName);
            if (entry == null) return string.Empty;
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private static string ExtractLuaField(string content, string fieldName)
        {
            if (string.IsNullOrEmpty(content)) return null;
            // Most resilient regex: find fieldName, then an equals sign, then a quoted value.
            // Handles: ["theatre"]="Value", theatre = 'Value', ["theater"]   = "Value", etc.
            var pattern = $@"{fieldName}[""']?\s*\]?\s*=\s*[""']([^""']+)[""']";
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ExtractDate(string content)
        {
            // Find the root ["date"] block which is indented by 4 spaces
            var dateBlockMatch = Regex.Match(content, @"^    \[""date""\]\s*=\s*\{", RegexOptions.Multiline);
            if (dateBlockMatch.Success)
            {
                string dateBlock = ExtractBalancedBlock(content, dateBlockMatch.Index + dateBlockMatch.Length - 1);
                if (!string.IsNullOrEmpty(dateBlock))
                {
                    var dayMatch = Regex.Match(dateBlock, @"\[""Day""\]\s*=\s*(\d+)");
                    var monthMatch = Regex.Match(dateBlock, @"\[""Month""\]\s*=\s*(\d+)");
                    var yearMatch = Regex.Match(dateBlock, @"\[""Year""\]\s*=\s*(\d+)");

                    if (dayMatch.Success && monthMatch.Success && yearMatch.Success)
                    {
                        return $"{yearMatch.Groups[1].Value}-{monthMatch.Groups[1].Value.PadLeft(2, '0')}-{dayMatch.Groups[1].Value.PadLeft(2, '0')}";
                    }
                }
            }
            return "Unknown Date";
        }

        private static string ExtractStartTime(string content)
        {
            // Root keys in DCS mission files are typically indented with exactly 4 spaces
            var match = Regex.Match(content, @"^    \[""start_time""\]\s*=\s*([-\d\.eE+]+)", RegexOptions.Multiline);
            
            // Fallback if formatting differs
            if (!match.Success) match = Regex.Match(content, @"\[""start_time""\]\s*=\s*([-\d\.eE+]+)");

            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            {
                TimeSpan t = TimeSpan.FromSeconds(seconds);
                int totalHours = (int)t.TotalHours;
                return $"{totalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
            }
            return "00:00:00";
        }

        private static WeatherInfo ExtractWeather(string content)
        {
            var w = new WeatherInfo();
            w.QNH = ParseInt(content, @"\[""qnh""\]\s*=\s*(\d+)");
            w.Temperature = ParseDouble(content, @"\[""temperature""\]\s*=\s*([-\d\.]+)");
            w.WindSpeedGround = ParseNestedWind(content, "atGround", "speed");
            w.WindDirGround = ParseNestedWind(content, "atGround", "dir");
            w.WindSpeed2000 = ParseNestedWind(content, "at2000", "speed");
            w.WindDir2000 = ParseNestedWind(content, "at2000", "dir");
            w.WindSpeed8000 = ParseNestedWind(content, "at8000", "speed");
            w.WindDir8000 = ParseNestedWind(content, "at8000", "dir");
            return w;
        }

        private static int ParseNestedWind(string content, string level, string param)
        {
            var regex = new Regex(@"\[""" + level + @"""\]\s*=\s*\{.*?\[""" + param + @"""\]\s*=\s*(\d+)", RegexOptions.Singleline);
            var match = regex.Match(content);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int val)) return val;
            return 0;
        }

        private static int ParseInt(string content, string pattern)
        {
            var match = Regex.Match(content, pattern);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int val)) return val;
            return 0;
        }

        private static double ParseDouble(string content, string pattern)
        {
            var match = Regex.Match(content, pattern);
            if (match.Success && double.TryParse(match.Groups[1].Value, out double val)) return val;
            return 0;
        }

        private static List<FlightSlot> ExtractFlightSlots(string content)
        {
            var slots = new List<FlightSlot>();
            var skillMatches = Regex.Matches(content, @"\[""skill""\]\s*=\s*""(Client|Player)""");
            foreach (Match m in skillMatches)
            {
                int start = Math.Max(0, m.Index - 500);
                int length = Math.Min(content.Length - start, 1000);
                string snippet = content.Substring(start, length);

                int relativeSkillIndex = m.Index - start;
                int openBrace = snippet.LastIndexOf('{', relativeSkillIndex);
                int closeBrace = snippet.IndexOf('}', relativeSkillIndex);

                if (openBrace != -1 && closeBrace != -1 && closeBrace > openBrace)
                {
                    string unitBlock = snippet.Substring(openBrace, closeBrace - openBrace + 1);

                    var typeM = Regex.Match(unitBlock, @"\[""type""\]\s*=\s*""([^""]+)""");
                    var nameM = Regex.Match(unitBlock, @"\[""name""\]\s*=\s*""([^""]+)""");
                    var skillM = Regex.Match(unitBlock, @"\[""skill""\]\s*=\s*""([^""]+)""");

                    if (typeM.Success && nameM.Success && skillM.Success)
                    {
                        slots.Add(new FlightSlot
                        {
                            UnitName = nameM.Groups[1].Value,
                            Type = typeM.Groups[1].Value,
                            Skill = skillM.Groups[1].Value,
                            Coalition = "Unknown",
                        });
                    }
                }
            }
            return slots;
        }


        private static List<string> ExtractRequiredModules(string content)
        {
            var modules = new List<string>();
            var blockRegex = new Regex(@"\[""requiredModules""\]\s*=\s*\{(.*?)\},", RegexOptions.Singleline);
            var match = blockRegex.Match(content);
            if (match.Success)
            {
                var entryRegex = new Regex(@"=\s*""([^""]+)""");
                var matches = entryRegex.Matches(match.Groups[1].Value);
                foreach (Match m in matches) modules.Add(m.Groups[1].Value);
            }
            return modules;
        }

        private static string ExtractBriefingKey(string missionContent)
        {
            var regex = new Regex(@"\[""descriptionText""\]\s*=\s*""(DictKey_[^""]+)""");
            var match = regex.Match(missionContent);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ExtractDictionaryValue(string dictionaryContent, string key)
        {
            if (string.IsNullOrEmpty(dictionaryContent)) return null;
            var regex = new Regex(@"\[""" + Regex.Escape(key) + @"""\]\s*=\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Singleline);
            var match = regex.Match(dictionaryContent);
            if (match.Success) return UnescapeLuaString(match.Groups[1].Value);
            return null;
        }

        private static string UnescapeLuaString(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string result = Regex.Replace(text, @"\\(?=[\r\n])", "");
            result = result.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
            return result;
        }
    }
}