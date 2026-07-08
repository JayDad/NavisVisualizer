using System;
using System.Collections.Generic;

namespace NavisVisualizer.Searchers
{
    /// <summary>
    /// 공종별 NWD 파일 스코프 — 인덱스 빌드 시 walk할 모델을 파일명 키워드로 제한한다.
    /// federated 문서(전체 nwd 묶음)에서 대상 공종 파일만 인덱싱해 빌드 시간을 줄인다.
    ///
    /// 확정 파일명 규약 (예: 00-02_Trion_Topsides_Subsystem.nwd 하위):
    ///   *_Str.nwc        구조 (플러그인 없음 — 스코프 제외가 곧 성능 이득)
    ///   *_HYDROPKG.nwd   Hydrotest 패키지 (SPL 파일이 없으면 스풀도 여기 존재)
    ///   *_SPL*.nwd       배관 스풀 (있을 때 우선 탐색 대상)
    ///   *_MEQ.nwd        Mechanical Equipment
    ///   *_EIT.nwd        EIT 소형기기 / Tray / Tray Support
    ///   *_Cable.nwd      케이블 루트 (cable no.별 모델링; node box는 별도 추출 예정)
    ///   *_PIPSupport.nwd 배관 서포트 (플러그인 없음)
    ///
    /// 키워드로 한 모델도 못 찾거나 스코프 인덱스가 0건이면 ModelItemSearcher가
    /// 전체 모델로 자동 fallback한다 — 규약이 깨져도 동작은 유지(속도만 손해).
    /// 단일 공종 nwd만 열린 문서에서도 같은 키워드 매칭으로 동작한다.
    /// </summary>
    public sealed class NwdScope
    {
        /// <summary>사용자 표시용 라벨 (fallback 안내 문구 등).</summary>
        public string Label { get; }

        public IReadOnlyList<string> Keywords { get; }

        public NwdScope(string label, params string[] keywords)
        {
            if (keywords == null || keywords.Length == 0)
                throw new ArgumentException("스코프 키워드가 비어 있습니다.", nameof(keywords));
            Label = label;
            Keywords = keywords;
        }

        // Spool은 SPL 우선이지만 SPL 파일이 없는 문서에선 HYDROPKG 안에 스풀이 있으므로
        // 두 키워드의 합집합 스코프 하나를 Spool/Hydrotest가 공유한다 (양쪽 규칙 모두 충족,
        // 탭 전환 시 재빌드 핑퐁 없음).
        public static readonly NwdScope Piping = new NwdScope("SPL·HYDROPKG", "SPL", "HYDROPKG");
        public static readonly NwdScope Equipment = new NwdScope("MEQ", "MEQ");
        public static readonly NwdScope EitTray = new NwdScope("EIT", "EIT");
        /// <summary>node box nwd 파일명 규약 확정 시 키워드 추가 (미매칭 시 전체 fallback으로 동작은 유지).</summary>
        public static readonly NwdScope Cable = new NwdScope("CABLE", "CABLE");
        /// <summary>Sub-system 요소 = Equipment TAG + Piping PKG — 두 공종 파일 합집합.</summary>
        public static readonly NwdScope SubSystem = new NwdScope("MEQ·SPL·HYDROPKG", "MEQ", "SPL", "HYDROPKG");

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
