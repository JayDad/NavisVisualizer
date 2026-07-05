using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

namespace NavisVisualizer.Loaders
{
    /// <summary>
    /// OASIS(사내 SQL Server) 연결 설정. 플러그인 DLL 옆에 배포되는 oasis.config
    /// (key=value 텍스트)를 읽는다. 개인 PC별 예외가 필요하면
    /// %APPDATA%\NavisVisualizer\oasis.config 가 있을 때 그 파일이 우선한다.
    ///
    /// oasis.config 예:
    ///   server=192.168.10.5
    ///   database=TEST_NAVIS
    ///   user=navis_ro
    ///   password=****
    ///   project=Q557
    ///
    /// 사내망 전용 전제로 암호를 평문 보관한다. 실질 보안 경계는 DB 계정 권한 —
    /// 반드시 SELECT 전용(읽기 전용) 계정을 사용할 것.
    /// </summary>
    public class SqlConnectionSettings
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
        /// <summary>true면 Windows 인증(user/password 무시). 기본 false(SQL 인증).</summary>
        public bool IntegratedSecurity { get; set; }
        /// <summary>PJTNO/PRJTNO 필터 값. 비우면 전체 프로젝트 로드.</summary>
        public string ProjectNo { get; set; } = "";
        public int ConnectTimeoutSeconds { get; set; } = 5;

        /// <summary>설정을 읽어온 파일 경로 (오류 메시지 안내용).</summary>
        public string SourcePath { get; set; } = "";

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(Server)
            && !string.IsNullOrWhiteSpace(Database)
            && (IntegratedSecurity || !string.IsNullOrWhiteSpace(User));

        public const string FileName = "oasis.config";

        public static string PluginConfigPath
        {
            get
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                return Path.Combine(dir, FileName);
            }
        }

        public static string UserConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NavisVisualizer", FileName);

        /// <summary>
        /// 설정 파일 로드. APPDATA 오버라이드 → DLL 폴더 순으로 찾는다.
        /// 파일이 없으면 안내 메시지를 담은 예외를 던진다.
        /// </summary>
        public static SqlConnectionSettings Load()
        {
            string path;
            if (File.Exists(UserConfigPath)) path = UserConfigPath;
            else if (File.Exists(PluginConfigPath)) path = PluginConfigPath;
            else
                throw new FileNotFoundException(
                    $"OASIS 연결 설정 파일이 없습니다.\n" +
                    $"다음 위치에 {FileName}을 배치하세요:\n" +
                    $"  {PluginConfigPath}\n" +
                    $"(개인 설정: {UserConfigPath})");

            var settings = Parse(File.ReadAllLines(path));
            settings.SourcePath = path;

            if (!settings.IsComplete)
                throw new InvalidDataException(
                    $"OASIS 연결 설정이 불완전합니다 ({path}).\n" +
                    "server, database, user(또는 integrated=true) 값이 필요합니다.");

            return settings;
        }

        internal static SqlConnectionSettings Parse(IEnumerable<string> lines)
        {
            var s = new SqlConnectionSettings();
            foreach (var raw in lines)
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";"))
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "server":     s.Server = val; break;
                    case "database":   s.Database = val; break;
                    case "user":       s.User = val; break;
                    case "password":   s.Password = val; break;
                    case "project":    s.ProjectNo = val; break;
                    case "integrated": s.IntegratedSecurity = val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1"; break;
                    case "timeout":
                        if (int.TryParse(val, out int t) && t > 0) s.ConnectTimeoutSeconds = t;
                        break;
                }
            }
            return s;
        }

        public string BuildConnectionString()
        {
            var b = new SqlConnectionStringBuilder
            {
                DataSource = Server,
                InitialCatalog = Database,
                ConnectTimeout = ConnectTimeoutSeconds,
                ApplicationName = "NavisVisualizer",
            };
            if (IntegratedSecurity)
            {
                b.IntegratedSecurity = true;
            }
            else
            {
                b.UserID = User;
                b.Password = Password;
            }
            return b.ConnectionString;
        }
    }
}
