using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisVisualizer.Searchers
{
    /// <summary>스코프 하나의 파일 결정 방식.</summary>
    public enum ScopeSelectionMode
    {
        /// <summary>프로파일 별칭으로 자동 인식 (기본).</summary>
        Auto,
        /// <summary>사용자가 파일 노드를 직접 선택 (복수 가능, 하위 파일 제외 가능).</summary>
        Files,
        /// <summary>전체 모델을 대상으로 (느림 — 사용자가 명시적으로 고른 경우만).</summary>
        AllModels,
    }

    public sealed class ScopeSelection
    {
        public ScopeSelectionMode Mode = ScopeSelectionMode.Auto;

        /// <summary>Files 모드의 대상 파일 노드 이름(디렉터리 제외, 대소문자 무시). 선택한 파일 안의
        /// 하위 파일 노드 중 여기 없는 것은 walk에서 제외된다 (예: SPOOL 선택 + SUP 미선택 → SUP 건너뜀).</summary>
        public List<string> Files = new List<string>();

        public bool IsAuto => Mode == ScopeSelectionMode.Auto;

        public ScopeSelection Clone()
        {
            var c = new ScopeSelection { Mode = Mode };
            c.Files.AddRange(Files);
            return c;
        }
    }

    /// <summary>
    /// 문서(nwd) 하나의 스코프 매핑 설정 — 프로파일 이름 + 스코프 Key별 선택. 저장 형식은 줄 단위
    /// key=value (oasis.config와 같은 계열, JSON 라이브러리 불필요·Autodesk 비의존):
    /// <code>
    /// profile=Ruya
    /// Spool=files:BJ-RUY-SPOOL.nwd|BJ-RUY-SUP.nwd
    /// Hydrotest=all
    /// Equipment=auto            ← 기록 생략과 동일
    /// </code>
    /// </summary>
    public sealed class ScopeMappingConfig
    {
        /// <summary>null/빈 값 = 문서 파일명으로 자동 감지.</summary>
        public string ProfileName;

        public Dictionary<string, ScopeSelection> Selections { get; } =
            new Dictionary<string, ScopeSelection>(StringComparer.OrdinalIgnoreCase);

        public ScopeSelection Get(string scopeKey)
        {
            if (scopeKey != null && Selections.TryGetValue(scopeKey, out var s)) return s;
            return new ScopeSelection();
        }

        public void Set(string scopeKey, ScopeSelection selection)
        {
            if (selection == null || selection.IsAuto) Selections.Remove(scopeKey);
            else Selections[scopeKey] = selection;
        }

        public bool IsEmpty => string.IsNullOrEmpty(ProfileName) && Selections.Count == 0;

        public static ScopeMappingConfig Parse(IEnumerable<string> lines)
        {
            var cfg = new ScopeMappingConfig();
            if (lines == null) return cfg;
            foreach (var raw in lines)
            {
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (string.Equals(key, "profile", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.ProfileName = string.IsNullOrEmpty(val) ? null : val;
                    continue;
                }
                var scope = NwdScope.FindByKey(key);
                if (scope == null) continue;   // 모르는 키(구 버전 등)는 무시

                var sel = ParseSelection(val);
                if (sel != null) cfg.Set(scope.Key, sel);
            }
            return cfg;
        }

        private static ScopeSelection ParseSelection(string val)
        {
            if (string.IsNullOrEmpty(val) || string.Equals(val, "auto", StringComparison.OrdinalIgnoreCase))
                return new ScopeSelection();
            if (string.Equals(val, "all", StringComparison.OrdinalIgnoreCase))
                return new ScopeSelection { Mode = ScopeSelectionMode.AllModels };
            if (val.StartsWith("files:", StringComparison.OrdinalIgnoreCase))
            {
                var sel = new ScopeSelection { Mode = ScopeSelectionMode.Files };
                sel.Files.AddRange(ScopeProfiles.SplitList(val.Substring(6)));
                return sel;
            }
            return null;
        }

        public List<string> ToLines()
        {
            var lines = new List<string>
            {
                "# NavisVisualizer 문서별 스코프 매핑 — Overview > 매핑에서 저장됨 (수동 편집 가능)",
            };
            if (!string.IsNullOrEmpty(ProfileName)) lines.Add($"profile={ProfileName}");
            foreach (var scope in NwdScope.All)
            {
                if (!Selections.TryGetValue(scope.Key, out var sel) || sel.IsAuto) continue;
                lines.Add(sel.Mode == ScopeSelectionMode.AllModels
                    ? $"{scope.Key}=all"
                    : $"{scope.Key}=files:{string.Join("|", sel.Files)}");
            }
            return lines;
        }

        /// <summary>문서 파일 경로 → 설정 파일 이름 (파일명만, 파일명에 못 쓰는 문자는 '_').
        /// 같은 이름의 문서는 같은 매핑을 공유한다 — 리비전(_260911 등) 접미가 바뀌면 별개 설정.</summary>
        public static string ConfigFileNameFor(string docFileName)
        {
            string name = NwdScope.StripDirectory(docFileName ?? "");
            if (string.IsNullOrWhiteSpace(name)) name = "untitled";
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            return sb.ToString() + ".scope.cfg";
        }
    }
}
