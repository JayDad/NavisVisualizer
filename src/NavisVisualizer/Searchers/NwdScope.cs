using System;
using System.Collections.Generic;

namespace NavisVisualizer.Searchers
{
    /// <summary>
    /// 공종별 NWD 파일 스코프 — 인덱스 빌드 시 walk할 모델을 파일명 키워드로 제한한다.
    /// federated 문서(전체 nwd 묶음)에서 대상 공종 파일만 인덱싱해 빌드 시간을 줄인다.
    ///
    /// 확정 파일명 규약 (예: 00-02_Trion_Topsides_Subsystem.nwd 하위):
    ///   *_Str.nwc        구조 (Structure 탭 — 레벨1 영역 노드만 열거, geometry walk 없음)
    ///   *_HYDROPKG.nwd   Hydrotest 패키지 (SPL 파일이 없으면 스풀도 여기 존재)
    ///   *_SPL*.nwd       배관 스풀 (있을 때 우선 탐색 대상)
    ///   *_MEQ.nwd        Mechanical Equipment
    ///   *_EIT.nwd        EIT 소형기기 / Tray / Tray Support
    ///   *_Cable.nwd      케이블 루트 (cable no.별 모델링; node box는 별도 추출 예정)
    ///   *_PIPSupport.nwd 배관 서포트 (플러그인 없음)
    ///
    /// **키워드(별칭)는 프로젝트 프로파일로 교체 가능** (2026-09 — Ruya 대응): 위 목록은 Trion
    /// 기본 프로파일이고, 문서마다 `ScopeProfiles`가 고른 프로파일의 별칭이 `AliasProvider`를
    /// 통해 `Keywords`로 노출된다 (예: Ruya = Spool→"SPOOL", Equipment→"MEC"). 프로파일에 별칭이
    /// 비어 있으면 자동 인식 없음 = 사용자가 Overview 매핑에서 파일을 직접 지정해야 한다.
    ///
    /// **전체 모델 자동 fallback은 폐지됨** (2026-09 사용자 결정): 별칭으로 한 파일도 못 찾으면
    /// ModelItemSearcher가 전체 모델을 훑지 않고 "미지정"으로 멈춘다 — 적용 시 ScopeGate가
    /// 파일 지정/전체 검색을 명시적으로 묻는다. 단일 공종 nwd만 열린 문서에서도 같은 별칭 매칭.
    /// </summary>
    public sealed class NwdScope
    {
        /// <summary>안정 식별자 — 프로파일 별칭·문서별 매핑 저장의 키 (라벨은 표시용이라 바뀔 수 있음).</summary>
        public string Key { get; }

        /// <summary>사용자 표시용 라벨 (fallback 안내 문구 등).</summary>
        public string Label { get; }

        /// <summary>코드 내장 기본 별칭 (Trion 규약). 프로파일이 없거나 이 Key를 모르면 이걸 쓴다.</summary>
        public IReadOnlyList<string> DefaultKeywords { get; }

        /// <summary>
        /// 현재 프로파일 기준 별칭 — <see cref="AliasProvider"/>가 이 Key에 대해 null이 아닌 목록을
        /// 주면 그것(빈 목록 = 자동 인식 없음), 아니면 <see cref="DefaultKeywords"/>.
        /// </summary>
        public IReadOnlyList<string> Keywords
        {
            get
            {
                var provider = AliasProvider;
                if (provider != null)
                {
                    var over = provider(Key);
                    if (over != null) return over;
                }
                return DefaultKeywords;
            }
        }

        /// <summary>
        /// 프로파일 별칭 제공자 (Key → 별칭 목록, 모르면 null). ScopeMappingService가 활성 문서의
        /// 프로파일로 세팅한다. 전역 static인 이유: Navisworks는 활성 문서가 하나이고, searcher·
        /// preflight·Structure 프로브가 전부 static 스코프 인스턴스를 공유하기 때문. 테스트는 끝에 null로 복원.
        /// </summary>
        public static Func<string, IReadOnlyList<string>> AliasProvider { get; set; }

        /// <summary>
        /// 우선순위 체인(자동 인식에서만): 이 스코프로 대상 모델을 한 건도 못 찾을 때 대신 시도할 다음 스코프.
        /// null이면 체인 끝 — 그래도 없으면 미지정 (ScopeGate가 파일 지정을 묻는다).
        /// </summary>
        public NwdScope Fallback { get; }

        public NwdScope(string key, string label, params string[] keywords)
            : this(key, label, null, keywords)
        {
        }

        public NwdScope(string key, string label, NwdScope fallback, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("스코프 키가 비어 있습니다.", nameof(key));
            if (keywords == null || keywords.Length == 0)
                throw new ArgumentException("스코프 기본 키워드가 비어 있습니다.", nameof(keywords));
            Key = key;
            Label = label;
            Fallback = fallback;
            DefaultKeywords = keywords;
        }

