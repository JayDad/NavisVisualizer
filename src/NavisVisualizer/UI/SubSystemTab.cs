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
    /// Sub-system 탭: OASIS의 Sub-system 마스터([Navis].[SubSystem_Master] —
    /// Walkdown/P-MCC/MCC/RFCC/PCC 날짜 + ITR/Punch 수치)와 요소 데이터
    /// (Equipment SUB-SYSTEM / Hydrotest Sub-System)를 묶어, 선택한 Sub-system들의
    /// 요소를 3D에 가시화하고 현황 리포트를 출력한다.
    ///
    /// - 데이터: OASIS 전용. 마스터 미구성 시 요소 파생 목록으로 자동 fallback
    /// - 매칭: TagSearcher 공유 (digit 포함 DisplayName 정확 일치 — 개발 규칙)
    /// - 가시화 2모드: Sub-system 단계별(Walkdown→PCC 6색, 마스터 필요) /
    ///   요소 진행상태별(미착수·진행중·완료)
    /// - 선택 UI: 좌측 검색+상태 테이블(~400개) ↔ [▶ ◀ ▶▶ ◀◀] ↔ 우측 선택 누적
    ///   테이블 + 하단 개수 라벨. 다중 선택 후 화살표로 이동
    /// </summary>
    public class SubSystemTab : UserControl
    {
        private enum ApplyMode { Stage, Progress }

        private readonly MainDockablePanel _main;

        private List<SubSystemElement> _elements = new List<SubSystemElement>();
        private Dictionary<string, List<SubSystemElement>> _bySubSystem
            = new Dictionary<string, List<SubSystemElement>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>null = 마스터 미구성(요소 파생 fallback).</summary>
        private Dictionary<string, SubSystemMasterData> _master;
        private List<string> _subSystemNames = new List<string>();
        private string _loadLabel = "";

        // 선택 상태 — _selected가 진실의 원천, _selectionOrder는 우측 테이블 누적 순서
        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _selectionOrder = new List<string>();

        // 마지막 적용 결과 (적용된 Sub-system 스냅샷 기준으로만 매칭 O/X가 유효)
        private HashSet<string> _matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _unmatchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _appliedSubSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _appliedOnce;
        private ApplyMode? _appliedMode;

        private Dictionary<SubSystemStage, ColorSetting> _stageSettings;
        private Dictionary<ProgressStatus, ColorSetting> _progressSettings;

        private Label _dotOasis;
        private Label _lblOasis;
        private RadioButton _rdoByStage;
        private RadioButton _rdoByProgress;
        private DateTimePicker _dtpReference;
        private Panel _stagePanel;
        private Panel _progressPanel;
        private TextBox _txtFilter;
        private ListView _lvAll;
        private ListView _lvSelected;
        private Label _lblSelCount;
        private ProgressBar _progressBar;
        private Label _lblStats;

        // 그리드 밖에서는 단계별 체크 상태만 필요 (색/투명도는 빌더 클로저가 직접 갱신)
        private Dictionary<SubSystemStage, CheckBox> _stageChecks = new Dictionary<SubSystemStage, CheckBox>();
        private Dictionary<ProgressStatus, CheckBox> _progressChecks = new Dictionary<ProgressStatus, CheckBox>();

        private static readonly Color DotLoaded = Color.FromArgb(0, 160, 60);
        private static readonly Color DotEmpty = Color.FromArgb(170, 170, 170);
        private static readonly Color DotFailed = Color.FromArgb(200, 40, 40);
        /// <summary>좌측 목록에서 이미 우측에 담긴 행의 배경.</summary>
        private static readonly Color PickedBack = Color.FromArgb(223, 240, 230);

        public SubSystemTab(MainDockablePanel main)
        {
            _main = main;
            _stageSettings = CloneDefaults(ColorSetting.SubSystemStageDefaults);
            _progressSettings = CloneDefaults(ColorSetting.ProgressDefaults);
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
            _dotOasis = new Label { Text = "●", AutoSize = true, ForeColor = DotEmpty, Padding = new Padding(4, 5, 0, 0) };
            _lblOasis = new Label { Text = "(미로드)", AutoSize = true, ForeColor = Color.Gray, Padding = new Padding(0, 5, 0, 0) };
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
            // 단계 모드는 마스터가 있어야 의미가 있음 — 로드 성공 시 활성화
            _rdoByStage = new RadioButton { Text = "Sub-system 단계별", AutoSize = true, Checked = true, Enabled = false };
            _rdoByProgress = new RadioButton { Text = "요소 진행상태별", AutoSize = true, Margin = new Padding(8, 3, 0, 0) };
            _rdoByStage.CheckedChanged += (s, e) => ModeChanged();
            modeFlow.Controls.Add(_rdoByStage);
            modeFlow.Controls.Add(_rdoByProgress);
            modeFlow.Controls.Add(new Label { Text = "기준일:", AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
            _dtpReference = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today,
                Width = 105,
            };
            _dtpReference.ValueChanged += (s, e) =>
            {
                // 단계/스와치 컬럼이 기준일에 의존 → 목록·통계 재표시
                if (_subSystemNames.Count > 0) { RefreshLeftList(); RefreshRightList(); UpdateStats(); }
            };
            modeFlow.Controls.Add(_dtpReference);
            modeGroup.Controls.Add(modeFlow);

            _stagePanel = BuildColorGrid(
                new[] { SubSystemStage.NotStarted }.Concat(SubSystemStageInfo.OrderedStages).ToArray(),
                SubSystemStageInfo.Labels, _stageSettings, _stageChecks, ApplyMode.Stage);
            _progressPanel = BuildColorGrid(
                ProgressStatusInfo.Ordered,
                ProgressStatusInfo.Labels, _progressSettings, _progressChecks, ApplyMode.Progress);
            _progressPanel.Visible = false;

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
            _lblStats = new Label { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false, Height = 78 };

            layout.Controls.Add(loadPanel);
            layout.Controls.Add(modeGroup);
            layout.Controls.Add(new Label { Text = "단계 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(_stagePanel);
            layout.Controls.Add(_progressPanel);
            layout.Controls.Add(selGroup);
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(_lblStats);

            Controls.Add(layout);
        }

        /// <summary>공통 색상 그리드 (2열 × N행) — 단계/진행상태 두 모드가 공유하는 빌더.</summary>
        private Panel BuildColorGrid<T>(T[] keys, Dictionary<T, string> labels,
            Dictionary<T, ColorSetting> settings,
            Dictionary<T, CheckBox> checks,
            ApplyMode mode)
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

            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                var setting = settings[key];

                var chk = new CheckBox { Text = labels[key], Checked = true, AutoSize = true };
                var colorBox = new Panel { Width = 32, Height = 20, BackColor = setting.DisplayColor, BorderStyle = BorderStyle.FixedSingle };
                var colorBtn = new Button { Text = "▼", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
                colorBtn.FlatAppearance.BorderSize = 0;
                var transparencyBox = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%", "100%" })
                    transparencyBox.Items.Add(t);
                transparencyBox.Text = $"{(int)(setting.Transparency * 100)}%";

                var ck = key;
                colorBtn.Click += (s, e) =>
                {
                    using (var dlg = new ColorDialog { Color = settings[ck].DisplayColor })
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            settings[ck].DisplayColor = dlg.Color;
                            colorBox.BackColor = dlg.Color;
                            IncrementalUpdate(mode, ck.ToString(), settings[ck]);
                            if (mode == ApplyMode.Stage) RefreshRightList(); // 우측 스와치 동기화
                        }
                    }
                };
                transparencyBox.SelectedIndexChanged += (s, e) =>
                {
                    if (double.TryParse(transparencyBox.Text.Replace("%", ""), out double pct))
                    {
                        settings[ck].Transparency = pct / 100.0;
                        IncrementalUpdate(mode, ck.ToString(), settings[ck]);
                    }
                };

                checks[key] = chk;

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
            var group = new GroupBox { Text = "Sub-system 선택", Dock = DockStyle.Fill, Height = 340 };

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

            // 좌측 상단: 검색 (코드 + 설명 매칭)
            var leftTop = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            leftTop.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            _txtFilter = new TextBox { Width = 130, Margin = new Padding(0, 3, 3, 0) };
            _txtFilter.TextChanged += (s, e) => RefreshLeftList();
            leftTop.Controls.Add(_txtFilter);

            var rightTopLbl = new Label
            {
                Dock = DockStyle.Fill,
                Text = "선택됨 (더블클릭 = 제거)",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Gray,
            };

            // 가운데 화살표 열: ▶ 추가, ◀ 제거, ▶▶ 표시 전체 추가, ◀◀ 전체 해제
            var arrows = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0, 46, 0, 0),
                Margin = Padding.Empty,
            };
            var btnAdd = MakeArrow("▶", "선택 항목 추가");
            var btnRemove = MakeArrow("◀", "선택 항목 제거");
            var btnAddAll = MakeArrow("▶▶", "표시(필터) 전체 추가");
            var btnRemoveAll = MakeArrow("◀◀", "전체 해제");
            btnAdd.Click += (s, e) => AddHighlightedLeft();
            btnRemove.Click += (s, e) => RemoveHighlightedRight();
            btnAddAll.Click += (s, e) => AddAllShown();
            btnRemoveAll.Click += (s, e) => ClearSelection();
            arrows.Controls.AddRange(new Control[] { btnAdd, btnRemove, btnAddAll, btnRemoveAll });

            _lvAll = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = true,
            };
            _lvAll.Columns.Add("Sub-system", 96);
            _lvAll.Columns.Add("Description", 105);
            _lvAll.Columns.Add("단계", 54);
            _lvAll.Columns.Add("A-ITR", 52);
            _lvAll.Columns.Add("B-ITR", 52);
            _lvAll.Columns.Add("C-ITR", 52);
            _lvAll.Columns.Add("P.A", 48);
            _lvAll.Columns.Add("P.B", 48);
            _lvAll.Columns.Add("요소", 40);
            _lvAll.DoubleClick += (s, e) => AddHighlightedLeft();

            _lvSelected = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = true,
            };
            _lvSelected.Columns.Add("", 20);
            _lvSelected.Columns.Add("Sub-system", 92);
            _lvSelected.Columns.Add("단계", 54);
            _lvSelected.Columns.Add("요소", 38);
            _lvSelected.Columns.Add("매칭", 44);
            _lvSelected.SelectedIndexChanged += LvSelected_SelectedIndexChanged;
            _lvSelected.DoubleClick += (s, e) => RemoveHighlightedRight();

            _lblSelCount = new Label
            {
                Dock = DockStyle.Fill,
                Text = "선택된 Sub-system: 0개",
                TextAlign = ContentAlignment.MiddleRight,
            };

            grid.Controls.Add(leftTop, 0, 0);
            grid.Controls.Add(rightTopLbl, 2, 0);
            grid.Controls.Add(_lvAll, 0, 1);
            grid.Controls.Add(arrows, 1, 1);
            grid.Controls.Add(_lvSelected, 2, 1);
            grid.Controls.Add(_lblSelCount, 0, 2);
            grid.SetColumnSpan(_lblSelCount, 3);

            group.Controls.Add(grid);
            return group;
        }

        private static Button MakeArrow(string text, string tip)
        {
            return new Button
            {
                Text = text,
                Width = 30,
                Height = 26,
                Margin = new Padding(1, 2, 1, 2),
                AccessibleDescription = tip,
            };
        }

        // ----- 데이터 로드 -----

        private void LoadOasis()
        {
            try
            {
                var settings = SqlConnectionSettings.Load();
                var list = SqlLoader.LoadSubSystemElements(settings, out int noSubSystem);

                // 마스터는 별도 시도 — 테이블 미구성 환경에서도 요소 기준으로 동작해야 함
                Dictionary<string, SubSystemMasterData> master = null;
                string masterNote;
                try
                {
                    master = new Dictionary<string, SubSystemMasterData>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in SqlLoader.LoadSubSystemMaster(settings))
                        master[m.SubSystemNo] = m;
                    masterNote = $" · 마스터 {master.Count}개";
                }
                catch
                {
                    master = null;
                    masterNote = " · 마스터 미구성(요소 기준)";
                }

                _elements = list;
                _master = master;
                RebuildNames(out int outsideMaster);

                _selected.RemoveWhere(name => !_subSystemNames.Contains(name, StringComparer.OrdinalIgnoreCase));
                _selectionOrder.RemoveAll(name => !_selected.Contains(name));

                _matchedIds.Clear();
                _unmatchedIds.Clear();
                _appliedSubSystems.Clear();
                _appliedOnce = false;
                _appliedMode = null;

                // 단계 모드는 마스터가 있을 때만
                _rdoByStage.Enabled = _master != null;
                if (_master == null && _rdoByStage.Checked)
                    _rdoByProgress.Checked = true;

                string prj = string.IsNullOrEmpty(settings.ProjectNo) ? "전체" : settings.ProjectNo;
                _loadLabel = $"{settings.Database}/{prj} · {DateTime.Now:HH:mm}";
                _dotOasis.ForeColor = DotLoaded;
                _lblOasis.ForeColor = Color.Black;
                _lblOasis.Text = $"요소 {list.Count:N0}건 · Sub-system {_subSystemNames.Count}개{masterNote}"
                    + (outsideMaster > 0 ? $" · 마스터 외 {outsideMaster}개" : "")
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

        /// <summary>
        /// 좌측 목록의 이름 축을 만든다. 마스터가 있으면 마스터가 기준(요소 0건 포함)이고
        /// 마스터에 없는 요소 sub-system도 진단 목적으로 함께 노출한다("마스터 외").
        /// 마스터 미구성이면 요소 파생 목록만 사용한다.
        /// </summary>
        private void RebuildNames(out int outsideMaster)
        {
            _bySubSystem = _elements
                .GroupBy(el => el.SubSystem, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var names = new HashSet<string>(_bySubSystem.Keys, StringComparer.OrdinalIgnoreCase);
            outsideMaster = 0;
            if (_master != null)
            {
                outsideMaster = names.Count(n => !_master.ContainsKey(n));
                names.UnionWith(_master.Keys);
            }
            _subSystemNames = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private SubSystemMasterData GetMaster(string name) =>
            _master != null && _master.TryGetValue(name, out var m) ? m : null;

        private int ElementCount(string name) =>
            _bySubSystem.TryGetValue(name, out var els) ? els.Count : 0;

        // ----- 선택 UI (dual-list + 화살표) -----

        private void RefreshLeftList()
        {
            _lvAll.BeginUpdate();
            _lvAll.Items.Clear();

            string keyword = _txtFilter.Text.Trim();
            var referenceDate = _dtpReference.Value;

            foreach (var name in _subSystemNames)
            {
                var m = GetMaster(name);
                string desc = m != null ? (m.Description ?? "") : (_master != null ? "(마스터 외)" : "");
                if (keyword.Length > 0
                    && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0
                    && desc.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                int count = ElementCount(name);
                var item = new ListViewItem(name) { Tag = name };
                item.SubItems.Add(desc);
                item.SubItems.Add(m != null ? SubSystemStageInfo.Labels[m.GetStageAtDate(referenceDate)] : "-");
                item.SubItems.Add(m?.ItrAText ?? "-");
                item.SubItems.Add(m?.ItrBText ?? "-");
                item.SubItems.Add(m?.ItrCText ?? "-");
                item.SubItems.Add(m?.PunchAText ?? "-");
                item.SubItems.Add(m?.PunchBText ?? "-");
                item.SubItems.Add(count.ToString());
                if (count == 0) item.ForeColor = Color.Gray;       // 마스터에만 있고 요소 미배정
                if (_selected.Contains(name)) item.BackColor = PickedBack;
                _lvAll.Items.Add(item);
            }

            _lvAll.EndUpdate();
        }

        private void AddSelection(string name)
        {
            if (!_selected.Add(name)) return;
            _selectionOrder.Add(name);
        }

        private void RemoveSelection(string name)
        {
            if (!_selected.Remove(name)) return;
            _selectionOrder.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>▶ — 좌측에서 하이라이트한 행들을 우측에 추가.</summary>
        private void AddHighlightedLeft()
        {
            if (_lvAll.SelectedItems.Count == 0) return;
            foreach (ListViewItem item in _lvAll.SelectedItems)
            {
                AddSelection((string)item.Tag);
                item.BackColor = PickedBack;
            }
            RefreshRightList();
            UpdateSelCount();
            UpdateStats();
        }

        /// <summary>◀ — 우측에서 하이라이트한 행들을 제거.</summary>
        private void RemoveHighlightedRight()
        {
            if (_lvSelected.SelectedItems.Count == 0) return;
            foreach (ListViewItem item in _lvSelected.SelectedItems)
                RemoveSelection((string)item.Tag);
            RefreshLeftList();
            RefreshRightList();
            UpdateSelCount();
            UpdateStats();
        }

        /// <summary>▶▶ — 현재 표시(필터 통과)된 항목 전부 추가.</summary>
        private void AddAllShown()
        {
            foreach (ListViewItem item in _lvAll.Items)
            {
                AddSelection((string)item.Tag);
                item.BackColor = PickedBack;
            }
            RefreshRightList();
            UpdateSelCount();
            UpdateStats();
        }

        /// <summary>◀◀ — 전체 해제.</summary>
        private void ClearSelection()
        {
            _selected.Clear();
            _selectionOrder.Clear();
            RefreshLeftList();
            RefreshRightList();
            UpdateSelCount();
            UpdateStats();
        }

        private void RefreshRightList()
        {
            var referenceDate = _dtpReference.Value;
            _lvSelected.BeginUpdate();
            _lvSelected.Items.Clear();

            foreach (var name in _selectionOrder)
            {
                var m = GetMaster(name);
                int count = ElementCount(name);

                var item = new ListViewItem("■") { UseItemStyleForSubItems = false, Tag = name };
                // 스와치 = 마스터 단계색 (기준일 기준) — 마스터 없으면 중립 회색
                item.ForeColor = m != null && _stageSettings.TryGetValue(m.GetStageAtDate(referenceDate), out var st)
                    ? st.DisplayColor : Color.DimGray;
                item.SubItems.Add(name);
                item.SubItems.Add(m != null ? SubSystemStageInfo.Labels[m.GetStageAtDate(referenceDate)] : "-");
                item.SubItems.Add(count.ToString());

                string matchText = "-";
                Color matchColor = Color.Black;
                if (_appliedOnce && _appliedSubSystems.Contains(name) && count > 0
                    && _bySubSystem.TryGetValue(name, out var els))
                {
                    int matched = els.Count(el => _matchedIds.Contains(el.ElementId));
                    matchText = matched.ToString();
                    if (matched < count) matchColor = Color.Red;
                }
                var matchSub = item.SubItems.Add(matchText);
                matchSub.ForeColor = matchColor;

                _lvSelected.Items.Add(item);
            }

            _lvSelected.EndUpdate();
        }

        private void UpdateSelCount()
        {
            int elemCount = _selectionOrder.Sum(ElementCount);
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
                if (_bySubSystem.TryGetValue((string)item.Tag, out var els))
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
            if (_stagePanel == null || _progressPanel == null) return; // 초기화 중 이벤트 방어
            _stagePanel.Visible = _rdoByStage.Checked;
            _progressPanel.Visible = !_rdoByStage.Checked;
            if (_appliedOnce)
                _lblStats.Text += "\n⚠ 화면 색상은 이전 적용 기준 — 적용을 다시 실행하세요";
        }

        /// <summary>색/투명도 증분 변경 — 마지막 적용 모드와 같은 그리드에서만 유효.</summary>
        private void IncrementalUpdate(ApplyMode mode, string key, ColorSetting setting)
        {
            var doc = _main.GetDocument();
            if (doc == null || _appliedMode != mode || !_main.OverrideEngine.HasCachedData(VisualModule.SubSystem)) return;
            _main.OverrideEngine.UpdateStageColor(doc, VisualModule.SubSystem, key, setting);
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
            if (_rdoByStage.Checked && _master == null)
            {
                MessageBox.Show("Sub-system 마스터가 없어 단계별 가시화를 할 수 없습니다.\n요소 진행상태별 모드를 사용하세요.");
                return;
            }
            if (_main.TagSearcher.NeedsRebuild(doc))
                BuildIndex();

            var targets = _elements.Where(el => _selected.Contains(el.SubSystem)).ToList();
            var referenceDate = _dtpReference.Value;

            OverrideResult result;
            ApplyMode mode;
            if (_rdoByStage.Checked)
            {
                mode = ApplyMode.Stage;
                var groupSettings = new Dictionary<string, ColorSetting>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _stageChecks)
                    if (kv.Value.Checked)
                        groupSettings[kv.Key.ToString()] = _stageSettings[kv.Key];

                // 요소는 자기 sub-system의 마스터 단계색을 받는다. 마스터 외 sub-system
                // 요소는 그룹 키 null → 색칠 제외(매칭 집계에는 포함).
                result = _main.OverrideEngine.ApplySubSystem(doc, targets, el =>
                {
                    var m = GetMaster(el.SubSystem);
                    return m?.GetStageAtDate(referenceDate).ToString();
                }, groupSettings);
            }
            else
            {
                mode = ApplyMode.Progress;
                var groupSettings = new Dictionary<string, ColorSetting>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _progressChecks)
                    if (kv.Value.Checked)
                        groupSettings[kv.Key.ToString()] = _progressSettings[kv.Key];

                result = _main.OverrideEngine.ApplySubSystem(doc, targets,
                    el => el.StatusAt(referenceDate).ToString(), groupSettings);
            }

            _unmatchedIds = new HashSet<string>(result.UnmatchedIds, StringComparer.OrdinalIgnoreCase);
            _matchedIds = new HashSet<string>(
                targets.Select(el => el.ElementId).Where(id => !_unmatchedIds.Contains(id)),
                StringComparer.OrdinalIgnoreCase);
            _appliedSubSystems = new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase);
            _appliedOnce = true;
            _appliedMode = mode;

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
        /// CSV 리포트 (CLAUDE.md 8번 단기안): 헤더 블록 + Sub-system별 요약(마스터
        /// 단계·ITR·Punch 병기) + 상세 리스트. 선택이 있으면 선택만, 없으면 전체.
        /// 매칭 O/X는 마지막 [적용]에 포함됐던 Sub-system에만 유효 — 그 외는 "-".
        /// </summary>
        private void BtnReport_Click(object sender, EventArgs e)
        {
            if (_subSystemNames.Count == 0)
            {
                MessageBox.Show("OASIS 데이터를 먼저 로드하세요.");
                return;
            }

            var referenceDate = _dtpReference.Value;
            bool selectedOnly = _selected.Count > 0;
            var names = selectedOnly ? _selectionOrder.ToList() : _subSystemNames.ToList();

            var lines = new List<string>();
            lines.Add("Sub-system 현황 리포트");
            lines.Add($"출력 시각,{DateTime.Now:yyyy-MM-dd HH:mm}");
            lines.Add($"기준일,{referenceDate:yyyy-MM-dd}");
            lines.Add($"데이터 소스,{Csv("OASIS " + _loadLabel)}");
            lines.Add($"Sub-system 마스터,{(_master != null ? $"{_master.Count}개 로드됨" : "미구성 — 요소 파생 목록 기준")}");
            lines.Add($"집계 대상,{(selectedOnly ? "선택" : "전체")} Sub-system {names.Count}개");
            lines.Add($"매칭 기준,{(_appliedOnce ? "가시화 적용 결과 (적용된 Sub-system만 산정)" : "미적용 — 매칭 미산정")}");

            lines.Add("");
            lines.Add("[Sub-system별 요약]");
            lines.Add("Sub-system,Description,단계,A-ITR,B-ITR,C-ITR,Punch A,Punch B,요소,Equipment,Piping,매칭,미매칭,미착수,진행중,완료,완료율(%)");

            int tElems = 0, tEq = 0, tPip = 0, tMatched = 0, tUnmatched = 0, tNs = 0, tIp = 0, tDone = 0;
            bool anyMatchInfo = false;
            foreach (var name in names)
            {
                var m = GetMaster(name);
                var els = _bySubSystem.TryGetValue(name, out var found) ? found : new List<SubSystemElement>();
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
                if (_appliedOnce && _appliedSubSystems.Contains(name) && els.Count > 0)
                {
                    int matched = els.Count(el => _matchedIds.Contains(el.ElementId));
                    matchedText = matched.ToString();
                    unmatchedText = (els.Count - matched).ToString();
                    tMatched += matched;
                    tUnmatched += els.Count - matched;
                    anyMatchInfo = true;
                }

                string stageLabel = m != null
                    ? SubSystemStageInfo.Labels[m.GetStageAtDate(referenceDate)]
                    : (_master != null ? "마스터 외" : "-");
                string doneRate = els.Count > 0 ? (done * 100.0 / els.Count).ToString("F1") : "-";
                lines.Add($"{Csv(name)},{Csv(m?.Description ?? "")},{Csv(stageLabel)}," +
                    $"{Csv(m?.ItrAText ?? "-")},{Csv(m?.ItrBText ?? "-")},{Csv(m?.ItrCText ?? "-")}," +
                    $"{Csv(m?.PunchAText ?? "-")},{Csv(m?.PunchBText ?? "-")}," +
                    $"{els.Count},{eq},{pip},{matchedText},{unmatchedText},{ns},{ip},{done},{doneRate}");
                tElems += els.Count; tEq += eq; tPip += pip; tNs += ns; tIp += ip; tDone += done;
            }
            string totalRate = tElems > 0 ? (tDone * 100.0 / tElems).ToString("F1") : "-";
            lines.Add($"합계 ({names.Count}개),,,,,,,,{tElems},{tEq},{tPip},{(anyMatchInfo ? tMatched.ToString() : "-")}," +
                $"{(anyMatchInfo ? tUnmatched.ToString() : "-")},{tNs},{tIp},{tDone},{totalRate}");

            lines.Add("");
            lines.Add("[상세 리스트]");
            lines.Add("Sub-system,공종,요소 ID,설명,현재 단계,진행 상태,매칭");
            foreach (var name in names)
            {
                if (!_bySubSystem.TryGetValue(name, out var els)) continue;
                var ordered = els
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
            if (_subSystemNames.Count == 0)
            {
                _lblStats.Text = "로드된 데이터 없음";
                return;
            }

            var referenceDate = _dtpReference.Value;
            var linesOut = new List<string>();

            if (_selected.Count == 0)
            {
                linesOut.Add($"요소 {_elements.Count:N0}건 · Sub-system {_subSystemNames.Count}개 — 좌측에서 선택하세요");
            }
            else
            {
                int total = _selectionOrder.Sum(ElementCount);
                linesOut.Add($"선택 Sub-system {_selected.Count}개 · 요소 {total:N0}건 (기준일 {referenceDate:yyyy-MM-dd})");

                // 마스터가 있으면 sub-system 단계 분포
                if (_master != null)
                {
                    var stageCounts = new Dictionary<SubSystemStage, int>();
                    foreach (var name in _selectionOrder)
                    {
                        var m = GetMaster(name);
                        if (m == null) continue;
                        var st = m.GetStageAtDate(referenceDate);
                        stageCounts[st] = stageCounts.TryGetValue(st, out int c) ? c + 1 : 1;
                    }
                    var parts = new List<string>();
                    var allStages = new[] { SubSystemStage.NotStarted }.Concat(SubSystemStageInfo.OrderedStages).Reverse();
                    foreach (var st in allStages)
                        if (stageCounts.TryGetValue(st, out int c) && c > 0)
                            parts.Add($"{SubSystemStageInfo.Labels[st]} {c}");
                    if (parts.Count > 0)
                        linesOut.Add("단계: " + string.Join(" · ", parts));
                }

                int ns = 0, ip = 0, done = 0;
                foreach (var el in _elements)
                {
                    if (!_selected.Contains(el.SubSystem)) continue;
                    switch (el.StatusAt(referenceDate))
                    {
                        case ProgressStatus.Completed: done++; break;
                        case ProgressStatus.InProgress: ip++; break;
                        default: ns++; break;
                    }
                }
                linesOut.Add($"요소 진행: 미착수 {ns} · 진행중 {ip} · 완료 {done}");
            }

            if (_appliedOnce)
            {
                string mode = _appliedMode == ApplyMode.Stage ? "Sub-system 단계별" : "요소 진행상태별";
                linesOut.Add($"매칭 {_matchedIds.Count:N0} / 미매칭 {_unmatchedIds.Count:N0} ({mode} 적용됨)");
            }

            _lblStats.Text = string.Join("\n", linesOut);
        }

        private Dictionary<T, ColorSetting> CloneDefaults<T>(Dictionary<T, ColorSetting> defaults)
        {
            var clone = new Dictionary<T, ColorSetting>();
            foreach (var kv in defaults) clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
