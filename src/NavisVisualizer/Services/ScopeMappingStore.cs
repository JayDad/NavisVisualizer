using System;
using System.Collections.Generic;
using System.IO;
using NavisVisualizer.Searchers;

namespace NavisVisualizer.Services
{
    /// <summary>
    /// 스코프 매핑 영속화 — %APPDATA%\NavisVisualizer\ 아래 (oasis.config·error.log와 같은 폴더).
    ///   scope_aliases.cfg      프로파일 별칭 덮어쓰기 (전 문서 공통)
    ///   scopes\{문서명}.scope.cfg  문서별 프로파일 선택 + 공종별 파일 지정
    /// 읽기 실패는 "설정 없음"으로, 쓰기 실패는 예외로 (호출 UI가 안내). Autodesk 비의존.
    /// </summary>
    public static class ScopeMappingStore
    {
        public static string BaseDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NavisVisualizer");

        public static string AliasesPath => Path.Combine(BaseDir, "scope_aliases.cfg");
        public static string ScopesDir => Path.Combine(BaseDir, "scopes");

        public static string ConfigPathFor(string docFileName) =>
            Path.Combine(ScopesDir, ScopeMappingConfig.ConfigFileNameFor(docFileName));

        /// <summary>내장 프로파일 + 사용자 별칭 파일 적용 결과. 파싱 오류는 errors로.</summary>
        public static List<ScopeProfile> LoadProfiles(List<string> errors = null)
        {
            var profiles = ScopeProfiles.BuiltIn();
            try
            {
                if (File.Exists(AliasesPath))
                    profiles = ScopeProfiles.ApplyOverrides(profiles, File.ReadAllLines(AliasesPath), errors);
            }
            catch (Exception ex)
            {
                errors?.Add($"별칭 파일 읽기 실패: {ex.Message}");
            }
            return profiles;
        }

        public static void SaveProfiles(IEnumerable<ScopeProfile> profiles)
        {
            Directory.CreateDirectory(BaseDir);
            File.WriteAllLines(AliasesPath, ScopeProfiles.ToOverrideLines(profiles), System.Text.Encoding.UTF8);
        }

        public static ScopeMappingConfig LoadConfig(string docFileName)
        {
            try
            {
                string path = ConfigPathFor(docFileName);
                if (File.Exists(path))
                    return ScopeMappingConfig.Parse(File.ReadAllLines(path));
            }
            catch { /* 손상·권한 문제는 설정 없음으로 */ }
            return new ScopeMappingConfig();
        }

        public static void SaveConfig(string docFileName, ScopeMappingConfig cfg)
        {
            string path = ConfigPathFor(docFileName);
            if (cfg == null || cfg.IsEmpty)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            Directory.CreateDirectory(ScopesDir);
            File.WriteAllLines(path, cfg.ToLines(), System.Text.Encoding.UTF8);
        }
    }
}
