using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;

namespace NavisVisualizer.UI
{
    /// <summary>공종 탭이 Overview에 현재 상태를 노출하는 계약.</summary>
    public interface IOverviewSource
    {
        OverviewStatus GetOverviewStatus();
    }

    /// <summary>Overview 한 행 분량의 탭 상태 스냅샷. 전부 인메모리 조회라 즉시 수준.</summary>
    public class OverviewStatus
    {
        public string DataText = "미로드";      // "OASIS 18,234건 · 14:32" 등
        public bool DataLoaded;
        public string IndexText = "-";          // 인덱싱 항목 수
        public string ApplyStateText = "3D: 미적용";
        public bool ApplyStale;
        public string MatchedText = "-";
        public string UnmatchedText = "-";
        public int UnmatchedCount;
        public string ScopeNote = "-";          // 마지막 인덱스 빌드의 스코프 진단
        public bool ScopeFellBack;
    }

    /// <summary>
    /// Overview 탭 (UX audit P1 — 첫 화면). 두 가지를 한 화면에서 사전 점검한다:
    ///
    /// ① 공종 현황 표 — 탭마다 열어보지 않아도 {데이터 로드 / 인덱스 / 3D 적용 상태 /
    ///    매칭·미매칭 / 인덱스 스코프 fallback 여부}를 한 표로. 행 더블클릭 = 그 탭으로 이동.
    /// ② NWD Preflight — 열린 문서에서 공종별 스코프 파일(STR / SPL→HYDROPKG / HYDROPKG /
    ///    MEQ / EIT / CABLE)이 발견되는지 인덱스 빌드 없이 판정 (ScopePreflight — 파일 노드만
    ///    얕게 하강, geometry walk 없음). 파일명 규약 + 하드 스코프 구조라 "대상 nwd
    ///    미발견"을 적용 전에 잡는 것이 이 탭의 핵심 가치.
    ///
    /// 갱신: [새로고침] 버튼 + 이 탭이 선택될 때 자동 (MainDockablePanel 배선).
    /// 상태는 캐시하지 않고 매번 재조회한다 — L2(라이브 외부 상태 캐시 금지)와 같은 취지.
    /// </summary>
    public class OverviewTab : UserControl
    {
        private class TabEntry
        {
            public string Title;
            public IOverviewSource Source;
            public TabPage Page;
        }

        private readonly MainDockablePanel _main;
        private readonly List<TabEntry> _entries = new List<TabEntry>();
        private TabControl _tabControl;   // 행 더블클릭 이동용 (Configure에서 주입)

        private Label _lblDoc;
        private ListView _lvTabs;
        private ListView _lvNwd;

        private static readonly Color WarnColor = Color.FromArgb(190, 90, 0);
        private static readonly Color BadColor = Color.FromArgb(200, 40, 40);
        private static readonly Color OkColor = Color.FromArgb(0, 120, 40);

        /// <summary>Preflight로 점검할 공종 스코프 (공종 라벨, 스코프 체인, 하드 스코프 여부).
        /// Hard = 그 탭이 hardScope로 빌드 — 미발견 시 전체 fallback이 아니라 인덱스 0건이
        /// 된다 (EIT Tray). Equipment/Hydrotest/Cable은 자기 탭에선 soft지만 Sub-system 탭의
        /// 공종별 인덱스로는 하드 — 하단 안내문으로 보완.</summary>
        private static readonly (string Label, NwdScope Scope, bool Hard)[] PreflightScopes =
        {
            ("Structure",          NwdScope.Structure, true),    // Str — 미발견 시 영역 0개 (하드 스코프, fallback 없음)
            ("Spool",              NwdScope.Spool,     false),   // SPL→HYDROPKG 체인
            ("Hydrotest",          NwdScope.Hydrotest, false),
            ("Equipment",          NwdScope.Equipment, false),
            ("EIT Tray / EIT EQ",  NwdScope.EitTray,   true),
            ("Cable",              NwdScope.Cable,     false),
        };

        public OverviewTab(MainDockablePanel main)
        {
            _main = main;
            InitializeComponent();
        }

