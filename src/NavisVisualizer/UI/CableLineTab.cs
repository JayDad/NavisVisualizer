using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;
using NavisVisualizer.Visualizers;
using Color = System.Drawing.Color;
using View = System.Windows.Forms.View;
using Application = System.Windows.Forms.Application;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// Cable(형상) 탭 — 07_Trion_All_Cable.nwd의 cable-no 컴포넌트를 직접 매칭·하이라이트한다
    /// (기존 Cable Pull은 트레이 노드 박스 집계 — 별개). Excel↔OASIS 듀얼소스
    /// (EIT_Cable 철자 실측 확정 2026-07 — SqlLoader.LoadCable). 주요 기능: ① 케이블 목록
    /// 하이라이트(stage 날짜 없으면 단색), ② 날짜 기반 4단계 공정 시각화, ③ 활성 단면 통과
    /// 케이블 추출(clash), ④ 겹침 완화(숨김 isolate + 투명 필터 포커스). 미매칭은 스코프와
    /// 직교(전역 고정, 코너 라벨 — §7/L3).
    /// </summary>
    public class CableLineTab : UserControl
    {
        private readonly MainDockablePanel _main;

        private readonly Dictionary<TabDataSource, List<CableLineData>> _cablesBySource
            = new Dictionary<TabDataSource, List<CableLineData>>();
        private List<CableLineData> _cables = new List<CableLineData>();
        private Dictionary<CableLineStage, ColorSetting> _colorSettings;
        private ColorSetting _highlightSetting;
        private bool _lastHighlightMode;

        private HashSet<string> _matchedCableNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _unmatchedCableNos = new List<string>();

        // Aggregation scope (현황 집계 범위) — Clipping 영역은 clash(선분-vs-볼륨)로 판정.
        private ScopePanel _scopePanel;
        private readonly ScopeFilter _scopeFilter;
        private HashSet<string> _scopeKeys;
        private readonly CableClashService _clash = new CableClashService();
        private readonly Func<string, List<ModelItem>, IList<ClipPlane>, bool> _volumeJudge;

        // 레벨 타겟 인덱스는 활성 소스 태그 셋 기반 → 로드/소스 전환 시 재빌드 플래그(Spool 패턴).
        private bool _needsIndexRebuild;

        // 겹침 완화용 숨김(두 isolate 버튼 공유 — 상호배타). null = 숨김 없음.
        private ModelItemCollection _cableHidden;

        private Document _subscribedDoc;
        private bool _suppressSelectionSync;
        private bool _focusOn;
        private bool _suppressFocusCheck;

        private DataSourcePanel _srcPanel;
        private DateTimePicker _dtpReference;
        private TextBox _txtSearch;
        private CheckBox _chkFocus;
        private TabControl _tabFilter;
        private ListView _listView;
        private Button _btnApply;
        private Button _btnHideOthers;   // 체크 단계 외 숨김
        private Button _btnIsolateSel;   // 선택 케이블만 보기
        private Button _btnResetModule;
        private Button _btnReset;
        private Button _btnViewpoint;
        private Button _btnNwd;
        private Label _lblStats;
        private Label _lblUnmatched;
        private ProgressBar _progressBar;

        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private Dictionary<CableLineStage, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<CableLineStage, (Panel, Button, ComboBox, CheckBox)>();

        public CableLineTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.CableLineDefaults);
            _highlightSetting = ColorSetting.CableLineHighlight;
            _scopeFilter = new ScopeFilter(main.SectionSvc);
            _volumeJudge = (cableNo, items, planes) =>
                _clash.PassesVolume(cableNo, items, planes, _main.SectionSvc.KeepPositiveSide);
            InitializeComponent();
            this.HandleDestroyed += (s, e) => UnsubscribeSelection();
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

            _srcPanel = new DataSourcePanel();
            _srcPanel.ExcelLoadClicked    += (s, e) => LoadExcel();
            _srcPanel.TemplateClicked     += (s, e) => ExportInputTemplate();
            _srcPanel.OasisLoadClicked    += (s, e) => LoadOasis();
            // 라디오 전환은 절대 자동 재적용하지 않는다(§6) — 리스트/통계만 새 소스로 갱신.
            _srcPanel.ActiveSourceChanged += (s, e) => ApplyActiveSourceData();
            _srcPanel.CompareClicked      += (s, e) => ExportComparison();

            var datePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            datePanel.Controls.Add(new Label { Text = "기준일:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _dtpReference = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Width = 110 };
            _dtpReference.ValueChanged += (s, e) => { if (_cables.Count > 0) { FilterList(); UpdateStats(); } };
            datePanel.Controls.Add(_dtpReference);
            datePanel.Controls.Add(new Label { Text = "(stage 날짜 없으면 하이라이트 단색)", AutoSize = true, ForeColor = Color.Gray, Padding = new Padding(6, 4, 0, 0) });

            var colorPanel = BuildColorPanel();

            // 1행(가시화)
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnApply       = new Button { Text = "가시화 적용",       Width = 90 };
            _btnHideOthers  = new Button { Text = "체크 단계 외 숨김",  Width = 130 };
            _btnIsolateSel  = new Button { Text = "선택 케이블만 보기", Width = 130 };
            _btnApply.Click      += BtnApply_Click;
            _btnHideOthers.Click += BtnHideOthers_Click;
            _btnIsolateSel.Click += BtnIsolateSel_Click;
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnHideOthers, _btnIsolateSel });

            // 2행(초기화)
            var btnPanelReset = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnResetModule = new Button { Text = "공종 초기화", Width = 100 };
            _btnReset       = new Button { Text = "전체 초기화", Width = 100 };
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

            // Stats row: scoped stats left, fixed 미매칭(모델 없음) count pinned right (§7/L3).
            var statsRow = new Panel { Dock = DockStyle.Fill, Height = 36 };
            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false };
            _lblUnmatched = new Label
            {
                Dock = DockStyle.Right, Width = 150, AutoSize = false,
                TextAlign = ContentAlignment.TopRight, ForeColor = Color.Gray, Text = "",
            };
            statsRow.Controls.Add(_lblStats);
            statsRow.Controls.Add(_lblUnmatched);

            var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            searchPanel.Controls.Add(new Label { Text = "검색(Cable/Equip):", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _txtSearch = new TextBox { Width = 170 };
            _txtSearch.TextChanged += (s, e) => { FilterList(); RefreshFocusIfActive(); };
            searchPanel.Controls.Add(_txtSearch);
            _chkFocus = new CheckBox { Text = "필터 포커스", AutoSize = true, Padding = new Padding(6, 5, 0, 0) };
            _chkFocus.CheckedChanged += ChkFocus_CheckedChanged;
            searchPanel.Controls.Add(_chkFocus);
            var btnClash = new Button { Text = "이 단면 지나가는 케이블 추출", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 0, 6, 0) };
            btnClash.Click += BtnClash_Click;
            searchPanel.Controls.Add(btnClash);
            var btnExport = new Button { Text = "매칭 Status 출력", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 0, 6, 0) };
            btnExport.Click += BtnExport_Click;
            searchPanel.Controls.Add(btnExport);

            _scopePanel = new ScopePanel { Dock = DockStyle.Fill };
            _scopePanel.ApplyRequested += (s, e) => ApplyScope();

            _tabFilter = new TabControl { Dock = DockStyle.Fill, Height = 240 };
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
                Dock = DockStyle.Fill, FullRowSelect = true, GridLines = true,
                View = View.Details, HideSelection = false,
            };
            _listView.Columns.Add("#", 40);
            _listView.Columns.Add("Cable No", 150);
            _listView.Columns.Add("단계", 70);
            _listView.Columns.Add("From", 110);
            _listView.Columns.Add("To", 110);
            _listView.Columns.Add("Design", 60);
            _listView.Columns.Add("Pulled", 60);
            _listView.Columns.Add("%", 46);
            _listView.Columns.Add("매칭", 40);
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
            _listView.ColumnClick += ListView_ColumnClick;
            tabAll.Controls.Add(_listView);

            layout.Controls.Add(_srcPanel);
            layout.Controls.Add(datePanel);
            layout.Controls.Add(new Label { Text = "단계 & 색상 (하이라이트 단색 포함)", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanel);
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
            var allStages = new[] { CableLineStage.NotStarted }.Concat(CableLineStageInfo.OrderedStages).ToArray();
            var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
            for (int col = 0; col < 4; col++)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, col % 4 == 0 ? 100 : (col % 4 == 3 ? 62 : (col % 4 == 1 ? 36 : 22))));

            int r = 0;
            foreach (var stage in allStages)
            {
                AddColorRow(layout, r++, CableLineStageInfo.Labels[stage], _colorSettings[stage],
                    () => _colorSettings[stage].DisplayColor,
                    c => { _colorSettings[stage].DisplayColor = c; IncrementalUpdate(stage.ToString()); },
                    t => { _colorSettings[stage].Transparency = t; IncrementalUpdate(stage.ToString()); },
                    chk => _colorRows[stage] = (null, null, null, chk));
            }
            // 하이라이트(단색) 행 — 하이라이트 우선 모드에서만 사용, 체크박스 없음.
            AddColorRow(layout, r, "하이라이트", _highlightSetting,
                () => _highlightSetting.DisplayColor,
                c => { _highlightSetting.DisplayColor = c; IncrementalUpdate(ColorOverrideEngine.CableLineHighlightGroup); },
                t => { _highlightSetting.Transparency = t; IncrementalUpdate(ColorOverrideEngine.CableLineHighlightGroup); },
                null);

            panel.Controls.Add(layout);
            return panel;
        }

        /// <summary>색상 행 1개 배치. hasCheck=null이면 체크박스 대신 라벨(하이라이트 행).</summary>
        private void AddColorRow(TableLayoutPanel layout, int row, string label, ColorSetting setting,
            Func<Color> getColor, Action<Color> setColor, Action<double> setTransparency, Action<CheckBox> registerCheck)
        {
            Control first;
            if (registerCheck != null)
            {
                var chk = new CheckBox { Text = label, Checked = true, AutoSize = true };
                registerCheck(chk);
                first = chk;
            }
            else
            {
                first = new Label { Text = label, AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
            }

            var colorBox = new Panel { Width = 32, Height = 20, BackColor = setting.DisplayColor, BorderStyle = BorderStyle.FixedSingle };
            var colorBtn = new Button { Text = "▼", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
            colorBtn.FlatAppearance.BorderSize = 0;
            var transparencyBox = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%", "100%" })
                transparencyBox.Items.Add(t);
            transparencyBox.Text = $"{(int)(setting.Transparency * 100)}%";

            colorBtn.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { Color = getColor() })
                    if (dlg.ShowDialog() == DialogResult.OK) { setColor(dlg.Color); colorBox.BackColor = dlg.Color; }
            };
            transparencyBox.SelectedIndexChanged += (s, e) =>
            {
                if (double.TryParse(transparencyBox.Text.Replace("%", ""), out double pct))
                    setTransparency(pct / 100.0);
            };

            layout.Controls.Add(first, 0, row);
            layout.Controls.Add(colorBox, 1, row);
            layout.Controls.Add(colorBtn, 2, row);
            layout.Controls.Add(transparencyBox, 3, row);
        }

        private void IncrementalUpdate(string groupKey)
        {
            var doc = _main.GetDocument();
            if (doc == null || !_main.OverrideEngine.HasCachedData(VisualModule.CableLine)) return;
            if (groupKey == ColorOverrideEngine.CableLineHighlightGroup)
                _main.OverrideEngine.UpdateStageColor(doc, VisualModule.CableLine, groupKey, _highlightSetting);
            else if (Enum.TryParse<CableLineStage>(groupKey, out var stage) && _colorSettings.TryGetValue(stage, out var setting))
                _main.OverrideEngine.UpdateStageColor(doc, VisualModule.CableLine, groupKey, setting);
        }

        private void LoadExcel()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Cable(형상) Excel 로드",
                Filter = "Excel 파일 (*.xlsx;*.xls;*.xlsb)|*.xlsx;*.xls;*.xlsb|모든 파일|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var list = ExcelLoader.LoadCable(dlg.FileName);
                    _cablesBySource[TabDataSource.Excel] = list;
                    _srcPanel.SetLoaded(TabDataSource.Excel, list.Count,
                        $"{Path.GetFileName(dlg.FileName)} · {DateTime.Now:HH:mm}");
                    if (_srcPanel.ActiveSource == TabDataSource.Excel)
                        ApplyActiveSourceData();
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
                var list = SqlLoader.LoadCable(settings);
                _cablesBySource[TabDataSource.Oasis] = list;
                // EIT_Cable엔 프로젝트 컬럼이 없어(§9) 전체 로드 — 라벨에 프로젝트 미표기.
                _srcPanel.SetLoaded(TabDataSource.Oasis, list.Count,
                    $"{settings.Database} · {DateTime.Now:HH:mm}");
                if (_srcPanel.ActiveSource == TabDataSource.Oasis)
                    ApplyActiveSourceData();
            }
            catch (Exception ex)
            {
                _srcPanel.SetFailed(TabDataSource.Oasis, "로드 실패");
                MessageBox.Show($"OASIS 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 적용 기준 소스의 리스트로 화면을 전환한다. 매칭 결과·범위 판정은 소스별로
        /// 다르므로 초기화하고, 레벨 타겟 인덱스는 활성 태그 셋 기반이라 재빌드를 강제한다.
        /// 자동 재색칠은 안 함(§6) — 색이 이전 소스 기준이면 경고만.
        /// </summary>
        private void ApplyActiveSourceData()
        {
            bool hadApplied = _matchedCableNos.Count > 0 || _unmatchedCableNos.Count > 0;
            _cables = _cablesBySource.TryGetValue(_srcPanel.ActiveSource, out var list)
                ? list : new List<CableLineData>();
            _matchedCableNos.Clear();
            _unmatchedCableNos.Clear();
            _scopeFilter.Reset();
            _scopeKeys = null;
            _scopePanel.ResetToFullModel();
            _needsIndexRebuild = true;
            SetFocusChecked(false);
            _tabFilter.TabPages[0].Text = $"전체 ({_cables.Count})";
            _tabFilter.TabPages[1].Text = "매칭";
            _tabFilter.TabPages[2].Text = "미매칭";
            FilterList();
            UpdateStats();
            if (hadApplied && _cables.Count > 0)
                _lblStats.Text = "⚠ 화면 색상은 이전 소스 기준 — [가시화 적용]을 눌러 새 소스로 갱신하세요";
        }

        private void ExportComparison()
        {
            if (!_cablesBySource.TryGetValue(TabDataSource.Excel, out var excelList) ||
                !_cablesBySource.TryGetValue(TabDataSource.Oasis, out var oasisList))
            {
                MessageBox.Show("Excel과 OASIS를 모두 로드해야 비교할 수 있습니다.");
                return;
            }

            var fields = new List<SourceComparer.Field<CableLineData>>
            {
                new SourceComparer.Field<CableLineData>("Pulling",
                    c => SourceComparer.FormatDate(c.StageDates.TryGetValue(CableLineStage.Pulling, out var d) ? d : null)),
                new SourceComparer.Field<CableLineData>("Pulled",
                    c => SourceComparer.FormatDate(c.StageDates.TryGetValue(CableLineStage.Pulled, out var d) ? d : null)),
                new SourceComparer.Field<CableLineData>("From Conn", c => SourceComparer.FormatDate(c.FromConnDate)),
                new SourceComparer.Field<CableLineData>("To Conn",   c => SourceComparer.FormatDate(c.ToConnDate)),
                new SourceComparer.Field<CableLineData>("Design Lth", c => c.DesignLth?.ToString("0.#") ?? ""),
                new SourceComparer.Field<CableLineData>("Pulled Lth", c => c.PulledLth?.ToString("0.#") ?? ""),
            };
            var lines = SourceComparer.BuildCsv("Cable No", excelList, oasisList, c => c.CableNo, fields);
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"CableLine_Compare_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            MessageBox.Show($"비교 결과 저장 완료: {path}");
        }

        private void ExportInputTemplate()
        {
            try
            {
                string path = InputTemplate.ExportCable();
                MessageBox.Show($"입력 양식 저장 완료: {path}\n작성 후 Excel 형식(.xlsx)으로 저장해 Import 하세요.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"입력 양식 저장 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildIndex()
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            Application.DoEvents();
            var cableNoSet = new HashSet<string>(_cables.Select(c => c.CableNo));
            _main.CableLineSearcher.BuildIndexForTags(doc, cableNoSet, NwdScope.Cable);
            _needsIndexRebuild = false;
            _progressBar.Visible = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _cables.Count == 0)
            {
                MessageBox.Show("Excel을 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_needsIndexRebuild || _main.CableLineSearcher.NeedsRebuild(doc))
                BuildIndex();

            // 색칠 전 focus·isolate 해제 (override/hide는 §10 누적 리셋이 안 잡음).
            _main.OverrideEngine.ClearCableLineFilterFocus(doc);
            SetFocusChecked(false);
            RestoreHidden(doc);

            // 하이라이트 우선 모드: stage 날짜가 하나도 없으면 단색 하이라이트.
            _lastHighlightMode = !_cables.Any(c => c.HasAnyStageDate);

            var activeSettings = new Dictionary<CableLineStage, ColorSetting>();
            foreach (var kv in _colorRows)
                if (kv.Value.check != null && kv.Value.check.Checked)
                    activeSettings[kv.Key] = _colorSettings[kv.Key];

            var referenceDate = _dtpReference.Value;
            OverrideResult result;
            _btnApply.Enabled = false;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            Application.DoEvents();
            try
            {
                result = _main.OverrideEngine.ApplyCableLines(doc, _cables, activeSettings, referenceDate,
                    _lastHighlightMode ? _highlightSetting : null);
            }
            finally
            {
                _progressBar.Visible = false;
                _progressBar.Style = ProgressBarStyle.Blocks;
                _btnApply.Enabled = true;
            }

            _unmatchedCableNos = result.UnmatchedIds;
            var unmatchedSet = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedCableNos = new HashSet<string>(
                _cables.Select(c => c.CableNo).Where(id => !unmatchedSet.Contains(id)),
                StringComparer.OrdinalIgnoreCase);

            _scopeFilter.Invalidate();
            ReapplyCurrentScope(doc);

            UpdateTabCounts();
            UpdateStats(result);
            FilterList();
            SubscribeSelection(doc);
        }

        // ----- 겹침 완화 (숨김 isolate) -----

        private void RestoreHidden(Document doc)
        {
            if (_cableHidden != null)
            {
                doc.Models.SetHidden(_cableHidden, false);
                _cableHidden = null;
            }
            _btnHideOthers.Text = "체크 단계 외 숨김";
            _btnIsolateSel.Text = "선택 케이블만 보기";
        }

        /// <summary>체크된 단계(기준일 기준)의 매칭 케이블만 남기고 나머지 매칭 케이블을 숨긴다(토글).</summary>
        private void BtnHideOthers_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            if (_cableHidden != null) { RestoreHidden(doc); return; }
            if (_matchedCableNos.Count == 0) { MessageBox.Show("먼저 가시화 적용을 실행하세요."); return; }

            var checkedStages = new HashSet<CableLineStage>(
                _colorRows.Where(kv => kv.Value.check != null && kv.Value.check.Checked).Select(kv => kv.Key));
            var referenceDate = _dtpReference.Value;
            var byCable = _cables.ToDictionary(c => c.CableNo, StringComparer.OrdinalIgnoreCase);

            var toHide = new ModelItemCollection();
            foreach (var cableNo in _matchedCableNos)
            {
                if (!byCable.TryGetValue(cableNo, out var cable)) continue;
                if (checkedStages.Contains(cable.GetStageAtDate(referenceDate))) continue;
                var col = _main.OverrideEngine.GetCableLineItems(cableNo);
                if (col != null) foreach (ModelItem mi in col) toHide.Add(mi);
            }
            if (toHide.Count == 0) { MessageBox.Show("숨길 대상이 없습니다 (모든 매칭 케이블이 체크된 단계입니다)."); return; }

            doc.Models.SetHidden(toHide, true);
            _cableHidden = toHide;
            _btnHideOthers.Text = "전체 보기";
        }

        /// <summary>현재 선택(3D 또는 목록)한 케이블만 남기고 나머지 매칭 케이블을 숨긴다(토글).</summary>
        private void BtnIsolateSel_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            if (_cableHidden != null) { RestoreHidden(doc); return; }
            if (_matchedCableNos.Count == 0) { MessageBox.Show("먼저 가시화 적용을 실행하세요."); return; }

            var keep = ResolveSelectedCableNos(doc);
            foreach (ListViewItem it in _listView.SelectedItems)
                if (it.Tag is CableLineData c) keep.Add(c.CableNo);
            if (keep.Count == 0) { MessageBox.Show("3D 뷰나 목록에서 케이블을 선택하세요."); return; }

            var toHide = new ModelItemCollection();
            foreach (var cableNo in _matchedCableNos)
            {
                if (keep.Contains(cableNo)) continue;
                var col = _main.OverrideEngine.GetCableLineItems(cableNo);
                if (col != null) foreach (ModelItem mi in col) toHide.Add(mi);
            }
            if (toHide.Count == 0) { MessageBox.Show("숨길 대상이 없습니다."); return; }

            doc.Models.SetHidden(toHide, true);
            _cableHidden = toHide;
            _btnIsolateSel.Text = "전체 보기";
        }

        // ----- 필터 포커스 (투명 dim) -----

        private void ChkFocus_CheckedChanged(object sender, EventArgs e)
        {
            if (_suppressFocusCheck) return;
            var doc = _main.GetDocument();
            if (_chkFocus.Checked)
            {
                if (doc == null || _matchedCableNos.Count == 0)
                {
                    MessageBox.Show("먼저 가시화 적용을 실행하세요.");
                    SetFocusChecked(false);
                    return;
                }
                var hits = GetCurrentFilterHitCableNos();
                if (hits.Count == 0) { MessageBox.Show("현재 필터에 일치하는 케이블이 없습니다."); SetFocusChecked(false); return; }
                _main.OverrideEngine.SetCableLineFilterFocus(doc, hits);
                _focusOn = true;
            }
            else
            {
                if (doc != null) _main.OverrideEngine.ClearCableLineFilterFocus(doc);
                _focusOn = false;
            }
        }

        private void SetFocusChecked(bool value)
        {
            _suppressFocusCheck = true;
            _chkFocus.Checked = value;
            _suppressFocusCheck = false;
            _focusOn = value;
        }

        private void RefreshFocusIfActive()
        {
            if (!_focusOn) return;
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.OverrideEngine.SetCableLineFilterFocus(doc, GetCurrentFilterHitCableNos());
        }

        private List<string> GetCurrentFilterHitCableNos()
        {
            string keyword = _txtSearch?.Text?.Trim().ToUpperInvariant() ?? "";
            var hits = _cables.Where(c => _matchedCableNos.Contains(c.CableNo));
            if (!string.IsNullOrEmpty(keyword))
                hits = hits.Where(c => CableMatchesKeyword(c, keyword));
            return hits.Select(c => c.CableNo).ToList();
        }

        private static bool CableMatchesKeyword(CableLineData c, string keywordUpper)
        {
            return (c.CableNo ?? "").ToUpperInvariant().Contains(keywordUpper)
                || (c.FromEquip ?? "").ToUpperInvariant().Contains(keywordUpper)
                || (c.ToEquip ?? "").ToUpperInvariant().Contains(keywordUpper);
        }

        // ----- 현황 집계 범위 (Clipping = clash) -----

        private Dictionary<string, List<ModelItem>> BuildScopeItemsByKey()
        {
            var found = _main.CableLineSearcher.FindBySpoolIds(
                _matchedCableNos.Select(CableLineData.NormalizeCableNo).Distinct(StringComparer.OrdinalIgnoreCase));
            var itemsByKey = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in _matchedCableNos)
                itemsByKey[id] = found.TryGetValue(CableLineData.NormalizeCableNo(id), out var items)
                    ? items : new List<ModelItem>();
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
                if (doc == null || _matchedCableNos.Count == 0)
                {
                    MessageBox.Show("먼저 가시화 적용을 실행하세요. 집계 범위는 매칭된 케이블에 적용됩니다.");
                    return;
                }
                if (_needsIndexRebuild || _main.CableLineSearcher.NeedsRebuild(doc))
                {
                    MessageBox.Show("모델 또는 데이터가 변경되었습니다. 가시화 적용을 다시 실행한 뒤 범위를 선택하세요.");
                    return;
                }
                if (scope == MatchScope.ClippingVolume) _clash.EnsureFresh(doc);

                _progressBar.Style = ProgressBarStyle.Marquee;
                _progressBar.Visible = true;
                Application.DoEvents();
                try
                {
                    _scopeKeys = _scopeFilter.Apply(doc, scope, BuildScopeItemsByKey(), _volumeJudge);
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
            RefreshFocusIfActive();
        }

        private void ReapplyCurrentScope(Document doc)
        {
            var scope = _scopePanel.CurrentScope;
            if (scope == MatchScope.FullModel) { _scopeKeys = null; return; }
            if (scope == MatchScope.ClippingVolume) _clash.EnsureFresh(doc);
            _scopeKeys = _scopeFilter.Apply(doc, scope, BuildScopeItemsByKey(), _volumeJudge);
        }

        /// <summary>단면 통과 케이블 추출: Clipping 영역 스코프로 판정 + CSV 출력.</summary>
        private void BtnClash_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _matchedCableNos.Count == 0)
            {
                MessageBox.Show("먼저 가시화 적용을 실행하세요. 단면 통과 판정은 매칭된 케이블에 적용됩니다.");
                return;
            }
            var planes = _main.SectionSvc.GetActiveClipPlanes(doc);
            if (planes == null || planes.Count == 0)
            {
                MessageBox.Show("활성 단면(clip plane/box)이 없습니다. Navisworks Sectioning으로 영역을 자른 뒤 다시 시도하세요.");
                return;
            }
            _clash.EnsureFresh(doc);
            _clash.ResetBatchCounters();

            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            Application.DoEvents();
            try
            {
                _scopeKeys = _scopeFilter.Apply(doc, MatchScope.ClippingVolume, BuildScopeItemsByKey(), _volumeJudge);
            }
            finally
            {
                _progressBar.Visible = false;
                _progressBar.Style = ProgressBarStyle.Blocks;
            }
            _scopePanel.SetCurrentScope(MatchScope.ClippingVolume);
            _tabFilter.SelectedIndex = 1; // 매칭 탭
            UpdateTabCounts();
            FilterList();
            UpdateStats();

            int inCount = _scopeKeys?.Count ?? 0;
            ExportClashCsv(inCount);
        }

        private void ExportClashCsv(int inCount)
        {
            var lines = new List<string>();
            lines.Add($"단면 통과 케이블,{inCount}건");
            lines.Add($"추출/AABB배제,{_clash.LastExtracted}/{_clash.LastCulled}");
            lines.Add($"인덱스 스코프,\"{_main.CableLineSearcher.LastScopeNote ?? "-"}\"");
            lines.Add("Cable No,단계,From,To,Design,Pulled");
            var referenceDate = _dtpReference.Value;
            foreach (var c in _cables)
            {
                if (_scopeKeys == null || !_scopeKeys.Contains(c.CableNo)) continue;
                string stageLabel = _lastHighlightMode ? "하이라이트" : CableLineStageInfo.Labels[c.GetStageAtDate(referenceDate)];
                lines.Add($"\"{c.CableNo}\",\"{stageLabel}\",\"{FromText(c)}\",\"{ToText(c)}\"," +
                          $"{(c.DesignLth?.ToString("0.##") ?? "")},{(c.PulledLth?.ToString("0.##") ?? "")}");
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"CableClash_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(path, string.Join("\r\n", lines), new System.Text.UTF8Encoding(true));
            MessageBox.Show($"단면 통과 케이블 {inCount}건.\n저장 완료: {path}");
        }

        // ----- Buttons -----

        private void BtnResetModule_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            RestoreHidden(doc);
            SetFocusChecked(false);
            _main.OverrideEngine.ResetCableLineModule(doc);
            _lblStats.Text = "공종 초기화 완료 (Cable 형상 색만 제거)";
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            RestoreHidden(doc);
            SetFocusChecked(false);
            _main.OverrideEngine.Reset(doc);
            _scopeFilter.Reset();
            _scopeKeys = null;
            _scopePanel.ResetToFullModel();
            _lblStats.Text = "전체 초기화 완료";
            _lblUnmatched.Text = "";
        }

        private void BtnViewpoint_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            string name = $"Cable_{DateTime.Now:yyyyMMdd_HHmm}";
            try { _main.ExportSvc.SaveViewpoint(doc, name); MessageBox.Show($"Viewpoint '{name}' 저장 완료"); }
            catch (Exception ex) { MessageBox.Show($"Viewpoint 저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void BtnNwd_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.ExportSvc.ExportNwdWithDialog(doc);
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (_cables.Count == 0) { MessageBox.Show("Excel을 먼저 로드하세요."); return; }
            var referenceDate = _dtpReference.Value;
            var lines = new List<string>();
            lines.Add($"집계 범위,{MatchScopeInfo.Label(_scopePanel.CurrentScope)}");
            lines.Add($"인덱스 스코프,\"{_main.CableLineSearcher.LastScopeNote ?? "-"}\"");
            lines.Add("Cable No,단계,From,To,Design,Pulled,%,Matched");
            foreach (var c in _cables)
            {
                if (!InScope(c.CableNo)) continue;
                string stageLabel = _lastHighlightMode ? "하이라이트" : CableLineStageInfo.Labels[c.GetStageAtDate(referenceDate)];
                bool matched = _matchedCableNos.Count == 0 || _matchedCableNos.Contains(c.CableNo);
                string pct = c.PullingProgress.HasValue ? $"{c.PullingProgress.Value * 100:0}%" : "";
                lines.Add($"\"{c.CableNo}\",\"{stageLabel}\",\"{FromText(c)}\",\"{ToText(c)}\"," +
                          $"{(c.DesignLth?.ToString("0.##") ?? "")},{(c.PulledLth?.ToString("0.##") ?? "")},\"{pct}\",\"{(matched ? "O" : "X")}\"");
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Cable_Match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(path, string.Join("\r\n", lines), new System.Text.UTF8Encoding(true));
            MessageBox.Show($"저장 완료: {path}");
        }

        // ----- List / selection -----

        private bool InScope(string id) =>
            _scopeKeys == null || !_matchedCableNos.Contains(id) || _scopeKeys.Contains(id);

        private void UpdateTabCounts()
        {
            bool hasApplied = _matchedCableNos.Count > 0 || _unmatchedCableNos.Count > 0;
            if (!hasApplied) return;
            int matchedInScope = _scopeKeys == null ? _matchedCableNos.Count : _matchedCableNos.Count(id => _scopeKeys.Contains(id));
            int total = _scopeKeys == null ? _cables.Count : _cables.Count(c => InScope(c.CableNo));
            _tabFilter.TabPages[0].Text = $"전체 ({total})";
            _tabFilter.TabPages[1].Text = $"매칭 ({matchedInScope})";
            _tabFilter.TabPages[2].Text = $"미매칭 ({_unmatchedCableNos.Count})";
        }

        private void FilterList()
        {
            string keyword = _txtSearch?.Text?.Trim().ToUpperInvariant() ?? "";
            var referenceDate = _dtpReference.Value;
            int tabIndex = _tabFilter.SelectedIndex;

            var filtered = _cables.AsEnumerable();
            if (tabIndex == 1 && _matchedCableNos.Count > 0)
                filtered = filtered.Where(c => _matchedCableNos.Contains(c.CableNo));
            else if (tabIndex == 2 && _unmatchedCableNos.Count > 0)
                filtered = filtered.Where(c => _unmatchedCableNos.Contains(c.CableNo));

            if (_scopeKeys != null)
                filtered = filtered.Where(c => InScope(c.CableNo));
            if (!string.IsNullOrEmpty(keyword))
                filtered = filtered.Where(c => CableMatchesKeyword(c, keyword));

            _listView.BeginUpdate();
            _listView.Items.Clear();
            bool hasApplied = _matchedCableNos.Count > 0 || _unmatchedCableNos.Count > 0;
            int seq = 1;
            foreach (var c in filtered)
            {
                var stage = c.GetStageAtDate(referenceDate);
                string stageLabel = _lastHighlightMode && hasApplied ? "하이라이트" : CableLineStageInfo.Labels[stage];
                string matchLabel = !hasApplied ? "-" : (_matchedCableNos.Contains(c.CableNo) ? "O" : "X");
                string pct = c.PullingProgress.HasValue ? $"{c.PullingProgress.Value * 100:0}%" : "-";

                var item = new ListViewItem((seq++).ToString());
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(c.CableNo ?? "");
                var stageSub = item.SubItems.Add(stageLabel);
                if (!_lastHighlightMode && _colorSettings.TryGetValue(stage, out var setting))
                    stageSub.ForeColor = setting.DisplayColor;
                item.SubItems.Add(FromText(c));
                item.SubItems.Add(ToText(c));
                item.SubItems.Add(c.DesignLth?.ToString("0.##") ?? "-");
                item.SubItems.Add(c.PulledLth?.ToString("0.##") ?? "-");
                item.SubItems.Add(pct);
                var matchSub = item.SubItems.Add(matchLabel);
                if (matchLabel == "X") matchSub.ForeColor = Color.Red;
                item.Tag = c;
                _listView.Items.Add(item);
            }
            _listView.EndUpdate();
        }

        private static string FromText(CableLineData c) =>
            string.IsNullOrEmpty(c.FromModule) ? (c.FromEquip ?? "") : $"{c.FromModule}/{c.FromEquip}";
        private static string ToText(CableLineData c) =>
            string.IsNullOrEmpty(c.ToModule) ? (c.ToEquip ?? "") : $"{c.ToModule}/{c.ToEquip}";

        private void UpdateStats(OverrideResult result = null)
        {
            var referenceDate = _dtpReference.Value;
            bool hasApplied = _matchedCableNos.Count > 0 || _unmatchedCableNos.Count > 0;

            var statBasis = _cables.Where(c => InScope(c.CableNo));
            if (hasApplied) statBasis = statBasis.Where(c => _matchedCableNos.Contains(c.CableNo));

            var parts = new List<string>();
            if (_lastHighlightMode && hasApplied)
            {
                parts.Add($"하이라이트 {statBasis.Count()}");
            }
            else
            {
                var counts = statBasis.GroupBy(c => c.GetStageAtDate(referenceDate)).ToDictionary(g => g.Key, g => g.Count());
                var allStages = new[] { CableLineStage.NotStarted }.Concat(CableLineStageInfo.OrderedStages).Reverse();
                foreach (var stage in allStages)
                    if (counts.TryGetValue(stage, out int cnt) && cnt > 0)
                        parts.Add($"{CableLineStageInfo.Labels[stage]} {cnt}");
            }

            string line2 = "";
            if (hasApplied)
            {
                int matchedInScope = _scopeKeys == null ? _matchedCableNos.Count : _matchedCableNos.Count(id => _scopeKeys.Contains(id));
                line2 = $"매칭 {matchedInScope}";
                if (_scopeKeys != null) line2 += $" ({MatchScopeInfo.Label(_scopePanel.CurrentScope)} 기준)";
                if (result != null && _lastHighlightMode) line2 += "  · 하이라이트 모드(stage 날짜 없음)";
            }
            _lblStats.Text = string.Join("  ", parts) + (!string.IsNullOrEmpty(line2) ? $"\n{line2}" : "");
            _lblUnmatched.Text = hasApplied ? $"미매칭 {_unmatchedCableNos.Count}건" : "";
        }

        private void ListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressSelectionSync || _listView.SelectedItems.Count == 0) return;
            var doc = _main.GetDocument();
            if (doc == null) return;

            var combined = new ModelItemCollection();
            foreach (ListViewItem it in _listView.SelectedItems)
            {
                if (!(it.Tag is CableLineData c)) continue;
                var col = _main.OverrideEngine.GetCableLineItems(c.CableNo);
                if (col != null) foreach (ModelItem mi in col) combined.Add(mi);
            }
            if (combined.Count == 0) return;
            _suppressSelectionSync = true;
            try { doc.CurrentSelection.CopyFrom(combined); doc.ActiveView.FocusOnCurrentSelection(); }
            finally { _suppressSelectionSync = false; }
        }

        private void ListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == 0) return;
            if (e.Column == _sortColumn) _sortAscending = !_sortAscending;
            else { _sortColumn = e.Column; _sortAscending = true; }
            _listView.ListViewItemSorter = new ListViewItemComparer(_sortColumn, _sortAscending);
            _listView.Sort();
            for (int i = 0; i < _listView.Items.Count; i++)
                _listView.Items[i].SubItems[0].Text = (i + 1).ToString();
        }

        private class ListViewItemComparer : System.Collections.IComparer
        {
            private readonly int _col; private readonly int _dir;
            public ListViewItemComparer(int c, bool asc) { _col = c; _dir = asc ? 1 : -1; }
            public int Compare(object x, object y) =>
                string.Compare(((ListViewItem)x).SubItems[_col].Text, ((ListViewItem)y).SubItems[_col].Text, StringComparison.OrdinalIgnoreCase) * _dir;
        }

        /// <summary>3D 선택 → cable-no 해석: 각 선택 아이템 + 조상 DisplayName을 정규화해 매칭 케이블과 대조.</summary>
        private HashSet<string> ResolveSelectedCableNos(Document doc)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return result;

            var keyToCable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cableNo in _matchedCableNos)
                keyToCable[CableLineData.NormalizeCableNo(cableNo).ToUpperInvariant()] = cableNo;

            foreach (ModelItem selected in doc.CurrentSelection.SelectedItems)
            {
                for (var item = selected; item != null; item = item.Parent)
                {
                    string key = CableLineData.NormalizeCableNo(item.DisplayName ?? "").ToUpperInvariant();
                    if (key.Length > 0 && keyToCable.TryGetValue(key, out var cableNo)) { result.Add(cableNo); break; }
                }
            }
            return result;
        }

        // ----- 3D → list selection sync -----

        private void SubscribeSelection(Document doc)
        {
            if (_subscribedDoc == doc) return;
            UnsubscribeSelection();
            _subscribedDoc = doc;
            doc.CurrentSelection.Changed += CurrentSelection_Changed;
        }

        private void UnsubscribeSelection()
        {
            if (_subscribedDoc != null)
            {
                try { _subscribedDoc.CurrentSelection.Changed -= CurrentSelection_Changed; } catch { }
                _subscribedDoc = null;
            }
        }

        private void CurrentSelection_Changed(object sender, EventArgs e)
        {
            if (_suppressSelectionSync) return;
            var doc = _main.GetDocument();
            if (doc == null) return;
            var cableNos = ResolveSelectedCableNos(doc);
            if (cableNos.Count == 0) return;
            string first = cableNos.First();

            _suppressSelectionSync = true;
            try
            {
                foreach (ListViewItem item in _listView.Items)
                    if (item.Tag is CableLineData c && string.Equals(c.CableNo, first, StringComparison.OrdinalIgnoreCase))
                    {
                        _listView.SelectedItems.Clear();
                        item.Selected = true;
                        item.EnsureVisible();
                        break;
                    }
            }
            finally { _suppressSelectionSync = false; }
        }

        private Dictionary<CableLineStage, ColorSetting> CloneDefaults(Dictionary<CableLineStage, ColorSetting> defaults)
        {
            var clone = new Dictionary<CableLineStage, ColorSetting>();
            foreach (var kv in defaults) clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
