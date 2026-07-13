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
    /// </summary>
    public class StructureTab : UserControl, IOverviewSource
    {
        private class AreaRow
        {
            public StructureArea Area;
            public Autodesk.Navisworks.Api.ModelItemCollection Items;
            public CheckBox Check;
            public ComboBox TransparencyBox;
        }

        private const double DefaultTransparency = 0.7;   // 백도면 용도 기본값

        private readonly MainDockablePanel _main;

        private readonly List<AreaRow> _areaRows = new List<AreaRow>();
        private string _scopeNote = "-";
        // 영역 ModelItem은 조회 시점 문서 기준 — 문서가 바뀌면 재조회 강제 (searcher NeedsRebuild와 동일 취지).
        private string _areasDocId;
        private Autodesk.Navisworks.Api.ModelItemCollection _hiddenByKeepOnly;

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
                Text = "영역 & 투명도 (Str 레벨1)",
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

            // 영역 행 목록 (조회 시 동적 구성)
            _areaTable = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
            _areaTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _areaTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            _areaPanel = new Panel { Dock = DockStyle.Fill, Height = 240, AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };
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

            // 재조회 시 기존 체크/투명도 설정을 영역명 기준으로 보존.
            var prev = _areaRows.ToDictionary(
                r => r.Area.Name,
                r => (r.Check.Checked, r.TransparencyBox.Text),
                StringComparer.OrdinalIgnoreCase);

            var result = StructureAreaService.Probe(doc);
            _scopeNote = result.ScopeNote;
            _areasDocId = DocId(doc);

            _areaTable.SuspendLayout();
            _areaTable.Controls.Clear();
            _areaTable.RowCount = 0;
            _areaRows.Clear();

            foreach (var area in result.Areas)
            {
                var items = new Autodesk.Navisworks.Api.ModelItemCollection();
                items.AddRange(area.Items);

                var chk = new CheckBox { Text = area.Name, Checked = true, AutoSize = true, Padding = new Padding(0, 2, 0, 0) };
                var transparencyBox = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%" })
                    transparencyBox.Items.Add(t);
                transparencyBox.Text = $"{(int)(DefaultTransparency * 100)}%";

                if (prev.TryGetValue(area.Name, out var p))
                {
                    chk.Checked = p.Checked;
                    if (transparencyBox.Items.Contains(p.Text)) transparencyBox.Text = p.Text;
                }

                var row = new AreaRow { Area = area, Items = items, Check = chk, TransparencyBox = transparencyBox };
                chk.CheckedChanged += (s2, e2) => _applyState.MarkStale("영역 선택 변경");
                transparencyBox.SelectedIndexChanged += (s2, e2) => IncrementalUpdate(row);

                _areaTable.Controls.Add(chk, 0, _areaRows.Count);
                _areaTable.Controls.Add(transparencyBox, 1, _areaRows.Count);
                _areaRows.Add(row);
            }
            _areaTable.ResumeLayout();

            _lblScope.Text = _scopeNote;
            _lblScope.ForeColor = result.Found ? Color.Gray : Color.FromArgb(200, 40, 40);
            _lblStats.Text = result.Found
                ? $"영역 {_areaRows.Count}개 조회됨 — 체크·투명도 설정 후 [투명도 적용] 또는 [선택 항목만 남김]"
                : "영역 없음 — Str 파일명 규약(*_Str)과 열린 문서를 확인하세요.";
            _applyState.MarkStale("영역 재조회");
        }

        private void SetAllChecks(bool value)
        {
            foreach (var row in _areaRows)
                row.Check.Checked = value;
        }

        /// <summary>투명도 콤보 변경 시 캐시된 영역 컬렉션에 즉시 재적용 (적용 전/미체크 영역은 no-op).</summary>
        private void IncrementalUpdate(AreaRow row)
        {
            var doc = _main.GetDocument();
            if (doc == null || !_main.OverrideEngine.HasCachedData(VisualModule.Structure)) return;
            _main.OverrideEngine.UpdateGroupTransparency(
                doc, VisualModule.Structure, row.Area.Name, TransparencyOf(row));
        }

        private static double TransparencyOf(AreaRow row) =>
            double.TryParse(row.TransparencyBox.Text.Replace("%", ""), out double pct) ? pct / 100.0 : DefaultTransparency;

        // ----- 가시화 -----

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

            var groups = _areaRows
                .Where(r => r.Check.Checked)
                .Select(r => (r.Area.Name, r.Items, TransparencyOf(r)))
                .ToList();

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

            _applyState.SetApplied($"영역 {applied}개 투명도");
            _lblStats.Text = applied > 0
                ? $"투명도 적용: 체크 영역 {applied}개 (색은 원본 유지 — 백도면)" + KeepOnlySuffix()
                : "체크된 영역이 없어 이 탭의 투명도 오버라이드만 해제되었습니다." + KeepOnlySuffix();
        }

        /// <summary>체크 안 된 영역을 숨겨 구조 중 선택 항목만 남긴다(토글). 타 공종 객체는 건드리지 않음.</summary>
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
            foreach (var row in _areaRows)
                if (!row.Check.Checked)
                    toHide.AddRange(row.Items);

            if (toHide.Count == 0)
            {
                MessageBox.Show("숨길 대상이 없습니다 (모든 영역이 체크되어 있습니다). 남길 영역만 체크하세요.");
                return;
            }

            doc.Models.SetHidden(toHide, true);
            _hiddenByKeepOnly = toHide;
            _btnKeepOnly.Text = "구조 전체 보기";
            int kept = _areaRows.Count(r => r.Check.Checked);
            _lblStats.Text = $"선택 항목만 남김: 영역 {kept}개 유지, {_areaRows.Count - kept}개 숨김 (체크 변경 후에는 토글로 복원 후 다시 실행)";
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
            return new OverviewStatus
            {
                DataLoaded = _areaRows.Count > 0,
                DataText = _areaRows.Count > 0 ? $"영역 {_areaRows.Count}개" : "미조회",
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
