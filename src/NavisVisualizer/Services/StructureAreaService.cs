using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Searchers;

namespace NavisVisualizer.Services
{
    /// <summary>구조(Str) 파일의 영역 노드 하나(레벨1 또는 레벨2) — 이름 기준으로 복수 Str 파일의 동명 노드를 병합.</summary>
    public class StructureArea
    {
        public string Name;
        public List<ModelItem> Items = new List<ModelItem>();

        /// <summary>레벨2 하위 영역 (레벨1 영역에서만 채움 — 탭의 펼침 행). 레벨2의 Children은 항상 비어 있음.</summary>
        public List<StructureArea> Children = new List<StructureArea>();

        /// <summary>레벨2 자식이 상한(MaxLevel2PerArea)을 넘어 열거를 생략했는가 — 플랫 geometry 모델링 방어.
        /// true면 탭이 이 영역을 레벨1 단위로만 취급한다(펼침 없음) + 생략 사실을 행에 표기.</summary>
        public bool ChildrenTruncated;
    }

    /// <summary>
    /// Structure 탭용 영역 열거 — 열린 문서에서 Str 스코프(NwdScope.Structure) 파일 노드를
    /// ScopeMappingService(프로파일 별칭 자동 인식 / 사용자 직접 지정)로 찾고, 그 레벨1 자식(영역
    /// 노드: /QR/LG/STRU/HHI …)만 반환한다. geometry 트리는 내려가지 않아 인덱스 빌드
    /// 없이 즉시 수준. Str 파일 미지정 시 전체 모델을 훑지 않고 빈 목록 + 진단 노트만 남긴다
    /// (전 탭 공통 — 전체 fallback 폐지 2026-09). 직접 지정에서 뺀 하위 파일(STR 안의 PVV.nwd 등)은
    /// 영역 목록에서도 제외된다.
    /// ModelItemSearcher를 안 쓰는 이유: 태그 매칭이 아니라 노드 열거라 인덱스가 불필요하고,
    /// searcher의 LastScopeNote 등 다른 탭 진단 상태를 건드리면 안 되기 때문 (ScopeMappingService는 무상태).
    /// </summary>
    public static class StructureAreaService
    {
        /// <summary>영역당 레벨2 열거 상한 — 영역 바로 아래가 그룹이 아니라 대량 geometry leaf로
        /// 플랫하게 모델링된 경우 UI 행 폭주를 막는다. 초과 시 그 영역은 레벨1 단위로만 취급.</summary>
        public const int MaxLevel2PerArea = 200;

        public class Result
        {
            public List<StructureArea> Areas = new List<StructureArea>();
            /// <summary>발견 파일/미발견 사유 — 상태 라벨·Overview 노출용.</summary>
            public string ScopeNote = "-";
            public bool Found => Areas.Count > 0;
            /// <summary>Str 파일이 미지정(별칭 미매칭·지정 파일 미발견) — 탭이 파일 지정을 안내.</summary>
            public bool Unmapped;
        }

