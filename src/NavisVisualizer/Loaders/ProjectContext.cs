using System;
using System.Collections.Generic;

namespace NavisVisualizer.Loaders
{
    /// <summary>
    /// 현재 세션의 선택된 공사(프로젝트). **전역 공유** — 한 탭에서 공사를 바꾸면 전 탭이
    /// 같은 공사를 본다("지금 이 공사를 본다"는 한 개념. 탭마다 다른 공사가 섞여 오독되는
    /// 사고를 원천 차단). OASIS(SQL) 로드에만 영향을 주며 Excel import와는 무관하다.
    ///
    /// **저장하지 않는다**(2026-07 사용자 결정) — Navisworks를 다시 열면 oasis.config의
    /// `project=` 값에서 다시 시작한다. 세션 중에만 유효한 상태.
    ///
    /// 초기화는 지연(lazy) — 탭 생성 시점엔 config가 없을 수도 있으므로 조용히 실패하고
    /// 내장 기본 목록(Trion/Ruya)으로 동작한다. config 부재 오류는 실제 [OASIS 로드]
    /// 시점에 SqlConnectionSettings.Load()가 안내와 함께 던진다 — 드롭다운이 비어
    /// 아무것도 못 고르는 상태가 되면 안 되기 때문.
    ///
    /// Autodesk 비의존 — 리눅스에서 컴파일·테스트 검증 가능.
    /// </summary>
    public static class ProjectContext
    {
        private static readonly object Sync = new object();
        private static bool _initialized;
        private static List<ProjectInfo> _catalog = new List<ProjectInfo>();
        private static string _currentCode = "";

        /// <summary>
        /// 선택된 공사가 바뀌었을 때 발생. 구독자(각 탭의 ProjectSelector)는 Dispose에서
        /// 반드시 해제할 것 — static 이벤트라 해제하지 않으면 파괴된 컨트롤이 계속 불린다.
        /// </summary>
        public static event EventHandler Changed;

        /// <summary>선택 가능한 공사 목록 (전체 항목은 포함하지 않음 — UI가 별도로 넣는다).</summary>
        public static List<ProjectInfo> Catalog
        {
            get { EnsureInitialized(); return _catalog; }
        }

        /// <summary>
        /// 현재 공사코드. 빈 문자열 = 전체(WHERE 절 없음 — 기존 동작 유지).
        /// SQL 필터에 그대로 쓰이는 값.
        /// </summary>
        public static string CurrentCode
        {
            get { EnsureInitialized(); return _currentCode; }
        }

        /// <summary>현재 공사의 표시 문자열 ("Trion (Q557)" / "전체").</summary>
        public static string CurrentDisplay => ProjectCatalog.DisplayFor(Catalog, CurrentCode);

        /// <summary>
        /// 공사 변경. 실제로 값이 바뀔 때만 Changed를 발생시킨다(같은 값 재선택은 무시 —
        /// 불필요한 "재로드 필요" 경고 방지).
        /// </summary>
        public static void SetCurrent(string code)
        {
            EnsureInitialized();
            string normalized = (code ?? "").Trim();
            if (string.Equals(normalized, _currentCode, StringComparison.OrdinalIgnoreCase))
                return;
            _currentCode = normalized;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// DB에서 발견한 코드를 목록에 병합 (이름은 DB에 없으므로 기존 이름 보존).
        /// 반환값 = 새로 추가된 개수. 목록이 바뀌면 Changed로 UI 갱신을 유도한다.
        /// </summary>
        public static int MergeDiscovered(IEnumerable<string> codes)
        {
            EnsureInitialized();
            int added = ProjectCatalog.MergeDiscovered(_catalog, codes);
            if (added > 0) Changed?.Invoke(null, EventArgs.Empty);
            return added;
        }

        /// <summary>
        /// config에서 이름 목록과 초기 공사코드를 읽어 1회 초기화. config가 없거나 깨져도
        /// 던지지 않는다 (내장 기본 목록으로 degrade).
        /// </summary>
        private static void EnsureInitialized()
        {
            lock (Sync)
            {
                if (_initialized) return;
                _initialized = true;   // 재시도 루프 방지를 위해 실패해도 초기화 완료로 표시

                Dictionary<string, string> names = null;
                string initialCode = "";
                var settings = SqlConnectionSettings.TryLoadQuiet();
                if (settings != null)
                {
                    names = settings.ProjectNames;
                    initialCode = settings.ProjectNo ?? "";
                }

                _catalog = ProjectCatalog.Build(names);
                _currentCode = initialCode.Trim();
            }
        }

        /// <summary>테스트 전용 — 정적 상태를 초기 상태로 되돌린다.</summary>
        internal static void ResetForTests(List<ProjectInfo> catalog, string currentCode)
        {
            lock (Sync)
            {
                _catalog = catalog ?? new List<ProjectInfo>();
                _currentCode = currentCode ?? "";
                _initialized = true;
            }
        }
    }
}
