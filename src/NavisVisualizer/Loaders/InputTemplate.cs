using System;
using System.IO;
using System.Linq;

namespace NavisVisualizer.Loaders
{
    /// <summary>
    /// 실적 입력 양식(Input Template) 출력. 각 공종 ExcelLoader가 탐지하는 헤더
    /// 키워드와 정확히 일치하는 헤더 행 + 샘플 1행을 CSV로 저장한다 (Excel에서
    /// 바로 열림). 헤더명이 로더의 탐지 키워드와 어긋나면 Import가 실패하므로,
    /// ExcelLoader의 FindColumn 후보를 바꿀 때는 여기도 같이 갱신할 것.
    ///
    /// CSV는 ExcelDataReader가 읽지 않으므로 안내문에 "작성 후 .xlsx로 저장"을
    /// 명시한다. 안내문 행은 헤더 키워드를 포함하지 않아 헤더 자동 탐지(상위
    /// 20행 스캔)와 충돌하지 않고, .xlsx로 저장해 Import해도 그대로 무시된다.
    /// </summary>
    public static class InputTemplate
    {
        private const string Notice =
            "※ 입력 양식 — 헤더명을 변경하지 마세요. 작성 후 Excel 형식(.xlsx)으로 저장한 뒤 Excel Import 하세요.";

        public static string ExportSpool() => Write("Spool_Input_Template",
            new[]
            {
                "Spool Number", "ISO No",
                // Fabrication 12단계 + Install 2단계 — SpoolStageInfo.ColumnMap 키와 동일
                "B/V", "F/up", "W/D", "NDE", "PWHT", "S/out", "G-후공정인계", "Galv2",
                "Pnt1", "Pnt2", "Stock", "H/O일자", "Setting", "Welding",
            },
            new[]
            {
                "101210-SP-0012", "ISO-101210-01",
                "2026-01-05", "2026-01-08", "2026-01-12", "2026-01-15", "", "2026-01-20", "", "",
                "2026-02-01", "2026-02-05", "2026-02-10", "2026-02-15", "", "",
            });

        public static string ExportHydrotest() => Write("Hydrotest_Input_Template",
            new[]
            {
                "Test Package No.", "System No.", "Line Service",
                // HydrotestStageInfo.ColumnMap 키와 동일
                "Review", "Line Inspection", "Flushing", "Hydrotest", "Drying", "Reinstatement",
            },
            new[]
            {
                "101210-TP-001", "SYS-101", "CW",
                "2026-03-02", "2026-03-05", "", "", "", "",
            });

        public static string ExportEquipment() => Write("Equipment_Input_Template",
            new[]
            {
                "Tag No.", "Equipment Description", "SUB SYSTEM", "RFQ No.",
                // Delivery는 날짜가 아니라 "Delivered" 텍스트 + Confirmed ETA 날짜 조합
                "Delivery", "Confirmed ETA",
                // EquipmentStageInfo.ColumnMap 키와 동일
                "Loading", "Setting", "Inspection",
            },
            new[]
            {
                "101210-PBA-10240", "SEA WATER PUMP", "SS-101", "RFQ-0042",
                "Delivered", "2026-02-20",
                "2026-03-01", "2026-03-10", "",
            });

        public static string ExportEitTray() => Write("EitTray_Input_Template",
            new[]
            {
                "Tray Number", "Tray Lth", "Tray Installed", "Install %", "Tray install date",
            },
            new[]
            {
                "/101890-HVT-61003-SM-MEB/B1", "12.5", "12.5", "100%", "2026-04-01",
            });

        public static string ExportCablePull() => Write("CablePull_Input_Template",
            new[]
            {
                // 케이블 1가닥 × 경유 Node 1개 = 1행 (같은 케이블이 노드 수만큼 반복)
                "Node", "Count", "Equip No", "Route Sys", "Cable No",
                "Cable Design Lth", "Cable Pulled Lth", "Pulling %",
                "From Module", "From Equip", "To Module", "To Equip", "Install Module",
                "System", "Type", "Core", "Size", "Out Dia", "Tray Sys", "Design Lth", "Layer Code",
            },
            new[]
            {
                "101780-EMCT-52101_A-ND", "1", "101780-EMCT-52101", "R1", "CB-52101-001",
                "120", "80", "67%",
                "CM", "SWBD-01", "SM", "MCC-03", "SM",
                "ELEC", "PWR", "3C", "95SQ", "32", "T1", "115", "L2",
            });

        public static string ExportCable() => Write("Cable_Input_Template",
            new[]
            {
                // 한 행 = 한 케이블 (형상 탭). Cable No만 필수 — 나머지 비우면 하이라이트 전용 모드.
                // ExcelLoader.LoadCable의 FindColumn 후보와 1:1 (바꾸면 여기도 갱신).
                "Cable No", "Pulling Start", "Pulling End", "From Conn", "To Conn",
                "Cable Design Lth", "Cable Pulled Lth", "Pulling %",
                "From Module", "From Equip", "To Module", "To Equip",
                "System", "Type", "Core", "Size", "Out Dia", "Tray Sys", "Route",
            },
            new[]
            {
                "CB-52101-001", "2026-04-01", "2026-04-03", "2026-04-05", "2026-04-06",
                "120", "80", "67%",
                "CM", "SWBD-01", "SM", "MCC-03",
                "PWR", "PWR", "3C", "95SQ", "32", "T1", "LQ_LT_IN_LV1048",
            });

        private static string Write(string baseName, string[] header, string[] sample)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"{baseName}.csv");
            var lines = new[]
            {
                Csv(new[] { Notice }),
                Csv(header),
                Csv(sample),
            };
            // UTF-8 BOM so Excel renders Korean headers correctly.
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            return path;
        }

        private static string Csv(string[] cells) =>
            string.Join(",", cells.Select(c => $"\"{(c ?? "").Replace("\"", "'")}\""));
    }
}
