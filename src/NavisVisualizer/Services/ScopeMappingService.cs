using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Searchers;

namespace NavisVisualizer.Services
{
    /// <summary>열린 문서의 파일 노드 하나 (Model 루트 또는 federated 트리 안의 중첩 파일 노드).</summary>
    public sealed class FileNodeInfo
    {
        /// <summary>파일명(디렉터리 제외) — 매핑 저장·별칭 매칭의 키.</summary>
        public string Name;
        /// <summary>Model 루트의 RootItem.DisplayName (파일명과 다를 수 있어 별칭 매칭에 같이 씀). 중첩 노드는 null.</summary>
        public string AltName;
        public ModelItem Item;
        public int Depth;                 // 0 = Model 루트
        public FileNodeInfo Parent;
        public List<FileNodeInfo> Children = new List<FileNodeInfo>();

        public bool IsModelRoot => Depth == 0;

        public IEnumerable<FileNodeInfo> Descendants()
        {
            foreach (var c in Children)
            {
                yield return c;
                foreach (var d in c.Descendants()) yield return d;
            }
        }
    }

    /// <summary>스코프 하나의 해석 결과 — walk 시작 루트 + 진단.</summary>
    public sealed class ScopeResolution
    {
        public NwdScope Scope;
        public ScopeSelectionMode Mode;
        /// <summary>walk 시작점. 미지정이면 비어 있음.</summary>
        public List<ModelItem> Roots = new List<ModelItem>();
        /// <summary>루트 파일명 (표시용).</summary>
        public List<string> Files = new List<string>();
        /// <summary>선택 루트 안의 하위 파일 노드 중 walk에서 제외할 것 (Files 모드에서 미선택 하위 파일).</summary>
        public HashSet<string> ExcludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Files 모드에서 지정했으나 문서에 없는 파일명 (리비전 교체 등).</summary>
        public List<string> MissingFiles = new List<string>();
        /// <summary>자동 모드에서 실제 매칭된 체인 단계 라벨 (예: SPL 없음 → "HYDROPKG").</summary>
        public string MatchedTier;
        /// <summary>진단 노트 — searcher LastScopeNote·Overview 표시용.</summary>
        public string Note = "-";

        /// <summary>walk할 루트가 하나라도 있는가. false = 미지정 → 인덱스 0건 (전체 fallback 없음).</summary>
        public bool IsMapped => Roots.Count > 0;
    }

    /// <summary>
    /// 스코프 → 파일 노드 해석의 단일 진입점 (2026-09). ModelItemSearcher·StructureAreaService·
    /// Overview 매핑 표가 전부 이걸 쓴다 (구 ScopePreflight의 "searcher 상태를 안 건드리는 읽기 전용
    /// 미러"는 이 서비스가 상태를 갖지 않으므로 자연히 충족).
    ///
    /// 해석 3단:
    ///   ① 문서별 저장 매핑(ScopeMappingConfig) — Files(직접 선택) / AllModels(전체, 명시)
    ///   ② Auto — 활성 프로파일(ScopeProfiles) 별칭으로 파일명 부분일치, 체인(Fallback) 순서
    ///   ③ 둘 다 없으면 **미지정**(Roots 비어 있음) — 전체 모델 자동 fallback은 폐지됨(사용자 결정).
    ///      적용 시 ScopeGate가 "파일 지정 / 전체 검색 / 취소"를 명시적으로 묻는다.
    ///
    /// 파일 노드 열거는 geometry를 안 내려간다(확장자 보유 DisplayName만 depth≤3) — 대형 모델에서도 즉시.
    /// </summary>
    public static class ScopeMappingService
    {
        private const int MaxFileDepth = 3;

        private static readonly Dictionary<string, ScopeMappingConfig> _configCache =
            new Dictionary<string, ScopeMappingConfig>(StringComparer.OrdinalIgnoreCase);

        /// <summary>마지막 별칭 파일 파싱 오류 (Overview 표시용).</summary>
        public static List<string> LastProfileErrors { get; private set; } = new List<string>();

        public static string DocKey(Document doc)
        {
            try { return doc?.FileName ?? ""; } catch { return ""; }
        }

        // ---------------- 프로파일 ----------------

        public static List<ScopeProfile> LoadProfiles()
        {
            var errors = new List<string>();
            var profiles = ScopeMappingStore.LoadProfiles(errors);
            LastProfileErrors = errors;
            return profiles;
        }

