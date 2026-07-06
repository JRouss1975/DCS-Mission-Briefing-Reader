using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DCSMissionReader
{
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
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            var theatre = ExtractLuaField(line, "theatre") ?? ExtractLuaField(line, "map");
                            if (theatre != null) return theatre;
                        }
                    }
                }
                return "Unknown";
            }
            catch (Exception ex) { Debug.WriteLine($"MizParser.GetTheatreAsync failed: {ex.Message}"); return "Unknown"; }
        }

        public static async Task<(string Theatre, string MainUnit)> GetMissionBriefInfoAsync(string mizFilePath)
        {
            string theatre = "Unknown";
            string mainUnit = "None";
            try
            {
                using (var fs = new FileStream(mizFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    var entry = archive.GetEntry("mission");
                    if (entry == null) return (theatre, mainUnit);

                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        string content = await reader.ReadToEndAsync();
                        theatre = ExtractLuaField(content, "theatre") ?? ExtractLuaField(content, "map") ?? "Unknown";

                        // Exclude known non-aircraft types that appear in ["type"] fields (waypoints, tasks, etc)
                        var nonUnitTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "TakeOff", "TakeOffParking", "TakeOffParkingHot", "TakeOffGround", "TakeOffGroundHot",
                            "Take Off", "Take Off Parking", "Take Off Parking Hot", "Take Off Ground", "Take Off Ground Hot",
                            "TurningPoint", "Turning Point", "Landing", "Land", "LandingReFuAr", "Landing ReFuAr", 
                            "Flyover", "Fly Over Point", "FinPoint", "RaceTrackHeading", "PathPoint", 
                            "CAS", "AFAC", "Refueling", "Nothing", "Transport", "Escort", 
                            "SEAD", "Intercept", "Fighter Sweep", "Anti-ship Strike", 
                            "Runway Attack", "Pinpoint Strike", "BAI", "Ground Attack",
                            "Combined Arms", "CAP", "DEAD", "OCA/Runway", "OCA/Aircraft",
                            "Reconnaissance", "Ground Escort", "On route", "On Route"
                        };

                        // FAST BUT ROBUST PASS
                        var playerUnitTypes = new List<string>();
                        var skillMatches = Regex.Matches(content, @"\[""skill""\]\s*=\s*""(Player|Client)""");

                        foreach (Match skillMatch in skillMatches)
                        {
                            int skillPos = skillMatch.Index;

                            // Find categories backwards
                            int planeIdx = content.LastIndexOf("[\"plane\"]", skillPos, StringComparison.Ordinal);
                            int heliIdx  = content.LastIndexOf("[\"helicopter\"]", skillPos, StringComparison.Ordinal);
                            int vehIdx   = content.LastIndexOf("[\"vehicle\"]", skillPos, StringComparison.Ordinal);
                            int shipIdx  = content.LastIndexOf("[\"ship\"]", skillPos, StringComparison.Ordinal);
                            int statIdx  = content.LastIndexOf("[\"static\"]", skillPos, StringComparison.Ordinal);

                            int maxCatIdx = Math.Max(planeIdx, Math.Max(heliIdx, Math.Max(vehIdx, Math.Max(shipIdx, statIdx))));

                            // Skip ground units, ships, statics (Combined Arms)
                            if (maxCatIdx == -1 || (maxCatIdx != planeIdx && maxCatIdx != heliIdx))
                                continue;

                            // Window to find type field
                            int startSearch = Math.Max(maxCatIdx, skillPos - 2000);
                            int endSearch = Math.Min(content.Length, skillPos + 2000);
                            string searchWindow = content.Substring(startSearch, endSearch - startSearch);
                            int localSkillPos = skillPos - startSearch;

                            var allTypes = Regex.Matches(searchWindow, @"\[""type""\]\s*=\s*""([^""]+)""");
                            
                            string? foundType = null;
                            int minDistanceMatch = int.MaxValue;

                            foreach (Match tm in allTypes)
                            {
                                string candidate = tm.Groups[1].Value;
                                // Ignore categories and excluded types
                                if (candidate == "plane" || candidate == "helicopter" || candidate == "vehicle" || candidate == "ship" || candidate == "static") continue;
                                if (candidate == "blue" || candidate == "red" || candidate == "neutrals") continue;
                                if (nonUnitTypes.Contains(candidate)) continue;

                                int dist = Math.Abs(tm.Index - localSkillPos);
                                if (dist < minDistanceMatch)
                                {
                                    minDistanceMatch = dist;
                                    foundType = candidate;
                                }
                            }

                            if (foundType != null)
                                playerUnitTypes.Add(foundType);
                        }

                        if (playerUnitTypes.Count == 0)
                        {
                            mainUnit = "Various";
                        }
                        else
                        {
                            // Pick the most common player unit type.
                            var grouped = playerUnitTypes
                                .GroupBy(t => t)
                                .OrderByDescending(g => g.Count())
                                .ToList();

                            // Always use the unit with the highest count.
                            // In case of a tie for highest count, grouped[0] is still one of the highest.
                            mainUnit = grouped[0].Key;
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"MizParser.GetMissionBriefInfoAsync failed for {mizFilePath}: {ex.Message}"); }
            return (theatre, mainUnit);
        }

        /// <summary>
        /// Updates the briefing text in the .miz file's dictionary (backward compatible - updates situation only)
        /// </summary>
        public static async Task UpdateBriefingAsync(string mizFilePath, string newBriefingText)
        {
            await UpdateAllBriefingsAsync(mizFilePath, newBriefingText, null, null, null);
        }

        /// <summary>
        /// Updates all briefing sections including sortie in the .miz file's dictionary
        /// </summary>
        public static async Task UpdateAllBriefingsAsync(string mizFilePath, string? situationText, string? redTaskText, string? blueTaskText, string? neutralsTaskText, string? sortieText = null)
        {
            // First, read the mission file to find all briefing keys
            BriefingKeys? briefingKeys = null;
            string? dictionaryContent = null;

            using (var fs = new FileStream(mizFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                string missionContent = await ReadEntryAsync(archive, "mission");
                if (!string.IsNullOrEmpty(missionContent))
                {
                    briefingKeys = ExtractAllBriefingKeys(missionContent);
                }
                dictionaryContent = await ReadEntryAsync(archive, "l10n/DEFAULT/dictionary");
            }

            if (briefingKeys == null)
            {
                throw new InvalidOperationException("Could not find briefing keys in mission file.");
            }

            if (string.IsNullOrEmpty(dictionaryContent))
            {
                throw new InvalidOperationException("Could not find dictionary file in mission archive.");
            }

            string newDictionaryContent = dictionaryContent;

            // Update Situation (descriptionText) if key exists and text is provided
            if (!string.IsNullOrEmpty(briefingKeys.SituationKey) && situationText != null)
            {
                string escapedText = EscapeLuaString(situationText);
                var regex = new Regex(@"(\[""" + Regex.Escape(briefingKeys.SituationKey) + @"""\]\s*=\s*"")(?:[^""\\]|\\.)*("")", RegexOptions.Singleline);
                if (regex.IsMatch(newDictionaryContent))
                {
                    newDictionaryContent = regex.Replace(newDictionaryContent, $"$1{escapedText}$2");
                }
            }

            // Update Red Task (descriptionRedTask) if key exists and text is provided
            if (!string.IsNullOrEmpty(briefingKeys.RedTaskKey) && redTaskText != null)
            {
                string escapedText = EscapeLuaString(redTaskText);
                var regex = new Regex(@"(\[""" + Regex.Escape(briefingKeys.RedTaskKey) + @"""\]\s*=\s*"")(?:[^""\\]|\\.)*("")", RegexOptions.Singleline);
                if (regex.IsMatch(newDictionaryContent))
                {
                    newDictionaryContent = regex.Replace(newDictionaryContent, $"$1{escapedText}$2");
                }
            }

            // Update Blue Task (descriptionBlueTask) if key exists and text is provided
            if (!string.IsNullOrEmpty(briefingKeys.BlueTaskKey) && blueTaskText != null)
            {
                string escapedText = EscapeLuaString(blueTaskText);
                var regex = new Regex(@"(\[""" + Regex.Escape(briefingKeys.BlueTaskKey) + @"""\]\s*=\s*"")(?:[^""\\]|\\.)*("")", RegexOptions.Singleline);
                if (regex.IsMatch(newDictionaryContent))
                {
                    newDictionaryContent = regex.Replace(newDictionaryContent, $"$1{escapedText}$2");
                }
            }

            // Update Neutrals Task (descriptionNeutralsTask) if key exists and text is provided
            if (!string.IsNullOrEmpty(briefingKeys.NeutralsTaskKey) && neutralsTaskText != null)
            {
                string escapedText = EscapeLuaString(neutralsTaskText);
                var regex = new Regex(@"(\[""" + Regex.Escape(briefingKeys.NeutralsTaskKey) + @"""\]\s*=\s*"")(?:[^""\\]|\\.)*("")", RegexOptions.Singleline);
                if (regex.IsMatch(newDictionaryContent))
                {
                    newDictionaryContent = regex.Replace(newDictionaryContent, $"$1{escapedText}$2");
                }
            }

            // Update Sortie if key exists and text is provided
            if (!string.IsNullOrEmpty(briefingKeys.SortieKey) && sortieText != null)
            {
                string escapedText = EscapeLuaString(sortieText);
                var regex = new Regex(@"(\[""" + Regex.Escape(briefingKeys.SortieKey) + @"""\]\s*=\s*"")(?:[^""\\]|\\.)*("")", RegexOptions.Singleline);
                if (regex.IsMatch(newDictionaryContent))
                {
                    newDictionaryContent = regex.Replace(newDictionaryContent, $"$1{escapedText}$2");
                }
            }

            // Now update the archive with the new dictionary content
            // We need to copy to a temp file, then replace the original
            string tempPath = mizFilePath + ".tmp";
            
            try
            {
                using (var originalFs = new FileStream(mizFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var originalArchive = new ZipArchive(originalFs, ZipArchiveMode.Read))
                using (var newFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                using (var newArchive = new ZipArchive(newFs, ZipArchiveMode.Create))
                {
                    foreach (var entry in originalArchive.Entries)
                    {
                        var newEntry = newArchive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                        newEntry.LastWriteTime = entry.LastWriteTime;

                        using (var sourceStream = entry.Open())
                        using (var destStream = newEntry.Open())
                        {
                            if (entry.FullName == "l10n/DEFAULT/dictionary")
                            {
                                // Write the modified dictionary
                                using (var writer = new StreamWriter(destStream))
                                {
                                    await writer.WriteAsync(newDictionaryContent);
                                }
                            }
                            else
                            {
                                // Copy existing content
                                await sourceStream.CopyToAsync(destStream);
                            }
                        }
                    }
                }

                // Atomically replace original with temp file (preserves original as .bak on failure)
                string backup = mizFilePath + ".bak";
                File.Replace(tempPath, mizFilePath, backup, ignoreMetadataErrors: true);
                try { File.Delete(backup); } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MizParser.UpdateAllBriefingsAsync failed for {mizFilePath}: {ex.Message}");
                // Clean up temp file if it exists
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch (Exception cleanupEx) { Debug.WriteLine($"Cleanup failed: {cleanupEx.Message}"); }
                }
                throw;
            }
        }

        /// <summary>
        /// Escapes a string for Lua format (opposite of UnescapeLuaString)
        /// </summary>
        private static string EscapeLuaString(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Escape backslashes first, then quotes, then newlines
            string result = text.Replace("\\", "\\\\");
            result = result.Replace("\"", "\\\"");
            result = result.Replace("\r\n", "\\n");
            result = result.Replace("\n", "\\n");
            result = result.Replace("\r", "\\n");
            return result;
        }

        public static async Task<MissionDetails> ParseMissionAsync(string mizFilePath, bool loadImages = true)
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

                    details.Theatre = ExtractLuaField(missionFileContent, "theatre") ?? ExtractLuaField(missionFileContent, "map") ?? "Unknown";
                    details.Sortie = ExtractLuaField(missionFileContent, "sortie") ?? "";
                    details.RequiredModules = ExtractRequiredModules(missionFileContent);
                    details.Date = ExtractDate(missionFileContent);
                    details.StartTime = ExtractStartTime(missionFileContent);

                    // Extract all four briefing sections
                    var briefingKeys = ExtractAllBriefingKeys(missionFileContent);
                    if (!string.IsNullOrEmpty(dictionaryContent))
                    {
                        // Situation (descriptionText)
                        if (!string.IsNullOrEmpty(briefingKeys.SituationKey))
                        {
                            details.BriefingSituation = ExtractDictionaryValue(dictionaryContent, briefingKeys.SituationKey) ?? "";
                        }
                        if (string.IsNullOrEmpty(details.BriefingSituation)) details.BriefingSituation = "";
                        
                        // Red Tasks (descriptionRedTask)
                        if (!string.IsNullOrEmpty(briefingKeys.RedTaskKey))
                        {
                            details.BriefingRedTask = ExtractDictionaryValue(dictionaryContent, briefingKeys.RedTaskKey) ?? "";
                        }
                        if (string.IsNullOrEmpty(details.BriefingRedTask)) details.BriefingRedTask = "";
                        
                        // Blue Tasks (descriptionBlueTask)
                        if (!string.IsNullOrEmpty(briefingKeys.BlueTaskKey))
                        {
                            details.BriefingBlueTask = ExtractDictionaryValue(dictionaryContent, briefingKeys.BlueTaskKey) ?? "";
                        }
                        if (string.IsNullOrEmpty(details.BriefingBlueTask)) details.BriefingBlueTask = "";
                        
                        // Neutrals (descriptionNeutralsTask)
                        if (!string.IsNullOrEmpty(briefingKeys.NeutralsTaskKey))
                        {
                            details.BriefingNeutralsTask = ExtractDictionaryValue(dictionaryContent, briefingKeys.NeutralsTaskKey) ?? "";
                        }
                        if (string.IsNullOrEmpty(details.BriefingNeutralsTask)) details.BriefingNeutralsTask = "";
                    }
                    
                    // Backward compatibility: set Briefing to Situation
                    details.Briefing = details.BriefingSituation;
                    if (string.IsNullOrEmpty(details.Briefing)) details.Briefing = "No briefing available.";

                    if (!string.IsNullOrEmpty(details.Sortie) && details.Sortie.StartsWith("DictKey_"))
                    {
                        string? sortieText = ExtractDictionaryValue(dictionaryContent, details.Sortie);
                        if (!string.IsNullOrEmpty(sortieText)) 
                            details.Sortie = sortieText;
                        else
                            details.Sortie = "";
                    }
                    else if (string.IsNullOrEmpty(details.Sortie))
                    {
                        details.Sortie = "";
                    }

                    details.Weather = ExtractWeather(missionFileContent);
                    
                    // Extract ALL groups with the new robust method
                    details.AllGroups = ExtractAllGroupsRobust(missionFileContent, dictionaryContent);

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

                            var groups = details.AllGroups;
                            int unitCount = 0, wpCount = 0;
                            foreach (var g in groups) { unitCount += g.Units.Count; wpCount += g.Route.Count; }
                            details.DebugInfo = $"Found {groups.Count} groups, {unitCount} units, {wpCount} waypoints";

                    // Images from l10n/DEFAULT folder
                    string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };
                    if (loadImages)
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
                    if (loadImages)
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
        private static List<UnitGroup> ExtractAllGroupsRobust(string content, string? dictionaryContent = null)
        {
            var groups = new List<UnitGroup>();

            // The mission file structure has ["coalition"] containing ["blue"] and ["red"]
            // Each contains ["country"] array with group types

            // Step 1: Find the coalition block
            var coalitionBlockMatch = Regex.Match(content, @"\[""coalition""\]\s*=\s*\{", RegexOptions.Singleline);
            if (!coalitionBlockMatch.Success) return groups;

            string? coalitionBlock = ExtractBalancedBlock(content, coalitionBlockMatch.Index + coalitionBlockMatch.Length - 1);
            if (string.IsNullOrEmpty(coalitionBlock)) return groups;

            // Step 2: Process each side (blue, red)
            foreach (var side in new[] { "blue", "red" })
            {
                var sideMatch = Regex.Match(coalitionBlock, @"\[""" + side + @"""\]\s*=\s*\{", RegexOptions.Singleline);
                if (!sideMatch.Success) continue;

                string? sideBlock = ExtractBalancedBlock(coalitionBlock, sideMatch.Index + sideMatch.Length - 1);
                if (string.IsNullOrEmpty(sideBlock)) continue;

                // Step 3: Find country blocks within side
                var countryArrayMatch = Regex.Match(sideBlock, @"\[""country""\]\s*=\s*\{", RegexOptions.Singleline);
                if (!countryArrayMatch.Success) continue;

                string? countryArray = ExtractBalancedBlock(sideBlock, countryArrayMatch.Index + countryArrayMatch.Length - 1);
                if (string.IsNullOrEmpty(countryArray)) continue;

                // Step 4: Find each numbered country entry
                var countryEntries = Regex.Matches(countryArray, @"\[(\d+)\]\s*=\s*\{", RegexOptions.Singleline);
                foreach (Match ce in countryEntries)
                {
                    string? countryBlock = ExtractBalancedBlock(countryArray, ce.Index + ce.Length - 1);
                    if (string.IsNullOrEmpty(countryBlock)) continue;

                    // Get country name
                    // DCS Lua country blocks place ["name"] AFTER all nested group blocks,
                    // so finding it by ID is the most reliable approach.
                    string countryName = "Unknown";
                    
                    // Method 1: Use country ID (most reliable)
                    var countryIdMatch = Regex.Match(countryBlock, @"\[""id""\]\s*=\s*(\d+)");
                    if (countryIdMatch.Success)
                    {
                        countryName = GetCountryNameById(int.Parse(countryIdMatch.Groups[1].Value));
                    }
                    
                    // Method 2: Fallback - find the LAST non-DictKey ["name"] in the block
                    // (country name is typically the last ["name"] field, after all nested groups)
                    if (countryName == "Unknown")
                    {
                        var allNameMatches = Regex.Matches(countryBlock, @"\[""name""\]\s*=\s*""([^""]+)""");
                        for (int i = allNameMatches.Count - 1; i >= 0; i--)
                        {
                            string val = allNameMatches[i].Groups[1].Value;
                            if (!val.StartsWith("DictKey_"))
                            {
                                countryName = val;
                                break;
                            }
                        }
                    }

                    // Step 5: Process each group type in this country
                    foreach (var groupType in new[] { "plane", "helicopter", "vehicle", "ship", "static" })
                    {
                        var groupTypeMatch = Regex.Match(countryBlock, @"\[""" + groupType + @"""\]\s*=\s*\{", RegexOptions.Singleline);
                        if (!groupTypeMatch.Success) continue;

                        string? groupTypeBlock = ExtractBalancedBlock(countryBlock, groupTypeMatch.Index + groupTypeMatch.Length - 1);
                        if (string.IsNullOrEmpty(groupTypeBlock)) continue;

                        // Find "group" array
                        var groupArrayMatch = Regex.Match(groupTypeBlock, @"\[""group""\]\s*=\s*\{", RegexOptions.Singleline);
                        if (!groupArrayMatch.Success) continue;

                        string? groupArray = ExtractBalancedBlock(groupTypeBlock, groupArrayMatch.Index + groupArrayMatch.Length - 1);
                        if (string.IsNullOrEmpty(groupArray)) continue;

                        // Step 6: Parse each individual group
                        var groupEntries = Regex.Matches(groupArray, @"\[(\d+)\]\s*=\s*\{", RegexOptions.Singleline);
                        foreach (Match ge in groupEntries)
                        {
                            string? singleGroupBlock = ExtractBalancedBlock(groupArray, ge.Index + ge.Length - 1);
                            if (string.IsNullOrEmpty(singleGroupBlock)) continue;

                            var group = new UnitGroup
                            {
                                Coalition = side,
                                Country = countryName,
                                GroupType = groupType
                            };

                            // Group name
                            var nameMatch = Regex.Match(singleGroupBlock, @"\[""name""\]\s*=\s*""([^""]+)""");
                            if (nameMatch.Success)
                            {
                                group.GroupName = nameMatch.Groups[1].Value;
                                if (group.GroupName.StartsWith("DictKey_") && !string.IsNullOrEmpty(dictionaryContent))
                                {
                                    group.GroupName = ExtractDictionaryValue(dictionaryContent, group.GroupName) ?? group.GroupName;
                                }
                            }

                            // Task
                            var taskMatch = Regex.Match(singleGroupBlock, @"\[""task""\]\s*=\s*""([^""]+)""");
                            if (taskMatch.Success) group.Task = taskMatch.Groups[1].Value;

                            // Parse units
                            var unitsMatch = Regex.Match(singleGroupBlock, @"\[""units""\]\s*=\s*\{", RegexOptions.Singleline);
                            if (unitsMatch.Success)
                            {
                                string? unitsBlock = ExtractBalancedBlock(singleGroupBlock, unitsMatch.Index + unitsMatch.Length - 1);
                                if (!string.IsNullOrEmpty(unitsBlock))
                                {
                                    var unitEntries = Regex.Matches(unitsBlock, @"\[(\d+)\]\s*=\s*\{", RegexOptions.Singleline);
                                    foreach (Match ue in unitEntries)
                                    {
                                        string? unitBlock = ExtractBalancedBlock(unitsBlock, ue.Index + ue.Length - 1);
                                        if (!string.IsNullOrEmpty(unitBlock))
                                        {
                                            var unit = ParseUnit(unitBlock, dictionaryContent);
                                            if (unit != null) group.Units.Add(unit);
                                        }
                                    }
                                }
                            }

                            // Parse route
                            var routeMatch = Regex.Match(singleGroupBlock, @"\[""route""\]\s*=\s*\{", RegexOptions.Singleline);
                            if (routeMatch.Success)
                            {
                                string? routeBlock = ExtractBalancedBlock(singleGroupBlock, routeMatch.Index + routeMatch.Length - 1);
                                if (!string.IsNullOrEmpty(routeBlock))
                                {
                                    var pointsMatch = Regex.Match(routeBlock, @"\[""points""\]\s*=\s*\{", RegexOptions.Singleline);
                                    if (pointsMatch.Success)
                                    {
                                        string? pointsBlock = ExtractBalancedBlock(routeBlock, pointsMatch.Index + pointsMatch.Length - 1);
                                        if (!string.IsNullOrEmpty(pointsBlock))
                                        {
                                            var wpEntries = Regex.Matches(pointsBlock, @"\[(\d+)\]\s*=\s*\{", RegexOptions.Singleline);
                                            foreach (Match we in wpEntries)
                                            {
                                                string? wpBlock = ExtractBalancedBlock(pointsBlock, we.Index + we.Length - 1);
                                                if (!string.IsNullOrEmpty(wpBlock))
                                                {
                                                    var wp = ParseWaypoint(wpBlock, dictionaryContent);
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

        private static Unit? ParseUnit(string unitBlock, string? dictionaryContent = null)
        {
            var unit = new Unit();
            var nameMatch = Regex.Match(unitBlock, @"\[""name""\]\s*=\s*""([^""]+)""");
            if (nameMatch.Success)
            {
                unit.Name = nameMatch.Groups[1].Value;
                if (unit.Name.StartsWith("DictKey_") && !string.IsNullOrEmpty(dictionaryContent))
                {
                    unit.Name = ExtractDictionaryValue(dictionaryContent, unit.Name) ?? unit.Name;
                }
            }

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
                // If a unit is missing a skill string, the DCS engine defaults it to Average
                unit.Skill = "Average";
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

        private static Waypoint ParseWaypoint(string wpBlock, string? dictionaryContent = null)
        {
            var wp = new Waypoint();

            var nameMatch = Regex.Match(wpBlock, @"\[""name""\]\s*=\s*""([^""]+)""");
            if (nameMatch.Success)
            {
                wp.Name = nameMatch.Groups[1].Value;
                if (wp.Name.StartsWith("DictKey_") && !string.IsNullOrEmpty(dictionaryContent))
                {
                    wp.Name = ExtractDictionaryValue(dictionaryContent, wp.Name) ?? wp.Name;
                }
            }

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

        private static string? ExtractBalancedBlock(string content, int startIndex)
        {
            if (startIndex >= content.Length || content[startIndex] != '{') return null;

            int openBraces = 0;
            bool inString = false;
            bool escapeNext = false;

            for (int i = startIndex; i < content.Length; i++)
            {
                char c = content[i];

                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (c == '\\')
                {
                    escapeNext = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    if (c == '{') openBraces++;
                    else if (c == '}')
                    {
                        openBraces--;
                        if (openBraces == 0) return content.Substring(startIndex, i - startIndex + 1);
                    }
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

        private static string? ExtractLuaField(string content, string fieldName)
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
            // Find the root ["date"] block which is indented by 4 spaces or a single tab
            var dateBlockMatch = Regex.Match(content, @"^(?:    |\t)\[""date""\]\s*=", RegexOptions.Multiline);
            if (dateBlockMatch.Success)
            {
                // Find the opening brace after the match
                int braceStart = content.IndexOf('{', dateBlockMatch.Index);
                if (braceStart != -1)
                {
                    string? dateBlock = ExtractBalancedBlock(content, braceStart);
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
            }
            return "Unknown Date";
        }

        private static string ExtractStartTime(string content)
        {
            // Root keys in DCS mission files are typically indented with exactly 4 spaces or a single tab
            var match = Regex.Match(content, @"^(?:    |\t)\[""start_time""\]\s*=\s*([-\d\.eE+]+)", RegexOptions.Multiline);
            
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

        private static string? ExtractBriefingKey(string missionContent)
        {
            var regex = new Regex(@"\[""descriptionText""\]\s*=\s*""(DictKey_[^""]+)""");
            var match = regex.Match(missionContent);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static BriefingKeys ExtractAllBriefingKeys(string missionContent)
        {
            var keys = new BriefingKeys();
            
            // Extract Situation key (descriptionText)
            var situationRegex = new Regex(@"\[""descriptionText""\]\s*=\s*""(DictKey_[^""]+)""");
            var situationMatch = situationRegex.Match(missionContent);
            if (situationMatch.Success) keys.SituationKey = situationMatch.Groups[1].Value;
            
            // Extract Red Task key (descriptionRedTask)
            var redTaskRegex = new Regex(@"\[""descriptionRedTask""\]\s*=\s*""(DictKey_[^""]+)""");
            var redTaskMatch = redTaskRegex.Match(missionContent);
            if (redTaskMatch.Success) keys.RedTaskKey = redTaskMatch.Groups[1].Value;
            
            // Extract Blue Task key (descriptionBlueTask)
            var blueTaskRegex = new Regex(@"\[""descriptionBlueTask""\]\s*=\s*""(DictKey_[^""]+)""");
            var blueTaskMatch = blueTaskRegex.Match(missionContent);
            if (blueTaskMatch.Success) keys.BlueTaskKey = blueTaskMatch.Groups[1].Value;
            
            // Extract Neutrals Task key (descriptionNeutralsTask)
            var neutralsTaskRegex = new Regex(@"\[""descriptionNeutralsTask""\]\s*=\s*""(DictKey_[^""]+)""");
            var neutralsTaskMatch = neutralsTaskRegex.Match(missionContent);
            if (neutralsTaskMatch.Success) keys.NeutralsTaskKey = neutralsTaskMatch.Groups[1].Value;
            
            // Extract Sortie key
            var sortieRegex = new Regex(@"\[""sortie""\]\s*=\s*""(DictKey_[^""]+)""");
            var sortieMatch = sortieRegex.Match(missionContent);
            if (sortieMatch.Success) keys.SortieKey = sortieMatch.Groups[1].Value;
            
            return keys;
        }

        private static string? ExtractDictionaryValue(string? dictionaryContent, string? key)
        {
            if (string.IsNullOrEmpty(dictionaryContent) || string.IsNullOrEmpty(key)) return null;
            var regex = new Regex(@"\[""" + Regex.Escape(key) + @"""\]\s*=\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Singleline);
            var match = regex.Match(dictionaryContent);
            if (match.Success) return UnescapeLuaString(match.Groups[1].Value);
            return null;
        }

        private static string UnescapeLuaString(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string result = Regex.Replace(text, @"\\(?=[\r\n])", "");
            result = result.Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\\"", "\"");
            return result;
        }
        private static string GetCountryNameById(int id)
        {
            // DCS World country IDs
            var countries = new Dictionary<int, string>
            {
                {0, "Russia"}, {1, "Ukraine"}, {2, "USA"}, {3, "Turkey"}, {4, "UK"},
                {5, "France"}, {6, "Germany"}, {7, "USAF Aggressors"}, {8, "Canada"},
                {9, "Spain"}, {10, "The Netherlands"}, {11, "Belgium"}, {12, "Norway"},
                {13, "Denmark"}, {14, "Not used 1"}, {15, "Israel"}, {16, "Georgia"},
                {17, "Insurgents"}, {18, "Abkhazia"}, {19, "South Ossetia"},
                {20, "Italy"}, {21, "Australia"}, {22, "Switzerland"}, {23, "Austria"},
                {24, "Belarus"}, {25, "Bulgaria"}, {26, "Czech Republic"}, {27, "China"},
                {28, "Croatia"}, {29, "Egypt"}, {30, "Finland"}, {31, "Greece"},
                {32, "Hungary"}, {33, "India"}, {34, "Iran"}, {35, "Iraq"},
                {36, "Japan"}, {37, "Kazakhstan"}, {38, "North Korea"}, {39, "Pakistan"},
                {40, "Poland"}, {41, "Romania"}, {42, "Saudi Arabia"}, {43, "Serbia"},
                {44, "Slovakia"}, {45, "South Korea"}, {46, "Sweden"}, {47, "Syria"},
                {48, "Yemen"}, {49, "Vietnam"}, {50, "Venezuela"}, {51, "Tunisia"},
                {52, "Thailand"}, {53, "Sudan"}, {54, "Philippines"}, {55, "Morocco"},
                {56, "Mexico"}, {57, "Malaysia"}, {58, "Libya"}, {59, "Jordan"},
                {60, "Indonesia"}, {61, "Honduras"}, {62, "Ethiopia"}, {63, "Chile"},
                {64, "Brazil"}, {65, "Bahrain"}, {66, "Third Reich"}, {67, "Yugoslavia"},
                {68, "USSR"}, {69, "Italian Social Republic"}, {70, "Algeria"},
                {71, "Kuwait"}, {72, "Qatar"}, {73, "Oman"}, {74, "United Arab Emirates"},
                {75, "South Africa"}, {76, "Cuba"}, {77, "Portugal"}, {78, "GDR"},
                {79, "Lebanon"}, {80, "CJTF Blue"}, {81, "CJTF Red"},
                {82, "UN Peacekeepers"}, {83, "Argentina"}, {84, "Cyprus"},
                {85, "Slovenia"}
            };
            return countries.TryGetValue(id, out var name) ? name : $"Country {id}";
        }
    }
}