        public static Result Probe(Document doc)
        {
            var result = new Result();
            if (doc == null || doc.Models.Count == 0)
            {
                result.ScopeNote = "모델 미열림";
                return result;
            }

            // 스코프 해석은 ScopeMappingService 단일 진입점 (프로파일 별칭 자동 인식 또는 사용자 직접
            // 지정, 2026-09). 미지정이면 전체 모델을 훑지 않고 빈 목록 + 노트 (Structure 탭이 파일 지정 안내).
            var res = ScopeMappingService.Resolve(doc, NwdScope.Structure);
            var roots = res.Roots;
            var files = res.Files;
            var excluded = res.ExcludedFiles;
            result.Unmapped = !res.IsMapped;

            if (roots.Count == 0)
            {
                result.ScopeNote = res.Note + " — Structure 파일을 지정하세요 (전체 모델 fallback 없음)";
                return result;
            }

            // 이름 기준 병합 — 복수 Str 파일(granular 분할)에 같은 영역명이 있으면 한 행으로.
            var byName = new Dictionary<string, StructureArea>(System.StringComparer.OrdinalIgnoreCase);
            var childByArea = new Dictionary<StructureArea, Dictionary<string, StructureArea>>();
            for (int i = 0; i < roots.Count; i++)
            {
                var top = UnwrapSingleFileChild(roots[i]);
                int unnamed = 0;
                foreach (ModelItem child in top.Children)
                {
                    string name = child.DisplayName?.Trim();
                    // 직접 지정 매핑에서 뺀 하위 파일 노드(예: STR 안의 PVV.nwd)는 영역으로 나열하지 않음.
                    if (!string.IsNullOrEmpty(name) && excluded.Contains(name) && NwdScope.LooksLikeFileNode(name))
                        continue;
                    if (string.IsNullOrEmpty(name))
                    {
                        // 무명 노드도 숨김/투명 대상에서 빠지지 않게 포함. 합성 이름은 위치 기반이라
                        // 파일 간 정체성이 없음 — 파일명을 붙여 다른 Str 파일의 무명 노드와 병합되지
                        // 않게 하고, 재조회 시 설정 보존(이름 키)도 파일 순서와 무관하게 유지한다.
                        name = $"(이름 없음 {++unnamed} · {files[i]})";
                    }
                    if (!byName.TryGetValue(name, out var area))
                    {
                        area = new StructureArea { Name = name };
                        byName[name] = area;
                        result.Areas.Add(area);
                    }
                    area.Items.Add(child);
                    CollectLevel2(child, area, childByArea, files[i]);
                }
            }

            result.ScopeNote = result.Areas.Count > 0
                ? $"{res.Note} · 영역 {result.Areas.Count}개"
                : $"{res.Note} · 레벨1 자식 없음";
            return result;
        }

        /// <summary>
        /// 레벨1 영역 노드의 직계 자식(레벨2)을 영역에 병합 수집 — 탭의 펼침 행용. 한 단계만
        /// 내려가므로 여전히 geometry walk 없음. 영역당 상한 초과 시(플랫 geometry 모델링)
        /// ChildrenTruncated만 표시하고 열거 생략 — 탭은 그 영역을 레벨1 단위로만 취급.
        /// </summary>
        private static void CollectLevel2(ModelItem l1Node, StructureArea area,
            Dictionary<StructureArea, Dictionary<string, StructureArea>> childByArea, string file)
        {
            if (area.ChildrenTruncated) return;

            int count = 0;
            foreach (ModelItem c in l1Node.Children)
            {
                if (++count > MaxLevel2PerArea)
                {
                    // 병합 도중 한 파일에서라도 초과하면 영역 전체를 레벨1 단위로 강등
                    // (부분 목록을 남기면 "전부인 줄" 오해 — 생략은 명시적으로).
                    area.ChildrenTruncated = true;
                    return;
                }
            }

            if (!childByArea.TryGetValue(area, out var byName))
            {
                byName = new Dictionary<string, StructureArea>(System.StringComparer.OrdinalIgnoreCase);
                childByArea[area] = byName;
            }
            int unnamed = 0;
            foreach (ModelItem c in l1Node.Children)
            {
                string name = c.DisplayName?.Trim();
                if (string.IsNullOrEmpty(name))
                    name = $"(이름 없음 {++unnamed} · {file})";
                if (!byName.TryGetValue(name, out var child))
                {
                    child = new StructureArea { Name = name };
                    byName[name] = child;
                    area.Children.Add(child);
                }
                child.Items.Add(c);
            }
        }

        /// <summary>
        /// nwd→nwc 중첩처럼 매칭 루트가 단일 파일 노드(또는 무명 노드) 하나만 감싸고 있으면
        /// 그 안으로 내려가 실제 영역 레벨을 레벨1로 만든다. 영역 노드가 여럿이면 그대로 반환.
        /// (federated 구성별 래핑 깊이 차이 대비 — Windows 실측 확인 필요.)
        /// </summary>
        private static ModelItem UnwrapSingleFileChild(ModelItem root)
        {
            var current = root;
            for (int i = 0; i < 3; i++)
            {
                ModelItem only = null;
                int count = 0;
                foreach (ModelItem child in current.Children)
                {
                    only = child;
                    if (++count > 1) break;
                }
                if (count != 1) return current;

                string dn = only.DisplayName?.Trim();
                if (NwdScope.LooksLikeFileNode(dn) || string.IsNullOrEmpty(dn))
                    current = only;
                else
                    return current;
            }
            return current;
        }
    }
}
