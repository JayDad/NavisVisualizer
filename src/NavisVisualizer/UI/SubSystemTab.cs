using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// Sub-system 탭: OASIS의 Sub-system 속성(Equipment SUB-SYSTEM / Hydrotest Sub-System)
    /// 축으로 요소를 묶어, 선택한 Sub-system들을 3D에 가시화하고 현황 리포트를 출력한다.
    ///
    /// - 데이터: OASIS 전용 (기존 검증 쿼리 재사용 — SqlLoader.LoadSubSystemElements)
    /// - 매칭: TagSearcher 공유 (digit 포함 DisplayName 정확 일치 — 개발 규칙)
    /// - 가시화 2모드: Sub-system별 고유색(팔레트 자동 배정) / 공정 단계별(미착수·진행중·완료)
    /// - 선택 UI: 좌측 검색+체크 테이블(~400개 스크롤) → 우측 선택 누적 테이블 + 하단 개수
    /// </summary>
    public class SubSystemTab : UserControl
    {
        private readonly MainDockablePanel _main;

        private List<SubSystemElement> _elements = new List<SubSystemElement>();
        private Dictionary<string, List<SubSystemElement>> _bySubSystem
            = new Dictionary<string, List<SubSystemElement>>(StringComparer.OrdinalIgnoreCase);
        private List<string> _subSystemNames = new List<string>();
        private string _loadLabel = "";

        // 선택 상태 — _selected가 진실의 원천, _selectionOrder는 우측 테이블 누적 순서
        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _selectionOrder = new List<string>();
        // 한 번 배정된 색은 해제 후 재선택해도 유지 (재현 가능한 화면)
        private readonly Dictionary<string, Color> _assignedColors
            = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        private int _nextColorIndex;

        // 마지막 적용 결과 (적용된 Sub-system 스냅샷 기준으로만 매칭 O/X가 유효)
        private HashSet<string> _matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _unmatchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _appliedSubSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _appliedOnce;
        private bool _appliedByStatus;

        private Dictionary<ProgressStatus, ColorSetting> _colorSettings;

        private Label _dotOasis;
        private Label _lblOasis;
        private RadioButton _rdoBySubSystem;
        private RadioButton _rdoByStatus;
        private DateTimePicker _dtpReference;
        private Panel _colorPanel;
        private TextBox _txtFilter;
        private ListView _lvAll;
        private ListView _lvSelected;
        private Label _lblSelCount;
        private ProgressBar _progressBar;
        private Label _lblStats;
        private bool _suppressCheck;

        private Dictionary<ProgressStatus, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<ProgressStatus, (Panel, Button, ComboBox, CheckBox)>();

        private static readonly Color DotLoaded = Color.FromArgb(0, 160, 60);
        private static readonly Color DotEmpty = Color.FromArgb(170, 170, 170);
        private static readonly Color DotFailed = Color.FromArgb(200, 40, 40);

        public SubSystemTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.ProgressDefaults);
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

            // ----- OASIS 로드 행 -----
            var loadPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 30, AutoSize = false, WrapContents = false };
            var btnOasis = new Button { Text = "OASIS 로드", Width = 100, Height = 24 };
            btnOasis.Click += (s, e) => LoadOasis();
            _dotOasis = new Label
            {
                Text = "●",
                AutoSize = true,
                ForeColor = DotEmpty,
                Padding = new Padding(4, 5, 0, 0),
            };
            _lblOasis = new Label
            {
                Text = "(미로드)",
                AutoSize = true,
                ForeColor = Color.Gray,
                Padding = new Padding(0, 5, 0, 0),
            };
            loadPanel.Controls.Add(btnOasis);
            loadPanel.Controls.Add(_dotOasis);
            loadPanel.Controls.Add(_lblOasis);

            // ----- 시각화 모드 + 기준일 -----
            var modeGroup = new GroupBox { Text = "시각화 모드", Dock = DockStyle.Fill, Height = 50 };
            var modeFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = true,
                Padding = new Padding(4, 0, 4, 0),
            };
            _rdoBySubSystem = new RadioButton { Text = "Sub-system별 색상", AutoSize = true, Checked = true };
            _rdoByStatus = new RadioButton { Text = "공정 단계별", AutoSize = true, Margin = new Padding(8, 3, 0, 0) };
            _rdoByStatus.CheckedChanged += (s, e) => ModeChanged();
            modeFlow.Controls.Add(_rdoBySubSystem);
            modeFlow.Controls.Add(_rdoByStatus);
            modeFlow.Controls.Add(new Label { Text = "기준일:", AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
            _dtpReference = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today,
                Width = 105,
            };
            _dtpReference.ValueChanged += (s, e) =>
            {
                if (_elements.Count > 0) UpdateStats();
            };
            modeFlow.Controls.Add(_dtpReference);
            modeGroup.Controls.Add(modeFlow);

            _colorPanel = BuildColorPanel();
            _colorPanel.Enabled = false; // 기본 모드는 Sub-system별 색상

            // ----- Sub-system 선택 (좌: 전체+검색 / 우: 선택 누적) -----
            var selGroup = BuildSelectionGroup();

            // ----- 버튼 행 -----
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 65, AutoSize = true };
            var btnApply = new Button { Text = "적용", Width = 80 };
            var btnReset = new Button { Text = "전체 초기화", Width = 90 };
            var btnReport = new Button { Text = "현황 리포트 출력", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            var btnViewpoint = new Button { Text = "Viewpoint 저장", Width = 120 };
            var btnNwd = new Button { Text = "NWD Export", Width = 110 };
            btnApply.Click += BtnApply_Click;
            btnReset.Click += BtnReset_Click;
            btnReport.Click += BtnReport_Click;
            btnViewpoint.Click += BtnViewpoint_Click;
            btnNwd.Click += BtnNwd_Click;
            btnPanel.Controls.AddRange(new Control[] { btnApply, btnReset, btnReport, btnViewpoint, btnNwd });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };
            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false, Height = 48 };

            layout.Controls.Add(loadPanel);
            layout.Controls.Add(modeGroup);
            layout.Controls.Add(new Label { Text = "공정 단계 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(_colorPanel);
            layout.Controls.Add(selGroup);
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(_lblStats);

            Controls.Add(layout);
        }

        private Panel BuildColorPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, AutoSize = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));

            var statuses = ProgressStatusInfo.Ordered;
            for (int i = 0; i < statuses.Length; i++)
            {
                var status = statuses[i];
                var setting = _colorSettings[status];

                var chk = new CheckBox { Text = ProgressStatusInfo.Labels[status], Checked = true, AutoSize = true };
                var colorBox = new Panel { Width = 32, Height = 20, BackColor = setting.DisplayColor, BorderStyle = BorderStyle.FixedSingle };
                var colorBtn = new Button { Text = "▼", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
                colorBtn.FlatAppearance.BorderSize = 0;
                var transparencyBox = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%", "100%" })
                    transparencyBox.Items.Add(t);
                transparencyBox.Text = $"{(int)(setting.Transparency * 100)}%";

                var cs = status;
                colorBtn.Click += (s, e) =>
                {
                    using (var dlg = new ColorDialog { Color = _colorSettings[cs].DisplayColor })
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            _colorSettings[cs].DisplayColor = dlg.Color;
                            colorBox.BackColor = dlg.Color;
                            IncrementalUpdate(cs);
                        }
                    }
                };
                transparencyBox.SelectedIndexChanged += (s, e) =>
                {
                    if (double.TryParse(transparencyBox.Text.Replace("%", ""), out double pct))
                    {
                        _colorSettings[cs].Transparency = pct / 100.0;
                        IncrementalUpdate(cs);
                    }
                };

                _colorRows[status] = (colorBox, colorBtn, transparencyBox, chk);

                int row = i / 2;
                int colOffset = (i % 2) * 4;
                grid.Controls.Add(chk, colOffset + 0, row);
                grid.Controls.Add(colorBox, colOffset + 1, row);
                grid.Controls.Add(colorBtn, colOffset + 2, row);
                grid.Controls.Add(transparencyBox, colOffset + 3, row);
            }

            panel.Controls.Add(grid);
            return panel;
        }

        private GroupBox BuildSelectionGroup()
        {
            var group = new GroupBox { Text = "Sub-system 선택", Dock = DockStyle.Fill, Height = 330 };

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

            // 좌측 상단: 검색 + 표시항목 전체 선택
            var leftTop = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            leftTop.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            _txtFilter = new TextBox { Width = 100, Margin = new Padding(0, 3, 3, 0) };
            _txtFilter.TextChanged += (s, e) => RefreshLeftList();
            leftTop.Controls.Add(_txtFilter);
            var btnCheckShown = new Button
            {
                Text = "표시 전체선택",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(4, 0, 4, 0),
            };
            btnCheckShown.Click += (s, e) => CheckAllShown();
            leftTop.Controls.Add(btnCheckShown);

            // 우측 상단: 선택 해제 / 전체 해제
            var rightTop = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            var btnRemoveSel = new Button
            {
                Text = "선택 해제",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(4, 0, 4, 0),
            };
            btnRemoveSel.Click += (s, e) => RemoveHighlighted();
            var btnClearSel = new Button
            {
                Text = "전체 해제",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(4, 0, 4, 0),
            };
            btnClearSel.Click += (s, e) => ClearSelection();
            rightTop.Controls.Add(btnRemoveSel);
            rightTop.Controls.Add(btnClearSel);

            _lvAll = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = true,
            };
            _lvAll.Columns.Add("Sub-system", 140);
            _lvAll.Columns.Add("요소", 45);
            _lvAll.ItemChecked += LvAll_ItemChecked;

            _lvSelected = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = true,
            };
            _lvSelected.Columns.Add("", 22);
            _lvSelected.Columns.Add("Sub-system", 115);
            _lvSelected.Columns.Add("요소", 42);
            _lvSelected.Columns.Add("매칭", 45);
            _lvSelected.SelectedIndexChanged += LvSelected_SelectedIndexChanged;

            _lblSelCount = new Label
            {
                Dock = DockStyle.Fill,
                Text = "선택된 Sub-system: 0개",
                TextAlign = ContentAlignment.MiddleRight,
            };

            grid.Controls.Add(leftTop, 0, 0);
            grid.Controls.Add(rightTop, 1, 0);
            grid.Controls.Add(_lvAll, 0, 1);
            grid.Controls.Add(_lvSelected, 1, 1);
            grid.Controls.Add(_lblSelCount, 0, 2);
            grid.SetColumnSpan(_lblSelCount, 2);

            group.Controls.Add(grid);
            return group;
        }

        // ----- 데이터 로드 -----

        private void LoadOasis()
        {
            try
            {
                var settings = SqlConnectionSettings.Load();
                var list = SqlLoader.LoadSubSystemElements(settings, out int noSubSystem);

                _elements = list;
                GroupBySubSystem();

                // 재로드 후에도 살아있는 이름만 선택 유지
                _selected.RemoveWhere(name => !_bySubSystem.ContainsKey(name));
                _selectionOrder.RemoveAll(name => !_bySubSystem.ContainsKey(name));

                _matchedIds.Clear();
                _unmatchedIds.Clear();
                _appliedSubSystems.Clear();
                _appliedOnce = false;

                string prj = string.IsNullOrEmpty(settings.ProjectNo) ? "전체" : settings.ProjectNo;
                _loadLabel = $"{settings.Database}/{prj} · {DateTime.Now:HH:mm}";
                _dotOasis.ForeColor = DotLoaded;
                _lblOasis.ForeColor = Color.Black;
                _lblOasis.Text = $"{list.Count:N0}건 · Sub-system {_subSystemNames.Count}개 · {_loadLabel}"
                    + (noSubSystem > 0 ? $" · 미지정 {noSubSystem}건 제외" : "");

                RefreshLeftList();
                RefreshRightList();
                UpdateSelCount();
                UpdateStats();
            }
            catch (Exception ex)
            {
                _dotOasis.ForeColor = DotFailed;
                _lblOasis.ForeColor = DotFailed;
                _lblOasis.Text = "로드 실패";
                MessageBox.Show($"OASIS 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GroupBySubSystem()
        {
            _bySubSystem = _elements
                .GroupBy(el => el.SubSystem, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            _subSystemNames = _bySubSystem.Keys
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ----- 선택 UI -----

        private void RefreshLeftList()
        {
            _suppressCheck = true;
            _lvAll.BeginUpdate();
            _lvAll.Items.Clear();

            string keyword = _txtFilter.Text.Trim();
            foreach (var name in _subSystemNames)
            {
                if (keyword.Length > 0 && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var item = new ListViewItem(name) { Checked = _selected.Contains(name) };
                item.SubItems.Add(_bySubSystem[name].Count.ToString());
                _lvAll.Items.Add(item);
            }

            _lvAll.EndUpdate();
            _suppressCheck = false;
        }

        private void LvAll_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_suppressCheck) return;
            string name = e.Item.Text;
            if (e.Item.Checked) AddSelection(name);
            else RemoveSelection(name);
            RefreshRightList();
            UpdateSelCount();
        }

        private void AddSelection(string name)
        {
            if (!_selected.Add(name)) return;
            _selectionOrder.Add(name);
            if (!_assignedColors.ContainsKey(name))
                _assignedColors[name] = SubSystemPalette.At(_nextColorIndex++);
        }

        private void RemoveSelection(string name)
        {
            if (!_selected.Remove(name)) return;
            _selectionOrder.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>좌측에 현재 표시(필터 통과)된 항목 전부 선택.</summary>
        private void CheckAllShown()
        {
            _suppressCheck = true;
            foreach (ListViewItem item in _lvAll.Items)
            {
                item.Checked = true;
                AddSelection(item.Text);
            }
            _suppressCheck = false;
            RefreshRightList();
            UpdateSelCount();
        }

        /// <summary>우측에서 하이라이트된 행들만 선택 해제.</summary>
        private void RemoveHighlighted()
        {
            if (_lvSelected.SelectedItems.Count == 0) return;
            foreach (ListViewItem item in _lvSelected.SelectedItems)
                RemoveSelection((string)item.Tag);
            RefreshLeftList();
            RefreshRightList();
            UpdateSelCount();
        }

        private void ClearSelection()
        {
            _selected.Clear();
            _selectionOrder.Clear();
            RefreshLeftList();
            RefreshRightList();
            UpdateSelCount();
        }

        private void RefreshRightList()
        {
            _lvSelected.BeginUpdate();
            _lvSelected.Items.Clear();

            foreach (var name in _selectionOrder)
            {
                if (!_bySubSystem.TryGetValue(name, out var els)) continue;

                var item = new ListViewItem("■") { UseItemStyleForSubItems = false };
                item.ForeColor = _assignedColors.TryGetValue(name, out var c) ? c : Color.Black;
                item.SubItems.Add(name);
                item.SubItems.Add(els.Count.ToString());

                string matchText = "-";
                Color matchColor = Color.Black;
                if (_appliedOnce && _appliedSubSystems.Contains(name))
                {
                    int matched = els.Count(el => _matchedIds.Contains(el.ElementId));
                    matchText = matched.ToString();
                    if (matched < els.Count) matchColor = Color.Red;
                }
                var matchSub = item.SubItems.Add(matchText);
                matchSub.ForeColor = matchColor;

                item.Tag = name;
                _lvSelected.Items.Add(item);
            }

            _lvSelected.EndUpdate();
        }

        private void UpdateSelCount()
        {
            int elemCount = _selectionOrder
                .Where(_bySubSystem.ContainsKey)
                .Sum(name => _bySubSystem[name].Count);
            _lblSelCount.Text = $"선택된 Sub-system: {_selected.Count}개 · 요소 {elemCount:N0}건";
        }

        /// <summary>우측 행 클릭 → 해당 Sub-system의 매칭 아이템을 3D에서 선택·포커스.</summary>
        private void LvSelected_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvSelected.SelectedItems.Count == 0) return;
            var doc = _main.GetDocument();
            if (doc == null || !_main.TagSearcher.IsIndexBuilt || _main.TagSearcher.NeedsRebuild(doc)) return;

            var ids = new List<string>();
            foreach (ListViewItem item in _lvSelected.SelectedItems)
            {
                var name = (string)item.Tag;
                if (_bySubSystem.TryGetValue(name, out var els))
                    ids.AddRange(els.Select(el => el.ElementId));
            }
            if (ids.Count == 0) return;

            var found = _main.TagSearcher.FindBySpoolIds(ids.Distinct());
            var collection = new Autodesk.Navisworks.Api.ModelItemCollection();
            foreach (var items in found.Values)
                collection.AddRange(items);
            if (collection.Count == 0) return;

            doc.CurrentSelection.CopyFrom(collection);
            doc.ActiveView.FocusOnCurrentSelection();
        }

        // ----- 가시화 -----

        private void ModeChanged()
        {
            if (_colorPanel == null) return; // 초기화 중 이벤트 방어
            _colorPanel.Enabled = _rdoByStatus.Checked;
            if (_appliedOnce)
                _lblStats.Text += "\n⚠ 화면 색상은 이전 적용 기준 — 적용을 다시 실행하세요";
        }

        private void IncrementalUpdate(ProgressStatus status)
        {
            var doc = _main.GetDocument();
            if (doc == null || !_appliedByStatus || !_main.OverrideEngine.HasCachedData(VisualModule.SubSystem)) return;
            _main.OverrideEngine.UpdateStageColor(doc, VisualModule.SubSystem, status.ToString(), _colorSettings[status]);
        }

        private void BuildIndex()
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            Application.DoEvents();
            _main.TagSearcher.BuildIndex(doc);
            _progressBar.Visible = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _elements.Count == 0)
            {
                MessageBox.Show("OASIS 데이터를 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (_selected.Count == 0)
            {
                MessageBox.Show("가시화할 Sub-system을 먼저 선택하세요.");
                return;
            }
            if (_main.TagSearcher.NeedsRebuild(doc))
                BuildIndex();

            var targets = _elements.Where(el => _selected.Contains(el.SubSystem)).ToList();

            OverrideResult result;
            if (_rdoByStatus.Checked)
            {
                var groupSettings = new Dictionary<string, ColorSetting>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _colorRows)
                    if (kv.Value.check.Checked)
                        groupSettings[kv.Key.ToString()] = _colorSettings[kv.Key];

                var referenceDate = _dtpReference.Value;
                result = _main.OverrideEngine.ApplySubSystem(doc, targets,
                    el => el.StatusAt(referenceDate).ToString(), groupSettings);
            }
            else
            {
                var groupSettings = new Dictionary<string, ColorSetting>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in _selected)
                {
                    if (!_assignedColors.TryGetValue(name, out var color))
                    {
                        color = SubSystemPalette.At(_nextColorIndex++);
                        _assignedColors[name] = color;
                    }
                    groupSettings[name] = new ColorSetting { DisplayColor = color, Transparency = 0.0 };
                }
                result = _main.OverrideEngine.ApplySubSystem(doc, targets, el => el.SubSystem, groupSettings);
            }

            _unmatchedIds = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedIds = new HashSet<string>(
                targets.Select(el => el.ElementId).Where(id => !_unmatchedIds.Contains(id)),
                StringComparer.OrdinalIgnoreCase);
            _appliedSubSystems = new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase);
            _appliedOnce = true;
            _appliedByStatus = _rdoByStatus.Checked;

            RefreshRightList();
            UpdateStats(result);
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.OverrideEngine.Reset(doc);
            _lblStats.Text = "전체 초기화 완료";
        }

        // ----- 현황 리포트 -----

        /// <summary>
        /// CSV 리포트 (CLAUDE.md 8번 단기안): 헤더 블록 + Sub-system별 요약 + 상세 리스트.
        /// 선택된 Sub-system이 있으면 선택만, 없으면 전체를 집계한다.
        /// 매칭 O/X는 마지막 [적용]에 포함됐던 Sub-system에만 유효 — 그 외는 "-".
        /// </summary>
        private void BtnReport_Click(object sender, EventArgs e)
        {
            if (_elements.Count == 0)
            {
                MessageBox.Show("OASIS 데이터를 먼저 로드하세요.");
                return;
            }

            var referenceDate = _dtpReference.Value;
            bool selectedOnly = _selected.Count > 0;
            var names = selectedOnly
                ? _selectionOrder.Where(_bySubSystem.ContainsKey).ToList()
                : _subSystemNames.ToList();

            var lines = new List<string>();
            lines.Add("Sub-system 현황 리포트");
            lines.Add($"출력 시각,{DateTime.Now:yyyy-MM-dd HH:mm}");
            lines.Add($"기준일,{referenceDate:yyyy-MM-dd}");
            lines.Add($"데이터 소스,{Csv("OASIS " + _loadLabel)}");
            lines.Add($"집계 대상,{(selectedOnly ? "선택" : "전체")} Sub-system {names.Count}개");
            lines.Add($"매칭 기준,{(_appliedOnce ? "가시화 적용 결과 (적용된 Sub-system만 산정)" : "미적용 — 매칭 미산정")}");

            lines.Add("");
            lines.Add("[Sub-system별 요약]");
            lines.Add("Sub-system,요소,Equipment,Piping,매칭,미매칭,미착수,진행중,완료,완료율(%)");

            int tElems = 0, tEq = 0, tPip = 0, tMatched = 0, tUnmatched = 0, tNs = 0, tIp = 0, tDone = 0;
            bool anyMatchInfo = false;
            foreach (var name in names)
            {
                var els = _bySubSystem[name];
                int eq = els.Count(el => el.Discipline == SubSystemDiscipline.Equipment);
                int pip = els.Count - eq;

                int ns = 0, ip = 0, done = 0;
                foreach (var el in els)
                {
                    switch (el.StatusAt(referenceDate))
                    {
                        case ProgressStatus.Completed: done++; break;
                        case ProgressStatus.InProgress: ip++; break;
                        default: ns++; break;
                    }
                }

                string matchedText = "-", unmatchedText = "-";
                if (_appliedOnce && _appliedSubSystems.Contains(name))
                {
                    int matched = els.Count(el => _matchedIds.Contains(el.ElementId));
                    matchedText = matched.ToString();
                    unmatchedText = (els.Count - matched).ToString();
                    tMatched += matched;
                    tUnmatched += els.Count - matched;
                    anyMatchInfo = true;
                }

                double doneRate = els.Count > 0 ? done * 100.0 / els.Count : 0.0;
                lines.Add($"{Csv(name)},{els.Count},{eq},{pip},{matchedText},{unmatchedText},{ns},{ip},{done},{doneRate:F1}");
                tElems += els.Count; tEq += eq; tPip += pip; tNs += ns; tIp += ip; tDone += done;
            }
            double totalRate = tElems > 0 ? tDone * 100.0 / tElems : 0.0;
            lines.Add($"합계,{tElems},{tEq},{tPip},{(anyMatchInfo ? tMatched.ToString() : "-")},{(anyMatchInfo ? tUnmatched.ToString() : "-")},{tNs},{tIp},{tDone},{totalRate:F1}");

            lines.Add("");
            lines.Add("[상세 리스트]");
            lines.Add("Sub-system,공종,요소 ID,설명,현재 단계,진행 상태,매칭");
            foreach (var name in names)
            {
                var ordered = _bySubSystem[name]
                    .OrderBy(el => el.Discipline)
                    .ThenBy(el => el.ElementId, StringComparer.OrdinalIgnoreCase);
                foreach (var el in ordered)
                {
                    string matched = "-";
                    if (_appliedOnce && _appliedSubSystems.Contains(name))
                        matched = _matchedIds.Contains(el.ElementId) ? "O" : "X";
                    lines.Add($"{Csv(name)},{SubSystemDisciplineInfo.Labels[el.Discipline]},{Csv(el.ElementId)},{Csv(el.Description)}," +
                        $"{Csv(el.StageLabelAt(referenceDate))},{ProgressStatusInfo.Labels[el.StatusAt(referenceDate)]},{matched}");
                }
            }

            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SubSystem_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            MessageBox.Show($"리포트 저장 완료: {path}");
        }

        private static string Csv(string s) =>
            "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        // ----- 기타 -----

        private void BtnViewpoint_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            string name = $"SubSystem_{DateTime.Now:yyyyMMdd_HHmm}";
            try
            {
                _main.ExportSvc.SaveViewpoint(doc, name);
                MessageBox.Show($"Viewpoint '{name}' 저장 완료");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Viewpoint 저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnNwd_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.ExportSvc.ExportNwdWithDialog(doc);
        }

        private void UpdateStats(OverrideResult result = null)
        {
            if (_elements.Count == 0)
            {
                _lblStats.Text = "로드된 데이터 없음";
                return;
            }

            var referenceDate = _dtpReference.Value;
            string line1;
            if (_selected.Count == 0)
            {
                line1 = $"전체 {_elements.Count:N0}건 · Sub-system {_subSystemNames.Count}개 — 좌측에서 선택하세요";
            }
            else
            {
                int ns = 0, ip = 0, done = 0, total = 0;
                foreach (var el in _elements)
                {
                    if (!_selected.Contains(el.SubSystem)) continue;
                    total++;
                    switch (el.StatusAt(referenceDate))
                    {
                        case ProgressStatus.Completed: done++; break;
                        case ProgressStatus.InProgress: ip++; break;
                        default: ns++; break;
                    }
                }
                line1 = $"선택 요소 {total:N0}건: 미착수 {ns} · 진행중 {ip} · 완료 {done} (기준일 {referenceDate:yyyy-MM-dd})";
            }

            string line2 = "";
            if (_appliedOnce)
            {
                string mode = _appliedByStatus ? "공정 단계별" : "Sub-system별 색상";
                line2 = $"매칭 {_matchedIds.Count:N0} / 미매칭 {_unmatchedIds.Count:N0} ({mode} 적용됨)";
            }

            _lblStats.Text = line1 + (string.IsNullOrEmpty(line2) ? "" : "\n" + line2);
        }

        private Dictionary<ProgressStatus, ColorSetting> CloneDefaults(Dictionary<ProgressStatus, ColorSetting> defaults)
        {
            var clone = new Dictionary<ProgressStatus, ColorSetting>();
            foreach (var kv in defaults) clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
