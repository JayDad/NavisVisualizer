using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NavisVisualizer.Services;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// 구조(Structure) 탭 — Str nwd/nwc의 레벨1 영역 노드(/QR/LG/STRU/HHI …)를 그대로 나열해
    /// 체크박스 + 투명도로 백도면을 만든다. 실적 데이터·매칭·색상 없음(간단 기능):
    ///   [투명도 적용]      체크된 영역에 투명도만 오버라이드 (색은 원본 유지 — 반투명 백도면)
    ///   [선택 항목만 남김]  체크 안 된 영역을 숨김(토글) — 구조 중 선택 영역만 남아
    ///                      다른 공종 가시화의 배경 역할 (타 공종 객체는 건드리지 않음)
    /// 영역 목록은 모델에서 읽으므로 [영역 불러오기]로 조회·새로고침한다 (StructureAreaService —
    /// Str 파일 노드만 찾는 하드 스코프 성격, 미발견 시 전체 모델을 훑지 않고 진단 노트만).
    ///
    /// 레벨2 펼침(기본 접힘): 레벨1 행의 ▸ 버튼으로 하위(레벨2) 행을 펼쳐 개별 체크/투명도 조정.
    /// 레벨1 체크박스 = 하위 전체 토글(부분 선택 시 중간 상태 표시), 레벨1 콤보 = 하위 전체 전파.
    /// 적용/숨김 단위: 영역이 균일(전부 체크 + 투명도 동일)하면 레벨1 노드 통째로(직속 geometry
    /// 포함), 부분 선택이면 체크된 레벨2 노드 단위 — 이때 레벨2 그룹에 속하지 않는 레벨1 직속
    /// geometry는 대상에서 빠진다(수용된 한계, CLAUDE.md §17).
    /// 행의 ⊙ 버튼 = 그 영역/하위를 3D에서 선택·포커스 (Navisworks 선택 하이라이트로 강조).
    /// </summary>
    public class StructureTab : UserControl, IOverviewSource
    {
        private class AreaRow
        {
            public StructureArea Area;
            public Autodesk.Navisworks.Api.ModelItemCollection Items;
            public CheckBox Check;
            public ComboBox TransparencyBox;
            public Button FocusBtn;                                    // ⊙ = 3D 선택·포커스
            public Button ExpandBtn;                                   // 레벨1 + 하위 보유 시에만
            public AreaRow Parent;                                     // 레벨2 행이면 소속 레벨1
            public List<AreaRow> ChildRows = new List<AreaRow>();      // 레벨1 행의 레벨2 행들
            public bool Expanded;
        }

        private const double DefaultTransparency = 0.7;   // 백도면 용도 기본값
        private static readonly string[] TransparencyChoices = { "0%", "20%", "40%", "60%", "70%", "80%", "90%" };

        private readonly MainDockablePanel _main;

        private readonly List<AreaRow> _areaRows = new List<AreaRow>();   // 레벨1 행만 (레벨2는 ChildRows)
        private string _scopeNote = "-";
        // 영역 ModelItem은 조회 시점 문서 기준 — 문서가 바뀌면 재조회 강제 (searcher NeedsRebuild와 동일 취지).
        private string _areasDocId;
        private Autodesk.Navisworks.Api.ModelItemCollection _hiddenByKeepOnly;
        // 부모↔자식 체크/콤보 동기화 중 핸들러 연쇄(MarkStale 중복·무한 재귀) 차단.
        private bool _syncing;

        private Label _lblScope;
        private Panel _areaPanel;
        private TableLayoutPanel _areaTable;
        private Button _btnLoadAreas;
        private Button _btnApply;
        private Button _btnKeepOnly;
        private Button _btnResetModule;
        private Button _btnReset;
        private Label _lblStats;
        private ApplyStatePanel _applyState;
        private ProgressBar _progressBar;

        public StructureTab(MainDockablePanel main)
        {
            _main = main;
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

            _applyState = new ApplyStatePanel();

            // 상단: 영역 조회 (실적 로드가 없는 탭 — 데이터 소스 대신 모델에서 영역을 읽는다)
            var loadPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 30, AutoSize = false, WrapContents = false };
            _btnLoadAreas = new Button { Text = "영역 불러오기(새로고침)", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 1, 8, 1) };
            _btnLoadAreas.Click += BtnLoadAreas_Click;
            _lblScope = new Label { Text = "영역 미조회 — 모델을 열고 [영역 불러오기]를 실행하세요.", AutoSize = true, Padding = new Padding(8, 5, 0, 0), ForeColor = Color.Gray };
            loadPanel.Controls.Add(_btnLoadAreas);
            loadPanel.Controls.Add(_lblScope);

            // 영역 헤더: 제목 + 전체 체크/해제
            var headerPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 26, AutoSize = false, WrapContents = false };
            headerPanel.Controls.Add(new Label
            {
                Text = "영역 & 투명도 (▸ 레벨2 펼침 · ⊙ 3D 선택·포커스)",
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(0, 4, 8, 0)
            });
            var btnCheckAll = new Button { Text = "전체 체크", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1) };
            var btnUncheckAll = new Button { Text = "전체 해제", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1) };
            btnCheckAll.Click += (s, e) => SetAllChecks(true);
            btnUncheckAll.Click += (s, e) => SetAllChecks(false);
            headerPanel.Controls.Add(btnCheckAll);
            headerPanel.Controls.Add(btnUncheckAll);

            // 영역 행 목록 (조회·펼침 시 동적 구성 — RebuildAreaTable)
            _areaTable = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true };
            _areaTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
            _areaTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _areaTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            _areaTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            // 레벨2 펼침으로 행이 늘어남 — 다른 탭 리스트(230)보다 크게 잡는다 (사용자 요청 2026-07).
            _areaPanel = new Panel { Dock = DockStyle.Fill, Height = 360, AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };
            _areaPanel.Controls.Add(_areaTable);

            // 1행(가시화)
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnApply = new Button { Text = "투명도 적용", Width = 100 };
            _btnKeepOnly = new Button { Text = "선택 항목만 남김", Width = 130 };
            _btnApply.Click += BtnApply_Click;
            _btnKeepOnly.Click += BtnKeepOnly_Click;
            _applyState.AttachApplyButton(_btnApply);
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnKeepOnly, _applyState });

            // 2행(해제)
            var btnPanelReset = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, AutoSize = true };
            _btnResetModule = new Button { Text = "이 탭 가시화 해제", Width = 130 };
            _btnReset = new Button { Text = "전체 가시화 해제", Width = 130 };
            _btnResetModule.Click += BtnResetModule_Click;
            _btnReset.Click += BtnReset_Click;
            btnPanelReset.Controls.AddRange(new Control[] { _btnResetModule, _btnReset });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };

            _lblStats = new Label { Dock = DockStyle.Fill, Height = 36, Text = "영역 미조회", AutoSize = false };

            layout.Controls.Add(loadPanel);
            layout.Controls.Add(headerPanel);
            layout.Controls.Add(_areaPanel);
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(btnPanelReset);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(_lblStats);

            Controls.Add(layout);
        }

        // ----- 영역 조회 -----

        private void BtnLoadAreas_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || doc.Models.Count == 0)
            {
                MessageBox.Show("모델을 먼저 열어주세요.");
                return;
            }

            // 재조회 전 숨김 원복 — 옛 문서의 컬렉션이면 실패할 수 있으므로 방어적으로.
            RestoreKeepOnlyHidden(doc);

            // 재조회 시 기존 체크/투명도/펼침 설정을 영역명 기준으로 보존.
            var prevL1 = new Dictionary<string, (CheckState State, string T, bool Expanded)>(StringComparer.OrdinalIgnoreCase);
            var prevL2 = new Dictionary<string, (bool Checked, string T)>(StringComparer.OrdinalIgnoreCase);
            foreach (var l1 in _areaRows)
            {
                prevL1[l1.Area.Name] = (l1.Check.CheckState, l1.TransparencyBox.Text, l1.Expanded);
                foreach (var c in l1.ChildRows)
                    prevL2[ChildKey(l1, c)] = (c.Check.Checked, c.TransparencyBox.Text);
            }

            var result = StructureAreaService.Probe(doc);
            _scopeNote = result.ScopeNote;
            _areasDocId = DocId(doc);
            _areaRows.Clear();

            // 행 구성·설정 복원 중 부모↔자식 동기화 핸들러 연쇄 차단. 예외가 나도 가드가
            // 참으로 고착돼 이후 세션 내내 동기화·MarkStale이 죽는 일이 없도록 try/finally로 복원.
            _syncing = true;
            try
            {
                foreach (var area in result.Areas)
                {
                    var l1 = MakeRow(area, null);

                    bool hasPrev = prevL1.TryGetValue(area.Name, out var p);
                    bool parentPrevChecked = !hasPrev || p.State != CheckState.Unchecked;
                    string parentPrevT = hasPrev && l1.TransparencyBox.Items.Contains(p.T) ? p.T : null;
                    if (parentPrevT != null) l1.TransparencyBox.Text = parentPrevT;

                    if (!area.ChildrenTruncated)
                    {
                        foreach (var childArea in area.Children)
                        {
                            var c = MakeRow(childArea, l1);
                            // 하위 개별 설정이 없으면 부모의 이전 상태를 상속.
                            c.Check.Checked = parentPrevChecked;
                            if (parentPrevT != null) c.TransparencyBox.Text = parentPrevT;
                            if (prevL2.TryGetValue(ChildKey(l1, c), out var cp))
                            {
                                c.Check.Checked = cp.Checked;
                                if (c.TransparencyBox.Items.Contains(cp.T)) c.TransparencyBox.Text = cp.T;
                            }
                            l1.ChildRows.Add(c);
                        }
                    }

                    if (l1.ChildRows.Count > 0)
                    {
                        l1.Expanded = hasPrev && p.Expanded;
                        l1.ExpandBtn = new Button { Text = l1.Expanded ? "▾" : "▸", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
                        l1.ExpandBtn.FlatAppearance.BorderSize = 0;
                        var captured = l1;
                        l1.ExpandBtn.Click += (s2, e2) =>
                        {
                            captured.Expanded = !captured.Expanded;
                            captured.ExpandBtn.Text = captured.Expanded ? "▾" : "▸";
                            RebuildAreaTable();
                        };
                        SyncParentCheckState(l1);   // 자식 복원 결과 기준으로 부모 상태 확정 (혼합 = 중간 상태)
                    }
                    else
                    {
                        l1.Check.Checked = parentPrevChecked;
                    }

                    _areaRows.Add(l1);
                }
            }
            finally
            {
                _syncing = false;
            }

            RebuildAreaTable();

            int childTotal = _areaRows.Sum(r => r.ChildRows.Count);
            _lblScope.Text = _scopeNote;
            _lblScope.ForeColor = result.Found ? Color.Gray : Color.FromArgb(200, 40, 40);
            _lblStats.Text = result.Found
                ? $"영역 {_areaRows.Count}개 (하위 {childTotal}개) 조회됨 — 체크·투명도 설정 후 [투명도 적용] 또는 [선택 항목만 남김]"
                : "영역 없음 — Str 파일명 규약(*_Str)과 열린 문서를 확인하세요.";
            _applyState.MarkStale("영역 재조회");
        }

        /// <summary>행 하나 생성 (parent=null이면 레벨1). 핸들러는 _syncing 가드로 구성 중 연쇄가 차단된다.</summary>
        private AreaRow MakeRow(StructureArea area, AreaRow parent)
        {
            var items = new Autodesk.Navisworks.Api.ModelItemCollection();
            items.AddRange(area.Items);

            string text = area.Name;
            if (parent == null && area.ChildrenTruncated)
                text += $" (하위 {StructureAreaService.MaxLevel2PerArea}개 초과 — 펼침 생략)";

            var chk = new CheckBox
            {
                Text = text,
                Checked = true,
                AutoSize = true,
                Padding = new Padding(0, 2, 0, 0),
                // 레벨2는 들여쓰기로 소속을 표시.
                Margin = parent == null ? new Padding(3, 2, 3, 2) : new Padding(24, 2, 3, 2),
            };
            var transparencyBox = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var t in TransparencyChoices)
                transparencyBox.Items.Add(t);
            transparencyBox.Text = $"{(int)(DefaultTransparency * 100)}%";

            var focusBtn = new Button { Text = "⊙", Width = 24, Height = 20, FlatStyle = FlatStyle.Flat };
            focusBtn.FlatAppearance.BorderSize = 0;

            var row = new AreaRow { Area = area, Items = items, Check = chk, TransparencyBox = transparencyBox, FocusBtn = focusBtn, Parent = parent };
            chk.CheckedChanged += (s, e) => OnCheckChanged(row);
            transparencyBox.SelectedIndexChanged += (s, e) => OnTransparencyChanged(row);
            focusBtn.Click += (s, e) => FocusInModel(row);
            return row;
        }

        /// <summary>⊙: 행의 영역(레벨1 전체 또는 레벨2 하나)을 3D에서 선택·포커스 — Navisworks 기본
        /// 선택 하이라이트가 강조 역할 (EitTray 등 리스트 선택 동기화와 동일 패턴).</summary>
        private void FocusInModel(AreaRow row)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            if (DocId(doc) != _areasDocId)
            {
                _lblStats.Text = "모델이 변경되었습니다. [영역 불러오기]를 다시 실행하세요.";
                return;
            }
            if (row.Items.Count == 0) return;
            doc.CurrentSelection.CopyFrom(row.Items);
            doc.ActiveView.FocusOnCurrentSelection();
        }

        /// <summary>보이는 행(레벨1 + 펼쳐진 레벨2)만 테이블에 재배치 — 행 수가 수십 수준이라 전체 재구성이 단순·안전.</summary>
        private void RebuildAreaTable()
        {
            _areaTable.SuspendLayout();
            _areaTable.Controls.Clear();
            int r = 0;
            foreach (var l1 in _areaRows)
            {
                if (l1.ExpandBtn != null)
                    _areaTable.Controls.Add(l1.ExpandBtn, 0, r);
                _areaTable.Controls.Add(l1.Check, 1, r);
                _areaTable.Controls.Add(l1.TransparencyBox, 2, r);
                _areaTable.Controls.Add(l1.FocusBtn, 3, r);
                r++;
                if (!l1.Expanded) continue;
                foreach (var c in l1.ChildRows)
                {
                    _areaTable.Controls.Add(c.Check, 1, r);
                    _areaTable.Controls.Add(c.TransparencyBox, 2, r);
                    _areaTable.Controls.Add(c.FocusBtn, 3, r);
                    r++;
                }
            }
            _areaTable.RowCount = r;
            _areaTable.ResumeLayout();
        }

        private void OnCheckChanged(AreaRow row)
        {
            if (_syncing) return;
            if (row.Parent == null && row.ChildRows.Count > 0)
            {
                // 레벨1 클릭 = 하위 전체 토글 (중간 상태에서 클릭하면 WinForms 기본 동작으로 Checked가 됨).
                _syncing = true;
                foreach (var c in row.ChildRows)
                    c.Check.Checked = row.Check.Checked;
                _syncing = false;
            }
            else if (row.Parent != null)
            {
                SyncParentCheckState(row.Parent);
            }
            _applyState.MarkStale("영역 선택 변경");
        }

        /// <summary>하위 체크 상태로 레벨1 표시 확정 — 전부/전무/혼합(중간 상태).
        /// 로드(외부 가드) 안에서도 호출되므로 가드를 저장/복원해 중첩에 안전하게.</summary>
        private void SyncParentCheckState(AreaRow l1)
        {
            bool outer = _syncing;
            _syncing = true;
            int cnt = l1.ChildRows.Count(c => c.Check.Checked);
            l1.Check.CheckState = cnt == 0 ? CheckState.Unchecked
                : cnt == l1.ChildRows.Count ? CheckState.Checked
                : CheckState.Indeterminate;
            _syncing = outer;
        }

        private void OnTransparencyChanged(AreaRow row)
        {
            if (_syncing) return;
            if (row.Parent == null && row.ChildRows.Count > 0)
            {
                // 레벨1 콤보 = 하위 전체 전파 (개별 조정은 펼쳐서 레벨2 콤보로).
                _syncing = true;
                foreach (var c in row.ChildRows)
                    c.TransparencyBox.Text = row.TransparencyBox.Text;
                _syncing = false;
            }
            IncrementalUpdate(row);
        }

        private void SetAllChecks(bool value)
        {
            _syncing = true;
            foreach (var l1 in _areaRows)
            {
                // Indeterminate에서 Checked=true 대입은 값이 안 바뀌어 이벤트가 안 뜨므로 CheckState로 직접.
                l1.Check.CheckState = value ? CheckState.Checked : CheckState.Unchecked;
                foreach (var c in l1.ChildRows)
                    c.Check.Checked = value;
            }
            _syncing = false;
            _applyState.MarkStale("영역 선택 변경");
        }

        private static string ChildKey(AreaRow l1, AreaRow child) =>
            $"{l1.Area.Name} ▸ {child.Area.Name}";

        /// <summary>
        /// 투명도 콤보 변경 시 캐시된 그룹에 즉시 재적용. 적용 시점의 그룹 단위(레벨1 통째 vs
        /// 레벨2 개별)와 현재 변경이 어긋나 캐시 키가 없으면 재적용 필요를 상태 표시기로 알린다.
        /// </summary>
        private void IncrementalUpdate(AreaRow row)
        {
            var doc = _main.GetDocument();
            if (doc == null || !_main.OverrideEngine.HasCachedData(VisualModule.Structure)) return;

            bool any;
            if (row.Parent == null)
            {
                any = _main.OverrideEngine.UpdateGroupTransparency(
                    doc, VisualModule.Structure, row.Area.Name, TransparencyOf(row));
                foreach (var c in row.ChildRows)
                    any |= _main.OverrideEngine.UpdateGroupTransparency(
                        doc, VisualModule.Structure, ChildKey(row, c), TransparencyOf(c));
            }
            else
            {
                any = _main.OverrideEngine.UpdateGroupTransparency(
                    doc, VisualModule.Structure, ChildKey(row.Parent, row), TransparencyOf(row));
            }
            if (!any)
                _applyState.MarkStale("투명도 변경");
        }

        private static double TransparencyOf(AreaRow row) =>
            double.TryParse(row.TransparencyBox.Text.Replace("%", ""), out double pct) ? pct / 100.0 : DefaultTransparency;

        // ----- 가시화 -----

        /// <summary>
        /// 적용/숨김 단위 결정: 영역이 균일(하위 전부 체크 + 투명도 동일 또는 하위 없음)하면
        /// 레벨1 노드 통째로(직속 geometry 포함 + 그룹 수 최소), 부분 선택이면 체크된 레벨2 단위.
        /// </summary>
        private List<(string AreaName, Autodesk.Navisworks.Api.ModelItemCollection Items, double Transparency)> BuildTransparencyGroups()
        {
            var groups = new List<(string, Autodesk.Navisworks.Api.ModelItemCollection, double)>();
            foreach (var l1 in _areaRows)
            {
                if (l1.ChildRows.Count == 0)
                {
                    if (l1.Check.Checked)
                        groups.Add((l1.Area.Name, l1.Items, TransparencyOf(l1)));
                    continue;
                }

                var checkedKids = l1.ChildRows.Where(c => c.Check.Checked).ToList();
                if (checkedKids.Count == 0) continue;

                bool uniform = checkedKids.Count == l1.ChildRows.Count
                    && l1.ChildRows.All(c => c.TransparencyBox.Text == l1.ChildRows[0].TransparencyBox.Text);
                if (uniform)
                    groups.Add((l1.Area.Name, l1.Items, TransparencyOf(l1.ChildRows[0])));
                else
                    foreach (var c in checkedKids)
                        groups.Add((ChildKey(l1, c), c.Items, TransparencyOf(c)));
            }
            return groups;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) { MessageBox.Show("모델을 먼저 열어주세요."); return; }
            if (_areaRows.Count == 0) { MessageBox.Show("먼저 [영역 불러오기]를 실행하세요."); return; }
            if (DocId(doc) != _areasDocId)
            {
                MessageBox.Show("모델이 변경되었습니다. [영역 불러오기]를 다시 실행하세요.");
                return;
            }

            var groups = BuildTransparencyGroups();

            int applied;
            _btnApply.Enabled = false;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Visible = true;
            _lblStats.Text = "투명도 적용 중…";
            Application.DoEvents();
            try
            {
                applied = _main.OverrideEngine.ApplyStructureTransparency(doc, groups);
            }
            finally
            {
                _progressBar.Visible = false;
                _progressBar.Style = ProgressBarStyle.Blocks;
                _btnApply.Enabled = true;
            }

            _applyState.SetApplied($"그룹 {applied}개 투명도");
            _lblStats.Text = applied > 0
                ? $"투명도 적용: 체크 그룹 {applied}개 (색은 원본 유지 — 백도면)" + KeepOnlySuffix()
                : "체크된 영역이 없어 이 탭의 투명도 오버라이드만 해제되었습니다." + KeepOnlySuffix();
        }

        /// <summary>체크 안 된 영역/하위를 숨겨 구조 중 선택 항목만 남긴다(토글). 타 공종 객체는 건드리지 않음.</summary>
        private void BtnKeepOnly_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) { MessageBox.Show("모델을 먼저 열어주세요."); return; }

            if (_hiddenByKeepOnly != null)
            {
                // 문서가 바뀌었으면 옛 컬렉션 복원이 실패할 수 있음 — 조용히 상태만 정리
                // (닫힌 문서의 숨김은 어차피 소멸).
                RestoreKeepOnlyHidden(doc);
                _lblStats.Text = "구조 전체 보기로 복원되었습니다.";
                return;
            }

            if (_areaRows.Count == 0) { MessageBox.Show("먼저 [영역 불러오기]를 실행하세요."); return; }
            if (DocId(doc) != _areasDocId)
            {
                MessageBox.Show("모델이 변경되었습니다. [영역 불러오기]를 다시 실행하세요.");
                return;
            }

            var toHide = new Autodesk.Navisworks.Api.ModelItemCollection();
            foreach (var l1 in _areaRows)
            {
                if (l1.ChildRows.Count == 0)
                {
                    if (!l1.Check.Checked) toHide.AddRange(l1.Items);
                    continue;
                }
                var uncheckedKids = l1.ChildRows.Where(c => !c.Check.Checked).ToList();
                if (uncheckedKids.Count == l1.ChildRows.Count)
                    toHide.AddRange(l1.Items);   // 전부 해제 → 영역 통째 숨김 (레벨1 직속 geometry 포함)
                else
                    foreach (var c in uncheckedKids)
                        toHide.AddRange(c.Items);
            }

            if (toHide.Count == 0)
            {
                MessageBox.Show("숨길 대상이 없습니다 (모든 영역이 체크되어 있습니다). 남길 영역만 체크하세요.");
                return;
            }

            doc.Models.SetHidden(toHide, true);
            _hiddenByKeepOnly = toHide;
            _btnKeepOnly.Text = "구조 전체 보기";
            _lblStats.Text = $"선택 항목만 남김: 체크 영역만 표시 (숨김 노드 {toHide.Count}개 — 체크 변경 후에는 토글로 복원 후 다시 실행)";
        }

        /// <summary>keep-only 숨김 복원 — 실패(문서 전환으로 stale 컬렉션 등)해도 조용히 상태만 정리.</summary>
        private void RestoreKeepOnlyHidden(Autodesk.Navisworks.Api.Document doc)
        {
            if (_hiddenByKeepOnly == null) return;
            try
            {
                doc.Models.SetHidden(_hiddenByKeepOnly, false);
            }
            catch
            {
                // 닫힌 문서의 숨김은 어차피 소멸 — 참조만 버린다.
            }
            _hiddenByKeepOnly = null;
            _btnKeepOnly.Text = "선택 항목만 남김";
        }

        private string KeepOnlySuffix() =>
            _hiddenByKeepOnly != null ? " · 숨김 활성(선택 항목만 남김)" : "";

        // ----- 해제 -----

        /// <summary>이 탭 가시화 해제: 구조 투명도 오버라이드만 제거(다른 공종 색 유지) + 숨김 복원.</summary>
        private void BtnResetModule_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            RestoreKeepOnlyHidden(doc);
            _main.OverrideEngine.ResetModule(doc, VisualModule.Structure);
            _lblStats.Text = "이 탭 가시화 해제 완료 (구조 투명도만 제거)";
            _applyState.SetCleared();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            RestoreKeepOnlyHidden(doc);
            _main.OverrideEngine.Reset(doc);
            _lblStats.Text = "전체 가시화 해제 완료";
            _applyState.SetCleared();
        }

        // ----- 공통 -----

        /// <summary>ModelItemSearcher.GetDocumentId와 동일 규칙 — 파일 경로 + 모델 수로 문서 변경 감지.</summary>
        private static string DocId(Autodesk.Navisworks.Api.Document doc)
        {
            try
            {
                return $"{doc.FileName ?? ""}|{doc.Models.Count}";
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }

        /// <summary>Overview 탭 상태 노출 — 실적/매칭이 없는 탭이라 영역 조회·적용 상태만 (IOverviewSource).</summary>
        public OverviewStatus GetOverviewStatus()
        {
            int childTotal = _areaRows.Sum(r => r.ChildRows.Count);
            return new OverviewStatus
            {
                DataLoaded = _areaRows.Count > 0,
                DataText = _areaRows.Count > 0 ? $"영역 {_areaRows.Count}개 · 하위 {childTotal}개" : "미조회",
                IndexText = "-",
                ApplyStateText = _applyState.Text + (_hiddenByKeepOnly != null ? " · 숨김 활성" : ""),
                ApplyStale = _applyState.IsStale,
                MatchedText = "-",
                UnmatchedText = "-",
                UnmatchedCount = 0,
                ScopeNote = _scopeNote,
                ScopeFellBack = false,
            };
        }
    }
}