        /// <summary>탭 페이지 생성 후 MainDockablePanel이 호출 — 행 더블클릭 이동과 상태 조회 대상 등록.</summary>
        public void Configure(TabControl tabControl,
            IEnumerable<(string title, IOverviewSource source, TabPage page)> entries)
        {
            _tabControl = tabControl;
            _entries.Clear();
            foreach (var (title, source, page) in entries)
                _entries.Add(new TabEntry { Title = title, Source = source, Page = page });
            RefreshOverview();
        }

        private void InitializeComponent()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoScroll = true,
                Padding = new Padding(4)
            };

            // 상단: 문서 상태 + 새로고침
            var topRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 30, AutoSize = false, WrapContents = false };
            var btnRefresh = new Button { Text = "새로고침", Width = 90, Height = 24 };
            btnRefresh.Click += (s, e) => RefreshOverview();
            _lblDoc = new Label { Text = "", AutoSize = true, Padding = new Padding(8, 5, 0, 0), ForeColor = Color.Gray };
            topRow.Controls.Add(btnRefresh);
            topRow.Controls.Add(_lblDoc);

            // ① 공종 현황 표
            _lvTabs = new ListView
            {
                Dock = DockStyle.Fill,
                Height = 170,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details,
                MultiSelect = false,
            };
            _lvTabs.Columns.Add("공종", 100);
            _lvTabs.Columns.Add("데이터", 150);
            _lvTabs.Columns.Add("인덱스", 70);
            _lvTabs.Columns.Add("3D 상태", 210);
            _lvTabs.Columns.Add("매칭", 70);
            _lvTabs.Columns.Add("미매칭", 70);
            _lvTabs.Columns.Add("인덱스 스코프", 260);
            _lvTabs.DoubleClick += LvTabs_DoubleClick;
            ListViewClipboard.EnableCtrlC(_lvTabs, null);

            // ② NWD Preflight 표
            _lvNwd = new ListView
            {
                Dock = DockStyle.Fill,
                Height = 130,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details,
                MultiSelect = false,
            };
            _lvNwd.Columns.Add("공종", 130);
            _lvNwd.Columns.Add("스코프(체인)", 110);
            _lvNwd.Columns.Add("판정", 130);
            _lvNwd.Columns.Add("발견 파일", 400);
            ListViewClipboard.EnableCtrlC(_lvNwd, null);

            layout.Controls.Add(topRow);
            layout.Controls.Add(new Label
            {
                Text = "공종 현황 (행 더블클릭 = 해당 탭으로 이동)",
                Font = new Font(Font, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Height = 18
            });
            layout.Controls.Add(_lvTabs);
            layout.Controls.Add(new Label
            {
                Text = "NWD 파일 사전 점검 (파일명 규약 — 인덱스 빌드 없이 판정)",
                Font = new Font(Font, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Height = 18
            });
            layout.Controls.Add(_lvNwd);
            layout.Controls.Add(new Label
            {
                Text = "※ 미발견이어도 하드 스코프가 아닌 탭은 전체 모델 fallback으로 동작합니다 (속도만 손해).\n" +
                       "   EIT(하드 스코프)는 미발견 시 매칭 0건, Structure는 영역 0개 — 파일명 규약을 먼저 확인하세요.\n" +
                       "   Sub-system 탭의 공종별 인덱스(MEQ/HYDROPKG/EIT/CABLE)는 전부 하드 스코프 — 미발견 공종은 매칭 0건.",
                ForeColor = Color.Gray,
                Dock = DockStyle.Fill,
                Height = 46
            });

            Controls.Add(layout);
        }

        private void LvTabs_DoubleClick(object sender, EventArgs e)
        {
            if (_tabControl == null || _lvTabs.SelectedItems.Count == 0) return;
            if (_lvTabs.SelectedItems[0].Tag is TabPage page)
                _tabControl.SelectedTab = page;
        }

        /// <summary>상태 재조회 — 캐시 없음, 호출 시점 스냅샷 (탭 선택 시 자동 + 새로고침 버튼).</summary>
        public void RefreshOverview()
        {
            var doc = _main.GetDocument();
            bool docOpen = false;
            int modelCount = 0;
            try { docOpen = doc != null && doc.Models.Count > 0; modelCount = doc?.Models.Count ?? 0; }
            catch { /* 문서 전환 중 조회 실패 가능 — 미열림 취급 */ }

            string docName = "-";
            if (docOpen)
            {
                try { docName = NwdScope.StripDirectory(doc.FileName ?? "(무제)"); } catch { docName = "(무제)"; }
            }
            _lblDoc.Text = docOpen
                ? $"문서: {docName} · 모델 {modelCount}개 · 조회 {DateTime.Now:HH:mm:ss}"
                : "문서: 열린 모델 없음";

            RefreshTabRows();
            RefreshPreflightRows(docOpen ? doc : null);
        }

        private void RefreshTabRows()
        {
            _lvTabs.BeginUpdate();
            _lvTabs.Items.Clear();
            foreach (var entry in _entries)
            {
                OverviewStatus st;
                try { st = entry.Source.GetOverviewStatus() ?? new OverviewStatus(); }
                catch { st = new OverviewStatus { DataText = "(조회 실패)" }; }

                var item = new ListViewItem(entry.Title) { Tag = entry.Page, UseItemStyleForSubItems = false };
                var dataSub = item.SubItems.Add(st.DataText);
                if (!st.DataLoaded) dataSub.ForeColor = Color.Gray;
                item.SubItems.Add(st.IndexText);
                var applySub = item.SubItems.Add(st.ApplyStateText);
                applySub.ForeColor = st.ApplyStale ? WarnColor
                    : (st.ApplyStateText.Contains("적용됨") ? OkColor : Color.Gray);
                item.SubItems.Add(st.MatchedText);
                var unSub = item.SubItems.Add(st.UnmatchedText);
                if (st.UnmatchedCount > 0) unSub.ForeColor = BadColor;
                var scopeSub = item.SubItems.Add(st.ScopeNote);
                if (st.ScopeFellBack) scopeSub.ForeColor = WarnColor;
                _lvTabs.Items.Add(item);
            }
            _lvTabs.EndUpdate();
        }

        private void RefreshPreflightRows(Autodesk.Navisworks.Api.Document doc)
        {
            _lvNwd.BeginUpdate();
            _lvNwd.Items.Clear();
            foreach (var (label, scope, hard) in PreflightScopes)
            {
                var item = new ListViewItem(label) { UseItemStyleForSubItems = false };
                if (doc == null)
                {
                    item.SubItems.Add(ChainLabelOf(scope));
                    var s = item.SubItems.Add("모델 미열림");
                    s.ForeColor = Color.Gray;
                    item.SubItems.Add("");
                }
                else
                {
                    ScopePreflight.Result r;
                    try { r = ScopePreflight.Probe(doc, scope); }
                    catch { r = new ScopePreflight.Result { ChainLabel = ChainLabelOf(scope) }; }
                    item.SubItems.Add(r.ChainLabel);
                    // 하드 스코프는 미발견 시 전체 fallback이 아니라 인덱스 0건 (2차 audit P2 —
                    // 실동작과 안내 불일치 수정).
                    var verdict = item.SubItems.Add(r.Found
                        ? $"✓ 발견 ({r.MatchedTier}, {r.Files.Count}개)"
                        : (hard ? "✕ 미발견 → 매칭 0건 (하드 스코프)" : "✕ 미발견 → 전체 모델 fallback"));
                    verdict.ForeColor = r.Found ? OkColor : BadColor;
                    item.SubItems.Add(string.Join(", ", r.Files));
                }
                _lvNwd.Items.Add(item);
            }
            _lvNwd.EndUpdate();
        }

        private static string ChainLabelOf(NwdScope scope)
        {
            var parts = new List<string>();
            for (var tier = scope; tier != null; tier = tier.Fallback)
                parts.Add(tier.Label);
            return string.Join("→", parts);
        }
    }
}
