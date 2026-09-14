using System;
using System.Drawing;
using System.Windows.Forms;
using NavisVisualizer.Loaders;

namespace NavisVisualizer.UI
{
    public enum TabDataSource
    {
        Excel,
        Oasis,
    }

    /// <summary>
    /// 탭 상단의 데이터 소스 블록:
    ///   [Excel Import] [Template 출력]  ● 파일명 · N건
    ///   [OASIS 로드]                    ○ (미로드)
    ///   공사: [Trion (Q557) ▾] [목록 새로고침]
    ///   적용 기준: (•)Excel ( )OASIS   [비교 출력]
    ///
    /// Excel 버튼 문구는 전 탭 "Excel Import"로 통일 (탭 이름이 공종을 이미 말해줌).
    /// 라디오는 로드된 소스만 활성화되고 첫 로드 시 자동 선택된다(이벤트 미발생).
    /// 비교 버튼은 두 소스가 모두 로드됐을 때만 활성화. 데이터 자체는 탭이 보유하고
    /// 이 컨트롤은 상태 표시 + 이벤트만 담당한다.
    ///
    /// 공사 드롭다운은 OASIS 로드 행 바로 아래 — OASIS 전용이라는 게 배치로 드러나게 한다
    /// (Excel import는 파일이 곧 데이터라 공사와 무관). 공사를 바꾸면 이미 로드된 OASIS
    /// 데이터는 이전 공사 기준이므로 상태 라벨이 "재로드 필요"로 바뀐다 — 자동 재로드는
    /// 하지 않는다(네트워크 대기로 UI가 수 초 멈추는 것을 사용자 의도 없이 일으키지 않음, §6).
    /// </summary>
    public class DataSourcePanel : UserControl
    {
        public event EventHandler ExcelLoadClicked;
        public event EventHandler OasisLoadClicked;
        /// <summary>공사(전역)가 바뀌었을 때 — 탭이 3D 적용 상태를 stale로 표시하는 데 쓴다.</summary>
        public event EventHandler ProjectChanged;
        /// <summary>사용자가 라디오로 적용 기준을 바꿨을 때만 발생 (자동 선택 시 미발생).</summary>
        public event EventHandler ActiveSourceChanged;
        public event EventHandler CompareClicked;
        /// <summary>Input Template(입력 양식) 출력 버튼 — 공종별 양식 생성은 탭이 담당.</summary>
        public event EventHandler TemplateClicked;

        private Button _btnExcel;
        private Button _btnTemplate;
        private Button _btnOasis;
        private Label _dotExcel;
        private Label _dotOasis;
        private Label _lblExcel;
        private Label _lblOasis;
        private RadioButton _rdoExcel;
        private RadioButton _rdoOasis;
        private Button _btnCompare;
        private ProjectSelector _projectSelector;

        private bool _excelLoaded;
        private bool _oasisLoaded;
        private bool _suppressRadioEvent;

        /// <summary>OASIS를 로드했을 때의 공사코드 — 이후 공사가 바뀌면 stale 판정 기준.</summary>
        private string _oasisProjectCode = "";
        /// <summary>OASIS 상태 라벨 원문 (stale 표시를 걷어낼 때 복원).</summary>
        private string _oasisLabelText = "";

        private static readonly Color DotLoaded = Color.FromArgb(0, 160, 60);
        private static readonly Color DotEmpty = Color.FromArgb(170, 170, 170);
        private static readonly Color DotFailed = Color.FromArgb(200, 40, 40);
        // 공사 전환으로 "이전 공사 기준" 상태 — 실패(빨강)와 구분되는 주황
        // (ApplyStatePanel의 stale 색과 동일 언어).
        private static readonly Color DotStale = Color.FromArgb(190, 90, 0);

        public TabDataSource ActiveSource =>
            _rdoOasis.Checked ? TabDataSource.Oasis : TabDataSource.Excel;