        /// <summary>문서의 활성 프로파일 — 저장된 이름 우선, 없으면 문서 파일명으로 감지. NwdScope 별칭도 여기서 활성화.</summary>
        public static ScopeProfile ActivateProfile(Document doc, List<ScopeProfile> profiles = null)
        {
            profiles = profiles ?? LoadProfiles();
            var cfg = GetConfig(doc);
            var profile = ScopeProfiles.FindByName(cfg.ProfileName, profiles)
                          ?? ScopeProfiles.Detect(DocKey(doc), profiles);
            NwdScope.AliasProvider = key => profile.AliasesFor(key);
            return profile;
        }

        /// <summary>프로파일 이름이 문서 파일명 감지 결과인지(저장된 명시 선택이 아닌지).</summary>
        public static bool IsProfileAutoDetected(Document doc) => string.IsNullOrEmpty(GetConfig(doc).ProfileName);

        // ---------------- 문서별 매핑 ----------------

        public static ScopeMappingConfig GetConfig(Document doc)
        {
            string key = DocKey(doc);
            if (!_configCache.TryGetValue(key, out var cfg))
            {
                cfg = ScopeMappingStore.LoadConfig(key);
                _configCache[key] = cfg;
            }
            return cfg;
        }

        public static void SaveConfig(Document doc, ScopeMappingConfig cfg)
        {
            string key = DocKey(doc);
            ScopeMappingStore.SaveConfig(key, cfg);
            _configCache[key] = cfg;
        }

        /// <summary>편의: 한 스코프의 선택만 바꿔 저장.</summary>
        public static void SetSelection(Document doc, NwdScope scope, ScopeSelection selection)
        {
            var cfg = GetConfig(doc);
            cfg.Set(scope.Key, selection);
            SaveConfig(doc, cfg);
        }

        // ---------------- 파일 노드 열거 ----------------

        /// <summary>Model 루트 + federated 트리의 중첩 파일 노드(depth≤3)를 트리로. geometry walk 없음.</summary>
        public static List<FileNodeInfo> EnumerateFileNodes(Document doc)
        {
            var roots = new List<FileNodeInfo>();
            if (doc == null) return roots;
            foreach (Model model in doc.Models)
            {
                string fileName = null;
                try { fileName = model.FileName; } catch { /* 일부 모델은 FileName 조회 실패 가능 */ }
                string rootName = model.RootItem?.DisplayName;
                var info = new FileNodeInfo
                {
                    Name = NwdScope.StripDirectory(fileName ?? rootName ?? "?"),
                    AltName = rootName,
                    Item = model.RootItem,
                    Depth = 0,
                };
                roots.Add(info);
                CollectChildren(info);
            }
            return roots;
        }

        private static void CollectChildren(FileNodeInfo parent)
        {
            if (parent.Item == null || parent.Depth >= MaxFileDepth) return;
            foreach (ModelItem child in parent.Item.Children)
            {
                string dn = child.DisplayName?.Trim();
                if (!NwdScope.LooksLikeFileNode(dn)) continue;
                var info = new FileNodeInfo { Name = dn, Item = child, Depth = parent.Depth + 1, Parent = parent };
                parent.Children.Add(info);
                CollectChildren(info);
            }
        }

        // ---------------- 해석 ----------------

        public static ScopeResolution Resolve(Document doc, NwdScope scope)
        {
            var nodes = EnumerateFileNodes(doc);
            return Resolve(doc, scope, nodes);
        }

        /// <summary>파일 노드 트리를 이미 열거해 둔 경우 (Overview 표처럼 여러 스코프를 연달아 해석할 때).</summary>
        public static ScopeResolution Resolve(Document doc, NwdScope scope, List<FileNodeInfo> nodes)
        {
            var res = new ScopeResolution { Scope = scope };
            if (scope == null)
            {
                foreach (var n in nodes) { res.Roots.Add(n.Item); res.Files.Add(n.Name); }
                res.Mode = ScopeSelectionMode.AllModels;
                res.Note = "전체 모델 (스코프 없음)";
                return res;
            }

            ActivateProfile(doc);
            var sel = GetConfig(doc).Get(scope.Key);
            res.Mode = sel.Mode;

            switch (sel.Mode)
            {
                case ScopeSelectionMode.AllModels:
                    foreach (var n in nodes) { res.Roots.Add(n.Item); res.Files.Add(n.Name); }
                    res.Note = $"스코프 {scope.Key}: 전체 모델 (사용자 지정 — 느림)";
                    return res;

                case ScopeSelectionMode.Files:
                    ResolveFiles(scope, sel, nodes, res);
                    return res;

                default:
                    ResolveAuto(scope, nodes, res);
                    return res;
            }
        }

