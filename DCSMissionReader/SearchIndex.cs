using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DCSMissionReader
{
    /// <summary>
    /// Persistent full-text mission index (SQLite + FTS5) with DCS-aware tagging,
    /// synonym expansion, typo correction and "more like this" similarity search.
    /// </summary>
    public static class SearchIndex
    {
        private static readonly object WriteLock = new();
        private static readonly object VocabLock = new();
        private static volatile bool _vocabDirty = true;
        private static List<(string Term, long Docs)>? _vocabCache;
        private static long _docCountCache;

        private static string DbPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DCSMissionReader");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "missionindex.db");
            }
        }

        private static SqliteConnection Open()
        {
            var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString());
            conn.Open();
            Exec(conn, "PRAGMA journal_mode=WAL;");
            Exec(conn, "PRAGMA synchronous=NORMAL;");
            EnsureSchema(conn);
            return conn;
        }

        private static void EnsureSchema(SqliteConnection conn)
        {
            Exec(conn, @"
CREATE TABLE IF NOT EXISTS missions(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  path TEXT UNIQUE NOT NULL,
  file_size INTEGER NOT NULL,
  file_mtime INTEGER NOT NULL,
  file_name TEXT NOT NULL,
  theatre TEXT NOT NULL DEFAULT '',
  main_unit TEXT NOT NULL DEFAULT '',
  player_side TEXT NOT NULL DEFAULT '',
  mission_date TEXT NOT NULL DEFAULT '',
  start_time TEXT NOT NULL DEFAULT '',
  time_of_day TEXT NOT NULL DEFAULT '',
  sortie TEXT NOT NULL DEFAULT '',
  briefing TEXT NOT NULL DEFAULT '',
  units_text TEXT NOT NULL DEFAULT '',
  tasks_text TEXT NOT NULL DEFAULT '',
  tags TEXT NOT NULL DEFAULT '',
  player_aircraft TEXT NOT NULL DEFAULT '',
  countries TEXT NOT NULL DEFAULT '',
  slot_count INTEGER NOT NULL DEFAULT 0
);");
            Exec(conn, @"
CREATE VIRTUAL TABLE IF NOT EXISTS missions_fts USING fts5(
  name, sortie, briefing, units, tasks, tags, theatre,
  tokenize='unicode61 remove_diacritics 2');");
            Exec(conn, "CREATE VIRTUAL TABLE IF NOT EXISTS missions_vocab USING fts5vocab('missions_fts','row');");
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // ==================================================================
        // Fast startup cache (theatre + main unit for the mission list)
        // ==================================================================

        public static Dictionary<string, (long Size, long MTime, string Theatre, string MainUnit)> GetBriefCache()
        {
            var result = new Dictionary<string, (long, long, string, string)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT path, file_size, file_mtime, theatre, main_unit FROM missions";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    result[r.GetString(0)] = (r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4));
            }
            catch (Exception ex) { Debug.WriteLine($"SearchIndex.GetBriefCache failed: {ex.Message}"); }
            return result;
        }

        // ==================================================================
        // Indexing
        // ==================================================================

        public static async Task UpdateIndexAsync(IReadOnlyList<string> files, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
        {
            using var conn = Open();

            // Current state of files on disk
            var current = new Dictionary<string, (long Size, long MTime)>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Exists) current[f] = (fi.Length, fi.LastWriteTimeUtc.Ticks);
                }
                catch (Exception ex) { Debug.WriteLine($"SearchIndex.UpdateIndexAsync file enum failed: {ex.Message}"); }
            }

            // Existing rows
            var existing = new Dictionary<string, (long Id, long Size, long MTime)>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, path, file_size, file_mtime FROM missions";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    existing[r.GetString(1)] = (r.GetInt64(0), r.GetInt64(2), r.GetInt64(3));
            }

            // Remove rows for files that vanished from the watched folder tree.
            // (Only rows whose folder is under one of the current roots would be safer,
            //  but stale rows are harmless for search and get re-added when seen again,
            //  so we only delete entries whose file no longer exists on disk.)
            foreach (var kv in existing)
            {
                if (!current.ContainsKey(kv.Key) && !File.Exists(kv.Key))
                {
                    lock (WriteLock)
                    {
                        using (var d1 = conn.CreateCommand()) { d1.CommandText = "DELETE FROM missions_fts WHERE rowid=@id"; d1.Parameters.AddWithValue("@id", kv.Value.Id); d1.ExecuteNonQuery(); }
                        using (var d2 = conn.CreateCommand()) { d2.CommandText = "DELETE FROM missions WHERE id=@id"; d2.Parameters.AddWithValue("@id", kv.Value.Id); d2.ExecuteNonQuery(); }
                    }
                }
            }

            var toIndex = current
                .Where(kv => !existing.TryGetValue(kv.Key, out var ex) || ex.Size != kv.Value.Size || ex.MTime != kv.Value.MTime)
                .Select(kv => (Path: kv.Key, kv.Value.Size, kv.Value.MTime))
                .ToList();

            int total = toIndex.Count;
            int done = 0;
            progress?.Report((0, total));
            if (total == 0) { _vocabDirty = true; return; }

            using var sem = new SemaphoreSlim(Math.Max(Environment.ProcessorCount, 4));
            var tasks = toIndex.Select(async item =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var details = await MizParser.ParseMissionAsync(item.Path, loadImages: false);
                    var rec = BuildRecord(item.Path, item.Size, item.MTime, details);
                    ct.ThrowIfCancellationRequested();
                    lock (WriteLock) { WriteRecord(conn, rec); }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Debug.WriteLine($"SearchIndex.UpdateIndexAsync indexing failed: {ex.Message}"); /* unreadable/corrupt .miz - skip */ }
                finally
                {
                    sem.Release();
                    progress?.Report((Interlocked.Increment(ref done), total));
                }
            }).ToList();

            await Task.WhenAll(tasks);
            _vocabDirty = true;
        }

        private class MissionRecord
        {
            public string Path = ""; public long Size; public long MTime;
            public string FileName = ""; public string Theatre = ""; public string MainUnit = "";
            public string PlayerSide = ""; public string MissionDate = ""; public string StartTime = "";
            public string TimeOfDay = ""; public string Sortie = ""; public string Briefing = "";
            public string UnitsText = ""; public string TasksText = ""; public string Tags = "";
            public string PlayerAircraft = ""; public string Countries = ""; public int SlotCount;
        }

        private static MissionRecord BuildRecord(string path, long size, long mtime, MissionDetails d)
        {
            var rec = new MissionRecord
            {
                Path = path,
                Size = size,
                MTime = mtime,
                FileName = System.IO.Path.GetFileNameWithoutExtension(path),
                Theatre = d.Theatre ?? "",
                MissionDate = d.Date ?? "",
                StartTime = d.StartTime ?? "",
                Sortie = d.Sortie ?? ""
            };

            rec.TimeOfDay = ComputeTimeOfDay(rec.StartTime);

            // Briefing text (all sections)
            rec.Briefing = string.Join("\n", new[]
            {
                d.BriefingSituation, d.BriefingBlueTask, d.BriefingRedTask, d.BriefingNeutralsTask
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var groups = d.AllGroups ?? new List<UnitGroup>();

            // Player groups: aircraft/helicopter groups containing a Client/Player unit
            var playerGroups = groups
                .Where(g => (g.GroupType == "plane" || g.GroupType == "helicopter") && g.Units.Any(u => u.IsPlayer))
                .ToList();

            var playerTypes = playerGroups.SelectMany(g => g.Units.Where(u => u.IsPlayer).Select(u => u.Type))
                                          .Where(t => !string.IsNullOrEmpty(t)).ToList();
            rec.SlotCount = playerGroups.Sum(g => g.Units.Count(u => u.IsPlayer));
            rec.MainUnit = playerTypes.Count == 0
                ? "Various"
                : playerTypes.GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
            rec.PlayerAircraft = playerTypes.Count == 0 ? "" : "|" + string.Join("|", playerTypes.Distinct()) + "|";
            rec.PlayerSide = playerGroups.Select(g => g.Coalition).FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? "";

            // Units text: types + collapsed forms + friendly nicknames + names/callsigns
            var unitWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var taskWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                if (!string.IsNullOrEmpty(g.Country)) countries.Add(g.Country);
                if (!string.IsNullOrEmpty(g.Task)) taskWords.Add(g.Task);
                if (!string.IsNullOrEmpty(g.GroupName)) unitWords.Add(g.GroupName);
                foreach (var u in g.Units)
                {
                    if (string.IsNullOrEmpty(u.Type)) continue;
                    unitWords.Add(u.Type);
                    string collapsed = DcsSynonyms.Collapse(u.Type);
                    if (collapsed.Length >= 2) unitWords.Add(collapsed);
                    if (DcsSynonyms.FriendlyTokens.TryGetValue(u.Type, out var friendly)) unitWords.Add(friendly);
                    if (!string.IsNullOrEmpty(u.CallSign)) unitWords.Add(u.CallSign);
                    if (!string.IsNullOrEmpty(u.Name)) unitWords.Add(u.Name);
                }
            }
            rec.UnitsText = string.Join(" ", unitWords);
            rec.TasksText = string.Join(" ", taskWords);
            rec.Countries = string.Join("|", countries);

            rec.Tags = string.Join("|", InferTags(d, groups, playerGroups, rec));
            return rec;
        }

        private static string ComputeTimeOfDay(string startTime)
        {
            // startTime format "HH:MM:SS" (may exceed 24h for multi-day starts)
            var parts = startTime.Split(':');
            if (parts.Length > 0 && int.TryParse(parts[0], out int h))
            {
                h %= 24;
                if (h < 5 || h >= 21) return "NIGHT";
                if (h < 7) return "DAWN";
                if (h < 18) return "DAY";
                return "DUSK";
            }
            return "";
        }

        private static List<string> InferTags(MissionDetails d, List<UnitGroup> groups, List<UnitGroup> playerGroups, MissionRecord rec)
        {
            var tags = new List<string>();
            void Add(string t) { if (!tags.Contains(t)) tags.Add(t); }

            // Mission type from tasks: player groups first, otherwise all air groups
            var taskSource = playerGroups.Count > 0
                ? playerGroups
                : groups.Where(g => g.GroupType == "plane" || g.GroupType == "helicopter").ToList();
            foreach (var g in taskSource)
            {
                if (string.IsNullOrEmpty(g.Task)) continue;
                if (DcsSynonyms.TaskToTags.TryGetValue(g.Task, out var mapped))
                    foreach (var t in mapped) Add(t);
            }

            // Asset presence scans (all units, both sides)
            bool tanker = false, awacs = false, carrier = false, sam = false, aaa = false;
            foreach (var g in groups)
            {
                foreach (var u in g.Units)
                {
                    string t = (u.Type ?? "").ToLowerInvariant();
                    if (t.Length == 0) continue;
                    if (!tanker && DcsSynonyms.TankerKeywords.Any(k => t.Contains(k))) tanker = true;
                    if (!awacs && DcsSynonyms.AwacsKeywords.Any(k => t.Contains(k))) awacs = true;
                    if (!carrier && g.GroupType == "ship" && DcsSynonyms.CarrierKeywords.Any(k => t.Contains(k))) carrier = true;
                    if (!sam && g.GroupType == "vehicle" && DcsSynonyms.SamKeywords.Any(k => t.Contains(k))) sam = true;
                    if (!aaa && g.GroupType == "vehicle" && DcsSynonyms.AaaKeywords.Any(k => t.Contains(k))) aaa = true;
                }
            }
            if (tanker) Add("AAR");
            if (awacs) Add("AWACS");
            if (carrier) Add("CARRIER");
            if (sam) Add("SAM");
            if (aaa) Add("AAA");

            if (playerGroups.Any(g => g.GroupType == "helicopter")) Add("HELO");
            if (rec.SlotCount > 1) Add("COOP");

            // CSAR from briefing keywords (no dedicated DCS task exists)
            string brief = DcsSynonyms.Normalize(rec.Briefing + " " + rec.Sortie + " " + rec.FileName);
            if (brief.Contains("csar") || brief.Contains("downed pilot") || brief.Contains("combat search"))
                Add("CSAR");

            // Era from mission date
            if (rec.MissionDate.Length >= 4 && int.TryParse(rec.MissionDate.AsSpan(0, 4), out int year))
            {
                if (year < 1950) Add("WWII");
                else if (year <= 1990) Add("COLDWAR");
                else Add("MODERN");
            }

            if (!string.IsNullOrEmpty(rec.TimeOfDay)) Add(rec.TimeOfDay);
            return tags;
        }

        private static void WriteRecord(SqliteConnection conn, MissionRecord rec)
        {
            using var tx = conn.BeginTransaction();

            long? oldId = null;
            using (var find = conn.CreateCommand())
            {
                find.Transaction = tx;
                find.CommandText = "SELECT id FROM missions WHERE path=@p";
                find.Parameters.AddWithValue("@p", rec.Path);
                var v = find.ExecuteScalar();
                if (v != null && v != DBNull.Value) oldId = (long)v;
            }
            if (oldId.HasValue)
            {
                using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = "DELETE FROM missions_fts WHERE rowid=@id; DELETE FROM missions WHERE id=@id;";
                del.Parameters.AddWithValue("@id", oldId.Value);
                del.ExecuteNonQuery();
            }

            long newId;
            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = @"
INSERT INTO missions(path,file_size,file_mtime,file_name,theatre,main_unit,player_side,mission_date,start_time,
                     time_of_day,sortie,briefing,units_text,tasks_text,tags,player_aircraft,countries,slot_count)
VALUES(@path,@size,@mtime,@name,@theatre,@main,@side,@date,@stime,@tod,@sortie,@brief,@units,@tasks,@tags,@pac,@countries,@slots);
SELECT last_insert_rowid();";
                ins.Parameters.AddWithValue("@path", rec.Path);
                ins.Parameters.AddWithValue("@size", rec.Size);
                ins.Parameters.AddWithValue("@mtime", rec.MTime);
                ins.Parameters.AddWithValue("@name", rec.FileName);
                ins.Parameters.AddWithValue("@theatre", rec.Theatre);
                ins.Parameters.AddWithValue("@main", rec.MainUnit);
                ins.Parameters.AddWithValue("@side", rec.PlayerSide);
                ins.Parameters.AddWithValue("@date", rec.MissionDate);
                ins.Parameters.AddWithValue("@stime", rec.StartTime);
                ins.Parameters.AddWithValue("@tod", rec.TimeOfDay);
                ins.Parameters.AddWithValue("@sortie", rec.Sortie);
                ins.Parameters.AddWithValue("@brief", rec.Briefing);
                ins.Parameters.AddWithValue("@units", rec.UnitsText);
                ins.Parameters.AddWithValue("@tasks", rec.TasksText);
                ins.Parameters.AddWithValue("@tags", rec.Tags);
                ins.Parameters.AddWithValue("@pac", rec.PlayerAircraft);
                ins.Parameters.AddWithValue("@countries", rec.Countries);
                ins.Parameters.AddWithValue("@slots", rec.SlotCount);
                newId = (long)ins.ExecuteScalar()!;
            }

            using (var fts = conn.CreateCommand())
            {
                fts.Transaction = tx;
                fts.CommandText = @"
INSERT INTO missions_fts(rowid,name,sortie,briefing,units,tasks,tags,theatre)
VALUES(@id,@name,@sortie,@brief,@units,@tasks,@tags,@theatre);";
                fts.Parameters.AddWithValue("@id", newId);
                fts.Parameters.AddWithValue("@name", rec.FileName.Replace('_', ' ').Replace('-', ' '));
                fts.Parameters.AddWithValue("@sortie", rec.Sortie);
                fts.Parameters.AddWithValue("@brief", rec.Briefing);
                fts.Parameters.AddWithValue("@units", rec.UnitsText);
                fts.Parameters.AddWithValue("@tasks", rec.TasksText);
                fts.Parameters.AddWithValue("@tags", rec.Tags.Replace('|', ' '));
                fts.Parameters.AddWithValue("@theatre", rec.Theatre);
                fts.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // ==================================================================
        // Facets
        // ==================================================================

        // ==================================================================
        // Search
        // ==================================================================

        private class ParsedQuery
        {
            public List<List<string>> FreeGroups = new();   // each group = OR alternatives
            public List<string> Maps = new();
            public List<string> Planes = new();
            public List<string> Tags = new();
            public string? Side;
            public string? Country;
            public string? Time;
            public string? LikeFile;
            public List<string> ExcludeMaps = new();
            public List<string> ExcludePlanes = new();
            public List<string> ExcludeTags = new();
            public List<string> ExcludeFreeText = new();
        }

        public static List<SearchResult> Search(SearchRequest req)
        {
            try
            {
                using var conn = Open();
                var pq = ParseQuery(req.Text);

                if (!string.IsNullOrEmpty(req.TagFilter)) pq.Tags.Add(req.TagFilter!);
                if (!string.IsNullOrEmpty(req.AircraftFilter)) pq.Planes.Add(req.AircraftFilter!);
                if (!string.IsNullOrEmpty(req.TimeFilter)) pq.Time = req.TimeFilter;

                List<Row> rows;
                if (!string.IsNullOrEmpty(pq.LikeFile))
                {
                    rows = MoreLikeThis(conn, pq.LikeFile!);
                }
                else if (pq.FreeGroups.Count > 0 || pq.ExcludeFreeText.Count > 0)
                {
                    string BuildWithExclude(List<List<string>> groups, bool and)
                    {
                        string match = BuildMatch(groups, and);
                        foreach (var ex in pq.ExcludeFreeText)
                            match += " AND NOT " + ex;
                        return match;
                    }

                    if (pq.FreeGroups.Count > 0)
                    {
                        rows = FtsSearch(conn, BuildWithExclude(pq.FreeGroups, true));
                        if (rows.Count == 0)
                            rows = FtsSearch(conn, BuildWithExclude(pq.FreeGroups, false));
                        if (rows.Count == 0)
                        {
                            var corrected = FuzzyCorrect(conn, pq.FreeGroups);
                            if (corrected != null)
                            {
                                rows = FtsSearch(conn, BuildWithExclude(corrected, true));
                                if (rows.Count == 0)
                                    rows = FtsSearch(conn, BuildWithExclude(corrected, false));
                            }
                        }
                    }
                    else
                    {
                        // Only exclusion terms, no positive FTS terms: start from all rows, subtract FTS matches
                        rows = AllRows(conn);
                        var excludePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ex in pq.ExcludeFreeText)
                            foreach (var r in FtsSearch(conn, ex))
                                excludePaths.Add(r.Path);
                        rows = rows.Where(r => !excludePaths.Contains(r.Path)).ToList();
                    }
                }
                else
                {
                    rows = AllRows(conn);
                }

                // Structured filters applied in memory (row counts are small)
                IEnumerable<Row> filtered = rows;
                if (req.LimitToPaths != null)
                    filtered = filtered.Where(r => req.LimitToPaths.Contains(r.Path));
                foreach (var m in pq.Maps)
                    filtered = filtered.Where(r => DcsSynonyms.TheatreMatches(m, r.Theatre));
                foreach (var p in pq.Planes)
                    filtered = filtered.Where(r => DcsSynonyms.AircraftMatches(p, r.PlayerAircraft));
                foreach (var t in pq.Tags)
                {
                    string tag = DcsSynonyms.CanonicalTag(t);
                    filtered = filtered.Where(r => ("|" + r.Tags + "|").Contains("|" + tag + "|", StringComparison.OrdinalIgnoreCase));
                }
                if (!string.IsNullOrEmpty(pq.Side))
                    filtered = filtered.Where(r => r.PlayerSide.Equals(pq.Side, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(pq.Country))
                    filtered = filtered.Where(r => DcsSynonyms.Collapse(r.Countries).Contains(DcsSynonyms.Collapse(pq.Country!)));
                if (!string.IsNullOrEmpty(pq.Time))
                    filtered = filtered.Where(r => r.TimeOfDay.Equals(pq.Time, StringComparison.OrdinalIgnoreCase));
                foreach (var m in pq.ExcludeMaps)
                    filtered = filtered.Where(r => !DcsSynonyms.TheatreMatches(m, r.Theatre));
                foreach (var p in pq.ExcludePlanes)
                    filtered = filtered.Where(r => !DcsSynonyms.AircraftMatches(p, r.PlayerAircraft));
                foreach (var t in pq.ExcludeTags)
                    filtered = filtered.Where(r => !("|" + r.Tags + "|").Contains("|" + t + "|", StringComparison.OrdinalIgnoreCase));

                return filtered
                    .OrderByDescending(r => r.Score)
                    .Select(r => new SearchResult
                    {
                        Path = r.Path,
                        Score = r.Score,
                        Tags = r.Tags.Replace("|", " · "),
                        Snippet = CleanSnippet(r.Snippet)
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SearchIndex.Search failed: {ex.Message}");
                return new List<SearchResult>();
            }
        }

        private static string CleanSnippet(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ").Trim();
        }

        private class Row
        {
            public string Path = ""; public string Theatre = ""; public string Tags = "";
            public string PlayerAircraft = ""; public string PlayerSide = ""; public string TimeOfDay = "";
            public string Countries = ""; public string Snippet = ""; public double Score;
        }

        private static List<Row> ReadRows(SqliteCommand cmd)
        {
            var rows = new List<Row>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new Row
                {
                    Path = r.GetString(0),
                    Theatre = r.GetString(1),
                    Tags = r.GetString(2),
                    PlayerAircraft = r.GetString(3),
                    PlayerSide = r.GetString(4),
                    TimeOfDay = r.GetString(5),
                    Countries = r.GetString(6),
                    Snippet = r.IsDBNull(7) ? "" : r.GetString(7),
                    Score = r.IsDBNull(8) ? 0 : -r.GetDouble(8)   // bm25: lower = better, flip sign
                });
            }
            return rows;
        }

        private static List<Row> FtsSearch(SqliteConnection conn, string match)
        {
            if (string.IsNullOrWhiteSpace(match)) return new List<Row>();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT m.path, m.theatre, m.tags, m.player_aircraft, m.player_side, m.time_of_day, m.countries,
       snippet(missions_fts, -1, '«', '»', ' … ', 10),
       bm25(missions_fts, 10.0, 8.0, 4.0, 6.0, 8.0, 12.0, 5.0)
FROM missions_fts JOIN missions m ON m.id = missions_fts.rowid
WHERE missions_fts MATCH @q
LIMIT 500;";
                cmd.Parameters.AddWithValue("@q", match);
                return ReadRows(cmd);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SearchIndex.FtsSearch failed: {ex.Message}");
                return new List<Row>(); // malformed FTS query -> no results, caller falls back
            }
        }

        private static List<Row> AllRows(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT path, theatre, tags, player_aircraft, player_side, time_of_day, countries, '', 0.0
FROM missions;";
            return ReadRows(cmd);
        }

        // ------------------------------------------------------------------
        // Query parsing
        // ------------------------------------------------------------------

        private static readonly HashSet<string> FilterKeys = new(StringComparer.Ordinal)
        {
            "map", "theatre", "theater", "plane", "aircraft", "ac", "unit",
            "type", "task", "tag", "side", "coalition", "country", "time", "like"
        };

        private static ParsedQuery ParseQuery(string text)
        {
            var pq = new ParsedQuery();
            if (string.IsNullOrWhiteSpace(text)) return pq;

            string q = DcsSynonyms.FoldPhrases(DcsSynonyms.Normalize(text));

            // Extract chunks: key:"value" | key:value | "phrase" | word
            var matches = System.Text.RegularExpressions.Regex.Matches(q,
                "([a-zθ-ω]+):\"([^\"]+)\"|([a-zθ-ω]+):(\\S+)|\"([^\"]+)\"|(\\S+)");

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (m.Groups[1].Success || m.Groups[3].Success)
                {
                    string key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value;
                    string val = m.Groups[1].Success ? m.Groups[2].Value : m.Groups[4].Value;
                    if (FilterKeys.Contains(key))
                    {
                        switch (key)
                        {
                            case "map": case "theatre": case "theater": pq.Maps.Add(val); break;
                            case "plane": case "aircraft": case "ac": case "unit": pq.Planes.Add(val); break;
                            case "type": case "task": case "tag": pq.Tags.Add(val); break;
                            case "side": case "coalition": pq.Side = val; break;
                            case "country": pq.Country = val; break;
                            case "time": pq.Time = val.ToUpperInvariant(); break;
                            case "like": pq.LikeFile = val; break;
                        }
                        continue;
                    }
                    // Unknown key: treat whole chunk as free text
                    AddFreeChunk(pq, key + " " + val);
                }
                else if (m.Groups[5].Success)
                {
                    // Quoted phrase: exact match, no expansion
                    string phrase = m.Groups[5].Value.Trim();
                    if (phrase.Length > 0)
                        pq.FreeGroups.Add(new List<string> { phrase });
                }
                else if (m.Groups[6].Success)
                {
                    string raw = m.Groups[6].Value;
                    bool exclude = raw[0] == '-' || raw[0] == '!';
                    string word = exclude ? raw.Substring(1) : raw;
                    if (word.Length == 0) continue;
                    string collapsed = DcsSynonyms.Collapse(word);
                    if (collapsed.Length > 0 && !DcsSynonyms.StopWords.Contains(collapsed))
                    {
                        if (DcsSynonyms.AirportTokens.Contains(collapsed) || (collapsed.Length > 2 && DcsSynonyms.AirportTokens.Any(t => collapsed.StartsWith(t))))
                            (exclude ? pq.ExcludePlanes : pq.Planes).Add(word);
                        else if (DcsSynonyms.MapTokens.Contains(collapsed))
                            (exclude ? pq.ExcludeMaps : pq.Maps).Add(word);
                        else if (DcsSynonyms.MissionTypeTokens.Contains(collapsed))
                            (exclude ? pq.ExcludeTags : pq.Tags).Add(DcsSynonyms.CanonicalTag(word));
                        else if (DcsSynonyms.TimeTokens.Contains(collapsed))
                        {
                            if (exclude) pq.ExcludeFreeText.Add(word);
                            else pq.Time = word.ToUpperInvariant();
                        }
                        else if (DcsSynonyms.SideTokens.Contains(collapsed))
                        {
                            if (exclude) pq.ExcludeFreeText.Add(word);
                            else pq.Side = word;
                        }
                        else
                        {
                            if (exclude) pq.ExcludeFreeText.Add(word);
                            else AddFreeChunk(pq, word);
                        }
                    }
                }
            }
            return pq;
        }

        private static void AddFreeChunk(ParsedQuery pq, string chunk)
        {
            string collapsed = DcsSynonyms.Collapse(chunk);
            if (collapsed.Length == 0) return;
            if (DcsSynonyms.StopWords.Contains(collapsed)) return;

            var alts = new List<string>();
            if (DcsSynonyms.QueryAliases.TryGetValue(collapsed, out var aliasAlts))
            {
                alts.AddRange(aliasAlts);
            }
            else
            {
                alts.Add(collapsed);
                // "f-16" also as token phrase "f 16" so it can match hyphen-split indexing
                var tokens = DcsSynonyms.Tokenize(chunk);
                if (tokens.Count > 1)
                    alts.Add(string.Join(" ", tokens));
            }
            pq.FreeGroups.Add(alts.Distinct().ToList());
        }

        private static string BuildMatch(List<List<string>> groups, bool and)
        {
            var parts = new List<string>();
            foreach (var group in groups)
            {
                var alts = new List<string>();
                foreach (var alt in group)
                {
                    string a = alt.Trim();
                    if (a.Length == 0) continue;
                    if (a.Contains(' '))
                    {
                        // Phrase: sanitize each token
                        var toks = DcsSynonyms.Tokenize(a);
                        if (toks.Count > 0) alts.Add("\"" + string.Join(" ", toks) + "\"");
                    }
                    else
                    {
                        string tok = DcsSynonyms.Collapse(a);
                        if (tok.Length == 0) continue;
                        alts.Add(tok.Length >= 3 ? tok + "*" : tok);
                    }
                }
                if (alts.Count == 1) parts.Add(alts[0]);
                else if (alts.Count > 1) parts.Add("(" + string.Join(" OR ", alts) + ")");
            }
            return string.Join(and ? " AND " : " OR ", parts);
        }

        // ------------------------------------------------------------------
        // Typo correction against the indexed vocabulary
        // ------------------------------------------------------------------

        private static void EnsureVocab(SqliteConnection conn)
        {
            if (!_vocabDirty && _vocabCache != null) return;
            lock (VocabLock)
            {
                if (!_vocabDirty && _vocabCache != null) return;
                var vocab = new List<(string, long)>();
                try
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT term, doc FROM missions_vocab WHERE length(term) >= 3";
                        using var r = cmd.ExecuteReader();
                        while (r.Read()) vocab.Add((r.GetString(0), r.GetInt64(1)));
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM missions";
                        _docCountCache = (long)cmd.ExecuteScalar()!;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"SearchIndex.EnsureVocab failed: {ex.Message}"); }
                _vocabCache = vocab;
                _vocabDirty = false;
            }
        }

        private static List<List<string>>? FuzzyCorrect(SqliteConnection conn, List<List<string>> groups)
        {
            EnsureVocab(conn);
            if (_vocabCache == null || _vocabCache.Count == 0) return null;

            bool changed = false;
            var result = new List<List<string>>();
            foreach (var group in groups)
            {
                // Only correct simple single-token groups without alias expansion
                if (group.Count == 1 && !group[0].Contains(' ') && group[0].Length >= 4)
                {
                    string token = DcsSynonyms.Collapse(group[0]);
                    int maxDist = token.Length <= 5 ? 1 : 2;
                    string? best = null; long bestDocs = -1; int bestDist = maxDist + 1;
                    foreach (var (term, docs) in _vocabCache)
                    {
                        int dist = DcsSynonyms.EditDistance(token, term, maxDist);
                        if (dist < bestDist || (dist == bestDist && docs > bestDocs))
                        {
                            if (dist <= maxDist) { best = term; bestDocs = docs; bestDist = dist; }
                        }
                    }
                    if (best != null && best != token)
                    {
                        result.Add(new List<string> { best });
                        changed = true;
                        continue;
                    }
                }
                result.Add(group);
            }
            return changed ? result : null;
        }

        // ------------------------------------------------------------------
        // "More like this" (TF-IDF over the mission's own indexed text)
        // ------------------------------------------------------------------

        private static List<Row> MoreLikeThis(SqliteConnection conn, string fileName)
        {
            string target = DcsSynonyms.Collapse(System.IO.Path.GetFileNameWithoutExtension(fileName));

            long id = -1; string text = "";
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, file_name, sortie, briefing, units_text, tasks_text, tags FROM missions";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (DcsSynonyms.Collapse(r.GetString(1)) == target)
                    {
                        id = r.GetInt64(0);
                        text = string.Join(" ", r.GetString(1), r.GetString(2), r.GetString(3),
                                                r.GetString(4), r.GetString(5), r.GetString(6).Replace('|', ' '));
                        break;
                    }
                }
            }
            if (id < 0) return new List<Row>();

            EnsureVocab(conn);
            var df = (_vocabCache ?? new List<(string, long)>()).ToDictionary(v => v.Item1, v => v.Item2, StringComparer.Ordinal);
            long n = Math.Max(_docCountCache, 1);

            var tf = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var tok in DcsSynonyms.Tokenize(text))
            {
                if (tok.Length < 3 || DcsSynonyms.StopWords.Contains(tok)) continue;
                if (tok.All(char.IsDigit)) continue;
                tf[tok] = tf.GetValueOrDefault(tok) + 1;
            }

            var top = tf
                .Select(kv =>
                {
                    long docs = df.GetValueOrDefault(kv.Key, 0);
                    if (docs == 0 || docs > n / 2) return (Term: kv.Key, Weight: 0.0); // ubiquitous or unindexed
                    return (Term: kv.Key, Weight: kv.Value * Math.Log((double)n / (1 + docs)));
                })
                .Where(t => t.Weight > 0)
                .OrderByDescending(t => t.Weight)
                .Take(12)
                .Select(t => t.Term)
                .ToList();

            if (top.Count == 0) return new List<Row>();

            using var search = conn.CreateCommand();
            search.CommandText = @"
SELECT m.path, m.theatre, m.tags, m.player_aircraft, m.player_side, m.time_of_day, m.countries,
       snippet(missions_fts, -1, '«', '»', ' … ', 10),
       bm25(missions_fts, 10.0, 8.0, 4.0, 6.0, 8.0, 12.0, 5.0)
FROM missions_fts JOIN missions m ON m.id = missions_fts.rowid
WHERE missions_fts MATCH @q AND m.id <> @self
LIMIT 100;";
            search.Parameters.AddWithValue("@q", string.Join(" OR ", top));
            search.Parameters.AddWithValue("@self", id);
            return ReadRows(search);
        }

        public static void ResetCache()
        {
            lock (VocabLock)
            {
                _vocabDirty = true;
                _vocabCache = null;
                _docCountCache = 0;
            }
        }
    }
}
