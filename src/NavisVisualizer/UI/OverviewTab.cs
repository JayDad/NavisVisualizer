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
        /// <summary>마지막 빌드에서 스코프 파일이 미지정이라 인덱스 0건 (매핑 지정 필요).</summary>
        public bool ScopeUnmapped;
    }

    /// <summary>
    /// Overview 탭 (UX audit P1 — 첫 화면). 두 가지를 한 화면에서 사전 점검한다:
    ///
    /// ① 공종 현황 표 — 탭마다 열어보지 않아도 {데이터 로드 / 인덱스 / 3D 적용 상태 /
    ///    매칭·미매칭 / 인덱스 스코프 fallback 여부}를 한 표로. 행 더블클릭 = 그 탭으로 이동.
    /// ② 모델 파일 매핑 — 공종별로 어느 NWD 파일을 쓰는지(프로파일 별칭 자동 인식 / 직접 지정 /
    ///    전체 모델)를 인덱스 빌드 없이 판정해 표로 보이고, 행 더블클릭으로 바로 바꾼다
    ///    (ScopeMappingService — 파일 노드만 얕게 하강, geometry walk 없음). 전체 모델 자동
    ///    fallback이 폐지됐으므로(2026-09) "미지정"을 적용 전에 여기서 잡는 것이 이 탭의 핵심 가치.
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
        private ComboBox _cmbProfile;
        private Label _lblProfileNote;
        private bool _syncingProfile;

        private static readonly Color WarnColor = Color.FromArgb(190, 90, 0);
        private static readonly Color BadColor = Color.FromArgb(200, 40, 40);
        private static readonly Color OkColor = Color.FromArgb(0, 120, 40);

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

            // ② 모델 파일 매핑 표 (구 NWD Preflight — 판정만 하던 표를 "바꿀 수 있는" 표로)
            var profileRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 30, AutoSize = false, WrapContents = false };
            profileRow.Controls.Add(new Label { Text = "프로파일(별칭 셋):", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            _cmbProfile = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbProfile.SelectedIndexChanged += CmbProfile_SelectedIndexChanged;
            profileRow.Controls.Add(_cmbProfile);
            _lblProfileNote = new Label { AutoSize = true, ForeColor = Color.Gray, Padding = new Padding(8, 6, 0, 0) };
            profileRow.Controls.Add(_lblProfileNote);
            var btnChange = new Button { Text = "선택 공종 매핑 변경…", Width = 150, Height = 24, Margin = new Padding(16, 2, 0, 0) };
            btnChange.Click += (s, e) => ChangeSelectedMapping();
            profileRow.Controls.Add(btnChange);

            _lvNwd = new ListView
            {
                Dock = DockStyle.Fill,
                Height = 150,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details,
                MultiSelect = false,
            };
            _lvNwd.Columns.Add("공종", 130);
            _lvNwd.Columns.Add("방식", 90);
            _lvNwd.Columns.Add("판정", 150);
            _lvNwd.Columns.Add("적용 파일", 330);
            _lvNwd.Columns.Add("자동 인식 별칭", 160);
            _lvNwd.DoubleClick += (s, e) => ChangeSelectedMapping();
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
                Text = "모델 파일 매핑 — 공종별로 어느 NWD 파일을 쓰는지 (행 더블클릭 = 변경, 인덱스 빌드 없이 판정)",
                Font = new Font(Font, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Height = 18
            });
            layout.Controls.Add(profileRow);
            layout.Controls.Add(_lvNwd);
            layout.Controls.Add(new Label
            {
                Text = "※ 방식: 자동 = 프로파일 별칭으로 파일명 인식 / 직접 = 사용자가 체크한 파일(복수 가능) / 전체 = 모든 파일 순회(느림).\n" +
                       "   미지정 공종은 가시화 적용 시 파일 지정을 묻습니다 (전체 모델 자동 검색은 하지 않음). 매핑은 문서 이름별로 저장됩니다.\n" +
                       "   Sub-system 탭은 Equipment / Hydrotest / EIT EQ / Cable 매핑을 공종별로 그대로 씁니다.",
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
                if (st.ScopeUnmapped) scopeSub.ForeColor = BadColor;
                _lvTabs.Items.Add(item);
            }
            _lvTabs.EndUpdate();
        }

        private void RefreshPreflightRows(Autodesk.Navisworks.Api.Document doc)
        {
            RefreshProfileCombo(doc);

            _lvNwd.BeginUpdate();
            _lvNwd.Items.Clear();
            List<FileNodeInfo> nodes = null;
            if (doc != null)
            {
                try { nodes = ScopeMappingService.EnumerateFileNodes(doc); } catch { nodes = null; }
            }

            foreach (var scope in NwdScope.All)
            {
                var item = new ListViewItem(ScopeGate.TabLabelOf(scope)) { Tag = scope, UseItemStyleForSubItems = false };
                if (doc == null || nodes == null)
                {
                    item.SubItems.Add("-");
                    var s = item.SubItems.Add(doc == null ? "모델 미열림" : "(조회 실패)");
                    s.ForeColor = Color.Gray;
                    item.SubItems.Add("");
                    item.SubItems.Add(scope.ChainAliasLabel());
                }
                else
                {
                    ScopeResolution r;
                    try { r = ScopeMappingService.Resolve(doc, scope, nodes); }
                    catch (Exception ex) { r = new ScopeResolution { Scope = scope, Note = "해석 실패: " + ex.Message }; }

                    item.SubItems.Add(ModeLabel(r.Mode));
                    string verdictText;
                    Color verdictColor;
                    if (r.IsMapped)
                    {
                        verdictText = r.Mode == ScopeSelectionMode.AllModels
                            ? "⚠ 전체 모델 (느림)"
                            : $"✓ {r.Files.Count}개 파일" + (r.ExcludedFiles.Count > 0 ? $" (하위 {r.ExcludedFiles.Count}개 제외)" : "");
                        verdictColor = r.Mode == ScopeSelectionMode.AllModels ? WarnColor : OkColor;
                        if (r.MissingFiles.Count > 0) { verdictText += " · 일부 미발견"; verdictColor = WarnColor; }
                    }
                    else
                    {
                        verdictText = r.Mode == ScopeSelectionMode.Files ? "✕ 지정 파일 미발견 → 매칭 0건" : "✕ 미지정 → 적용 시 파일 지정 요청";
                        verdictColor = BadColor;
                    }
                    var verdict = item.SubItems.Add(verdictText);
                    verdict.ForeColor = verdictColor;
                    item.SubItems.Add(r.IsMapped ? string.Join(", ", r.Files) : (r.MissingFiles.Count > 0 ? "미발견: " + string.Join(", ", r.MissingFiles) : ""));
                    var aliasSub = item.SubItems.Add(scope.ChainAliasLabel());
                    aliasSub.ForeColor = Color.Gray;
                }
                _lvNwd.Items.Add(item);
            }
            _lvNwd.EndUpdate();
        }

        private static string ModeLabel(ScopeSelectionMode mode)
        {
            switch (mode)
            {
                case ScopeSelectionMode.Files: return "직접 지정";
                case ScopeSelectionMode.AllModels: return "전체 모델";
                default: return "자동";
            }
        }

        /// <summary>프로파일 콤보 = "(자동 감지: X)" + 프로파일 목록. 선택 변경은 문서별 설정에 저장.</summary>
        private void RefreshProfileCombo(Autodesk.Navisworks.Api.Document doc)
        {
            _syncingProfile = true;
            try
            {
                _cmbProfile.Items.Clear();
                if (doc == null)
                {
                    _cmbProfile.Enabled = false;
                    _lblProfileNote.Text = "";
                    return;
                }
                _cmbProfile.Enabled = true;
                var profiles = ScopeMappingService.LoadProfiles();
                var detected = ScopeProfiles.Detect(ScopeMappingService.DocKey(doc), profiles);
                var active = ScopeMappingService.ActivateProfile(doc, profiles);
                bool auto = ScopeMappingService.IsProfileAutoDetected(doc);

                _cmbProfile.Items.Add($"(자동 감지: {detected.Name})");
                foreach (var p in profiles) _cmbProfile.Items.Add(p.Name);
                _cmbProfile.SelectedIndex = auto ? 0 : Math.Max(0, 1 + profiles.FindIndex(p => p.Name == active.Name));

                string errs = ScopeMappingService.LastProfileErrors.Count > 0
                    ? $" · 별칭 파일 오류 {ScopeMappingService.LastProfileErrors.Count}건" : "";
                _lblProfileNote.Text = $"적용 중: {active.Name}{errs}";
            }
            finally { _syncingProfile = false; }
        }

        private void CmbProfile_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_syncingProfile) return;
            var doc = _main.GetDocument();
            if (doc == null) return;
            var cfg = ScopeMappingService.GetConfig(doc);
            cfg.ProfileName = _cmbProfile.SelectedIndex <= 0 ? null : _cmbProfile.SelectedItem.ToString();
            try { ScopeMappingService.SaveConfig(doc, cfg); }
            catch (Exception ex) { MessageBox.Show(this, $"프로파일 저장 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            _main.InvalidateScopeIndexes();   // 별칭 셋이 바뀌면 자동 인식 결과가 달라질 수 있음 → 재빌드
            RefreshOverview();
        }

        private void ChangeSelectedMapping()
        {
            var doc = _main.GetDocument();
            if (doc == null) { MessageBox.Show(this, "모델을 먼저 열어주세요.", "모델 파일 매핑"); return; }
            if (_lvNwd.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "바꿀 공종 행을 먼저 선택하세요 (행 더블클릭도 됩니다).", "모델 파일 매핑");
                return;
            }
            if (!(_lvNwd.SelectedItems[0].Tag is NwdScope scope)) return;
            ScopeGate.OpenMappingDialog(this, doc, scope);
            _main.InvalidateScopeIndexes();   // 매핑이 바뀌었을 수 있음 → 다음 적용 때 재빌드
            RefreshOverview();
        }
    }
}
