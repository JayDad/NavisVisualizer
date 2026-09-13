using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisVisualizer.Searchers
{
    /// <summary>
    /// 프로젝트별 NWD 파일명 별칭(alias) 프로파일 — "어느 파일이 어느 공종인가"를 코드에서
    /// 분리한다 (2026-09, Ruya 대응). 프로파일 = 이름 + 문서 파일명 감지 패턴 + 스코프 Key별 별칭 목록.
    ///
    /// - 별칭 매칭은 기존과 같은 "파일명 부분일치·대소문자 무시" (NwdScope.MatchesFileName).
    /// - 어떤 스코프의 별칭이 **비어 있으면 자동 인식 없음** — 그 공종은 사용자가 Overview 매핑에서
    ///   파일을 직접 지정한다 (추정 별칭을 기본값으로 박아 잘못된 매핑이 조용히 굳는 것을 막는다.
    ///   Ruya는 파일명이 자명한 SPOOL/MEC/STR만 내장 — 사용자 결정 2026-09).
    /// - 사용자는 %APPDATA%\NavisVisualizer\scope_aliases.cfg 로 별칭을 덮어쓰거나 새 프로파일을
    ///   추가할 수 있다 (<see cref="ApplyOverrides"/> — 형식은 그 메서드 주석).
    /// Autodesk 비의존 — 리눅스에서 단위 테스트 가능.
    /// </summary>
    public sealed class ScopeProfile
    {
        public string Name { get; }

        /// <summary>문서 파일명(디렉터리 제외)에 이 중 하나가 부분일치하면 자동 선택. 비면 자동 선택 안 됨.</summary>
        public List<string> DocPatterns { get; } = new List<string>();

        /// <summary>스코프 Key → 별칭 목록. 없는 Key는 NwdScope 기본 키워드(Trion)로 처리되므로,
        /// "자동 인식 없음"을 원하면 빈 목록을 명시적으로 넣는다.</summary>
        public Dictionary<string, List<string>> Aliases { get; } =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public ScopeProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("프로파일 이름이 비어 있습니다.", nameof(name));
            Name = name.Trim();
        }

        public ScopeProfile WithPatterns(params string[] patterns)
        {
            foreach (var p in patterns) if (!string.IsNullOrWhiteSpace(p)) DocPatterns.Add(p.Trim());
            return this;
        }

        public ScopeProfile WithAlias(string scopeKey, params string[] keywords)
        {
            Aliases[scopeKey] = keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList();
            return this;
        }

        /// <summary>이 프로파일이 아는 Key면 그 별칭(빈 목록 가능), 모르면 null (→ NwdScope 기본).</summary>
        public IReadOnlyList<string> AliasesFor(string scopeKey) =>
            Aliases.TryGetValue(scopeKey, out var list) ? list : null;

        public bool MatchesDocName(string docFileName)
        {
            if (string.IsNullOrEmpty(docFileName) || DocPatterns.Count == 0) return false;
            string name = NwdScope.StripDirectory(docFileName);
            return DocPatterns.Any(p => name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>표시용 — "Spool: SPL, SPOOL · Equipment: MEC" 형식.</summary>
        public string Describe()
        {
            var parts = new List<string>();
            foreach (var scope in NwdScope.All)
            {
                var a = AliasesFor(scope.Key) ?? scope.DefaultKeywords;
                parts.Add($"{scope.Key}: {(a.Count > 0 ? string.Join(", ", a) : "(없음)")}");
            }
            return string.Join(" · ", parts);
        }
    }

    public static class ScopeProfiles
    {
        /// <summary>Trion — NwdScope 기본 키워드와 동일(별칭 항목 없음 = 기본 사용). 프로파일 미감지 시 기본.</summary>
        public static ScopeProfile Trion() =>
            new ScopeProfile("Trion").WithPatterns("Trion");

        /// <summary>
        /// Ruya (BJ-RUY-* 규약). 파일명이 자명한 세 공종만 내장:
        ///   Spool → BJ-RUY-SPOOL.nwd / Equipment → BJ-RUY-MEC.nwd / Structure → BJ-RUY-STR.nwd.
        /// Hydrotest(패키지 파일 없음 — SPOOL 내부 추정)·EIT(ELE/INS/TEL 3분할)·Cable(전용 파일 없음)은
        /// 실 모델 확인 전이라 **빈 별칭 = 현장에서 파일 지정**. 별칭 충돌 검토(2026-09): Ruya 15개 파일명
        /// 중 SPOOL/MEC/STR은 각각 한 파일에만 부분일치 (CONST·Input_To_Others에 STR/INS 미포함).
        /// </summary>
        public static ScopeProfile Ruya() =>
            new ScopeProfile("Ruya")
                .WithPatterns("RUYA", "RUY")
                .WithAlias(NwdScope.Spool.Key, "SPOOL")
                .WithAlias(NwdScope.Hydrotest.Key)          // 없음 → 현장 지정
                .WithAlias(NwdScope.Equipment.Key, "MEC")
                .WithAlias(NwdScope.EitTray.Key)            // 없음 → 현장 지정 (ELE 추정)
                .WithAlias(NwdScope.Eit.Key)                // 없음 → 현장 지정 (ELE/INS/TEL 추정)
                .WithAlias(NwdScope.Cable.Key)              // 없음 → 현장 지정
                .WithAlias(NwdScope.Structure.Key, "STR");

        /// <summary>내장 프로파일 (순서 = 감지 우선순위·UI 표시 순서).</summary>
        public static List<ScopeProfile> BuiltIn() => new List<ScopeProfile> { Trion(), Ruya() };

        public const string DefaultProfileName = "Trion";

        /// <summary>
        /// 문서 파일명으로 프로파일 자동 감지 — 패턴이 맞는 첫 프로파일, 없으면 Trion(기본 키워드).
        /// 컨테이너 nwd 이름(예: RUYA-progress_260911.nwd)이 기준이라 개별 공종 파일만 연 경우
        /// (BJ-RUY-MEC.nwd)도 "RUY" 패턴으로 잡힌다.
        /// </summary>
        public static ScopeProfile Detect(string docFileName, IEnumerable<ScopeProfile> profiles)
        {
            var list = profiles?.ToList() ?? BuiltIn();
            foreach (var p in list)
                if (p.MatchesDocName(docFileName)) return p;
            return list.FirstOrDefault(p => string.Equals(p.Name, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault() ?? Trion();
        }

        public static ScopeProfile FindByName(string name, IEnumerable<ScopeProfile> profiles)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return profiles?.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 사용자 별칭 파일(scope_aliases.cfg) 적용. 한 줄 = 한 항목, '#' 주석:
        /// <code>
        /// Ruya.Spool=SPOOL|SPL          ← 프로파일.스코프Key=별칭|별칭 (비우면 자동 인식 없음)
        /// Ruya.pattern=RUYA|RUY         ← 문서 파일명 감지 패턴 (선택)
        /// Kaombo.Equipment=EQP          ← 없는 프로파일 이름이면 새 프로파일 생성
        /// </code>
        /// 내장 프로파일의 같은 Key는 덮어쓴다(병합 아님 — 별칭을 빼고 싶을 수도 있으므로).
        /// 잘못된 줄은 무시하고 <paramref name="errors"/>에 사유를 남긴다 (설정 오타로 플러그인이 죽지 않게).
        /// </summary>
        public static List<ScopeProfile> ApplyOverrides(IEnumerable<ScopeProfile> baseProfiles,
            IEnumerable<string> lines, List<string> errors = null)
        {
            var result = baseProfiles.ToList();
            if (lines == null) return result;

            int lineNo = 0;
            foreach (var raw in lines)
            {
                lineNo++;
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                int eq = line.IndexOf('=');
                int dot = eq > 0 ? line.LastIndexOf('.', eq) : -1;
                if (eq <= 0 || dot <= 0)
                {
                    errors?.Add($"{lineNo}행: '프로파일.스코프=별칭' 형식이 아님 — {line}");
                    continue;
                }
                string profileName = line.Substring(0, dot).Trim();
                string field = line.Substring(dot + 1, eq - dot - 1).Trim();
                var values = SplitList(line.Substring(eq + 1));

                var profile = FindByName(profileName, result);
                if (profile == null)
                {
                    profile = new ScopeProfile(profileName);
                    result.Add(profile);
                }

                if (string.Equals(field, "pattern", StringComparison.OrdinalIgnoreCase))
                {
                    profile.DocPatterns.Clear();
                    profile.DocPatterns.AddRange(values);
                    continue;
                }

                var scope = NwdScope.FindByKey(field);
                if (scope == null)
                {
                    errors?.Add($"{lineNo}행: 알 수 없는 스코프 '{field}' (가능: {string.Join(", ", NwdScope.All.Select(s => s.Key))}, pattern)");
                    continue;
                }
                profile.Aliases[scope.Key] = values;
            }
            return result;
        }

        /// <summary>프로파일 전체를 별칭 파일 형식으로 직렬화 (UI에서 별칭을 편집·저장할 때).</summary>
        public static List<string> ToOverrideLines(IEnumerable<ScopeProfile> profiles)
        {
            var lines = new List<string>
            {
                "# NavisVisualizer 스코프 별칭 — 프로파일.스코프=별칭|별칭 (비우면 자동 인식 없음, 파일 직접 지정)",
                "# pattern = 문서 파일명 감지 패턴. 이 파일은 플러그인 UI(Overview > 매핑)에서 저장됨.",
            };
            foreach (var p in profiles)
            {
                if (p.DocPatterns.Count > 0)
                    lines.Add($"{p.Name}.pattern={string.Join("|", p.DocPatterns)}");
                foreach (var scope in NwdScope.All)
                {
                    var a = p.AliasesFor(scope.Key);
                    if (a == null) continue;   // 기본 키워드 사용 → 기록 안 함
                    lines.Add($"{p.Name}.{scope.Key}={string.Join("|", a)}");
                }
            }
            return lines;
        }

        /// <summary>"a|b, c" → [a, b, c] (구분자 '|' 또는 ',', 공백·빈 항목 제거, 중복 제거).</summary>
        public static List<string> SplitList(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var part in text.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string v = part.Trim();
                if (v.Length == 0) continue;
                if (!list.Contains(v, StringComparer.OrdinalIgnoreCase)) list.Add(v);
            }
            return list;
        }
    }
}
