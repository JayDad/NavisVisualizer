using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// 공종 하나의 모델 파일 매핑 대화상자 (2026-09). 세 가지 중 하나를 고른다:
    ///   ◉ 자동 인식 — 프로파일 별칭으로 파일명 찾기 (별칭 편집·저장 가능, 인식 결과 즉시 표시)
    ///   ○ 파일 직접 선택 — 열린 문서의 파일 노드 트리에서 체크 (복수 가능, 하위 파일 체크 해제 = 제외)
    ///   ○ 전체 모델 — 모든 파일 순회 (느림, 명시 선택만)
    /// 확인 시 문서별 매핑(ScopeMappingConfig)에 저장되어 다음 열기부터 묻지 않는다.
    /// 파일 노드만 보여준다(geometry 노드는 안 보임) — 열거가 즉시 끝나고 사용자가 헷갈리지 않게.
    /// </summary>
    public class ScopeMappingDialog : Form
    {
        private readonly Document _doc;
        private readonly NwdScope _scope;
        private readonly string _tabLabel;
        private List<ScopeProfile> _profiles;
        private ScopeProfile _profile;
        private List<FileNodeInfo> _nodes;

        private RadioButton _rbAuto, _rbFiles, _rbAll;
        private TextBox _txtAlias;
        private Button _btnSaveAlias;
        private Label _lblAutoResult;
        private TreeView _tree;
        private Label _lblTreeHint;
        private bool _syncing;

        private static readonly Color OkColor = Color.FromArgb(0, 120, 40);
        private static readonly Color BadColor = Color.FromArgb(200, 40, 40);

        /// <summary>확인 후 해석 결과 (호출부가 IsMapped로 후속 판단).</summary>
        public ScopeResolution Result { get; private set; }

        public ScopeMappingDialog(Document doc, NwdScope scope, string tabLabel)
        {
            _doc = doc;
            _scope = scope;
            _tabLabel = tabLabel;
            _profiles = ScopeMappingService.LoadProfiles();
            _profile = ScopeMappingService.ActivateProfile(doc, _profiles);
            _nodes = ScopeMappingService.EnumerateFileNodes(doc);
            InitializeComponent();
            LoadCurrent();
        }

        private void InitializeComponent()
        {
            Text = $"모델 파일 지정 — {_tabLabel}";
            Width = 640;
            Height = 620;
            MinimumSize = new Size(520, 480);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Font = new Font("Malgun Gothic", 9f);

            string docName;
            try { docName = NwdScope.StripDirectory(_doc.FileName ?? "(무제)"); } catch { docName = "(무제)"; }

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 헤더
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 방식 선택
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 트리
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 힌트
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 버튼

            var header = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(600, 0),
                Text = $"공종: {_tabLabel}      문서: {docName}      프로파일: {_profile.Name}" +
                       (ScopeMappingService.IsProfileAutoDetected(_doc) ? " (파일명으로 자동 감지)" : ""),
                Padding = new Padding(0, 0, 0, 6),
            };

            // ---- 방식 선택 ----
            var grp = new GroupBox { Text = "이 공종의 모델 파일을 어떻게 정할까요?", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 6) };
            var grpLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };

            _rbAuto = new RadioButton { Text = "자동 인식 — 아래 별칭이 파일명에 포함된 파일을 씀", AutoSize = true, Checked = true };
            var aliasRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Padding = new Padding(22, 0, 0, 0) };
            aliasRow.Controls.Add(new Label { Text = "별칭:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            _txtAlias = new TextBox { Width = 260 };
            _btnSaveAlias = new Button { Text = "별칭 저장", Width = 80, Height = 24 };
            _btnSaveAlias.Click += BtnSaveAlias_Click;
            aliasRow.Controls.Add(_txtAlias);
            aliasRow.Controls.Add(_btnSaveAlias);
            aliasRow.Controls.Add(new Label { Text = "(쉼표로 여러 개, 대소문자 무시)", AutoSize = true, ForeColor = Color.Gray, Padding = new Padding(4, 5, 0, 0) });
            _lblAutoResult = new Label { AutoSize = true, Padding = new Padding(22, 0, 0, 6), MaximumSize = new Size(580, 0) };

            _rbFiles = new RadioButton { Text = "파일 직접 선택 — 아래 목록에서 체크 (여러 개 가능)", AutoSize = true };
            _rbAll = new RadioButton { Text = "전체 모델에서 찾기 — 모든 파일을 순회 (느림, 마지막 수단)", AutoSize = true };

            _rbAuto.CheckedChanged += (s, e) => UpdateModeUi();
            _rbFiles.CheckedChanged += (s, e) => UpdateModeUi();
            _rbAll.CheckedChanged += (s, e) => UpdateModeUi();

            grpLayout.Controls.Add(_rbAuto);
            grpLayout.Controls.Add(aliasRow);
            grpLayout.Controls.Add(_lblAutoResult);
            grpLayout.Controls.Add(_rbFiles);
            grpLayout.Controls.Add(_rbAll);
            grp.Controls.Add(grpLayout);

            // ---- 파일 트리 ----
            _tree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, HideSelection = false };
            _tree.AfterCheck += Tree_AfterCheck;
            _lblTreeHint = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(600, 0),
                ForeColor = Color.Gray,
                Text = "※ 상위 파일을 체크하면 그 안의 하위 파일도 함께 체크됩니다. 필요 없는 하위 파일(예: 서포트)은 체크를 해제하면 검색에서 제외됩니다.\n" +
                       "※ 초록 굵은 글씨 = 자동 인식이 찾은 파일. 여기서 정한 매핑은 이 문서 이름으로 저장되어 다음부터 묻지 않습니다.",
                Padding = new Padding(0, 4, 0, 4),
            };

            // ---- 버튼 ----
            var btnRow = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Top, AutoSize = true };
            var btnCancel = new Button { Text = "취소", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            var btnOk = new Button { Text = "확인", Width = 90, Height = 28 };
            btnOk.Click += BtnOk_Click;
            btnRow.Controls.Add(btnCancel);
            btnRow.Controls.Add(btnOk);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            layout.Controls.Add(header);
            layout.Controls.Add(grp);
            layout.Controls.Add(_tree);
            layout.Controls.Add(_lblTreeHint);
            layout.Controls.Add(btnRow);
            Controls.Add(layout);
        }

        /// <summary>현재 저장된 선택을 UI에 반영 + 트리 구성.</summary>
        private void LoadCurrent()
        {
            var sel = ScopeMappingService.GetConfig(_doc).Get(_scope.Key);
            _txtAlias.Text = string.Join(", ", _scope.Keywords);
            BuildTree();

            _syncing = true;
            try
            {
                switch (sel.Mode)
                {
                    case ScopeSelectionMode.Files:
                        _rbFiles.Checked = true;
                        var wanted = new HashSet<string>(sel.Files, StringComparer.OrdinalIgnoreCase);
                        foreach (var node in AllNodes(_tree.Nodes))
                            node.Checked = wanted.Contains(((FileNodeInfo)node.Tag).Name);
                        break;
                    case ScopeSelectionMode.AllModels:
                        _rbAll.Checked = true;
                        break;
                    default:
                        _rbAuto.Checked = true;
                        break;
                }
            }
            finally { _syncing = false; }
            RefreshAutoResult();
            UpdateModeUi();
        }

        private void BuildTree()
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            foreach (var n in _nodes)
                _tree.Nodes.Add(MakeNode(n));
            _tree.ExpandAll();
            _tree.EndUpdate();
            if (_nodes.Count == 0)
                _tree.Nodes.Add(new TreeNode("(열린 모델에 파일 노드가 없음)") { ForeColor = Color.Gray });
        }

        private static TreeNode MakeNode(FileNodeInfo info)
        {
            string text = info.Name;
            if (info.IsModelRoot && !string.IsNullOrEmpty(info.AltName)
                && !string.Equals(info.AltName, info.Name, StringComparison.OrdinalIgnoreCase))
                text += $"   ({info.AltName})";
            var node = new TreeNode(text) { Tag = info };
            foreach (var c in info.Children)
                node.Nodes.Add(MakeNode(c));
            return node;
        }

        private static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Tag is FileNodeInfo) yield return n;
                foreach (var c in AllNodes(n.Nodes)) yield return c;
            }
        }

        /// <summary>자동 인식 결과 라벨 + 트리 강조 갱신 (별칭 저장 후에도 호출).</summary>
        private void RefreshAutoResult()
        {
            ScopeResolution auto;
            try { auto = ScopeMappingService.ProbeAuto(_doc, _scope, _nodes); }
            catch (Exception ex) { auto = new ScopeResolution { Note = "인식 실패: " + ex.Message }; }

            _lblAutoResult.Text = auto.IsMapped
                ? $"→ 인식됨: {string.Join(", ", auto.Files)}"
                : $"→ 인식된 파일 없음 (별칭 {_scope.ChainAliasLabel()}) — 아래에서 직접 선택하세요";
            _lblAutoResult.ForeColor = auto.IsMapped ? OkColor : BadColor;

            var autoFiles = new HashSet<string>(auto.Files, StringComparer.OrdinalIgnoreCase);
            foreach (var node in AllNodes(_tree.Nodes))
            {
                bool hit = autoFiles.Contains(((FileNodeInfo)node.Tag).Name);
                node.ForeColor = hit ? OkColor : _tree.ForeColor;
                node.NodeFont = hit ? new Font(_tree.Font, FontStyle.Bold) : null;
            }
        }

        private void UpdateModeUi()
        {
            _txtAlias.Enabled = _btnSaveAlias.Enabled = _rbAuto.Checked;
            _tree.Enabled = _rbFiles.Checked;
            _lblTreeHint.Enabled = _rbFiles.Checked;
        }

        /// <summary>부모 체크 → 하위 전부 동기화 (해제도 동일). 하위만 개별 해제하면 그 파일은 제외.</summary>
        private void Tree_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_syncing || e.Node == null) return;
            _syncing = true;
            try
            {
                foreach (var d in AllNodes(e.Node.Nodes))
                    d.Checked = e.Node.Checked;
                if (e.Node.Checked && !_rbFiles.Checked)
                    _rbFiles.Checked = true;   // 목록을 만졌으면 직접 선택 의도
            }
            finally { _syncing = false; }
        }

        private void BtnSaveAlias_Click(object sender, EventArgs e)
        {
            var aliases = ScopeProfiles.SplitList(_txtAlias.Text);
            _profile.Aliases[_scope.Key] = aliases;
            try
            {
                ScopeMappingStore.SaveProfiles(_profiles);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"별칭 저장 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // 프로파일 객체가 갱신됐으므로 별칭 제공자도 다시 활성화 후 재인식
            ScopeMappingService.ActivateProfile(_doc, _profiles);
            RefreshAutoResult();
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            var sel = new ScopeSelection();
            if (_rbFiles.Checked)
            {
                sel.Mode = ScopeSelectionMode.Files;
                foreach (var node in AllNodes(_tree.Nodes))
                    if (node.Checked) sel.Files.Add(((FileNodeInfo)node.Tag).Name);
                if (sel.Files.Count == 0)
                {
                    MessageBox.Show(this, "파일을 하나 이상 체크하세요.", "모델 파일 지정", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            else if (_rbAll.Checked)
            {
                sel.Mode = ScopeSelectionMode.AllModels;
            }

            try
            {
                ScopeMappingService.SetSelection(_doc, _scope, sel);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"매핑 저장 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Result = ScopeMappingService.Resolve(_doc, _scope, _nodes);
            if (!Result.IsMapped && _rbAuto.Checked)
            {
                var r = MessageBox.Show(this,
                    "자동 인식으로는 파일을 찾지 못했습니다. 이대로 두면 이 공종은 매칭 0건입니다.\n그래도 닫을까요?",
                    "모델 파일 지정", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