        private static void ResolveFiles(NwdScope scope, ScopeSelection sel, List<FileNodeInfo> nodes, ScopeResolution res)
        {
            var wanted = new HashSet<string>(sel.Files, StringComparer.OrdinalIgnoreCase);
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectSelected(nodes, wanted, found, res);
            foreach (var w in sel.Files)
                if (!found.Contains(w)) res.MissingFiles.Add(w);

            if (res.Roots.Count == 0)
            {
                res.Note = $"스코프 {scope.Key}: 지정 파일 미발견 ({string.Join(", ", sel.Files)}) — Overview에서 다시 지정";
                return;
            }
            res.Note = $"스코프 {scope.Key}(직접 지정): {string.Join(", ", res.Files)}";
            if (res.ExcludedFiles.Count > 0)
                res.Note += $" · 하위 제외 {res.ExcludedFiles.Count}개";
            if (res.MissingFiles.Count > 0)
                res.Note += $" · 미발견 {string.Join(", ", res.MissingFiles)}";
        }

        /// <summary>선택 파일을 만나면 루트로 잡고 그 아래 미선택 하위 파일은 제외 목록에 (선택된 하위 파일은
        /// 부모 walk에 포함되므로 별도 루트로 안 잡음 — 중복 walk 방지).</summary>
        private static void CollectSelected(List<FileNodeInfo> nodes, HashSet<string> wanted,
            HashSet<string> found, ScopeResolution res)
        {
            foreach (var n in nodes)
            {
                if (wanted.Contains(n.Name))
                {
                    found.Add(n.Name);
                    res.Roots.Add(n.Item);
                    res.Files.Add(n.Name);
                    foreach (var d in n.Descendants())
                    {
                        if (wanted.Contains(d.Name)) found.Add(d.Name);
                        else res.ExcludedFiles.Add(d.Name);
                    }
                }
                else
                {
                    CollectSelected(n.Children, wanted, found, res);
                }
            }
        }

        private static void ResolveAuto(NwdScope scope, List<FileNodeInfo> nodes, ScopeResolution res)
        {
            var chainNotes = new List<string>();
            for (var tier = scope; tier != null; tier = tier.Fallback)
            {
                string aliasText = tier.Keywords.Count > 0 ? string.Join("/", tier.Keywords) : "별칭 없음";
                if (tier.Keywords.Count > 0)
                    CollectAuto(nodes, tier, res);
                if (res.Roots.Count > 0)
                {
                    res.MatchedTier = tier.Label;
                    chainNotes.Add($"{tier.Label}[{aliasText}]: {string.Join(", ", res.Files)}");
                    res.Note = "스코프 " + string.Join(" → ", chainNotes);
                    return;
                }
                chainNotes.Add($"{tier.Label}[{aliasText}] 없음");
            }
            res.Note = $"스코프 {string.Join(" → ", chainNotes)} → 미지정 (Overview 매핑에서 파일 지정 필요)";
        }

        private static void CollectAuto(List<FileNodeInfo> nodes, NwdScope tier, ScopeResolution res)
        {
            foreach (var n in nodes)
            {
                if (tier.MatchesFileName(n.Name) || (n.AltName != null && tier.MatchesFileName(n.AltName)))
                {
                    res.Roots.Add(n.Item);
                    res.Files.Add(n.Name);   // 매칭 파일의 하위는 통째로 스코프 — 더 안 내려감
                }
                else
                {
                    CollectAuto(n.Children, tier, res);
                }
            }
        }

        /// <summary>자동 인식만 미리 계산 (매핑 대화상자에서 "자동이면 어느 파일이 잡히는지" 힌트).</summary>
        public static ScopeResolution ProbeAuto(Document doc, NwdScope scope, List<FileNodeInfo> nodes)
        {
            ActivateProfile(doc);
            var res = new ScopeResolution { Scope = scope, Mode = ScopeSelectionMode.Auto };
            ResolveAuto(scope, nodes, res);
            return res;
        }

        /// <summary>문서 전환/재로드 시 캐시 비우기 (MainDockablePanel 무효화 경로에서 호출).</summary>
        public static void InvalidateCache() => _configCache.Clear();
    }
}
