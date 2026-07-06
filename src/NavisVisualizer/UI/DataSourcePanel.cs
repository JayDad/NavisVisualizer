using System;
using System.Drawing;
using System.Windows.Forms;

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
    ///   적용 기준: (•)Excel ( )OASIS   [비교 출력]
    ///
    /// Excel 버튼 문구는 전 탭 "Excel Import"로 통일 (탭 이름이 공종을 이미 말해줌).
    /// 라디오는 로드된 소스만 활성화되고 첫 로드 시 자동 선택된다(이벤트 미발생).
    /// 비교 버튼은 두 소스가 모두 로드됐을 때만 활성화. 데이터 자체는 탭이 보유하고
    /// 이 컨트롤은 상태 표시 + 이벤트만 담당한다.
    /// </summary>
    public class DataSourcePanel : UserControl
    {
        public event EventHandler ExcelLoadClicked;
        public event EventHandler OasisLoadClicked;
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

        private bool _excelLoaded;
        private bool _oasisLoaded;
        private bool _suppressRadioEvent;

        private static readonly Color DotLoaded = Color.FromArgb(0, 160, 60);
        private static readonly Color DotEmpty = Color.FromArgb(170, 170, 170);
        private static readonly Color DotFailed = Color.FromArgb(200, 40, 40);

        public TabDataSource ActiveSource =>
            _rdoOasis.Checked ? TabDataSource.Oasis : TabDataSource.Excel;

        public bool IsLoaded(TabDataSource src) =>
            src == TabDataSource.Excel ? _excelLoaded : _oasisLoaded;

        public DataSourcePanel()
        {
            Height = 86;
            Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 3,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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

            layout.Controls.Add(_btnExcel, 0, 0);
            layout.Controls.Add(_btnTemplate, 1, 0);
            layout.Controls.Add(_dotExcel, 2, 0);
            layout.Controls.Add(_lblExcel, 3, 0);
            layout.Controls.Add(_btnOasis, 0, 1);
            layout.Controls.Add(_dotOasis, 2, 1);
            layout.Controls.Add(_lblOasis, 3, 1);
            layout.Controls.Add(activePanel, 0, 2);
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
