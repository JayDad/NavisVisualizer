using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NavisVisualizer.Loaders;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// 공사(프로젝트) 선택 드롭다운:  공사: [Trion (Q557) ▾] [목록 새로고침]
    ///
    /// **OASIS 로드에만 영향** — Excel import는 파일이 곧 데이터라 공사와 무관하다.
    /// 선택은 **전역 공유**(ProjectContext)라 한 탭에서 바꾸면 전 탭 드롭다운이 같이 바뀐다.
    ///
    /// 목록은 코드 기본값(Trion/Ruya) + oasis.config의 `project.&lt;코드&gt;=&lt;이름&gt;`으로
    /// **DB 없이도 즉시** 채워진다 — DB 연결에 종속시키면 접속 실패 시 아무것도 못 고른다.
    /// [목록 새로고침]은 DB에서 실재 코드를 발견해 병합하는 선택적 보강(이름은 DB에 없음).
    ///
    /// static 이벤트를 구독하므로 Dispose에서 반드시 해제한다 — 안 하면 파괴된 컨트롤이
    /// 계속 불려 ObjectDisposedException이 난다.
    /// </summary>
    public class ProjectSelector : UserControl
    {
        private readonly ComboBox _combo;
        private readonly Button _btnRefresh;
        private readonly ToolTip _tip = new ToolTip();
        private bool _suppressEvent;

        /// <summary>드롭다운에서 "전체"(필터 없음)를 나타내는 항목.</summary>
        private static readonly ProjectInfo AllItem =
            new ProjectInfo { Code = ProjectCatalog.AllProjectsCode, Name = "" };

        /// <summary>
        /// 사용자가 공사를 바꿨을 때 발생 (전역 변경 통지를 받은 경우 포함).
        /// 탭은 이걸 받아 "로드된 OASIS 데이터는 이전 공사 기준"임을 표시한다.
        /// </summary>
        public event EventHandler ProjectChanged;

        public ProjectSelector()
        {
            Height = 28;
            Dock = DockStyle.Fill;

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };

            flow.Controls.Add(new Label
            {
                Text = "공사:",
                AutoSize = true,
                Padding = new Padding(0, 5, 0, 0),
            });

            _combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,   // 직접 입력 금지 — 오타 코드로 0건 로드 방지
                Width = 165,
                Margin = new Padding(3, 2, 3, 0),
            };
            _combo.SelectedIndexChanged += Combo_SelectedIndexChanged;
            flow.Controls.Add(_combo);

            _btnRefresh = new Button
            {
                Text = "목록 새로고침",
                Width = 96,
                Height = 23,
                Margin = new Padding(3, 1, 0, 0),
            };
            _btnRefresh.Click += (s, e) => RefreshFromDatabase();
            flow.Controls.Add(_btnRefresh);

            _tip.SetToolTip(_combo,
                "OASIS 로드에 적용할 공사입니다 (Excel import와는 무관).\n"
                + "전 탭이 같은 공사를 공유하며, 바꾼 뒤에는 [OASIS 로드]를 다시 눌러야 반영됩니다.");
            _tip.SetToolTip(_btnRefresh,
                "DB에 실재하는 공사코드를 조회해 목록에 추가합니다.\n"
                + "공사명은 DB에 없으므로 새 코드는 '(이름 미등록)'으로 표시됩니다 —\n"
                + "이름을 붙이려면 oasis.config에 project.<코드>=<이름> 을 추가하세요.");

            Controls.Add(flow);

            Populate();
            ProjectContext.Changed += OnGlobalProjectChanged;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ProjectContext.Changed -= OnGlobalProjectChanged;
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>다른 탭(또는 목록 병합)에서 전역 선택이 바뀌었을 때 콤보를 맞춘다.</summary>
        private void OnGlobalProjectChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                if (InvokeRequired) { BeginInvoke((Action)Populate); return; }
                Populate();
                ProjectChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (ObjectDisposedException) { /* 탭이 막 닫힘 — 무시 */ }
        }

        /// <summary>현재 카탈로그로 항목을 다시 채우고 현재 선택을 반영한다.</summary>
        private void Populate()
        {
            _suppressEvent = true;
            try
            {
                _combo.BeginUpdate();
                _combo.Items.Clear();
                foreach (var p in ProjectContext.Catalog)
                    _combo.Items.Add(p);
                // "전체"는 맨 아래 — 기본 동선은 특정 공사이고, 전체는 예외적 선택.
                _combo.Items.Add(AllItem);
                _combo.EndUpdate();

                SelectCode(ProjectContext.CurrentCode);
            }
            finally { _suppressEvent = false; }
        }

        private void SelectCode(string code)
        {
            for (int i = 0; i < _combo.Items.Count; i++)
            {
                var item = _combo.Items[i] as ProjectInfo;
                if (item != null && string.Equals(item.Code, code ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    _combo.SelectedIndex = i;
                    return;
                }
            }

            // 목록에 없는 코드(config에만 있고 이름 미등록 등) — 조용히 다른 공사로
            // 바뀐 것처럼 보이면 안 되므로 그 코드를 항목으로 넣고 선택한다.
            if (!string.IsNullOrWhiteSpace(code))
            {
                int idx = _combo.Items.Add(new ProjectInfo { Code = code, Name = "" });
                _combo.SelectedIndex = idx;
            }
            else if (_combo.Items.Count > 0)
            {
                _combo.SelectedIndex = _combo.Items.Count - 1;   // "전체"
            }
        }

        private void Combo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressEvent) return;
            var item = _combo.SelectedItem as ProjectInfo;
            if (item == null) return;
            // 전역에 알린다 → ProjectContext.Changed → 다른 탭의 셀렉터도 같이 갱신되고
            // 이 컨트롤의 OnGlobalProjectChanged가 ProjectChanged를 발생시킨다.
            ProjectContext.SetCurrent(item.Code);
        }

        /// <summary>
        /// DB에서 실재 공사코드를 조회해 목록에 병합. 연결 실패는 여기서 치명적이지 않다 —
        /// 기존 목록은 그대로 쓸 수 있으므로 안내만 하고 되돌아간다.
        /// </summary>
        private void RefreshFromDatabase()
        {
            Cursor prev = Cursor.Current;
            _btnRefresh.Enabled = false;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                var settings = SqlConnectionSettings.Load();
                var codes = SqlLoader.LoadProjectCodes(settings, out string sourceNote);
                if (codes.Count == 0)
                {
                    MessageBox.Show(this,
                        "DB에서 공사코드를 찾지 못했습니다.\n" + sourceNote,
                        "목록 새로고침", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int added = ProjectContext.MergeDiscovered(codes);
                var unnamed = ProjectContext.Catalog
                    .Where(p => p.NameUnknown)
                    .Select(p => p.Code)
                    .ToList();

                string msg = $"DB 공사코드 {codes.Count}개 조회 ({sourceNote}).\n"
                           + $"목록에 새로 추가: {added}개";
                if (unnamed.Count > 0)
                    msg += "\n\n이름 미등록: " + string.Join(", ", unnamed)
                         + "\noasis.config에 다음 형식으로 추가하면 이름이 표시됩니다:\n"
                         + $"  project.{unnamed[0]}=공사명";

                MessageBox.Show(this, msg, "목록 새로고침",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"공사 목록 조회 실패:\n{ex.Message}\n\n기존 목록은 그대로 사용할 수 있습니다.",
                    "목록 새로고침", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Cursor.Current = prev;
                _btnRefresh.Enabled = true;
            }
        }
    }
}