        public static readonly NwdScope Hydrotest = new NwdScope("Hydrotest", "HYDROPKG", "HYDROPKG");
        /// <summary>스풀은 SPL 파일 우선 — SPL 파일이 없는 문서에선 스풀이 HYDROPKG 안에 있으므로 체인 fallback.</summary>
        public static readonly NwdScope Spool = new NwdScope("Spool", "SPL", Hydrotest, "SPL");
        public static readonly NwdScope Equipment = new NwdScope("Equipment", "MEQ", "MEQ");
        /// <summary>넓은 EIT 스코프 — 통합 *_EIT.nwd(소형기기+Tray+Support 한 파일)든 EQ 전용
        /// 파일이든 "EIT" 부분일치로 전부 잡는다. Sub-system 탭의 EIT EQ 공종이 이 스코프를 쓰고,
        /// EitTray의 fallback이기도 하다(아래).</summary>
        public static readonly NwdScope Eit = new NwdScope("Eit", "EIT", "EIT");
        /// <summary>Tray 탭 전용 스코프 — granular *_EIT_Tray 파일만 "TRAY"로 우선 매칭해
        /// EQ/Support 파일 walk를 건너뛴다(효율: Tray 탭은 트레이 파일만 필요한데 구 "EIT"는
        /// EQ 파일까지 스코프에 넣어 헛되이 순회했다). combined *_EIT.nwd만 있는 문서에선 "TRAY"
        /// 미매칭 → Eit로 체인 fallback해 통합 파일을 잡으므로 정확성 유지. 체인 전부 미매칭이면
        /// 미지정(전체 fallback 없음 — 2026-09 전 탭 공통).
        /// **Windows 검증**: 트레이가 오직 *_EIT_Tray 파일에만 있다는 규약 전제 — 통합 *_EIT.nwd에도
        /// 섞여 있으면 "TRAY"가 트레이 파일을 잡는 순간 fallback이 안 돌아 통합 파일 트레이를 놓친다.
        /// 변경 전(구 "EIT") 매칭 건수와 대조할 것.</summary>
        public static readonly NwdScope EitTray = new NwdScope("EitTray", "EIT_Tray", Eit, "TRAY");
        /// <summary>node box nwd 파일명 규약 확정 시 키워드 추가 (미매칭 시 전체 fallback으로 동작은 유지).</summary>
        public static readonly NwdScope Cable = new NwdScope("Cable", "CABLE", "CABLE");
        /// <summary>구조(Str) — Structure 탭 전용. 인덱스 빌드 없이 Str 파일의 레벨1 영역 노드만
        /// 열거한다(StructureAreaService). 하드 스코프 성격: Str 파일 미발견 시 전체 모델을
        /// 훑지 않고 빈 목록 + 진단 노트만 남긴다 (전 트리 walk로 인한 지연 방지).</summary>
        public static readonly NwdScope Structure = new NwdScope("Structure", "STR", "STR");
        // (구 SubSystem 합집합 스코프는 폐기 — Sub-system 탭이 공종별로 Equipment/Hydrotest/EitTray/Cable
        //  스코프를 각각 레벨 타겟하므로 union 불필요. 2026-07 §11.)

        /// <summary>플러그인이 쓰는 스코프 전부 (Overview 매핑 표·프로파일 별칭 편집 순서).</summary>
        public static readonly IReadOnlyList<NwdScope> All = new[]
        {
            Structure, Spool, Hydrotest, Equipment, EitTray, Eit, Cable,
        };

        public static NwdScope FindByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var s in All)
                if (string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <summary>체인 표시용 — 현재 별칭 기준 (예: "SPL, SPOOL → HYDROPKG"). 별칭이 비면 "(없음)".</summary>
        public string ChainAliasLabel()
        {
            var parts = new List<string>();
            for (var tier = this; tier != null; tier = tier.Fallback)
                parts.Add(tier.Keywords.Count > 0 ? string.Join(", ", tier.Keywords) : "(없음)");
            return string.Join(" → ", parts);
        }

        /// <summary>
        /// 파일명(전체 경로 허용) 또는 파일 노드 DisplayName이 키워드를 포함하는가.
        /// 디렉터리명 오탐을 막기 위해 경로 구분자 뒤 파일명만 비교. 대소문자 무시.
        /// </summary>
        public bool MatchesFileName(string fileNameOrDisplayName)
        {
            if (string.IsNullOrEmpty(fileNameOrDisplayName)) return false;
            string name = StripDirectory(fileNameOrDisplayName);
            foreach (var kw in Keywords)
                if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>
        /// federated 트리 안에서 파일 노드 판정 — DisplayName에 Navisworks/원본 확장자가
        /// 보이면 파일 노드로 취급 (예: "04-02_Trion_Topsides_MEQ.nwd", "MEBTray1.nwc").
        /// 파일 노드만 따라 내려가므로 geometry 트리 walk 없이 스코프를 찾는다.
        /// </summary>
        public static bool LooksLikeFileNode(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return false;
            return displayName.IndexOf(".nw", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf(".rvm", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>경로에서 파일명만 — Windows(\)·URL(/) 구분자 모두 처리 (Path.GetFileName은 플랫폼 의존).</summary>
        public static string StripDirectory(string path)
        {
            int cut = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return cut >= 0 ? path.Substring(cut + 1) : path;
        }
    }
}
