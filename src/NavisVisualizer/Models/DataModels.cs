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
                [EitStage.NotStarted] = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7 },
                [EitStage.Installing] = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.0 },
                [EitStage.Installed]  = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0 },
            };

        public static Dictionary<CableStage, ColorSetting> CableDefaults =>
            new Dictionary<CableStage, ColorSetting>
            {
                [CableStage.NotStarted] = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7 },
                [CableStage.Pulling]    = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.0 },
                [CableStage.Completed]  = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0 },
            };

        public static Dictionary<ProgressStatus, ColorSetting> ProgressDefaults =>
            new Dictionary<ProgressStatus, ColorSetting>
            {
                [ProgressStatus.NotStarted] = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7 },
                [ProgressStatus.InProgress] = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.0 },
                [ProgressStatus.Completed]  = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0 },
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
        NotStarted,   // Install % == 0 (or null)
        Installing,   // 0 < Install % < 100
        Installed,    // Install % == 100
    }

    public static class EitStageInfo
    {
        public static readonly EitStage[] OrderedStages =
        {
            EitStage.Installing, EitStage.Installed
        };

        public static readonly Dictionary<EitStage, string> Labels = new Dictionary<EitStage, string>
        {
            [EitStage.NotStarted] = "미착수",
            [EitStage.Installing] = "설치중",
            [EitStage.Installed]  = "설치완료",
        };
    }

    public class EitTrayData
    {
        public string TrayNumber { get; set; }   // Match key (e.g. /101890-HVT-61003-SM-MEB/B1)
        public double? TrayLth { get; set; }
        public double? TrayInstalled { get; set; }
        public double? InstallProgress { get; set; }   // 0.0 - 1.0 (Install %)

        // Reserved for future date-based filtering; currently unused.
        public DateTime? TrayInstallDate { get; set; }

        public EitStage GetStage()
        {
            if (!InstallProgress.HasValue) return EitStage.NotStarted;
            if (InstallProgress.Value >= 0.999) return EitStage.Installed;
            if (InstallProgress.Value > 0.0) return EitStage.Installing;
            return EitStage.NotStarted;
        }

        /// <summary>
        /// Model tree indexer strips leading '/' from DisplayName, so Excel Tray
        /// Numbers like "/101890-INT-25018-CM-PDA-CV/B1" must be normalized the
        /// same way before lookup.
        /// </summary>
        public static string NormalizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            return id.TrimStart('/').Trim();
        }
    }

    // ============================================================
    // Cable Pull (per-Node aggregation; one Excel row per cable)
    // ============================================================

    public enum CableStage
    {
        NotStarted, // overall progress == 0 (or no data)
        Pulling,    // 0 < overall < 100
        Completed,  // overall >= 100
    }

    public static class CableStageInfo
    {
        public static readonly CableStage[] OrderedStages =
        {
            CableStage.Pulling, CableStage.Completed
        };

        public static readonly Dictionary<CableStage, string> Labels = new Dictionary<CableStage, string>
        {
            [CableStage.NotStarted] = "미착수",
            [CableStage.Pulling]    = "포설중",
            [CableStage.Completed]  = "포설완료",
        };
    }

    public class CableNodeData
    {
        public string NodeId { get; set; }   // Match key, e.g. "101780-EMCT-52101_A-ND"
        public List<CableRecord> Cables { get; set; } = new List<CableRecord>();

        public double TotalDesignLth =>
            Cables.Sum(c => c.DesignLth ?? 0.0);

        public double TotalPulledLth =>
            Cables.Sum(c => c.PulledLth ?? 0.0);

        /// <summary>Aggregate progress = sum(pulled) / sum(design). Null when no design length.</summary>
        public double? OverallProgress
        {
            get
            {
                double design = TotalDesignLth;
                if (design <= 0.0) return null;
                return TotalPulledLth / design;
            }
        }

        public CableStage GetStage()
        {
            var p = OverallProgress;
            if (!p.HasValue) return CableStage.NotStarted;
            if (p.Value >= 0.999) return CableStage.Completed;
            if (p.Value > 0.0)    return CableStage.Pulling;
            return CableStage.NotStarted;
        }

        /// <summary>Box DisplayName format is "{NodeId}-BOX...", so the index key is
        /// the prefix before "-BOX". Excel NodeIds are normalized the same way
        /// (trim, leading '/' stripped, uppercased on lookup).</summary>
        public static string NormalizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            return id.TrimStart('/').Trim();
        }
    }

    // ============================================================
    // Sub-system (Equipment + Piping 통합 축)
    // ============================================================

    public enum SubSystemDiscipline
    {
        Equipment,   // Mech_EQ / All_EQ — TAG NO 매칭
        Piping,      // Piping_HydrotestPKG — PKGNO 매칭 (PKG 노드 색칠이 하위 스풀/배관을 커버)
    }

    public static class SubSystemDisciplineInfo
    {
        public static readonly Dictionary<SubSystemDiscipline, string> Labels = new Dictionary<SubSystemDiscipline, string>
        {
            [SubSystemDiscipline.Equipment] = "Equipment",
            [SubSystemDiscipline.Piping]    = "Piping",
        };
    }

    /// <summary>
    /// 공종별 stage 체계를 Sub-system 탭의 공통 축으로 정규화한 진행 상태.
    /// 공종마다 stage 수가 달라(Equipment 4 / Hydrotest 6) 단일 색상 체계로
    /// 묶으려면 미착수/진행중/완료 3단계로 접는다.
    /// </summary>
    public enum ProgressStatus
    {
        NotStarted,
        InProgress,
        Completed,
    }

    public static class ProgressStatusInfo
    {
        public static readonly ProgressStatus[] Ordered =
        {
            ProgressStatus.NotStarted, ProgressStatus.InProgress, ProgressStatus.Completed
        };

        public static readonly Dictionary<ProgressStatus, string> Labels = new Dictionary<ProgressStatus, string>
        {
            [ProgressStatus.NotStarted] = "미착수",
            [ProgressStatus.InProgress] = "진행중",
            [ProgressStatus.Completed]  = "완료",
        };
    }

    /// <summary>
    /// Sub-system 탭의 통합 요소. 공종별 원본 데이터(EquipmentData/TestPackageData)를
    /// 감싸 공통 축(Sub-system, 매칭 키, 진행 상태)으로 노출한다 — stage 계산은
    /// 원본 모델의 GetStageAtDate를 그대로 재사용한다.
    /// </summary>
    public class SubSystemElement
    {
        public string SubSystem { get; }
        public SubSystemDiscipline Discipline { get; }
        /// <summary>모델 매칭 키. Equipment는 TAG NO(로더에서 선행 '/' 정규화됨), Piping은 PKGNO.</summary>
        public string ElementId { get; }
        public string Description { get; }

        private readonly EquipmentData _equipment;
        private readonly TestPackageData _package;

        private SubSystemElement(string subSystem, SubSystemDiscipline discipline,
            string elementId, string description, EquipmentData equipment, TestPackageData package)
        {
            SubSystem = subSystem;
            Discipline = discipline;
            ElementId = elementId;
            Description = description;
            _equipment = equipment;
            _package = package;
        }

        public static SubSystemElement FromEquipment(EquipmentData eq) =>
            new SubSystemElement(eq.SubSystem?.Trim(), SubSystemDiscipline.Equipment,
                eq.TagNo, eq.Description ?? "", eq, null);

        public static SubSystemElement FromPackage(TestPackageData pkg) =>
            new SubSystemElement(pkg.SystemNo?.Trim(), SubSystemDiscipline.Piping,
                pkg.TestPkgId, pkg.LineService ?? "", null, pkg);

        /// <summary>기준일 시점의 공종별 상세 단계 라벨 (리포트 상세 리스트용).</summary>
        public string StageLabelAt(DateTime referenceDate)
        {
            if (_equipment != null)
                return EquipmentStageInfo.Labels[_equipment.GetStageAtDate(referenceDate)];
            return HydrotestStageInfo.Labels[_package.GetStageAtDate(referenceDate)];
        }

        /// <summary>기준일 시점의 정규화 진행 상태. 마지막 stage 도달 = 완료, 그 외 착수 = 진행중.</summary>
        public ProgressStatus StatusAt(DateTime referenceDate)
        {
            if (_equipment != null)
            {
                var stage = _equipment.GetStageAtDate(referenceDate);
                if (stage == EquipmentStage.NotStarted) return ProgressStatus.NotStarted;
                return stage == EquipmentStage.Inspection ? ProgressStatus.Completed : ProgressStatus.InProgress;
            }
            var pkgStage = _package.GetStageAtDate(referenceDate);
            if (pkgStage == HydrotestStage.NotStarted) return ProgressStatus.NotStarted;
            return pkgStage == HydrotestStage.Reinstatement ? ProgressStatus.Completed : ProgressStatus.InProgress;
        }
    }

    /// <summary>
    /// Sub-system별 색상 모드에서 선택 순서대로 배정되는 구분색 팔레트.
    /// 20색을 넘으면 순환한다 (동시 가시화가 수십 개를 넘으면 어차피 색으로 구분 불가).
    /// </summary>
    public static class SubSystemPalette
    {
        public static readonly Color[] Colors =
        {
            Color.FromArgb(230,  25,  75), Color.FromArgb(  0, 130, 200),
            Color.FromArgb( 60, 180,  75), Color.FromArgb(245, 130,  48),
            Color.FromArgb(145,  30, 180), Color.FromArgb( 70, 240, 240),
            Color.FromArgb(240,  50, 230), Color.FromArgb(255, 225,  25),
            Color.FromArgb(  0, 128, 128), Color.FromArgb(170, 110,  40),
            Color.FromArgb(  0,   0, 128), Color.FromArgb(210, 245,  60),
            Color.FromArgb(128,   0,   0), Color.FromArgb(128, 128,   0),
            Color.FromArgb(255, 105, 180), Color.FromArgb(  0, 100,   0),
            Color.FromArgb(138,  43, 226), Color.FromArgb( 70, 130, 180),
            Color.FromArgb(218, 165,  32), Color.FromArgb(250, 128, 114),
        };

        public static Color At(int index) =>
            Colors[((index % Colors.Length) + Colors.Length) % Colors.Length];
    }

    public class CableRecord
    {
        public int? Count { get; set; }
        public string EquipNo { get; set; }
        public string RouteSys { get; set; }
        public string CableNo { get; set; }
        public double? DesignLth { get; set; }   // Cable Design Lth
        public double? PulledLth { get; set; }   // Cable Pulled Lth
        public double? PullingProgress { get; set; } // 0.0 - 1.0
        public string FromModule { get; set; }
        public string FromEquip { get; set; }
        public string ToModule { get; set; }
        public string ToEquip { get; set; }
        public string InstallModule { get; set; }
        public string System { get; set; }
        public string Type { get; set; }
        public string Core { get; set; }
        public string Size { get; set; }
        public string OutDia { get; set; }
        public string TraySys { get; set; }
        public double? RouteDesignLth { get; set; } // separate "Design Lth" col
        public string LayerCode { get; set; }
    }
}
