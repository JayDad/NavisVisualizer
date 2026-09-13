using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NavisVisualizer.Services
{
    /// <summary>일괄 갱신(배치)이 다루는 공종 — 탭 상태 없이 "로드→인덱스→적용" 3줄로 끝나는 5개.
    /// Sub-system은 선택 목록·모드 상태가 탭에 있어 제외 (CLAUDE.md §19).</summary>
    public enum BatchDiscipline
    {
        Spool,
        Hydrotest,
        Equipment,
        EitTray,
        Cable,
    }

    public static class BatchDisciplineInfo
    {
        public static readonly BatchDiscipline[] Ordered =
        {
            BatchDiscipline.Spool,
            BatchDiscipline.Hydrotest,
            BatchDiscipline.Equipment,
            BatchDiscipline.EitTray,
            BatchDiscipline.Cable,
        };

        public static string Label(BatchDiscipline d)
        {
            switch (d)
            {
                case BatchDiscipline.Spool:     return "Spool";
                case BatchDiscipline.Hydrotest: return "Hydrotest";
                case BatchDiscipline.Equipment: return "Equipment";
                case BatchDiscipline.EitTray:   return "EIT Tray";
                case BatchDiscipline.Cable:     return "Cable";
                default: return d.ToString();
            }
        }

        /// <summary>설정 파일의 느슨한 표기(대소문자·별칭)를 enum으로. 모르는 값은 null.</summary>
        public static BatchDiscipline? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim().Replace(" ", "").Replace("-", "").Replace("_", "").ToUpperInvariant();
            switch (t)
            {
                case "SPOOL":     case "SPL":                       return BatchDiscipline.Spool;
                case "HYDROTEST": case "HYDRO":  case "HYDROPKG":   return BatchDiscipline.Hydrotest;
                case "EQUIPMENT": case "EQUIP":  case "MEQ":        return BatchDiscipline.Equipment;
                case "EITTRAY":   case "EIT":    case "TRAY":       return BatchDiscipline.EitTray;
                case "CABLE":     case "CABLELINE":                 return BatchDiscipline.Cable;
                default: return null;
            }
        }
    }

    /// <summary>배치 작업 1건 = 모델 파일 1개 + 그 모델에 돌릴 공종 조합.</summary>
    public class BatchJob
    {
        public string Name { get; set; } = "";
        public string ModelPath { get; set; } = "";
        /// <summary>OASIS PJTNO/PRJTNO 필터. 비우면 oasis.config의 project 값 사용.
        /// 프로젝트 2개를 한 DB에서 읽는 구성이라 job마다 지정할 수 있어야 한다.</summary>
        public string ProjectNo { get; set; } = "";
        /// <summary>마지막 실행 때 이 job이 체크돼 있었는가 (다음 실행 창의 기본값).</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>마지막 실행 때 체크된 공종 (다음 실행 창의 기본값 — "어제 조합 기억").</summary>
        public HashSet<BatchDiscipline> Disciplines { get; set; } = new HashSet<BatchDiscipline>();

        public string ModelFileNameWithoutExt
        {
            get
            {
                try { return Path.GetFileNameWithoutExtension(ModelPath) ?? ""; }
                catch { return ""; }
            }
        }
    }

    /// <summary>
    /// 일괄 갱신 설정 — %APPDATA%\NavisVisualizer\batch.config (oasis.config와 같은 폴더·같은
    /// key=value 형식). JSON 대신 이 형식을 쓰는 이유: Windows 경로의 백슬래시를 이스케이프 없이
    /// 그대로 적을 수 있어 사용자가 손으로 고칠 때 실수가 없다.
    ///
    ///   [settings]
    ///   outputFolder=D:\발행
    ///   fileNamePattern={model}_{date}
    ///
    ///   [job:Trion]
    ///   model=D:\Models\00-02_Trion_Topsides_Subsystem.nwd
    ///   project=Q557
    ///   enabled=true
    ///   disciplines=Spool,Hydrotest
    ///
    /// enabled/disciplines는 실행 창에서 마지막 선택을 되써 넣는다 (기억 기능). 사용자가 직접
    /// 편집하는 파일이므로 저장 시에도 주석 헤더를 포함해 사람이 읽을 수 있게 유지한다.
    /// Autodesk 비의존.
    /// </summary>
    public class BatchConfig
    {
        public const string FileName = "batch.config";
        public const string DefaultFileNamePattern = "{model}_{date}";

        /// <summary>저장 폴더. 비우면 모델 파일과 같은 폴더.</summary>
        public string OutputFolder { get; set; } = "";
        /// <summary>출력 파일명 패턴. 토큰: {model}=모델 파일명(확장자 제외) / {name}=job 이름 /
        /// {date}=yyyyMMdd / {time}=HHmm. 확장자 .nwd는 자동.</summary>
        public string FileNamePattern { get; set; } = DefaultFileNamePattern;
        public List<BatchJob> Jobs { get; } = new List<BatchJob>();
        /// <summary>읽어온 파일 경로 (안내용). 기본 템플릿으로 생성됐으면 그 경로.</summary>
        public string SourcePath { get; set; } = "";

        public static string UserConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NavisVisualizer", FileName);

        public static string PluginConfigPath
        {
            get
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                return Path.Combine(dir, FileName);
            }
        }

        /// <summary>
        /// APPDATA → 플러그인 폴더 순으로 찾는다. 둘 다 없으면 placeholder 템플릿을 APPDATA에
        /// 생성하고 그것을 반환한다 (첫 실행에서 "설정 파일 열기"로 바로 고칠 수 있게).
        /// </summary>
        public static BatchConfig Load()
        {
            string path;
            if (File.Exists(UserConfigPath)) path = UserConfigPath;
            else if (File.Exists(PluginConfigPath)) path = PluginConfigPath;
            else
            {
                var fresh = Parse(DefaultTemplate.Split('\n'));
                fresh.SourcePath = UserConfigPath;
                try { fresh.Save(); } catch { /* 생성 실패해도 메모리 설정으로 동작 */ }
                return fresh;
            }

            var cfg = Parse(File.ReadAllLines(path, Encoding.UTF8));
            cfg.SourcePath = path;
            return cfg;
        }

        /// <summary>항상 APPDATA 경로에 저장 (플러그인 폴더 파일은 배포 원본이라 안 건드림).</summary>
        public void Save()
        {
            string path = UserConfigPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Serialize(), new UTF8Encoding(false));
            SourcePath = path;
        }

        public static BatchConfig Parse(IEnumerable<string> lines)
        {
            var cfg = new BatchConfig();
            BatchJob current = null;   // null = [settings] 또는 섹션 전
            bool inSettings = true;

            foreach (var raw in lines)
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string section = line.Substring(1, line.Length - 2).Trim();
                    if (section.Equals("settings", StringComparison.OrdinalIgnoreCase))
                    {
                        current = null;
                        inSettings = true;
                    }
                    else if (section.StartsWith("job:", StringComparison.OrdinalIgnoreCase))
                    {
                        current = new BatchJob { Name = section.Substring(4).Trim() };
                        if (current.Name.Length == 0) current.Name = $"job{cfg.Jobs.Count + 1}";
                        cfg.Jobs.Add(current);
                        inSettings = false;
                    }
                    else
                    {
                        current = null;
                        inSettings = false;   // 모르는 섹션 — 키 무시
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();

                if (current != null)
                {
                    switch (key)
                    {
                        case "model":   current.ModelPath = val; break;
                        case "project": current.ProjectNo = val; break;
                        case "enabled": current.Enabled = IsTrue(val); break;
                        case "disciplines":
                            current.Disciplines = new HashSet<BatchDiscipline>(
                                val.Split(',').Select(BatchDisciplineInfo.Parse)
                                   .Where(d => d.HasValue).Select(d => d.Value));
                            break;
                    }
                }
                else if (inSettings)
                {
                    switch (key)
                    {
                        case "outputfolder":     cfg.OutputFolder = val; break;
                        case "filenamepattern":
                            if (!string.IsNullOrWhiteSpace(val)) cfg.FileNamePattern = val;
                            break;
                    }
                }
            }
            return cfg;
        }

        private static bool IsTrue(string val) =>
            val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1"
            || val.Equals("yes", StringComparison.OrdinalIgnoreCase);

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# NavisVisualizer 일괄 갱신(배치) 설정");
            sb.AppendLine("# - 이 파일은 실행 창에서 마지막 선택(enabled/disciplines)을 자동으로 되써 넣습니다.");
            sb.AppendLine("# - model 경로·project·outputFolder는 직접 고치세요 (백슬래시 그대로, 따옴표 없이).");
            sb.AppendLine("# - disciplines 가능값: Spool, Hydrotest, Equipment, EitTray, Cable");
            sb.AppendLine("# - fileNamePattern 토큰: {model} {name} {date}(yyyyMMdd) {time}(HHmm). 확장자 .nwd 자동.");
            sb.AppendLine();
            sb.AppendLine("[settings]");
            sb.AppendLine($"outputFolder={OutputFolder}");
            sb.AppendLine($"fileNamePattern={FileNamePattern}");
            foreach (var job in Jobs)
            {
                sb.AppendLine();
                sb.AppendLine($"[job:{job.Name}]");
                sb.AppendLine($"model={job.ModelPath}");
                sb.AppendLine($"project={job.ProjectNo}");
                sb.AppendLine($"enabled={(job.Enabled ? "true" : "false")}");
                sb.AppendLine("disciplines=" + string.Join(",",
                    BatchDisciplineInfo.Ordered.Where(d => job.Disciplines.Contains(d)).Select(d => d.ToString())));
            }
            return sb.ToString();
        }

        /// <summary>출력 NWD 전체 경로. 폴더 미지정이면 모델 파일 옆.</summary>
        public string BuildOutputPath(BatchJob job, DateTime at)
        {
            string folder = string.IsNullOrWhiteSpace(OutputFolder)
                ? (Path.GetDirectoryName(job.ModelPath) ?? "")
                : OutputFolder;
            string pattern = string.IsNullOrWhiteSpace(FileNamePattern) ? DefaultFileNamePattern : FileNamePattern;
            string name = pattern
                .Replace("{model}", job.ModelFileNameWithoutExt)
                .Replace("{name}", job.Name)
                .Replace("{date}", at.ToString("yyyyMMdd"))
                .Replace("{time}", at.ToString("HHmm"));
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            if (!name.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase))
                name += ".nwd";
            return Path.Combine(folder, name);
        }

        /// <summary>첫 실행용 placeholder — 경로만 바꾸면 되도록 두 프로젝트 골격을 미리 둔다.</summary>
        public const string DefaultTemplate =
@"[settings]
outputFolder=
fileNamePattern={model}_{date}

[job:Project A]
model=C:\CHANGE_ME\ProjectA_Subsystem.nwd
project=
enabled=true
disciplines=Spool,Hydrotest

[job:Project B]
model=C:\CHANGE_ME\ProjectB_Subsystem.nwd
project=
enabled=false
disciplines=Equipment,Cable
";
    }
}
