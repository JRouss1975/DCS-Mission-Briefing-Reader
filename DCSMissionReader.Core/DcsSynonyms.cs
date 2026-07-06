using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DCSMissionReader
{
    /// <summary>
    /// DCS domain knowledge used by the search engine:
    /// aircraft nicknames, mission-type synonyms, SAM/tanker/AWACS/carrier detection,
    /// theatre aliases and text normalization helpers.
    /// </summary>
    public static class DcsSynonyms
    {
        // ------------------------------------------------------------------
        // Text normalization
        // ------------------------------------------------------------------

        /// <summary>Lowercase + strip diacritics (works for Greek tonos too).</summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string formD = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (char c in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            // Final sigma -> sigma so Greek tokens compare consistently
            sb.Replace('ς', 'σ');
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>Keep only letters/digits: "F/A-18C" -> "fa18c". Used as alias key and for fuzzy matching.</summary>
        public static string Collapse(string? text)
        {
            string norm = Normalize(text);
            var sb = new StringBuilder(norm.Length);
            foreach (char c in norm)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Split into plain alphanumeric tokens (same splitting FTS unicode61 does).</summary>
        public static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            foreach (char c in Normalize(text))
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
            }
            if (sb.Length > 0) tokens.Add(sb.ToString());
            return tokens;
        }

        public static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
        {
            // English
            "the","a","an","and","or","of","to","in","on","at","for","with","from","by","is","are",
            "mission","missions","vs","versus","any","all","some","my","me","find","search",
            // Greek (normalized, no accents)
            "με","και","στο","στη","στην","στον","το","τα","η","ο","οι","του","της","των","για","απο","σε","ενα","μια",
            "αποστολη","αποστολες","βρες","ψαξε","θελω"
        };

        // ------------------------------------------------------------------
        // Multi-word phrases folded into single keys BEFORE tokenizing
        // ------------------------------------------------------------------
        private static readonly (string Phrase, string Replacement)[] PhraseFolds =
        {
            ("air to air", "a2a"), ("air 2 air", "a2a"), ("air-to-air", "a2a"),
            ("air to ground", "a2g"), ("air 2 ground", "a2g"), ("air-to-ground", "a2g"),
            ("close air support", "cas"),
            ("combat air patrol", "cap"),
            ("fighter sweep", "sweep"),
            ("wild weasel", "sead"), ("iron hand", "sead"),
            ("anti ship", "antiship"), ("anti-ship", "antiship"),
            ("deep strike", "strike"),
            ("combat search and rescue", "csar"), ("search and rescue", "csar"),
            ("cold war", "coldwar"),
            ("world war", "wwii"), ("ww 2", "wwii"),
            ("αερος αερος", "a2a"), ("αερος εδαφους", "a2g")
        };

        public static string FoldPhrases(string normalizedQuery)
        {
            string q = normalizedQuery;
            foreach (var (phrase, repl) in PhraseFolds)
                q = q.Replace(phrase, repl);
            return q;
        }

        // ------------------------------------------------------------------
        // Query-time aliases: collapsed user token -> FTS alternatives.
        // Alternatives containing a space are treated as exact phrases.
        // ------------------------------------------------------------------
        public static readonly Dictionary<string, string[]> QueryAliases = new(StringComparer.Ordinal)
        {
            // Mission types / roles
            ["a2a"] = new[] { "a2a" },
            ["bvr"] = new[] { "a2a", "bvr" },
            ["dogfight"] = new[] { "a2a", "dogfight" },
            ["a2g"] = new[] { "a2g" },
            ["cap"] = new[] { "cap" },
            ["barcap"] = new[] { "cap" },
            ["tarcap"] = new[] { "cap" },
            ["intercept"] = new[] { "intercept" },
            ["interception"] = new[] { "intercept" },
            ["sweep"] = new[] { "sweep" },
            ["escort"] = new[] { "escort" },
            ["sead"] = new[] { "sead", "dead" },
            ["dead"] = new[] { "sead", "dead" },
            ["wildweasel"] = new[] { "sead" },
            ["cas"] = new[] { "cas" },
            ["strike"] = new[] { "strike" },
            ["bombing"] = new[] { "strike", "bomb" },
            ["bomb"] = new[] { "strike", "bomb" },
            ["antiship"] = new[] { "antiship" },
            ["oca"] = new[] { "oca" },
            ["recon"] = new[] { "recon" },
            ["recce"] = new[] { "recon" },
            ["reconnaissance"] = new[] { "recon" },
            ["csar"] = new[] { "csar", "rescue" },
            ["rescue"] = new[] { "csar", "rescue" },
            ["transport"] = new[] { "transport" },
            ["logistics"] = new[] { "transport" },
            ["cargo"] = new[] { "transport", "cargo" },
            ["tanker"] = new[] { "tanker", "aar" },
            ["aar"] = new[] { "tanker", "aar" },
            ["refuel"] = new[] { "tanker", "aar", "refuel" },
            ["refueling"] = new[] { "tanker", "aar", "refuel" },
            ["awacs"] = new[] { "awacs" },
            ["fac"] = new[] { "fac", "afac" },
            ["afac"] = new[] { "fac", "afac" },
            // Time of day / era / misc tags
            ["night"] = new[] { "night" },
            ["nite"] = new[] { "night" },
            ["day"] = new[] { "day" },
            ["dawn"] = new[] { "dawn" },
            ["morning"] = new[] { "dawn", "morning" },
            ["dusk"] = new[] { "dusk" },
            ["evening"] = new[] { "dusk", "evening" },
            ["sunset"] = new[] { "dusk", "sunset" },
            ["carrier"] = new[] { "carrier" },
            ["cvn"] = new[] { "carrier" },
            ["boat"] = new[] { "carrier" },
            ["helo"] = new[] { "helo", "helicopter" },
            ["heli"] = new[] { "helo", "helicopter" },
            ["helicopter"] = new[] { "helo", "helicopter" },
            ["chopper"] = new[] { "helo", "helicopter" },
            ["wwii"] = new[] { "wwii" },
            ["ww2"] = new[] { "wwii" },
            ["warbird"] = new[] { "wwii", "warbird" },
            ["coldwar"] = new[] { "coldwar" },
            ["modern"] = new[] { "modern" },
            ["coop"] = new[] { "coop" },
            ["multiplayer"] = new[] { "coop" },
            ["mp"] = new[] { "coop" },
            ["sam"] = new[] { "sam" },
            ["sams"] = new[] { "sam" },
            ["iads"] = new[] { "sam" },
            ["airdefense"] = new[] { "sam" },
            ["aaa"] = new[] { "aaa" },
            ["flak"] = new[] { "aaa" },
            // Aircraft nicknames (index enrichment adds these words to the units text)
            ["f4"] = new[] { "phantom", "f4" },
            ["f4e"] = new[] { "phantom", "f4e" },
            ["phantom"] = new[] { "phantom" },
            ["f16"] = new[] { "viper", "f16" },
            ["viper"] = new[] { "viper" },
            ["falcon"] = new[] { "viper", "falcon" },
            ["f18"] = new[] { "hornet", "f18" },
            ["fa18"] = new[] { "hornet", "fa18" },
            ["hornet"] = new[] { "hornet" },
            ["a10"] = new[] { "warthog", "a10" },
            ["hog"] = new[] { "warthog" },
            ["warthog"] = new[] { "warthog" },
            ["f15"] = new[] { "eagle", "f15" },
            ["eagle"] = new[] { "eagle" },
            ["f15e"] = new[] { "strikeeagle", "f15e" },
            ["mudhen"] = new[] { "strikeeagle" },
            ["f14"] = new[] { "tomcat", "f14" },
            ["tomcat"] = new[] { "tomcat" },
            ["su27"] = new[] { "flanker", "su27" },
            ["flanker"] = new[] { "flanker" },
            ["su25"] = new[] { "frogfoot", "su25" },
            ["frogfoot"] = new[] { "frogfoot" },
            ["su33"] = new[] { "flanker", "su33" },
            ["mig29"] = new[] { "fulcrum", "mig29" },
            ["fulcrum"] = new[] { "fulcrum" },
            ["mig21"] = new[] { "fishbed", "mig21" },
            ["fishbed"] = new[] { "fishbed" },
            ["mig15"] = new[] { "fagot", "mig15" },
            ["ka50"] = new[] { "blackshark", "ka50" },
            ["blackshark"] = new[] { "blackshark" },
            ["mi8"] = new[] { "hip", "mi8" },
            ["hip"] = new[] { "hip" },
            ["mi24"] = new[] { "hind", "mi24" },
            ["hind"] = new[] { "hind" },
            ["uh1"] = new[] { "huey", "uh1" },
            ["huey"] = new[] { "huey" },
            ["ah64"] = new[] { "apache", "ah64" },
            ["apache"] = new[] { "apache" },
            ["av8b"] = new[] { "harrier", "av8b" },
            ["harrier"] = new[] { "harrier" },
            ["m2000"] = new[] { "mirage", "m2000" },
            ["mirage"] = new[] { "mirage" },
            ["viggen"] = new[] { "viggen" },
            ["jf17"] = new[] { "jf17", "thunder" },
            ["f5"] = new[] { "f5", "tiger" },
            ["f86"] = new[] { "sabre", "f86" },
            ["sabre"] = new[] { "sabre" },
            ["p51"] = new[] { "mustang", "p51" },
            ["mustang"] = new[] { "mustang" },
            ["spitfire"] = new[] { "spitfire" },
            ["bf109"] = new[] { "bf109" },
            ["fw190"] = new[] { "fw190" },
            ["c130"] = new[] { "hercules", "c130" },
            ["hercules"] = new[] { "hercules" },
            // Greek keywords
            ["νυχτα"] = new[] { "night" },
            ["νυχτερινη"] = new[] { "night" },
            ["νυχτερινο"] = new[] { "night" },
            ["μερα"] = new[] { "day" },
            ["ημερα"] = new[] { "day" },
            ["συρια"] = new[] { "syria" },
            ["ελικοπτερο"] = new[] { "helo", "helicopter" },
            ["ελικοπτερα"] = new[] { "helo", "helicopter" },
            ["αεροπλανοφορο"] = new[] { "carrier" },
            ["ανεφοδιασμοσ"] = new[] { "tanker", "aar" },
            ["διασωση"] = new[] { "csar", "rescue" },
            ["βομβαρδισμοσ"] = new[] { "strike", "bomb" },
            ["αναχαιτιση"] = new[] { "intercept" },
            ["συνοδεια"] = new[] { "escort" },
            ["μεταφορα"] = new[] { "transport" }
        };

        // ------------------------------------------------------------------
        // Collapsed tokens that should be treated as structured filters
        // when they appear as plain words in a query (auto-detect).
        // ------------------------------------------------------------------
        public static readonly HashSet<string> AirportTokens = new(StringComparer.Ordinal)
        {
            "f4", "f4e", "phantom",
            "f16", "viper", "falcon",
            "f18", "fa18", "hornet",
            "a10", "hog", "warthog",
            "f15", "eagle", "f15e", "strikeeagle", "mudhen",
            "f14", "tomcat",
            "su27", "flanker", "su25", "frogfoot", "su33", "su30", "su34",
            "mig29", "fulcrum", "mig21", "fishbed", "mig15", "fagot", "mig19", "mig23", "mig25", "mig31",
            "ka50", "blackshark",
            "mi8", "hip", "mi24", "hind", "mi26", "mi28",
            "uh1", "huey",
            "ah64", "apache", "ah1",
            "av8b", "harrier",
            "m2000", "mirage",
            "viggen",
            "jf17", "thunder",
            "f5", "tiger",
            "f86", "sabre",
            "p51", "mustang",
            "spitfire",
            "bf109", "fw190",
            "c130", "hercules",
            "a4", "skyhawk",
            "sa342", "gazelle",
            "ch47", "chinook",
            "ch53", "stallion",
            "oh58", "kiowa",
            "uh60", "blackhawk",
            "ελικοπτερο", "ελικοπτερα"
        };

        public static readonly HashSet<string> MapTokens = new(StringComparer.Ordinal)
        {
            "caucasus", "georgia", "cauc",
            "syria", "συρια",
            "persian", "persiangulf", "gulf", "pg", "hormuz",
            "nevada", "nttr", "vegas",
            "marianas", "mariana", "guam",
            "falklands", "falkland", "southatlantic", "malvinas",
            "sinai", "sinaimap", "egypt",
            "kola",
            "afghanistan", "afghan",
            "normandy",
            "thechannel", "channel",
            "germany", "germanycw",
            "iraq"
        };

        public static readonly HashSet<string> MissionTypeTokens = new(StringComparer.Ordinal)
        {
            "cap", "barcap", "tarcap",
            "intercept", "interception", "αναχαιτιση",
            "sweep",
            "escort", "συνοδεια",
            "sead", "dead", "wildweasel",
            "strike", "bombing", "bomb", "βομβαρδισμοσ",
            "cas",
            "fac", "afac",
            "oca",
            "antiship",
            "recon", "recce", "reconnaissance",
            "csar", "rescue", "διασωση",
            "transport", "logistics", "cargo", "μεταφορα",
            "aar", "refuel", "refueling", "tanker",
            "awacs",
            "heli", "helo", "helicopter", "chopper", "ελικοπτερο", "ελικοπτερα",
            "carrier", "cvn", "boat", "αεροπλανοφορο",
        };

        public static readonly HashSet<string> TimeTokens = new(StringComparer.Ordinal)
        {
            "day", "μερα", "ημερα",
            "night", "nite", "νυχτα", "νυχτερινη", "νυχτερινο",
            "dawn", "morning",
            "dusk", "evening", "sunset"
        };

        public static readonly HashSet<string> SideTokens = new(StringComparer.Ordinal)
        {
            "blue", "red", "μπλε", "κοκκινο"
        };

        // ------------------------------------------------------------------
        // Index-time enrichment: DCS unit type id -> extra searchable words
        // ------------------------------------------------------------------
        public static readonly Dictionary<string, string> FriendlyTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            ["F-4E"] = "phantom f4 f4e",
            ["F-4E_45"] = "phantom f4 f4e",
            ["F-4E-45MC"] = "phantom f4 f4e",
            ["F-4E_Late"] = "phantom f4 f4e",
            ["F-4E_Early"] = "phantom f4 f4e",
            ["F-4E_52"] = "phantom f4 f4e",
            ["PHANTOM"] = "phantom f4 f4e",
            ["PHANTOM_1"] = "phantom f4 f4e",
            ["FA-18C_hornet"] = "hornet f18 fa18",
            ["F/A-18C"] = "hornet f18 fa18",
            ["F-16C_50"] = "viper falcon f16",
            ["F-16C bl.52d"] = "viper falcon f16",
            ["F-16A"] = "viper falcon f16",
            ["F-15C"] = "eagle f15",
            ["F-15E"] = "strikeeagle mudhen f15e",
            ["F-15ESE"] = "strikeeagle mudhen f15e",
            ["F-14A"] = "tomcat f14",
            ["F-14A-135-GR"] = "tomcat f14",
            ["F-14B"] = "tomcat f14",
            ["A-10A"] = "warthog hog a10",
            ["A-10C"] = "warthog hog a10",
            ["A-10C_2"] = "warthog hog a10",
            ["AV8BNA"] = "harrier av8b",
            ["M-2000C"] = "mirage m2000",
            ["Mirage 2000-5"] = "mirage m2000",
            ["Mirage-F1CE"] = "mirage miragef1 f1",
            ["Mirage-F1EE"] = "mirage miragef1 f1",
            ["Mirage-F1BE"] = "mirage miragef1 f1",
            ["AJS37"] = "viggen ajs37",
            ["JF-17"] = "thunder jf17",
            ["C-101EB"] = "aviojet c101",
            ["C-101CC"] = "aviojet c101",
            ["MB-339A"] = "mb339",
            ["L-39C"] = "albatros l39",
            ["L-39ZA"] = "albatros l39",
            ["F-5E"] = "tiger f5",
            ["F-5E-3"] = "tiger f5",
            ["F-86F Sabre"] = "sabre f86",
            ["MiG-15bis"] = "fagot mig15",
            ["MiG-19P"] = "farmer mig19",
            ["MiG-21Bis"] = "fishbed mig21",
            ["MiG-23MLD"] = "flogger mig23",
            ["MiG-25PD"] = "foxbat mig25",
            ["MiG-29A"] = "fulcrum mig29",
            ["MiG-29S"] = "fulcrum mig29",
            ["MiG-29G"] = "fulcrum mig29",
            ["MiG-31"] = "foxhound mig31",
            ["Su-24M"] = "fencer su24",
            ["Su-25"] = "frogfoot su25",
            ["Su-25T"] = "frogfoot su25",
            ["Su-27"] = "flanker su27",
            ["Su-30"] = "flanker su30",
            ["Su-33"] = "flanker su33",
            ["Su-34"] = "fullback su34",
            ["J-11A"] = "flanker j11",
            ["Tu-22M3"] = "backfire tu22 bomber",
            ["Tu-95MS"] = "bear tu95 bomber",
            ["Tu-160"] = "blackjack tu160 bomber",
            ["B-52H"] = "buff b52 bomber",
            ["B-1B"] = "lancer b1 bomber",
            ["Ka-50"] = "blackshark ka50",
            ["Ka-50_3"] = "blackshark ka50",
            ["Mi-8MT"] = "hip mi8",
            ["Mi-24P"] = "hind mi24",
            ["Mi-26"] = "halo mi26",
            ["Mi-28N"] = "havoc mi28",
            ["UH-1H"] = "huey uh1",
            ["AH-64D"] = "apache ah64",
            ["AH-64D_BLK_II"] = "apache ah64",
            ["AH-1W"] = "cobra ah1",
            ["SA342M"] = "gazelle sa342",
            ["SA342L"] = "gazelle sa342",
            ["SA342Mistral"] = "gazelle sa342",
            ["CH-47Fbl1"] = "chinook ch47",
            ["CH-53E"] = "stallion ch53",
            ["OH-58D"] = "kiowa oh58",
            ["OH6A"] = "cayuse oh6",
            ["UH-60A"] = "blackhawk uh60",
            ["UH-60L"] = "blackhawk uh60",
            ["KC-135"] = "tanker aar kc135",
            ["KC135MPRS"] = "tanker aar kc135",
            ["KC130"] = "tanker aar kc130",
            ["KC_130"] = "tanker aar kc130",
            ["IL-78M"] = "tanker aar il78",
            ["S-3B Tanker"] = "tanker aar s3b",
            ["E-3A"] = "awacs sentry e3",
            ["E-2C"] = "awacs hawkeye e2",
            ["A-50"] = "awacs mainstay a50",
            ["KJ-2000"] = "awacs kj2000",
            ["C-130"] = "hercules c130",
            ["C-17A"] = "globemaster c17",
            ["IL-76MD"] = "il76",
            ["P-51D"] = "mustang p51 warbird",
            ["P-51D-30-NA"] = "mustang p51 warbird",
            ["TF-51D"] = "mustang tf51 warbird",
            ["P-47D-30"] = "thunderbolt p47 warbird",
            ["SpitfireLFMkIX"] = "spitfire warbird",
            ["SpitfireLFMkIXCW"] = "spitfire warbird",
            ["Bf-109K-4"] = "bf109 messerschmitt warbird",
            ["FW-190D9"] = "fw190 dora warbird",
            ["FW-190A8"] = "fw190 anton warbird",
            ["MosquitoFBMkVI"] = "mosquito warbird",
            ["A-4E-C"] = "skyhawk a4"
        };

        /// <summary>Pretty names for the aircraft facet dropdown.</summary>
        public static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["F-4E"] = "F-4E Phantom II",
            ["F-4E_45"] = "F-4E Phantom II",
            ["F-4E-45MC"] = "F-4E Phantom II",
            ["F-4E_Late"] = "F-4E Phantom II",
            ["F-4E_Early"] = "F-4E Phantom II",
            ["F-4E_52"] = "F-4E Phantom II",
            ["FA-18C_hornet"] = "F/A-18C Hornet",
            ["F-16C_50"] = "F-16C Viper",
            ["F-15C"] = "F-15C Eagle",
            ["F-15ESE"] = "F-15E Strike Eagle",
            ["F-14A-135-GR"] = "F-14A Tomcat",
            ["F-14B"] = "F-14B Tomcat",
            ["A-10C"] = "A-10C Warthog",
            ["A-10C_2"] = "A-10C II Warthog",
            ["A-10A"] = "A-10A Warthog",
            ["AV8BNA"] = "AV-8B Harrier",
            ["M-2000C"] = "Mirage 2000C",
            ["AJS37"] = "AJS-37 Viggen",
            ["JF-17"] = "JF-17 Thunder",
            ["F-5E-3"] = "F-5E Tiger II",
            ["F-86F Sabre"] = "F-86F Sabre",
            ["MiG-15bis"] = "MiG-15bis",
            ["MiG-21Bis"] = "MiG-21bis",
            ["MiG-29A"] = "MiG-29A",
            ["Su-25T"] = "Su-25T Frogfoot",
            ["Su-27"] = "Su-27 Flanker",
            ["Su-33"] = "Su-33 Flanker-D",
            ["Ka-50"] = "Ka-50 Black Shark",
            ["Ka-50_3"] = "Ka-50 III Black Shark",
            ["Mi-8MT"] = "Mi-8MTV2 Hip",
            ["Mi-24P"] = "Mi-24P Hind",
            ["UH-1H"] = "UH-1H Huey",
            ["AH-64D_BLK_II"] = "AH-64D Apache",
            ["SA342M"] = "SA342 Gazelle",
            ["CH-47Fbl1"] = "CH-47F Chinook",
            ["OH-58D"] = "OH-58D Kiowa",
            ["Mirage-F1CE"] = "Mirage F1CE",
            ["MB-339A"] = "MB-339A",
            ["C-101EB"] = "C-101 Aviojet",
            ["L-39C"] = "L-39 Albatros",
            ["P-51D"] = "P-51D Mustang",
            ["TF-51D"] = "TF-51D Mustang",
            ["SpitfireLFMkIX"] = "Spitfire LF Mk.IX",
            ["Bf-109K-4"] = "Bf 109 K-4",
            ["FW-190D9"] = "Fw 190 D-9",
            ["FW-190A8"] = "Fw 190 A-8",
            ["A-4E-C"] = "A-4E Skyhawk"
        };

        public static string DisplayName(string type) =>
            DisplayNames.TryGetValue(type, out var d) ? d : type;

        // ------------------------------------------------------------------
        // Unit type keyword detection (substring match, lowercase)
        // ------------------------------------------------------------------
        public static readonly string[] SamKeywords =
        {
            "sa-2", "s-75", "sa-3", "s-125", "sa-5", "s-200", "kub", "sa-6", "osa", "sa-8",
            "strela", "sa-13", "buk", "sa-11", "tor ", "tor_", "9a331", "tunguska", "sa-19",
            "s-300", "hawk", "patriot", "roland", "rapier", "nasams", "hq-7", "dog ear",
            "snr", "p-19", "1l13", "55g6"
        };

        public static readonly string[] AaaKeywords =
        {
            "zsu-23", "shilka", "vulcan", "gepard", "zu-23", "flak", "bofors", "s-60"
        };

        public static readonly string[] TankerKeywords = { "kc-135", "kc135", "kc-130", "kc_130", "kc130", "il-78", "s-3b tanker" };
        public static readonly string[] AwacsKeywords = { "e-3a", "e-2c", "e-2d", "a-50", "kj-2000" };
        public static readonly string[] CarrierKeywords =
        {
            "cvn", "stennis", "kuznetsov", "forrestal", "tarawa", "essex", "hermes",
            "invincible", "vinson", "america", "juan carlos", "kiev"
        };

        // ------------------------------------------------------------------
        // Group task -> canonical tags
        // ------------------------------------------------------------------
        public static readonly Dictionary<string, string[]> TaskToTags = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CAP"] = new[] { "CAP", "A2A" },
            ["Intercept"] = new[] { "INTERCEPT", "A2A" },
            ["Fighter Sweep"] = new[] { "SWEEP", "A2A" },
            ["Escort"] = new[] { "ESCORT", "A2A" },
            ["SEAD"] = new[] { "SEAD", "A2G" },
            ["Pinpoint Strike"] = new[] { "STRIKE", "A2G" },
            ["Ground Attack"] = new[] { "STRIKE", "A2G" },
            ["CAS"] = new[] { "CAS", "A2G" },
            ["AFAC"] = new[] { "FAC", "A2G" },
            ["Antiship Strike"] = new[] { "ANTISHIP" },
            ["Anti-ship Strike"] = new[] { "ANTISHIP" },
            ["Runway Attack"] = new[] { "OCA", "STRIKE" },
            ["Runway Strike"] = new[] { "OCA", "STRIKE" },
            ["Reconnaissance"] = new[] { "RECON" },
            ["Transport"] = new[] { "TRANSPORT" },
            ["Refueling"] = new[] { "TANKER" },
            ["AWACS"] = new[] { "AWACS" },
            ["Ground Escort"] = new[] { "ESCORT" }
        };

        /// <summary>Tags offered in the mission-type filter dropdown (order = display order).</summary>
        public static readonly string[] MissionTypeTags =
        {
            "CAP", "INTERCEPT", "SWEEP", "ESCORT", "A2A",
            "SEAD", "STRIKE", "CAS", "FAC", "OCA", "ANTISHIP", "A2G",
            "RECON", "TRANSPORT", "CSAR", "CARRIER", "AAR", "HELO",
            "NIGHT", "SAM", "WWII", "COLDWAR", "MODERN", "COOP"
        };

        // ------------------------------------------------------------------
        // Theatre aliases for map: filter
        // ------------------------------------------------------------------
        private static readonly (string Alias, string Canonical)[] TheatreAliases =
        {
            ("cauc", "caucasus"), ("georgia", "caucasus"),
            ("syria", "syria"), ("συρια", "syria"),
            ("persian", "persiangulf"), ("gulf", "persiangulf"), ("pg", "persiangulf"), ("hormuz", "persiangulf"),
            ("nevada", "nevada"), ("nttr", "nevada"), ("vegas", "nevada"),
            ("mariana", "marianaislands"), ("guam", "marianaislands"),
            ("falkland", "falklands"), ("southatlantic", "falklands"), ("malvinas", "falklands"),
            ("sinai", "sinaimap"), ("egypt", "sinaimap"),
            ("kola", "kola"),
            ("afghan", "afghanistan"),
            ("normandy", "normandy"),
            ("channel", "thechannel"),
            ("germany", "germanycw"), ("cw", "germanycw"),
            ("iraq", "iraq")
        };

        /// <summary>True if a mission theatre string satisfies the user's map filter.</summary>
        public static bool TheatreMatches(string filterValue, string missionTheatre)
        {
            string f = Collapse(filterValue);
            string t = Collapse(missionTheatre);
            if (f.Length == 0 || t.Length == 0) return false;
            if (t.Contains(f)) return true;
            foreach (var (alias, canonical) in TheatreAliases)
            {
                if (f.Contains(alias) || f == alias)
                    if (t.Contains(canonical) || canonical.Contains(t)) return true;
            }
            return false;
        }

        /// <summary>True if the aircraft filter (nickname or type) matches any of the mission's player aircraft.</summary>
        public static bool AircraftMatches(string filterValue, string playerAircraftCsv)
        {
            string f = Collapse(filterValue);
            if (f.Length == 0) return false;

            // Expand nickname to canonical words too (viper -> also matches enrichment)
            var wanted = new HashSet<string> { f };
            if (QueryAliases.TryGetValue(f, out var alts))
                foreach (var a in alts) wanted.Add(Collapse(a));

            foreach (var type in playerAircraftCsv.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                string collapsedType = Collapse(type);
                string friendly = FriendlyTokens.TryGetValue(type, out var ft) ? Collapse(ft) : "";
                foreach (var w in wanted)
                {
                    if (collapsedType.Contains(w) || (friendly.Length > 0 && friendly.Contains(w)))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Canonical tag for a type/tag filter value ("dead" -> "SEAD").</summary>
        public static string CanonicalTag(string value)
        {
            string key = Collapse(value);
            if (QueryAliases.TryGetValue(key, out var alts) && alts.Length > 0)
                return alts[0].ToUpperInvariant();
            return key.ToUpperInvariant();
        }

        /// <summary>True if a single plain word can be auto-detected as a structured filter (plane/map/type/time/side or stop word).</summary>
        public static bool CanAutoDetect(string word)
        {
            string collapsed = Collapse(word);
            if (string.IsNullOrEmpty(collapsed)) return true;
            if (StopWords.Contains(collapsed)) return true;
            return (AirportTokens.Contains(collapsed) || (collapsed.Length > 2 && AirportTokens.Any(t => collapsed.StartsWith(t))))
                || MapTokens.Contains(collapsed)
                || MissionTypeTokens.Contains(collapsed)
                || TimeTokens.Contains(collapsed)
                || SideTokens.Contains(collapsed);
        }

        /// <summary>Damerau-ish Levenshtein distance with cutoff (used for typo correction).</summary>
        public static int EditDistance(string a, string b, int max)
        {
            if (Math.Abs(a.Length - b.Length) > max) return max + 1;
            int[] prev = new int[b.Length + 1];
            int[] curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                int rowMin = curr[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                    if (curr[j] < rowMin) rowMin = curr[j];
                }
                if (rowMin > max) return max + 1;
                (prev, curr) = (curr, prev);
            }
            return prev[b.Length];
        }
    }
}