        public bool IsLoaded(TabDataSource src) =>
            src == TabDataSource.Excel ? _excelLoaded : _oasisLoaded;

        /// <summary>
        /// 로드된 OASIS 데이터가 현재 선택된 공사와 다른 공사 기준인가.
        /// 미로드면 false — 아직 아무 공사 기준도 아니므로 어긋날 것이 없다.
        /// </summary>
        public bool IsOasisProjectStale =>
            _oasisLoaded && !string.Equals(_oasisProjectCode, ProjectContext.CurrentCode,
                StringComparison.OrdinalIgnoreCase);

        public DataSourcePanel()
        {
            Height = 114;   // 공사 선택 행(28) 추가분 포함
            Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 4,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            _btnExcel = new Button { Text = "Excel Import", Dock = DockStyle.Fill, Height = 24 };
            _btnExcel.Click += (s, e) => ExcelLoadClicked?.Invoke(this, EventArgs.Empty);
            _btnTemplate = new Button { Text = "Template 출력", Dock = DockStyle.Fill, Height = 24 };
            _btnTemplate.Click += (s, e) => TemplateClicked?.Invoke(this, EventArgs.Empty);
            _dotExcel = MakeDot();
            _lblExcel = MakeStatusLabel();

            _btnOasis = new Button { Text = "OASIS 로드", Dock = DockStyle.Fill, Height = 24 };
            _btnOasis.Click += (s, e) => OasisLoadClicked?.Invoke(this, EventArgs.Empty);
            _dotOasis = MakeDot();
            _lblOasis = MakeStatusLabel();

            var activePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            activePanel.Controls.Add(new Label
            {
                Text = "적용 기준:",
                AutoSize = true,
                Padding = new Padding(0, 5, 0, 0),
            });
            _rdoExcel = new RadioButton { Text = "Excel", AutoSize = true, Enabled = false, Checked = true };
            _rdoOasis = new RadioButton { Text = "OASIS", AutoSize = true, Enabled = false };
            _rdoExcel.CheckedChanged += Radio_CheckedChanged;
            _rdoOasis.CheckedChanged += Radio_CheckedChanged;
            activePanel.Controls.Add(_rdoExcel);
            activePanel.Controls.Add(_rdoOasis);

            _btnCompare = new Button
            {
                Text = "비교 출력",
                Width = 80,
                Height = 23,
                Enabled = false,
                Margin = new Padding(12, 1, 0, 0),
            };
            _btnCompare.Click += (s, e) => CompareClicked?.Invoke(this, EventArgs.Empty);
            activePanel.Controls.Add(_btnCompare);

            // 공사 선택 — OASIS 행 바로 아래에 두어 "OASIS에만 적용"이 배치로 읽히게 한다.
            _projectSelector = new ProjectSelector();
            _projectSelector.ProjectChanged += (s, e) =>
            {
                MarkOasisProjectStale();
                ProjectChanged?.Invoke(this, EventArgs.Empty);
            };

            layout.Controls.Add(_btnExcel, 0, 0);
            layout.Controls.Add(_btnTemplate, 1, 0);
            layout.Controls.Add(_dotExcel, 2, 0);
            layout.Controls.Add(_lblExcel, 3, 0);
            layout.Controls.Add(_btnOasis, 0, 1);
            layout.Controls.Add(_dotOasis, 2, 1);
            layout.Controls.Add(_lblOasis, 3, 1);
            layout.Controls.Add(_projectSelector, 0, 2);
            layout.SetColumnSpan(_projectSelector, 4);
            layout.Controls.Add(activePanel, 0, 3);
            layout.SetColumnSpan(activePanel, 4);

            Controls.Add(layout);

            SetNotLoaded(TabDataSource.Excel);
            SetNotLoaded(TabDataSource.Oasis);
        }

        private static Label MakeDot() => new Label
        {
            Text = "●",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = DotEmpty,
        };

        private static Label MakeStatusLabel() => new Label
        {
            Text = "",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray,
        };

