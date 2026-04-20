using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace NavisVisualizer.Models
{
    public enum HydrotestStage
    {
        NotStarted,
        Review,          // Review
        LineInspection,  // Line inspection
        Flushing,        // Flushing
        Hydrotest,       // Hydrotest
        Drying,          // Drying
        Reinstatement,   // Reinstatement
    }

    public static class HydrotestStageInfo
    {
        public static readonly HydrotestStage[] OrderedStages =
        {
            HydrotestStage.Review, HydrotestStage.LineInspection, HydrotestStage.Flushing,
            HydrotestStage.Hydrotest, HydrotestStage.Drying, HydrotestStage.Reinstatement
        };

        public static readonly Dictionary<HydrotestStage, string> Labels = new Dictionary<HydrotestStage, string>
        {
            [HydrotestStage.NotStarted]     = "미착수",
            [HydrotestStage.Review]         = "Review",
            [HydrotestStage.LineInspection] = "Line Insp.",
            [HydrotestStage.Flushing]       = "Flushing",
            [HydrotestStage.Hydrotest]      = "Hydrotest",
            [HydrotestStage.Drying]         = "Drying",
            [HydrotestStage.Reinstatement]  = "Reinstate",
        };

        public static readonly Dictionary<string, HydrotestStage> ColumnMap = new Dictionary<string, HydrotestStage>(StringComparer.OrdinalIgnoreCase)
        {
            ["Review"]          = HydrotestStage.Review,
            ["Line inspection"] = HydrotestStage.LineInspection,
            ["Line Inspection"] = HydrotestStage.LineInspection,
            ["Flushing"]        = HydrotestStage.Flushing,
            ["Hydrotest"]       = HydrotestStage.Hydrotest,
            ["Drying"]          = HydrotestStage.Drying,
            ["Reinstatement"]   = HydrotestStage.Reinstatement,
        };
    }

    public enum SpoolStage
    {
        NotStarted,
        BV, FitUp, WeldDone, NDE, PWHT, ShipOut, PostProcess,
        Galvanizing, Paint1, Paint2, Stock, HandOver,
        Setting, Welding,
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

        public static readonly Dictionary<string, SpoolStage> ColumnMap = new Dictionary<string, SpoolStage>(StringComparer.OrdinalIgnoreCase)
        {
            ["B/V"] = SpoolStage.BV, ["F/up"] = SpoolStage.FitUp, ["W/D"] = SpoolStage.WeldDone,
            ["NDE"] = SpoolStage.NDE, ["PWHT"] = SpoolStage.PWHT, ["S/out"] = SpoolStage.ShipOut,
            ["G-후공정인계"] = SpoolStage.PostProcess, ["Galv2"] = SpoolStage.Galvanizing,
            ["Pnt1"] = SpoolStage.Paint1, ["Pnt2"] = SpoolStage.Paint2,
            ["Stock"] = SpoolStage.Stock, ["H/O일자"] = SpoolStage.HandOver,
            ["Setting"] = SpoolStage.Setting, ["Welding"] = SpoolStage.Welding,
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

        public static Dictionary<HydrotestStage, ColorSetting> HydrotestDefaults =>
            new Dictionary<HydrotestStage, ColorSetting>
            {
                [HydrotestStage.NotStarted]     = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7 },
                [HydrotestStage.Review]         = new ColorSetting { DisplayColor = Color.FromArgb(255, 235, 130), Transparency = 0.2 },
                [HydrotestStage.LineInspection] = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.0 },
                [HydrotestStage.Flushing]       = new ColorSetting { DisplayColor = Color.FromArgb(135, 206, 235), Transparency = 0.0 },
                [HydrotestStage.Hydrotest]      = new ColorSetting { DisplayColor = Color.FromArgb(65, 105, 225),  Transparency = 0.0 },
                [HydrotestStage.Drying]         = new ColorSetting { DisplayColor = Color.FromArgb(138, 43, 226),  Transparency = 0.0 },
                [HydrotestStage.Reinstatement]  = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0 },
            };

        public static Dictionary<SpoolStage, ColorSetting> SpoolDefaults =>
            new Dictionary<SpoolStage, ColorSetting>
            {
                [SpoolStage.NotStarted]   = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7  },
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
                [SpoolStage.Setting]      = new ColorSetting { DisplayColor = Color.FromArgb(138, 43, 226),  Transparency = 0.0  },
                [SpoolStage.Welding]      = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0  },
            };

        public static Dictionary<EquipmentStage, ColorSetting> EquipmentDefaults =>
            new Dictionary<EquipmentStage, ColorSetting>
            {
                [EquipmentStage.NotStarted] = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7 },
                [EquipmentStage.Delivery]   = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.0 },
                [EquipmentStage.Loading]    = new ColorSetting { DisplayColor = Color.FromArgb(65, 105, 225),  Transparency = 0.0 },
                [EquipmentStage.Setting]    = new ColorSetting { DisplayColor = Color.FromArgb(138, 43, 226),  Transparency = 0.0 },
                [EquipmentStage.Inspection] = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0 },
            };

        public static Dictionary<EitStage, ColorSetting> EitDefaults =>
            new Dictionary<EitStage, ColorSetting>
            {
                [EitStage.NotStarted]     = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7 },
                [EitStage.TrayInstalled]  = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.0 },
                [EitStage.CablePulling]   = new ColorSetting { DisplayColor = Color.FromArgb(65, 105, 225),  Transparency = 0.0 },
                [EitStage.CableCompleted] = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0 },
            };

        public static ColorSetting Unmatched =>
            new ColorSetting { DisplayColor = Color.FromArgb(200, 200, 200), Transparency = 0.9 };
    }

    public class TestPackageData
    {
        public string TestPkgId { get; set; }
        public string SystemNo { get; set; }
        public string LineService { get; set; }
        public Dictionary<HydrotestStage, DateTime?> StageDates { get; set; } = new Dictionary<HydrotestStage, DateTime?>();

        public HydrotestStage GetStageAtDate(DateTime referenceDate)
        {
            var stages = HydrotestStageInfo.OrderedStages;
            for (int i = stages.Length - 1; i >= 0; i--)
            {
                if (StageDates.TryGetValue(stages[i], out var date) && date.HasValue && date.Value.Date <= referenceDate.Date)
                    return stages[i];
            }
            return HydrotestStage.NotStarted;
        }
    }

    public class SpoolData
    {
        public string SpoolId { get; set; }
        public string IsoNo { get; set; }
        public Dictionary<SpoolStage, DateTime?> StageDates { get; set; } = new Dictionary<SpoolStage, DateTime?>();

        public SpoolStage GetStageAtDate(DateTime referenceDate)
        {
            var stages = SpoolStageInfo.OrderedStages;
            for (int i = stages.Length - 1; i >= 0; i--)
            {
                if (StageDates.TryGetValue(stages[i], out var date) && date.HasValue && date.Value.Date <= referenceDate.Date)
                    return stages[i];
            }
            return SpoolStage.NotStarted;
        }
    }

    // ============================================================
    // Equipment
    // ============================================================

    public enum EquipmentStage
    {
        NotStarted,
        Delivery,    // Delivered (Confirmed ETA as date)
        Loading,     // Loading
        Setting,     // Setting
        Inspection,  // Inspection
    }

    public static class EquipmentStageInfo
    {
        public static readonly EquipmentStage[] OrderedStages =
        {
            EquipmentStage.Delivery, EquipmentStage.Loading,
            EquipmentStage.Setting, EquipmentStage.Inspection
        };

        public static readonly Dictionary<EquipmentStage, string> Labels = new Dictionary<EquipmentStage, string>
        {
            [EquipmentStage.NotStarted] = "미착수",
            [EquipmentStage.Delivery]   = "Delivery",
            [EquipmentStage.Loading]    = "Loading",
            [EquipmentStage.Setting]    = "Setting",
            [EquipmentStage.Inspection] = "Inspection",
        };

        /// <summary>Excel column → EquipmentStage (Loading, Setting, Inspection only; Delivery handled separately)</summary>
        public static readonly Dictionary<string, EquipmentStage> ColumnMap = new Dictionary<string, EquipmentStage>(StringComparer.OrdinalIgnoreCase)
        {
            ["Loading"]    = EquipmentStage.Loading,
            ["Setting"]    = EquipmentStage.Setting,
            ["Inspection"] = EquipmentStage.Inspection,
        };
    }

    public class EquipmentData
    {
        public string TagNo { get; set; }
        public string RfqNo { get; set; }
        public string SubSystem { get; set; }
        public string Description { get; set; }
        public string DeliveryStatus { get; set; } // "Delivered" or empty
        public DateTime? ConfirmedEta { get; set; }
        public Dictionary<EquipmentStage, DateTime?> StageDates { get; set; } = new Dictionary<EquipmentStage, DateTime?>();

        public EquipmentStage GetStageAtDate(DateTime referenceDate)
        {
            // Check from last stage backwards
            var stages = EquipmentStageInfo.OrderedStages;
            for (int i = stages.Length - 1; i >= 0; i--)
            {
                if (StageDates.TryGetValue(stages[i], out var date) && date.HasValue && date.Value.Date <= referenceDate.Date)
                    return stages[i];
            }
            return EquipmentStage.NotStarted;
        }
    }

    // ============================================================
    // EIT (Electrical & Instrumentation) Tray
    // ============================================================

    public enum EitStage
    {
        NotStarted,
        TrayInstalled,   // Tray install date <= referenceDate, no cable pulled
        CablePulling,    // Some cable pulled but not 100%
        CableCompleted,  // Cable progress 100%
    }

    public static class EitStageInfo
    {
        public static readonly EitStage[] OrderedStages =
        {
            EitStage.TrayInstalled, EitStage.CablePulling, EitStage.CableCompleted
        };

        public static readonly Dictionary<EitStage, string> Labels = new Dictionary<EitStage, string>
        {
            [EitStage.NotStarted]     = "미착수",
            [EitStage.TrayInstalled]  = "Tray 설치",
            [EitStage.CablePulling]   = "Cable 포설중",
            [EitStage.CableCompleted] = "Cable 완료",
        };
    }

    public class EitTrayData
    {
        public string TrayType { get; set; }
        public string TrayNumber { get; set; }   // Match key (e.g. /101890-INT-22039-CM-MDB/B2)
        public double? TrayMr { get; set; }
        public double? TrayCompleteMr { get; set; }
        public double? TrayProgress { get; set; }   // 0.0 - 1.0
        public DateTime? TrayInstallDate { get; set; }

        // Cable rows (0..N per tray)
        public List<EitCableRecord> Cables { get; set; } = new List<EitCableRecord>();

        /// <summary>Best cable progress across assigned routes (1.0 if any 100%).</summary>
        public double? BestCableProgress
        {
            get
            {
                if (Cables == null || Cables.Count == 0) return null;
                double? max = null;
                foreach (var c in Cables)
                {
                    if (!c.Progress.HasValue) continue;
                    if (!max.HasValue || c.Progress.Value > max.Value) max = c.Progress.Value;
                }
                return max;
            }
        }

        public EitStage GetStageAtDate(DateTime referenceDate)
        {
            if (!TrayInstallDate.HasValue || TrayInstallDate.Value.Date > referenceDate.Date)
                return EitStage.NotStarted;

            var cableProgress = BestCableProgress;
            if (!cableProgress.HasValue) return EitStage.TrayInstalled;
            if (cableProgress.Value >= 0.999) return EitStage.CableCompleted;
            if (cableProgress.Value > 0.0) return EitStage.CablePulling;
            return EitStage.TrayInstalled;
        }
    }

    public class EitCableRecord
    {
        public string RouteNumber { get; set; }
        public double? AssumeLength { get; set; }
        public double? PullLength { get; set; }
        public double? Progress { get; set; }   // 0.0 - 1.0
    }
}
