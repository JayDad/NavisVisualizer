using System;
using System.Collections.Generic;
using System.Drawing;

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
        Fabricating,
        FabCompleted,
        HandOver,
        Loaded,
        Installed
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
                [SpoolStage.Fabricating]  = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.2  },
                [SpoolStage.FabCompleted] = new ColorSetting { DisplayColor = Color.FromArgb(135, 206, 235), Transparency = 0.0  },
                [SpoolStage.HandOver]     = new ColorSetting { DisplayColor = Color.FromArgb(65, 105, 225),  Transparency = 0.0  },
                [SpoolStage.Loaded]       = new ColorSetting { DisplayColor = Color.FromArgb(138, 43, 226),  Transparency = 0.0  },
                [SpoolStage.Installed]    = new ColorSetting { DisplayColor = Color.FromArgb(0, 100, 0),     Transparency = 0.0  },
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
        public SpoolStage Stage { get; set; }
        public DateTime? PlannedDate { get; set; }
        public DateTime? ActualDate { get; set; }
        public string Remarks { get; set; }
    }
}