        private void Radio_CheckedChanged(object sender, EventArgs e)
        {
            var rdo = (RadioButton)sender;
            if (!rdo.Checked) return; // 해제되는 쪽 이벤트는 무시
            if (_suppressRadioEvent) return;
            ActiveSourceChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>로드 성공 표시. 첫 로드 소스는 자동으로 적용 기준이 된다(이벤트 미발생).</summary>
        public void SetLoaded(TabDataSource src, int count, string detail)
        {
            string text = $"{count:N0}건 · {detail}";
            if (src == TabDataSource.Excel)
            {
                _excelLoaded = true;
                _dotExcel.ForeColor = DotLoaded;
                _lblExcel.Text = text;
                _lblExcel.ForeColor = Color.Black;
                _rdoExcel.Enabled = true;
            }
            else
            {
                _oasisLoaded = true;
                _dotOasis.ForeColor = DotLoaded;
                _lblOasis.Text = text;
                _lblOasis.ForeColor = Color.Black;
                _rdoOasis.Enabled = true;
                // 이 데이터가 어느 공사 기준인지 기록 — 이후 공사가 바뀌면 stale 표시.
                _oasisProjectCode = ProjectContext.CurrentCode;
                _oasisLabelText = text;
            }

            // 반대쪽이 미로드면 이 소스를 자동 선택 (사용자 액션이 아니므로 이벤트 억제)
            var rdo = src == TabDataSource.Excel ? _rdoExcel : _rdoOasis;
            var other = src == TabDataSource.Excel ? _oasisLoaded : _excelLoaded;
            if (!other && !rdo.Checked)
            {
                _suppressRadioEvent = true;
                rdo.Checked = true;
                _suppressRadioEvent = false;
            }

            _btnCompare.Enabled = _excelLoaded && _oasisLoaded;
        }

        public void SetFailed(TabDataSource src, string message)
        {
            if (src == TabDataSource.Excel)
            {
                _dotExcel.ForeColor = DotFailed;
                _lblExcel.Text = message;
                _lblExcel.ForeColor = DotFailed;
            }
            else
            {
                _dotOasis.ForeColor = DotFailed;
                _lblOasis.Text = message;
                _lblOasis.ForeColor = DotFailed;
            }
        }

        /// <summary>
        /// 공사가 바뀌었는데 OASIS 데이터가 이미 로드돼 있으면 "이전 공사 기준"임을 알린다.
        /// 데이터를 지우지는 않는다 — 지우면 되돌릴 방법이 없고, 사용자가 공사를 잘못
        /// 눌렀다 되돌린 경우 멀쩡한 데이터를 잃기 때문. 되돌아오면 표시도 원복된다.
        /// </summary>
        private void MarkOasisProjectStale()
        {
            if (!_oasisLoaded) return;

            bool sameProject = string.Equals(_oasisProjectCode, ProjectContext.CurrentCode,
                StringComparison.OrdinalIgnoreCase);
            if (sameProject)
            {
                _dotOasis.ForeColor = DotLoaded;
                _lblOasis.ForeColor = Color.Black;
                _lblOasis.Text = _oasisLabelText;
                return;
            }

            string loadedAs = ProjectCatalog.DisplayFor(ProjectContext.Catalog, _oasisProjectCode);
            _dotOasis.ForeColor = DotStale;
            _lblOasis.ForeColor = DotStale;
            _lblOasis.Text = $"⚠ {loadedAs} 기준 · [OASIS 로드] 재실행 필요";
        }

        private void SetNotLoaded(TabDataSource src)
        {
            if (src == TabDataSource.Excel)
            {
                _dotExcel.ForeColor = DotEmpty;
                _lblExcel.Text = "(미로드)";
                _lblExcel.ForeColor = Color.Gray;
            }
            else
            {
                _dotOasis.ForeColor = DotEmpty;
                _lblOasis.Text = "(미로드)";
                _lblOasis.ForeColor = Color.Gray;
            }
        }
    }
}
