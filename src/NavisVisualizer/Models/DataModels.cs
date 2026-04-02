using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace NavisVisualizer.Models
{
    public enum HydrotestStatus
    {
        NotStarted,
        Completed,
        Recovery
    }

    public enum SpoolStage
    {
        NotStarted,
        // Fabrication
        BV,           // B/V (Bevel)
        FitUp,        // F/up
        WeldDone,     // W/D
        NDE,          // NDE
        PWHT,         // PWHT
        ShipOut,      // S/out
        PostProcess,  // G-후공정인계
        Galvanizing,  // Galv2
        Paint1,       // Pnt1
        Paint2,       // Pnt2
        Stock,        // Stock
        HandOver,     // H/O일자
        // Install
        Setting,      // Setting
        Welding,      // Welding
    }

    public static class SpoolStageInfo
    {
        public static readonly SpoolStage[] OrderedStages =
        {
            SpoolStage.BV, SpoolStage.FitUp, SpoolStage.WeldDone, SpoolStage.NDE,
            SpoolStage.PWHT, SpoolStage.ShipOut, SpoolStage.PostProcess, SpoolStage.Galvanizing,
            SpoolStage.Paint1, SpoolStage.Paint2, SpoolStage.Stock, SpoolStage.HandOver,
            SpoolStage.Setting, SpoolStage.Welding
        };

        public static readonly Dictionary<SpoolStage, string> Labels = new Dictionary<SpoolStage, string>
        {
            [SpoolStage.NotStarted]  = "미착수",
            [SpoolStage.BV]          = "B/V",
            [SpoolStage.FitUp]       = "F/up",
            [SpoolStage.WeldDone]    = "W/D",
            [SpoolStage.NDE]         = "NDE",
            [SpoolStage.PWHT]        = "PWHT",
            [SpoolStage.ShipOut]     = "S/out",
            [SpoolStage.PostProcess] = "후공정인계",
            [SpoolStage.Galvanizing] = "Galv",
            [SpoolStage.Paint1]      = "Pnt1",
            [SpoolStage.Paint2]      = "Pnt2",
            [SpoolStage.Stock]       = "Stock",
            [SpoolStage.HandOver]    = "H/O",
            [SpoolStage.Setting]     = "Setting",
            [SpoolStage.Welding]     = "Welding",
        };

        /// <summary>Excel column header → SpoolStage mapping</summary>
        public static readonly Dictionary<string, SpoolStage> ColumnMap = new Dictionary<string, SpoolStage>(StringComparer.OrdinalIgnoreCase)
        {
            ["B/V"]        = SpoolStage.BV,
            ["F/up"]       = SpoolStage.FitUp,
            ["W/D"]        = SpoolStage.WeldDone,
            ["NDE"]        = SpoolStage.NDE,
            ["PWHT"]       = SpoolStage.PWHT,
            ["S/out"]      = SpoolStage.ShipOut,
            ["G-후공정인계"] = SpoolStage.PostProcess,
            ["Galv2"]      = SpoolStage.Galvanizing,
            ["Pnt1"]       = SpoolStage.Paint1,
            ["Pnt2"]       = SpoolStage.Paint2,
            ["Stock"]      = SpoolStage.Stock,
            ["H/O일자"]    = SpoolStage.HandOver,
            ["Setting"]    = SpoolStage.Setting,
            ["Welding"]    = SpoolStage.Welding,
        };

        public static readonly SpoolStage[] FabricationStages =
        {
            SpoolStage.BV, SpoolStage.FitUp, SpoolStage.WeldDone, SpoolStage.NDE,
            SpoolStage.PWHT, SpoolStage.ShipOut, SpoolStage.PostProcess, SpoolStage.Galvanizing,
            SpoolStage.Paint1, SpoolStage.Paint2, SpoolStage.Stock, SpoolStage.HandOver,
        };

        public static readonly SpoolStage[] InstallStages =
        {
            SpoolStage.Setting, SpoolStage.Welding,
        };
    }

    public class ColorSetting
    {
        public Color DisplayColor { get; set; }
        public double Transparency { get; set; }

        public ColorSetting Clone() =>
            new ColorSetting { DisplayColor = DisplayColor, Transparency = Transparency };

        public static Dictionary<HydrotestStatus, ColorSetting> HydrotestDefaults =>
            new Dictionary<HydrotestStatus, ColorSetting>
            {
                [HydrotestStatus.NotStarted] = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7 },
                [HydrotestStatus.Completed]  = new ColorSetting { DisplayColor = Color.FromArgb(34, 139, 34),   Transparency = 0.0 },
                [HydrotestStatus.Recovery]   = new ColorSetting { DisplayColor = Color.FromArgb(220, 20, 60),   Transparency = 0.0 },
            };

        public static Dictionary<SpoolStage, ColorSetting> SpoolDefaults =>
            new Dictionary<SpoolStage, ColorSetting>
            {
                [SpoolStage.NotStarted]   = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7  },
                // Fabrication - warm progression
                [SpoolStage.BV]           = new ColorSetting { DisplayColor = Color.FromArgb(255, 255, 180), Transparency = 0.2  },
                [SpoolStage.FitUp]        = new ColorSetting { DisplayColor = Color.FromArgb(255, 235, 130), Transparency = 0.2  },
                [SpoolStage.WeldDone]     = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.0  },
                [SpoolStage.NDE]          = new ColorSetting { DisplayColor = Color.FromArgb(255, 180, 50),  Transparency = 0.0  },
                [SpoolStage.PWHT]         = new ColorSetting { DisplayColor = Color.FromArgb(255, 140, 50),  Transparency = 0.0  },
                [SpoolStage.ShipOut]      = new ColorSetting { DisplayColor = Color.FromArgb(240, 128, 128), Transparency = 0.0  },
                [SpoolStage.PostProcess]  = new ColorSetting { DisplayColor = Color.FromArgb(255, 160, 160), Transparency = 0.0  },
                [SpoolStage.Galvanizing]  = new ColorSetting { DisplayColor = Color.FromArgb(173, 216, 230), Transparency = 0.0  },
                [SpoolStage.Paint1]       = new ColorSetting { DisplayColor = Color.FromArgb(135, 206, 235), Transparency = 0.0  },
                [SpoolStage.Paint2]       = new ColorSetting { DisplayColor = Color.FromArgb(100, 149, 237), Transparency = 0.0  },
                [SpoolStage.Stock]        = new ColorSetting { DisplayColor = Color.FromArgb(65, 105, 225),  Transparency = 0.0  },
                [SpoolStage.HandOver]     = new ColorSetting { DisplayColor = Color.FromArgb(30, 144, 255),  Transparency = 0.0  },
                // Install
                [SpoolStage.Setting]      = new ColorSetting { DisplayColor = Color.FromArgb(138, 43, 226),  Transparency = 0.0  },
                [SpoolStage.Welding]      = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0  },
            };

        public static ColorSetting Unmatched =>
            new ColorSetting { DisplayColor = Color.FromArgb(200, 200, 200), Transparency = 0.9 };
    }

    public class TestPackageData
    {
        public string TestPkgId { get; set; }
        public HydrotestStatus Status { get; set; }
        public DateTime? PlannedDate { get; set; }
        public DateTime? ActualDate { get; set; }
        public string System { get; set; }
        public string Remarks { get; set; }
        public List<string> SpoolIds { get; set; } = new List<string>();
    }

    public class SpoolData
    {
        public string SpoolId { get; set; }
        public string IsoNo { get; set; }
        public Dictionary<SpoolStage, DateTime?> StageDates { get; set; } = new Dictionary<SpoolStage, DateTime?>();

        /// <summary>
        /// Computes the current stage based on a reference date.
        /// Returns the latest stage whose date exists and is &lt;= referenceDate.
        /// </summary>
        public SpoolStage GetStageAtDate(DateTime referenceDate)
        {
            var stages = SpoolStageInfo.OrderedStages;
            // Walk backwards from last stage to find the latest completed one
            for (int i = stages.Length - 1; i >= 0; i--)
            {
                if (StageDates.TryGetValue(stages[i], out var date) && date.HasValue && date.Value.Date <= referenceDate.Date)
                    return stages[i];
            }
            return SpoolStage.NotStarted;
        }
    }
}
