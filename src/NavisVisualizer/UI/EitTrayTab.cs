using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// EIT Tray 탭 — 트레이 설치 진척(Tray Number/BRANCH NO. 매칭, EIT nwd 스코프)을 % 기반 3단계
    /// (미착수/설치중/설치완료)로 시각화. 케이블 포설과는 무관(형상 Cable 탭이 별개). Excel↔OASIS
    /// 이중 소스. EIT_Tray는 날짜 컬럼이 없어 % 기반 현재상태 판정 — 기준일 UI는 실제 날짜 컬럼이
    /// 생길 때까지 표시하지 않는다 (비활성 상태로 노출하면 "왜 안 되지" 혼란만 줌 — UX audit QW7).
    /// </summary>
    public class EitTrayTab : UserControl, IOverviewSource
    {
        private readonly MainDockablePanel _main;

        // 레벨 타겟 인덱스는 로드된 트레이 ID 셋 기반 → 소스 전환/재로드 시 재빌드 강제(Spool 패턴).
        private bool _needsIndexRebuild;
        private List<EitTrayData> _trays = new List<EitTrayData>();
        private readonly Dictionary<TabDataSource, List<EitTrayData>> _traysBySource
            = new Dictionary<TabDataSource, List<EitTrayData>>();
        private bool _appliedOnce;
        private Dictionary<EitStage, ColorSetting> _colorSettings;

        private HashSet<string> _matchedTrayNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _unmatchedTrayNos = new List<string>();

        // Aggregation scope (현황 집계 범위) — null = 전체 모델 (no filtering)
        private ScopePanel _scopePanel;
        private readonly ScopeFilter _scopeFilter;
        private HashSet<string> _scopeKeys;

        private DataSourcePanel _srcPanel;
        private TextBox _txtSearch;
        private TabControl _tabFilter;
        private ListView _listView;
        private Button _btnApply;
        private Button _btnHideOthers;   // 체크 단계 외 숨김 (토글)
        private Autodesk.Navisworks.Api.ModelItemCollection _trayHiddenByStage;
        private Button _btnResetModule;  // 이 공종(EitTray) 색만 제거
        private Button _btnReset;
        private Button _btnViewpoint;
        private Button _btnNwd;
        private Label _lblStats;
        private Label _lblUnmatched;   // fixed 미매칭(모델 없음) count, pinned to the corner
        private ApplyStatePanel _applyState;   // 3D 적용 상태 표시 (데이터↔3D 어긋남 경고 전담)
        private ProgressBar _progressBar;

        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private Dictionary<EitStage, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<EitStage, (Panel, Button, ComboBox, CheckBox)>();

        public EitTrayTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.EitDefaults);
            _scopeFilter = new ScopeFilter(main.SectionSvc);
            InitializeComponent();
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

            // 색상 패널 핸들러가 참조하므로 먼저 생성 (버튼 연결은 버튼 행에서).
            _applyState = new ApplyStatePanel();

            _srcPanel = new DataSourcePanel();
            _srcPanel.ExcelLoadClicked    += (s, e) => LoadExcel();
            _srcPanel.TemplateClicked     += (s, e) => ExportInputTemplate();
            _srcPanel.OasisLoadClicked    += (s, e) => LoadOasis();
            _srcPanel.ActiveSourceChanged += (s, e) => ApplyActiveSourceData(reapply: false);
            _srcPanel.CompareClicked      += (s, e) => ExportComparison();

            // 기준일 UI 없음 — EIT_Tray/Excel 모두 날짜 컬럼이 없어 % 기반 현재상태 판정.
            // 날짜 컬럼이 생기면(§3 Cable Stage 날짜화) Spool과 동일한 기준일 행을 되살릴 것.

            var colorPanel = BuildColorPanel();

            var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            searchPanel.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _txtSearch = new TextBox { Width = 210, Text = "" };
            _txtSearch.TextChanged += (s, e) => FilterList();
            searchPanel.Controls.Add(_txtSearch);
            var btnExport = new Button { Text = "매칭 Status 엑셀 출력", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            btnExport.Click += BtnExport_Click;
            searchPanel.Controls.Add(btnExport);

            // 선택 행(없으면 표시 중인 전체 행)을 클립보드로 복사 — Ctrl+C 대체 버튼.
            var btnCopy = new Button { Text = "클립보드 복사", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            btnCopy.Click += (s, e) => CopyListToClipboard();
            searchPanel.Controls.Add(btnCopy);

            var statsRow = new Panel { Dock = DockStyle.Fill, Height = 36 };
            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false };
            _lblUnmatched = new Label
            {
                Dock = DockStyle.Right, Width = 150, AutoSize = false,
                TextAlign = ContentAlignment.TopRight, ForeColor = Color.Gray, Text = "",
            };
            statsRow.Controls.Add(_lblStats);
            statsRow.Controls.Add(_lblUnmatched);

            _scopePanel = new ScopePanel { Dock = DockStyle.Fill };
            _scopePanel.ApplyRequested += (s, e) => ApplyScope();

            _tabFilter = new TabControl { Dock = DockStyle.Fill, Height = 230 };
            var tabAll = new TabPage("전체");
            var tabMatched = new TabPage("매칭");
            var tabUnmatched = new TabPage("미매칭");
            _tabFilter.TabPages.Add(tabAll);
            _tabFilter.TabPages.Add(tabMatched);
            _tabFilter.TabPages.Add(tabUnmatched);
            _tabFilter.SelectedIndexChanged += (s, e) =>
            {
                var selectedTab = _tabFilter.SelectedTab;
                if (!selectedTab.Controls.Contains(_listView))
                    selectedTab.Controls.Add(_listView);
                FilterList();
            };

            _listView = new ListView
            {
                Dock = DockStyle.Fill, FullRowSelect = true, GridLines = true, View = View.Details,
            };
            _listView.Columns.Add("Tray No.", 200);
            _listView.Columns.Add("Lth", 55);
            _listView.Columns.Add("Installed", 65);
            _listView.Columns.Add("Install %", 65);
            _listView.Columns.Add("단계", 75);
            _listView.Columns.Add("매칭", 40);
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
            _listView.ColumnClick += ListView_ColumnClick;
            // ListView는 기본적으로 Ctrl+C를 지원하지 않으므로 공용 헬퍼로 배선.
            ListViewClipboard.EnableCtrlC(_listView, ShowCopied);
            tabAll.Controls.Add(_listView);

            // 1행(가시화)
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnApply      = new Button { Text = "가시화 적용",       Width = 90 };
            _btnHideOthers = new Button { Text = "체크 단계 외 숨김", Width = 130 };
            _btnApply.Click      += BtnApply_Click;
            _btnHideOthers.Click += BtnHideOthers_Click;
            _applyState.AttachApplyButton(_btnApply);
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnHideOthers, _applyState });

            // 2행(해제)
            var btnPanelReset = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnResetModule = new Button { Text = "이 탭 가시화 해제", Width = 130 };
            _btnReset       = new Button { Text = "전체 가시화 해제", Width = 130 };
            _btnResetModule.Click += BtnResetModule_Click;
            _btnReset.Click       += BtnReset_Click;
            btnPanelReset.Controls.AddRange(new Control[] { _btnResetModule, _btnReset });

            // 3행(출력)
            var btnPanel2 = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnViewpoint = new Button { Text = "Viewpoint 저장", Width = 120 };
            _btnNwd       = new Button { Text = "NWD Export",     Width = 110 };
            _btnViewpoint.Click += BtnViewpoint_Click;
            _btnNwd.Click       += BtnNwd_Click;
            btnPanel2.Controls.AddRange(new Control[] { _btnViewpoint, _btnNwd });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };

            layout.Controls.Add(_srcPanel);
            layout.Controls.Add(new Label { Text = "단계 & 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanel);
            // 색상 편집(▼·투명도)은 기본 접힘 — 체크박스·스와치만 상시 노출 (UX audit P1)
            layout.Controls.Add(ColorEditCollapse.BuildToggleRow(colorPanel));
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(btnPanelReset);
            layout.Controls.Add(btnPanel2);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(statsRow);
            layout.Controls.Add(searchPanel);
            layout.Controls.Add(_scopePanel);
            layout.Controls.Add(_tabFilter);

            Controls.Add(layout);
        }

        private Panel BuildColorPanel()
        {
            var allStages = new[] { EitStage.NotStarted }.Concat(EitStageInfo.OrderedStages).ToArray();
            var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, AutoSize = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));

            for (int i = 0; i < allStages.Length; i++)
            {
                var stage = allStages[i];
                var setting = _colorSettings[stage];
                string label = EitStageInfo.Labels[stage];

                var chk = new CheckBox { Text = label, Checked = true, AutoSize = true };
                chk.CheckedChanged += (s, e) => _applyState.MarkStale("단계 선택 변경");
                var colorBox = new Panel { Width = 32, Height = 20, BackColor = setting.DisplayColor, BorderStyle = BorderStyle.FixedSingle };
                var colorBtn = new Button { Text = "▼", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
                colorBtn.FlatAppearance.BorderSize = 0;
                var transparencyBox = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%", "100%" })
                    transparencyBox.Items.Add(t);
                transparencyBox.Text = $"{(int)(setting.Transparency * 100)}%";

                var cs = stage;
                colorBtn.Click += (s, e) =>
                {
                    using (var dlg = new ColorDialog { Color = _colorSettings[cs].DisplayColor })
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            _colorSettings[cs].DisplayColor = dlg.Color;
                            colorBox.BackColor = dlg.Color;
                            IncrementalUpdate(cs.ToString());
                        }
                    }
                };
                transparencyBox.SelectedIndexChanged += (s, e) =>
                {
                    if (double.TryParse(transparencyBox.Text.Replace("%", ""), out double pct))
                    {
                        _colorSettings[cs].Transparency = pct / 100.0;
                        IncrementalUpdate(cs.ToString());
                    }
                };

                _colorRows[stage] = (colorBox, colorBtn, transparencyBox, chk);

                int row = i / 2;
                int colOffset = (i % 2) * 4;
                layout.Controls.Add(chk, colOffset + 0, row);
                layout.Controls.Add(colorBox, colOffset + 1, row);
                layout.Controls.Add(colorBtn, colOffset + 2, row);
                layout.Controls.Add(transparencyBox, colOffset + 3, row);
            }

            panel.Controls.Add(layout);
            return panel;
        }

        private void IncrementalUpdate(string stageKey)
        {
            var doc = _main.GetDocument();
            if (doc == null || !_main.OverrideEngine.HasCachedData(VisualModule.EitTray)) return;
            if (Enum.TryParse<EitStage>(stageKey, out var stage) && _colorSettings.TryGetValue(stage, out var setting))
                _main.OverrideEngine.UpdateStageColor(doc, VisualModule.EitTray, stageKey, setting);
        }

        private void LoadExcel()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "EIT Tray Excel 로드",
                Filter = "Excel 파일 (*.xlsx;*.xls;*.xlsb)|*.xlsx;*.xls;*.xlsb|모든 파일|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var list = ExcelLoader.LoadEitTray(dlg.FileName);
                    _traysBySource[TabDataSource.Excel] = list;
                    _srcPanel.SetLoaded(TabDataSource.Excel, list.Count, Path.GetFileName(dlg.FileName));
                    if (_srcPanel.ActiveSource == TabDataSource.Excel)
                        ApplyActiveSourceData(reapply: false);
                }
                catch (Exception ex)
                {
                    _srcPanel.SetFailed(TabDataSource.Excel, "로드 실패");
                    MessageBox.Show($"Excel 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void LoadOasis()
        {
            try
            {
                var settings = SqlConnectionSettings.Load();
                var list = SqlLoader.LoadEitTray(settings);
                _traysBySource[TabDataSource.Oasis] = list;
                string prj = string.IsNullOrEmpty(settings.ProjectNo) ? "전체" : settings.ProjectNo;
                _srcPanel.SetLoaded(TabDataSource.Oasis, list.Count, $"{settings.Database}/{prj} · {DateTime.Now:HH:mm}");
                if (_srcPanel.ActiveSource == TabDataSource.Oasis)
                    ApplyActiveSourceData(reapply: false);
            }
            catch (Exception ex)
            {
                _srcPanel.SetFailed(TabDataSource.Oasis, "로드 실패");
                MessageBox.Show($"OASIS 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportInputTemplate()
        {
            try
            {
                string path = InputTemplate.ExportEitTray();
                SaveNotifier.ShowSaved(this, "Template 출력", path,
                    "작성 후 Excel 형식(.xlsx)으로 저장해 Import 하세요.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"입력 양식 저장 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>적용 기준 소스로 화면 전환. 레벨 타겟 인덱스는 트레이 ID 셋 기반 → 소스 전환 시 재빌드.</summary>
        private void ApplyActiveSourceData(bool reapply)
        {
            _trays = _traysBySource.TryGetValue(_srcPanel.ActiveSource, out var list) ? list : new List<EitTrayData>();
            _matchedTrayNos.Clear();
            _unmatchedTrayNos.Clear();
            _needsIndexRebuild = true;
            _scopeFilter.Invalidate();
            _scopeKeys = null;

            bool willReapply = reapply && _trays.Count > 0 && _main.GetDocument() != null;
            if (!willReapply) _scopePanel.ResetToFullModel();

            _tabFilter.TabPages[0].Text = $"전체 ({_trays.Count})";
            _tabFilter.TabPages[1].Text = "매칭";
            _tabFilter.TabPages[2].Text = "미매칭";
            FilterList();
            UpdateStats();

            // 색이 이전 소스 기준으로 남아 있으면 상태 표시기로 경고 (통계 라벨은 통계만 — P0-1)
            if (!willReapply)
                _applyState.MarkStale("데이터 변경");

            if (willReapply) BtnApply_Click(null, EventArgs.Empty);
        }

        private void ExportComparison()
        {
            if (!_traysBySource.TryGetValue(TabDataSource.Excel, out var excelList) ||
                !_traysBySource.TryGetValue(TabDataSource.Oasis, out var oasisList))
            {
                MessageBox.Show("Excel과 OASIS를 모두 로드해야 비교할 수 있습니다.");
                return;
            }
            // EIT_Tray엔 Lth/Installed/date가 없어 Stage/Install%만 diff (전 행 허위 delta 방지).
            var fields = new List<SourceComparer.Field<EitTrayData>>
            {
                new SourceComparer.Field<EitTrayData>("Install %",
                    t => t.InstallProgress.HasValue ? $"{t.InstallProgress.Value * 100:0}%" : ""),
                new SourceComparer.Field<EitTrayData>("단계",
                    t => EitStageInfo.Labels[t.GetStage()]),
            };
            var lines = SourceComparer.BuildCsv("Tray No", excelList, oasisList, t => t.TrayNumber, fields);
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"EitTray_Compare_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            SaveNotifier.ShowSaved(this, "Excel↔OASIS 비교 출력", path);
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (_trays.Count == 0) { MessageBox.Show("데이터를 먼저 로드하세요."); return; }
            var lines = new List<string>();
            lines.Add($"집계 범위,{MatchScopeInfo.Label(_scopePanel.CurrentScope)}");
            lines.Add($"인덱스 스코프,\"{_main.ElecTagSearcher.LastScopeNote ?? "-"}\"");
            lines.Add("Tray No.,Tray Lth,Tray Installed,Install %,Stage,Matched");
            foreach (var t in _trays)
            {
                if (!InScope(t.TrayNumber)) continue;
                var stage = t.GetStage();
                string stageLabel = EitStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool matched = _matchedTrayNos.Count == 0 || _matchedTrayNos.Contains(t.TrayNumber);
                string lth = t.TrayLth.HasValue ? t.TrayLth.Value.ToString("0.##") : "";
                string installed = t.TrayInstalled.HasValue ? t.TrayInstalled.Value.ToString("0.##") : "";
                string pct = t.InstallProgress.HasValue ? $"{t.InstallProgress.Value * 100:0}%" : "";
                lines.Add($"\"{t.TrayNumber}\",\"{lth}\",\"{installed}\",\"{pct}\",\"{stageLabel}\",\"{(matched ? "O" : "X")}\"");
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"EitTray_Match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, System.Text.Encoding.UTF8);
            SaveNotifier.ShowSaved(this, "매칭 Status 엑셀 출력", path);
        }

        private void BuildIndex()
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            // 단순 marquee만으로는 무엇을 하는지 알 수 없어 단계 문구 병기 (UX audit P0-3)
            _lblStats.Text = "모델 태그 인덱스 생성 중…";
            Application.DoEvents();
            // 레벨 타겟: 로드된 트레이 ID 셋으로 매칭 깊이만 인덱싱하고 그 아래(geometry)는 안 봄
            // — "매칭 → 하위 트리 무시 → 옆으로" (general walk의 자식 스캔 비용 제거, 최다 지연 대책).
            // 하드 스코프: EIT nwd에서만 (미발견 시 전체 트리 안 훑고 0건 + "파일명 규약 확인" 노트).
            // §2 리스크: 트레이가 여러 깊이에 섞이면 첫 매칭 깊이만 인덱싱 → 매칭 건수 대조 필요(Windows).
            var trayIds = new HashSet<string>(
                _trays.Select(t => EitTrayData.NormalizeId(t.TrayNumber)),
                StringComparer.OrdinalIgnoreCase);
            _main.ElecTagSearcher.BuildIndexForTags(doc, trayIds, NwdScope.EitTray, hardScope: true);
            _needsIndexRebuild = false;
            _progressBar.Visible = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _trays.Count == 0)
            {
                MessageBox.Show("데이터를 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_needsIndexRebuild || _main.ElecTagSearcher.NeedsRebuild(doc))
                BuildIndex();

            var activeSettings = new Dictionary<EitStage, ColorSetting>();
            foreach (var kv in _colorRows)
                if (kv.Value.check.Checked)
                    activeSettings[kv.Key] = _colorSettings[kv.Key];

            OverrideResult result;
            _btnApply.Enabled = false;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            _lblStats.Text = "색상 적용 중…";
            Application.DoEvents();
            try
            {
                result = _main.OverrideEngine.ApplyEit(doc, _trays, activeSettings);
            }
            finally
            {
                _progressBar.Visible = false;
                _progressBar.Style = ProgressBarStyle.Blocks;
                _btnApply.Enabled = true;
            }

            _unmatchedTrayNos = result.UnmatchedIds;
            var unmatchedSet = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedTrayNos = new HashSet<string>(
                _trays.Select(t => t.TrayNumber).Where(id => !unmatchedSet.Contains(id)),
                StringComparer.OrdinalIgnoreCase);

            _scopeFilter.Invalidate();
            ReapplyCurrentScope(doc);

            _appliedOnce = true;
            _applyState.SetApplied(
                _srcPanel.ActiveSource == TabDataSource.Oasis ? "OASIS" : "Excel");
            UpdateTabCounts();
            UpdateStats(result);
            FilterList();
        }

        /// <summary>체크된 단계의 매칭 트레이만 남기고 나머지 매칭 트레이를 3D에서 숨긴다(토글).</summary>
        private void BtnHideOthers_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;

            if (_trayHiddenByStage != null)
            {
                doc.Models.SetHidden(_trayHiddenByStage, false);
                _trayHiddenByStage = null;
                _btnHideOthers.Text = "체크 단계 외 숨김";
                return;
            }
            if (_matchedTrayNos.Count == 0) { MessageBox.Show("먼저 가시화 적용을 실행하세요."); return; }
            if (_needsIndexRebuild || _main.ElecTagSearcher.NeedsRebuild(doc))
            {
                MessageBox.Show("모델 또는 데이터 소스가 변경되었습니다. 가시화 적용을 다시 실행한 뒤 사용하세요.");
                return;
            }

            var checkedStages = new HashSet<EitStage>(_colorRows.Where(kv => kv.Value.check.Checked).Select(kv => kv.Key));
            var itemsByKey = _main.ElecTagSearcher.FindBySpoolIds(
                _matchedTrayNos.Select(EitTrayData.NormalizeId).Distinct(StringComparer.OrdinalIgnoreCase));
            var toHide = new Autodesk.Navisworks.Api.ModelItemCollection();

            foreach (var t in _trays)
            {
                if (!_matchedTrayNos.Contains(t.TrayNumber)) continue;
                if (checkedStages.Contains(t.GetStage())) continue;
                if (itemsByKey.TryGetValue(EitTrayData.NormalizeId(t.TrayNumber), out var items))
                    toHide.AddRange(items);
            }
            if (toHide.Count == 0) { MessageBox.Show("숨길 대상이 없습니다 (모든 매칭 트레이가 체크된 단계입니다)."); return; }

            doc.Models.SetHidden(toHide, true);
            _trayHiddenByStage = toHide;
            _btnHideOthers.Text = "전체 보기";
        }

        // ----- 현황 집계 범위 -----

        private bool InScope(string id) =>
            _scopeKeys == null || !_matchedTrayNos.Contains(id) || _scopeKeys.Contains(id);

        private Dictionary<string, List<Autodesk.Navisworks.Api.ModelItem>> BuildScopeItemsByKey()
        {
            var found = _main.ElecTagSearcher.FindBySpoolIds(
                _matchedTrayNos.Select(EitTrayData.NormalizeId).Distinct(StringComparer.OrdinalIgnoreCase));
            var itemsByKey = new Dictionary<string, List<Autodesk.Navisworks.Api.ModelItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in _matchedTrayNos)
                itemsByKey[id] = found.TryGetValue(EitTrayData.NormalizeId(id), out var items)
                    ? items : new List<Autodesk.Navisworks.Api.ModelItem>();
            return itemsByKey;
        }

        private void ApplyScope()
        {
            var scope = _scopePanel.SelectedScope;
            if (scope == MatchScope.FullModel)
            {
                _scopeKeys = null;
                _scopeFilter.SetFullModel();
            }
            else
            {
                var doc = _main.GetDocument();
                if (doc == null || _matchedTrayNos.Count == 0)
                {
                    MessageBox.Show("먼저 가시화 적용을 실행하세요. 집계 범위는 매칭된 항목에 적용됩니다.");
                    return;
                }
                if (_needsIndexRebuild || _main.ElecTagSearcher.NeedsRebuild(doc))
                {
                    MessageBox.Show("모델 또는 데이터 소스가 변경되었습니다. 가시화 적용을 다시 실행한 뒤 범위를 선택하세요.");
                    return;
                }

                _progressBar.Style = ProgressBarStyle.Marquee;
                _progressBar.Visible = true;
                Application.DoEvents();
                try
                {
                    _scopeKeys = _scopeFilter.Apply(doc, scope, BuildScopeItemsByKey());
                }
                finally
                {
                    _progressBar.Visible = false;
                    _progressBar.Style = ProgressBarStyle.Blocks;
                }
            }

            _scopePanel.SetCurrentScope(scope);
            UpdateTabCounts();
            FilterList();
            UpdateStats();
        }

        private void ReapplyCurrentScope(Autodesk.Navisworks.Api.Document doc)
        {
            var scope = _scopePanel.CurrentScope;
            if (scope == MatchScope.FullModel) { _scopeKeys = null; return; }
            _scopeKeys = _scopeFilter.Apply(doc, scope, BuildScopeItemsByKey());
        }

        private void UpdateTabCounts()
        {
            bool hasApplied = _matchedTrayNos.Count > 0 || _unmatchedTrayNos.Count > 0;
            if (!hasApplied) return;
            int matchedInScope = _scopeKeys == null ? _matchedTrayNos.Count : _matchedTrayNos.Count(id => _scopeKeys.Contains(id));
            int total = _scopeKeys == null ? _trays.Count : _trays.Count(t => InScope(t.TrayNumber));
            _tabFilter.TabPages[0].Text = $"전체 ({total})";
            _tabFilter.TabPages[1].Text = $"매칭 ({matchedInScope})";
            _tabFilter.TabPages[2].Text = $"미매칭 ({_unmatchedTrayNos.Count})";
        }

        /// <summary>이 탭 가시화 해제: 이 탭(EitTray) 색만 제거 — 다른 공종 색은 유지. 숨김도 복원.</summary>
        private void BtnResetModule_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            if (_trayHiddenByStage != null)
            {
                doc.Models.SetHidden(_trayHiddenByStage, false);
                _trayHiddenByStage = null;
                _btnHideOthers.Text = "체크 단계 외 숨김";
            }
            _main.OverrideEngine.ResetModule(doc, VisualModule.EitTray);
            _lblStats.Text = "이 탭 가시화 해제 완료 (EIT Tray 색만 제거)";
            _applyState.SetCleared();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            if (_trayHiddenByStage != null)
            {
                doc.Models.SetHidden(_trayHiddenByStage, false);
                _trayHiddenByStage = null;
                _btnHideOthers.Text = "체크 단계 외 숨김";
            }
            _main.OverrideEngine.Reset(doc);
            _lblStats.Text = "전체 가시화 해제 완료";
            _lblUnmatched.Text = "";
            _applyState.SetCleared();
        }

        private void BtnViewpoint_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            string name = $"EitTray_{DateTime.Now:yyyyMMdd_HHmm}";
            try { _main.ExportSvc.SaveViewpoint(doc, name); MessageBox.Show($"Viewpoint '{name}' 저장 완료"); }
            catch (Exception ex) { MessageBox.Show($"Viewpoint 저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void BtnNwd_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.ExportSvc.ExportNwdWithDialog(doc);
        }

        private void ListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;
            var doc = _main.GetDocument();
            if (doc == null || _needsIndexRebuild || !_main.ElecTagSearcher.IsIndexBuilt || _main.ElecTagSearcher.NeedsRebuild(doc)) return;

            var collection = new Autodesk.Navisworks.Api.ModelItemCollection();
            foreach (ListViewItem selected in _listView.SelectedItems)
            {
                var tray = selected.Tag as EitTrayData;
                if (tray == null) continue;
                var found = _main.ElecTagSearcher.FindBySpoolIds(new[] { EitTrayData.NormalizeId(tray.TrayNumber) });
                foreach (var items in found.Values)
                    collection.AddRange(items);
            }

            if (collection.Count == 0) return;
            doc.CurrentSelection.CopyFrom(collection);
            doc.ActiveView.FocusOnCurrentSelection();
        }

        /// <summary>[클립보드 복사] 버튼 → 공용 헬퍼 호출 후 결과 표시.</summary>
        private void CopyListToClipboard() => ShowCopied(ListViewClipboard.CopySelectedOrAll(_listView));

        private void ShowCopied(int n)
        {
            if (n > 0) _lblStats.Text = $"클립보드에 {n}행 복사됨";
        }

        private void ListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sortColumn) _sortAscending = !_sortAscending;
            else { _sortColumn = e.Column; _sortAscending = true; }
            _listView.ListViewItemSorter = new ListViewItemComparer(_sortColumn, _sortAscending);
            _listView.Sort();
        }

        private class ListViewItemComparer : System.Collections.IComparer
        {
            private readonly int _col; private readonly int _dir;
            public ListViewItemComparer(int c, bool asc) { _col = c; _dir = asc ? 1 : -1; }
            public int Compare(object x, object y) =>
                string.Compare(((ListViewItem)x).SubItems[_col].Text, ((ListViewItem)y).SubItems[_col].Text, StringComparison.OrdinalIgnoreCase) * _dir;
        }

        private void FilterList()
        {
            string keyword = _txtSearch?.Text?.Trim().ToUpperInvariant() ?? "";
            int tabIndex = _tabFilter.SelectedIndex;

            var filtered = _trays.AsEnumerable();
            if (tabIndex == 1 && _matchedTrayNos.Count > 0)
                filtered = filtered.Where(t => _matchedTrayNos.Contains(t.TrayNumber));
            else if (tabIndex == 2 && _unmatchedTrayNos.Count > 0)
                filtered = filtered.Where(t => _unmatchedTrayNos.Contains(t.TrayNumber));

            if (_scopeKeys != null)
                filtered = filtered.Where(t => InScope(t.TrayNumber));
            if (!string.IsNullOrEmpty(keyword))
                filtered = filtered.Where(t => (t.TrayNumber ?? "").ToUpperInvariant().Contains(keyword));

            _listView.Items.Clear();
            foreach (var tray in filtered)
            {
                var stage = tray.GetStage();
                string stageLabel = EitStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
                bool hasApplied = _matchedTrayNos.Count > 0 || _unmatchedTrayNos.Count > 0;
                string matchLabel = !hasApplied ? "-" : (_matchedTrayNos.Contains(tray.TrayNumber) ? "O" : "X");
                string lth = tray.TrayLth.HasValue ? tray.TrayLth.Value.ToString("0.##") : "-";
                string installed = tray.TrayInstalled.HasValue ? tray.TrayInstalled.Value.ToString("0.##") : "-";
                string pct = tray.InstallProgress.HasValue ? $"{tray.InstallProgress.Value * 100:0}%" : "-";

                var item = new ListViewItem(tray.TrayNumber);
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(lth);
                item.SubItems.Add(installed);
                item.SubItems.Add(pct);
                var stageSubItem = item.SubItems.Add(stageLabel);
                if (_colorSettings.TryGetValue(stage, out var setting))
                    stageSubItem.ForeColor = setting.DisplayColor;
                var matchSubItem = item.SubItems.Add(matchLabel);
                if (matchLabel == "X")
                    matchSubItem.ForeColor = Color.Red;
                item.Tag = tray;
                _listView.Items.Add(item);
            }
        }

        private void UpdateStats(OverrideResult result = null)
        {
            bool hasApplied = _matchedTrayNos.Count > 0 || _unmatchedTrayNos.Count > 0;

            var statBasis = _trays.Where(t => InScope(t.TrayNumber));
            if (hasApplied) statBasis = statBasis.Where(t => _matchedTrayNos.Contains(t.TrayNumber));
            var counts = statBasis.GroupBy(t => t.GetStage()).ToDictionary(g => g.Key, g => g.Count());

            var parts = new List<string>();
            var allStages = new[] { EitStage.NotStarted }.Concat(EitStageInfo.OrderedStages).Reverse();
            foreach (var stage in allStages)
                if (counts.TryGetValue(stage, out int cnt) && cnt > 0)
                    parts.Add($"{EitStageInfo.Labels[stage]} {cnt}");

            string line2 = "";
            if (hasApplied)
            {
                int matchedInScope = _scopeKeys == null ? _matchedTrayNos.Count : _matchedTrayNos.Count(id => _scopeKeys.Contains(id));
                line2 = $"매칭 {matchedInScope}";
                if (_scopeKeys != null) line2 += $" ({MatchScopeInfo.Label(_scopePanel.CurrentScope)} 기준)";
            }
            _lblStats.Text = string.Join("  ", parts) + (!string.IsNullOrEmpty(line2) ? $"\n{line2}" : "");
            _lblUnmatched.Text = hasApplied ? $"미매칭 {_unmatchedTrayNos.Count}건" : "";
        }

        private Dictionary<EitStage, ColorSetting> CloneDefaults(Dictionary<EitStage, ColorSetting> defaults)
        {
            var clone = new Dictionary<EitStage, ColorSetting>();
            foreach (var kv in defaults) clone[kv.Key] = kv.Value.Clone();
            return clone;
        }

        /// <summary>Overview 탭 상태 노출 — 인메모리 조회만 (IOverviewSource).</summary>
        public OverviewStatus GetOverviewStatus()
        {
            bool hasApplied = _matchedTrayNos.Count > 0 || _unmatchedTrayNos.Count > 0;
            string src = _srcPanel.ActiveSource == TabDataSource.Oasis ? "OASIS" : "Excel";
            return new OverviewStatus
            {
                DataLoaded = _trays.Count > 0,
                DataText = _trays.Count > 0 ? $"{src} {_trays.Count:N0}건" : "미로드",
                IndexText = _main.ElecTagSearcher.IsIndexBuilt
                    ? _main.ElecTagSearcher.IndexedCount.ToString("N0") : "-",
                ApplyStateText = _applyState.Text,
                ApplyStale = _applyState.IsStale,
                MatchedText = hasApplied ? _matchedTrayNos.Count.ToString("N0") : "-",
                UnmatchedText = hasApplied ? _unmatchedTrayNos.Count.ToString("N0") : "-",
                UnmatchedCount = hasApplied ? _unmatchedTrayNos.Count : 0,
                ScopeNote = _main.ElecTagSearcher.LastScopeNote ?? "-",
                ScopeFellBack = _main.ElecTagSearcher.LastScopeFellBack,
            };
        }
    }
}
