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
        // 설치 단계: Setting → FitUpInstall(설치 fit-up) → Welding.
        // FitUp(제작 fit-up, "F/up")과 FitUpInstall(설치 fit-up, "FIT-UP")은 별개 단계.
        Setting, FitUpInstall, Welding,
    }

    public static class SpoolStageInfo
    {
        public static readonly SpoolStage[] OrderedStages =
        {
            SpoolStage.BV, SpoolStage.FitUp, SpoolStage.WeldDone, SpoolStage.NDE,
            SpoolStage.PWHT, SpoolStage.ShipOut, SpoolStage.PostProcess, SpoolStage.Galvanizing,
            SpoolStage.Paint1, SpoolStage.Paint2, SpoolStage.Stock, SpoolStage.HandOver,
            SpoolStage.Setting, SpoolStage.FitUpInstall, SpoolStage.Welding
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
            [SpoolStage.Setting]      = "Setting",
            [SpoolStage.FitUpInstall] = "FIT-UP",
            [SpoolStage.Welding]      = "Install",   // Welding + Flange Connection 조합 = 설치 완료
        };

        public static readonly Dictionary<string, SpoolStage> ColumnMap = new Dictionary<string, SpoolStage>(StringComparer.OrdinalIgnoreCase)
        {
            ["B/V"] = SpoolStage.BV, ["F/up"] = SpoolStage.FitUp, ["W/D"] = SpoolStage.WeldDone,
            ["NDE"] = SpoolStage.NDE, ["PWHT"] = SpoolStage.PWHT, ["S/out"] = SpoolStage.ShipOut,
            ["G-후공정인계"] = SpoolStage.PostProcess, ["Galv2"] = SpoolStage.Galvanizing,
            ["Pnt1"] = SpoolStage.Paint1, ["Pnt2"] = SpoolStage.Paint2,
            ["Stock"] = SpoolStage.Stock, ["H/O일자"] = SpoolStage.HandOver,
            ["Setting"] = SpoolStage.Setting, ["FIT-UP"] = SpoolStage.FitUpInstall,
            ["Welding"] = SpoolStage.Welding,
        };

        public static readonly SpoolStage[] FabricationStages =
        {
            SpoolStage.BV, SpoolStage.FitUp, SpoolStage.WeldDone, SpoolStage.NDE,
            SpoolStage.PWHT, SpoolStage.ShipOut, SpoolStage.PostProcess, SpoolStage.Galvanizing,
            SpoolStage.Paint1, SpoolStage.Paint2, SpoolStage.Stock, SpoolStage.HandOver,
        };

        public static readonly SpoolStage[] InstallStages =
        {
            SpoolStage.Setting, SpoolStage.FitUpInstall, SpoolStage.Welding,
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
                [SpoolStage.FitUpInstall] = new ColorSetting { DisplayColor = Color.FromArgb(186, 85, 211),  Transparency = 0.0  },
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

        public static Dictionary<CableLineStage, ColorSetting> CableLineDefaults =>
            new Dictionary<CableLineStage, ColorSetting>
            {
                [CableLineStage.NotStarted] = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7 },
                [CableLineStage.Pulling]    = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.0 },
                [CableLineStage.Pulled]     = new ColorSetting { DisplayColor = Color.FromArgb(65, 105, 225),  Transparency = 0.0 },
                [CableLineStage.Terminated] = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0 },
            };

        /// <summary>'하이라이트 우선' 모드(stage 날짜 없는 맨 목록)에서 매칭 케이블을 칠하는 단색.</summary>
        public static ColorSetting CableLineHighlight =>
            new ColorSetting { DisplayColor = Color.FromArgb(255, 90, 0), Transparency = 0.0 };

        public static Dictionary<ProgressStatus, ColorSetting> ProgressDefaults =>
            new Dictionary<ProgressStatus, ColorSetting>
            {
                [ProgressStatus.NotStarted] = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7 },
                [ProgressStatus.InProgress] = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.0 },
                [ProgressStatus.Completed]  = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0 },
            };

        public static Dictionary<SubSystemStage, ColorSetting> SubSystemStageDefaults =>
            new Dictionary<SubSystemStage, ColorSetting>
            {
                [SubSystemStage.NotStarted] = new ColorSetting { DisplayColor = Color.FromArgb(169, 169, 169), Transparency = 0.7 },
                [SubSystemStage.Walkdown]   = new ColorSetting { DisplayColor = Color.FromArgb(255, 215, 0),   Transparency = 0.0 },
                [SubSystemStage.PartialMcc] = new ColorSetting { DisplayColor = Color.FromArgb(255, 140, 50),  Transparency = 0.0 },
                [SubSystemStage.Mcc]        = new ColorSetting { DisplayColor = Color.FromArgb(65, 105, 225),  Transparency = 0.0 },
                [SubSystemStage.Pcc]        = new ColorSetting { DisplayColor = Color.FromArgb(0, 128, 0),     Transparency = 0.0 },
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
        /// same way before lookup. OASIS EIT_Tray의 BRANCH NO.는 끝에 '.'이 붙은
        /// 행이 있어(실측 2026-07) 매칭이 깨졌다 — 모델 DisplayName엔 없는 장식이므로
        /// 후행 '.'도 제거한다.
        /// </summary>
        public static string NormalizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            return id.TrimStart('/').Trim().TrimEnd('.').Trim();
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
        Equipment,     // Mech_EQ — TAG NO 매칭 (SubSystemSearcher: MEQ·SPL·HYDROPKG)
        Piping,        // Piping_HydrotestPKG — PKGNO 매칭 (PKG 노드 색칠이 하위 스풀/배관을 커버)
        EitEquipment,  // EIT_EQ — TAG NO 매칭 (ElecTagSearcher: EIT 스코프). INSTALL DTE 단일 단계
        EitTray,       // EIT_Tray — BRANCH NO. 매칭 (ElecTagSearcher). Install % 기반 현재상태
        Cable,         // EIT_Cable — CABLE NO 매칭 (Sub-system 전용 레벨 타겟, CABLE 스코프)
    }

    public static class SubSystemDisciplineInfo
    {
        public static readonly Dictionary<SubSystemDiscipline, string> Labels = new Dictionary<SubSystemDiscipline, string>
        {
            [SubSystemDiscipline.Equipment]    = "Equipment",
            [SubSystemDiscipline.Piping]       = "Piping",
            [SubSystemDiscipline.EitEquipment] = "EIT EQ",
            [SubSystemDiscipline.EitTray]      = "EIT Tray",
            [SubSystemDiscipline.Cable]        = "Cable",
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
    /// Sub-system 탭의 통합 요소. 공종별 원본 데이터(EquipmentData/TestPackageData/
    /// EitTrayData/CableLineData/EIT_EQ 설치일)를 감싸 공통 축(Sub-system, 매칭 키,
    /// 진행 상태)으로 노출한다 — stage 계산은 원본 모델의 판정을 그대로 재사용한다.
    /// EIT Tray는 날짜가 없어(%기반) 기준일과 무관한 현재상태 판정 — 문서화된 예외.
    /// </summary>
    public class SubSystemElement
    {
        public string SubSystem { get; }
        public SubSystemDiscipline Discipline { get; }
        /// <summary>모델 매칭 키. Equipment/EIT EQ는 TAG NO, Piping은 PKGNO,
        /// EIT Tray는 정규화된 BRANCH NO., Cable은 CABLE NO.</summary>
        public string ElementId { get; }
        public string Description { get; }

        private readonly EquipmentData _equipment;
        private readonly TestPackageData _package;
        private readonly EitTrayData _tray;
        private readonly CableLineData _cable;
        private readonly DateTime? _eitInstallDate;   // EIT_EQ INSTALL DTE (단일 단계)

        private SubSystemElement(string subSystem, SubSystemDiscipline discipline,
            string elementId, string description,
            EquipmentData equipment = null, TestPackageData package = null,
            EitTrayData tray = null, CableLineData cable = null, DateTime? eitInstallDate = null)
        {
            SubSystem = subSystem;
            Discipline = discipline;
            ElementId = elementId;
            Description = description;
            _equipment = equipment;
            _package = package;
            _tray = tray;
            _cable = cable;
            _eitInstallDate = eitInstallDate;
        }

        public static SubSystemElement FromEquipment(EquipmentData eq) =>
            new SubSystemElement(eq.SubSystem?.Trim(), SubSystemDiscipline.Equipment,
                eq.TagNo, eq.Description ?? "", equipment: eq);

        public static SubSystemElement FromPackage(TestPackageData pkg) =>
            new SubSystemElement(pkg.SystemNo?.Trim(), SubSystemDiscipline.Piping,
                pkg.TestPkgId, pkg.LineService ?? "", package: pkg);

        /// <summary>EIT_EQ 행 — INSTALL DTE 단일 단계(미착수/설치완료). 선행 '/' 방어 정규화.</summary>
        public static SubSystemElement FromEitEquipment(string tagNo, string description,
            string subSystem, DateTime? installDate) =>
            new SubSystemElement(subSystem?.Trim(), SubSystemDiscipline.EitEquipment,
                EitTrayData.NormalizeId(tagNo), description ?? "", eitInstallDate: installDate);

        /// <summary>EIT Tray — ElementId는 모델 인덱스 키와 맞춘 정규화 BRANCH NO.(선행 '/'·후행 '.' 제거).</summary>
        public static SubSystemElement FromTray(EitTrayData tray, string subSystem) =>
            new SubSystemElement(subSystem?.Trim(), SubSystemDiscipline.EitTray,
                EitTrayData.NormalizeId(tray.TrayNumber), "", tray: tray);

        public static SubSystemElement FromCable(CableLineData cable) =>
            new SubSystemElement(cable.SubSystem?.Trim(), SubSystemDiscipline.Cable,
                cable.CableNo, cable.System ?? "", cable: cable);

        /// <summary>기준일 시점의 공종별 상세 단계 라벨 (리포트 상세 리스트용).
        /// EIT Tray만 예외적으로 기준일 무시(% 기반 현재상태 — 날짜 컬럼 부재).</summary>
        public string StageLabelAt(DateTime referenceDate)
        {
            if (_equipment != null)
                return EquipmentStageInfo.Labels[_equipment.GetStageAtDate(referenceDate)];
            if (_package != null)
                return HydrotestStageInfo.Labels[_package.GetStageAtDate(referenceDate)];
            if (_tray != null)
                return EitStageInfo.Labels[_tray.GetStage()];
            if (_cable != null)
                return CableLineStageInfo.Labels[_cable.GetStageAtDate(referenceDate)];
            // EIT_EQ 단일 단계
            return _eitInstallDate.HasValue && _eitInstallDate.Value.Date <= referenceDate.Date
                ? "설치완료" : "미착수";
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
            if (_package != null)
            {
                var pkgStage = _package.GetStageAtDate(referenceDate);
                if (pkgStage == HydrotestStage.NotStarted) return ProgressStatus.NotStarted;
                return pkgStage == HydrotestStage.Reinstatement ? ProgressStatus.Completed : ProgressStatus.InProgress;
            }
            if (_tray != null)
            {
                switch (_tray.GetStage())
                {
                    case EitStage.Installed: return ProgressStatus.Completed;
                    case EitStage.Installing: return ProgressStatus.InProgress;
                    default: return ProgressStatus.NotStarted;
                }
            }
            if (_cable != null)
            {
                var cableStage = _cable.GetStageAtDate(referenceDate);
                if (cableStage == CableLineStage.NotStarted) return ProgressStatus.NotStarted;
                return cableStage == CableLineStage.Terminated ? ProgressStatus.Completed : ProgressStatus.InProgress;
            }
            // EIT_EQ: 단일 단계 — 진행중 없음
            return _eitInstallDate.HasValue && _eitInstallDate.Value.Date <= referenceDate.Date
                ? ProgressStatus.Completed : ProgressStatus.NotStarted;
        }
    }

    /// <summary>
    /// Sub-system 마스터의 시운전 인계 마일스톤. 날짜 역순 스캔(GetStageAtDate)은
    /// 다른 공종과 동일 패턴 — "지난 날짜 = 달성" 가정.
    /// 별도 RFCC 단계는 없음 — MCC(또는 Partial MCC)가 Ready for Commissioning 의미.
    /// 마일스톤은 순차가 아닐 수 있음: P-MCC 없이 바로 MCC로 갈 수 있다. 역순 스캔은
    /// 날짜 보유 여부만 보므로 중간 단계 스킵을 자연스럽게 허용한다 (MCC 날짜만 있으면
    /// MCC로 판정 — 이 enum 순서는 시간 순서가 아니라 "달성 수준" 랭킹).
    /// </summary>
    public enum SubSystemStage
    {
        NotStarted,   // 0
        Walkdown,     // 1  Walkdown
        PartialMcc,   // 2  Partial MCC (부분 RFC)
        Mcc,          // 3  MCC = Ready for Commissioning (핵심 기점)
        Pcc,          // 4  PCC
    }

    public static class SubSystemStageInfo
    {
        /// <summary>날짜 역순 스캔에 쓰는 실적 단계.</summary>
        public static readonly SubSystemStage[] OrderedStages =
        {
            SubSystemStage.Walkdown, SubSystemStage.PartialMcc,
            SubSystemStage.Mcc, SubSystemStage.Pcc
        };

        /// <summary>색상 그리드 표시 순서 — 미착수 + 실적 단계.</summary>
        public static readonly SubSystemStage[] GridOrder =
        {
            SubSystemStage.NotStarted, SubSystemStage.Walkdown, SubSystemStage.PartialMcc,
            SubSystemStage.Mcc, SubSystemStage.Pcc,
        };

        public static readonly Dictionary<SubSystemStage, string> Labels = new Dictionary<SubSystemStage, string>
        {
            [SubSystemStage.NotStarted] = "미착수",
            [SubSystemStage.Walkdown]   = "Walkdown",
            [SubSystemStage.PartialMcc] = "P-MCC",
            [SubSystemStage.Mcc]        = "MCC",
            [SubSystemStage.Pcc]        = "PCC",
        };
    }

    /// <summary>
    /// Sub-system 마스터 행 ([Navis].[System_Summary] — 실측 스키마, CLAUDE.md 9·11번).
    /// 마일스톤 날짜 4개 + A/B/C-ITR·A/B Punch 수치(각 Total + 완료/종결).
    /// 선택 테이블·리포트에 status로 병기된다.
    /// </summary>
    public class SubSystemMasterData
    {
        public string SubSystemNo { get; set; }
        public string Description { get; set; }
        public Dictionary<SubSystemStage, DateTime?> StageDates { get; set; }
            = new Dictionary<SubSystemStage, DateTime?>();

        /// <summary>MCC 계획일 (핵심 기점). 실적(StageDates[Mcc])과 별개 — 지연 판정 기준.</summary>
        public DateTime? MccPlan { get; set; }

        public int? ItrATotal { get; set; }
        public int? ItrADone { get; set; }
        public int? ItrBTotal { get; set; }
        public int? ItrBDone { get; set; }
        public int? ItrCTotal { get; set; }
        public int? ItrCDone { get; set; }
        public int? PunchATotal { get; set; }
        public int? PunchAClosed { get; set; }   // 종결 수
        public int? PunchBTotal { get; set; }
        public int? PunchBClosed { get; set; }

        public SubSystemStage GetStageAtDate(DateTime referenceDate)
        {
            var stages = SubSystemStageInfo.OrderedStages;
            for (int i = stages.Length - 1; i >= 0; i--)
            {
                if (StageDates.TryGetValue(stages[i], out var date) && date.HasValue && date.Value.Date <= referenceDate.Date)
                    return stages[i];
            }
            return SubSystemStage.NotStarted;
        }

        /// <summary>
        /// 지연 = MCC 계획일이 기준일까지 도래했는데 P-MCC/MCC 실적이 아직 미입력
        /// (실적 단계가 P-MCC 미만). MCC가 핵심 기점이므로 계획일 경과 = 지연 관리 대상.
        /// P-MCC 또는 MCC 실적이 하나라도 있으면 지연 아님.
        /// </summary>
        public bool IsDelayed(DateTime referenceDate)
        {
            if (!MccPlan.HasValue || MccPlan.Value.Date > referenceDate.Date) return false;
            return GetStageAtDate(referenceDate) < SubSystemStage.PartialMcc;
        }

        /// <summary>지연일수 (계획일 경과 일수). 지연 아니면 0.</summary>
        public int DelayDays(DateTime referenceDate) =>
            IsDelayed(referenceDate) ? (referenceDate.Date - MccPlan.Value.Date).Days : 0;

        /// <summary>테이블/리포트 표기: 지연 시 "지연 Nd", 아니면 계획일(yy-MM-dd) 또는 "-".</summary>
        public string PlanText(DateTime referenceDate)
        {
            if (IsDelayed(referenceDate)) return $"지연 {DelayDays(referenceDate)}d";
            return MccPlan.HasValue ? MccPlan.Value.ToString("yy-MM-dd") : "-";
        }

        /// <summary>테이블/리포트 공통 표기: "완료(종결)/전체" (Total 미보유 시 "-").</summary>
        public static string Ratio(int? done, int? total) =>
            total.HasValue ? $"{done ?? 0}/{total}" : "-";

        public string ItrAText => Ratio(ItrADone, ItrATotal);
        public string ItrBText => Ratio(ItrBDone, ItrBTotal);
        public string ItrCText => Ratio(ItrCDone, ItrCTotal);
        public string PunchAText => Ratio(PunchAClosed, PunchATotal);
        public string PunchBText => Ratio(PunchBClosed, PunchBTotal);
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

    // ============================================================
    // Cable (형상 중심 — 07_Trion_All_Cable.nwd의 cable-no 컴포넌트를 직접 매칭)
    // 기존 Cable Pull(노드/박스 집계)과 별개. 한 행 = 한 케이블.
    // ============================================================

    /// <summary>
    /// 케이블 1가닥의 진척 단계. 날짜 역순 스캔(GetStageAtDate) — 다른 공종과 동일.
    /// Terminated(결선완료) = FROM CONN·TO CONN 둘 다 있을 때 (부분 결선을 완료로 안 찍음).
    /// </summary>
    public enum CableLineStage
    {
        NotStarted, // 0  착수 전
        Pulling,    // 1  포설중 (PULLING START)
        Pulled,     // 2  포설완료 (PULLING END)
        Terminated, // 3  결선완료 (FROM CONN AND TO CONN)
    }

    public static class CableLineStageInfo
    {
        public static readonly CableLineStage[] OrderedStages =
        {
            CableLineStage.Pulling, CableLineStage.Pulled, CableLineStage.Terminated
        };

        public static readonly Dictionary<CableLineStage, string> Labels = new Dictionary<CableLineStage, string>
        {
            [CableLineStage.NotStarted] = "미착수",
            [CableLineStage.Pulling]    = "포설중",
            [CableLineStage.Pulled]     = "포설완료",
            [CableLineStage.Terminated] = "결선완료",
        };
    }

    /// <summary>
    /// 케이블 1가닥 (형상 탭). 매칭 키 = Cable No (컴포넌트 DisplayName). 진척은 stage 날짜
    /// 역순 스캔. 길이/%는 표시 전용(§13-6 — PULLING LTH 의미 미확정이라 색에 안 씀).
    /// stage 날짜가 하나도 없으면(맨 목록) 탭이 '하이라이트 우선' 모드로 전환한다.
    /// </summary>
    public class CableLineData
    {
        public string CableNo { get; set; }   // Match key
        public Dictionary<CableLineStage, DateTime?> StageDates { get; set; }
            = new Dictionary<CableLineStage, DateTime?>();

        // 원시 결선 실적일 (상세/툴팁용). Terminated 계산은 둘 다 있을 때만.
        public DateTime? FromConnDate { get; set; }
        public DateTime? ToConnDate { get; set; }

        // 표시 전용
        public double? DesignLth { get; set; }
        public double? PulledLth { get; set; }
        public double? PullingProgress { get; set; }  // 0.0-1.0 (표시 전용)
        public string FromModule { get; set; }
        public string FromEquip { get; set; }
        public string ToModule { get; set; }
        public string ToEquip { get; set; }
        public string System { get; set; }
        public string SubSystem { get; set; }   // EIT_Cable [SUB-SYSTEM] — Sub-system 탭 편입 축
        public string Type { get; set; }
        public string Core { get; set; }
        public string Size { get; set; }
        public string OutDia { get; set; }
        public string TraySys { get; set; }
        public string Route { get; set; }

        /// <summary>stage 날짜가 하나라도 있는가 — 없으면 하이라이트 전용 모드.</summary>
        public bool HasAnyStageDate
        {
            get
            {
                foreach (var kv in StageDates)
                    if (kv.Value.HasValue) return true;
                return false;
            }
        }

        public CableLineStage GetStageAtDate(DateTime referenceDate)
        {
            var stages = CableLineStageInfo.OrderedStages;
            for (int i = stages.Length - 1; i >= 0; i--)
            {
                if (StageDates.TryGetValue(stages[i], out var date) && date.HasValue && date.Value.Date <= referenceDate.Date)
                    return stages[i];
            }
            return CableLineStage.NotStarted;
        }

        /// <summary>결선완료 = FROM CONN·TO CONN 둘 다 있을 때 Max(둘). StageDates[Terminated]에 세팅.</summary>
        public void ComputeTerminated()
        {
            if (FromConnDate.HasValue && ToConnDate.HasValue)
                StageDates[CableLineStage.Terminated] =
                    FromConnDate.Value >= ToConnDate.Value ? FromConnDate : ToConnDate;
        }

        /// <summary>
        /// 모델 인덱스는 DisplayName 선행 '/' 제거 + 대문자 정규화로 키를 만든다.
        /// Excel/OASIS Cable No도 동일 정규화 후 조회 (Windows 실측: 장식 문자 있으면 여기 확장).
        /// </summary>
        public static string NormalizeCableNo(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            return id.TrimStart('/').Trim();
        }
    }
}
