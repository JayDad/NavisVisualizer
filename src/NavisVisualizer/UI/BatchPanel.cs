using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NavisVisualizer.Services;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// 일괄 갱신(배치) 화면 (CLAUDE.md §19) — "아침에 딸각": batch.config에 등록된 프로젝트(모델)를
    /// 체크하고 공종 체크박스를 고른 뒤 [실행]하면 BatchRunner가 모델을 열어 OASIS 최신 데이터로
    /// 색칠하고 다른 이름으로 저장한다. 도크 패널의 탭과 Automation 러너의 독립 창(ShowStandalone)
    /// 양쪽에 같은 컨트롤을 쓴다.
    ///
    /// - 체크 상태(프로젝트·공종)는 [실행] 시점에 batch.config에 되써 넣어 다음 날 그대로 뜬다.
    /// - 이 화면은 MainDockablePanel에 의존하지 않는다 (searcher/엔진은 BatchRunner가 job마다 새로 생성).
    /// - Autodesk 네임스페이스는 import하지 않는다 — WinForms Application/Color와 이름 충돌.
    /// </summary>
    public class BatchPanel : UserControl
    {
        private class JobRow
        {
            public BatchJob Job;
            public CheckBox Enabled;
            public Label ModelLabel;
            public Dictionary<BatchDiscipline, CheckBox> Disciplines = new Dictionary<BatchDiscipline, CheckBox>();
        }

        private static readonly Color OkColor = Color.FromArgb(0, 120, 40);
        private static readonly Color WarnColor = Color.FromArgb(190, 90, 0);
        private static readonly Color ErrColor = Color.Firebrick;

        private BatchConfig _config;
        private readonly List<JobRow> _rows = new List<JobRow>();

        private TableLayoutPanel _jobsHost;
        private DateTimePicker _dtpReference;
        private Label _lblConfigPath;
        private Label _lblOutput;
        private Label _lblStatus;
        private Button _btnRun;
        private Button _btnOpenConfig;
        private Button _btnReload;
        private Button _btnOpenFolder;
        private Button _btnOpenLog;
        private ProgressBar _progress;
        private TextBox _txtResult;
        private ToolTip _tip;

        private bool _running;
        private string _lastOutputFolder;

        /// <summary>독립 창 자동 실행 모드 (러너 인자 "auto") — 현재 문서 교체 확인 없이 바로 실행.</summary>
        public bool AutoRun { get; set; }

        /// <summary>마지막 실행에서 저장에 실패한 job 수 (러너 exe 종료 코드용).</summary>
        public int LastFailedJobs { get; private set; }

        /// <summary>실행이 끝나면 발생 (독립 창의 자동 닫기 등).</summary>
        public event Action RunFinished;

        public BatchPanel()
        {
            InitializeComponent();
            ReloadConfig();
        }

        // ------------------------------------------------------------------
        // 독립 창 (AddInPlugin "NavisVisualizer.Batch" / Automation 러너가 호출)
        // ------------------------------------------------------------------

        /// <summary>
        /// 모달 창으로 띄운다 — ExecuteAddInPlugin이 창을 닫을 때까지 반환하지 않아야 러너가
        /// 처리 중에 Navisworks를 닫지 않는다. 인자: "auto" = 기억된 선택으로 즉시 실행,
        /// "close" = 실행 후 창 자동 닫기 (무인 스케줄용). 반환 = 저장 실패 job 수.
        /// </summary>
        public static int ShowStandalone(string[] args)
        {
            bool auto = HasArg(args, "auto");
            bool close = HasArg(args, "close");

            using (var form = new Form
            {
                Text = "Navis Visualizer — 일괄 갱신",
                Width = 760,
                Height = 660,
                MinimumSize = new Size(560, 480),
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true,        // Navisworks 창 뒤로 숨지 않게 — Shown 후 해제
                MinimizeBox = false,
                ShowInTaskbar = true,
            })
            {
                var panel = new BatchPanel { Dock = DockStyle.Fill, AutoRun = auto };
                form.Controls.Add(panel);
                if (auto && close)
                    panel.RunFinished += () => { try { form.Close(); } catch { } };
                form.Shown += (s, e) =>
                {
                    form.TopMost = false;
                    form.Activate();
                    if (auto) panel.StartRun();
                };
                form.ShowDialog();
                return panel.LastFailedJobs;
            }
        }

        private static bool HasArg(string[] args, string name) =>
            args != null && args.Any(a => a != null &&
                (a.Equals(name, StringComparison.OrdinalIgnoreCase)
                 || a.Equals("--" + name, StringComparison.OrdinalIgnoreCase)
                 || a.Equals("/" + name, StringComparison.OrdinalIgnoreCase)));

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private void InitializeComponent()
        {
            _tip = new ToolTip();

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoScroll = true,
                Padding = new Padding(6),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // 헤더
            var header = new Label
            {
                Text = "체크한 프로젝트의 모델을 열어 OASIS 최신 실적으로 색칠한 뒤 다른 이름으로 저장합니다.\n" +
                       "색은 기본 팔레트(미착수 = 빨강) · 전 단계. 실행 시 선택이 기억되어 다음에 그대로 뜹니다.",
                AutoSize = false,
                Height = 48,          // AutoSize 라벨은 줄바꿈이 안 돼 좁은 도크 패널에서 잘린다
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                Padding = new Padding(0, 0, 0, 6),
            };
            layout.Controls.Add(header);

            // 프로젝트(job) 목록
            _jobsHost = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            _jobsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(_jobsHost);

            // 옵션 행: 기준일 + 저장 폴더
            var optRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
            optRow.Controls.Add(new Label { Text = "기준일", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            _dtpReference = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110, Value = DateTime.Today };
            _tip.SetToolTip(_dtpReference, "Spool/Hydrotest/Equipment/Cable의 단계 판정 기준일 (EIT Tray는 날짜 없음 — 현재 상태).");
            optRow.Controls.Add(_dtpReference);
            _lblOutput = new Label { AutoSize = true, Padding = new Padding(12, 6, 0, 0), ForeColor = Color.DimGray };
            optRow.Controls.Add(_lblOutput);
            layout.Controls.Add(optRow);

            // 버튼 행
            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
            _btnRun = new Button
            {
                Text = "▶ 실행",
                Width = 110,
                Height = 32,
                Font = new Font(Font, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 232, 170),
            };
            _btnRun.Click += (s, e) => StartRun();
            btnRow.Controls.Add(_btnRun);

            _btnOpenConfig = new Button { Text = "설정 파일 열기", Width = 100, Height = 32 };
            _tip.SetToolTip(_btnOpenConfig, "batch.config를 메모장으로 엽니다 (모델 경로·저장 폴더·프로젝트 코드 수정). 수정 후 [다시 읽기].");
            _btnOpenConfig.Click += (s, e) => OpenConfigFile();
            btnRow.Controls.Add(_btnOpenConfig);

            _btnReload = new Button { Text = "다시 읽기", Width = 80, Height = 32 };
            _btnReload.Click += (s, e) => ReloadConfig();
            btnRow.Controls.Add(_btnReload);

            _btnOpenFolder = new Button { Text = "저장 폴더 열기", Width = 100, Height = 32, Enabled = false };
            _btnOpenFolder.Click += (s, e) => OpenFolder(_lastOutputFolder);
            btnRow.Controls.Add(_btnOpenFolder);

            _btnOpenLog = new Button { Text = "배치 로그", Width = 80, Height = 32 };
            _tip.SetToolTip(_btnOpenLog, "실행 이력 (batch.log) — 언제 무엇을 몇 건 칠해 어디에 저장했는지.");
            _btnOpenLog.Click += (s, e) => OpenTextFile(BatchRunner.BatchLogPath);
            btnRow.Controls.Add(_btnOpenLog);
            layout.Controls.Add(btnRow);

            // 진행바 + 상태
            _progress = new ProgressBar { Dock = DockStyle.Fill, Height = 14, Visible = false, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
            layout.Controls.Add(_progress);
            _lblStatus = new Label { AutoSize = false, Height = 36, Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4), ForeColor = Color.Gray, Text = "대기 중" };
            layout.Controls.Add(_lblStatus);

            // 설정 파일 경로
            _lblConfigPath = new Label { AutoSize = false, Height = 18, AutoEllipsis = true, Dock = DockStyle.Fill, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, Font.Size - 0.5f) };
            layout.Controls.Add(_lblConfigPath);

            // 결과
            _txtResult = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Font = new Font("Malgun Gothic", 9f),
                BackColor = SystemColors.Window,
                MinimumSize = new Size(0, 180),
            };
            layout.Controls.Add(_txtResult);

            // 행 높이: 마지막(결과)만 남는 공간 전부. RowCount를 명시해야 Percent 행이 실제로 적용된다.
            layout.RowCount = layout.Controls.Count;
            for (int i = 0; i < layout.Controls.Count; i++)
                layout.RowStyles.Add(new RowStyle(i == layout.Controls.Count - 1 ? SizeType.Percent : SizeType.AutoSize,
                    i == layout.Controls.Count - 1 ? 100 : 0));

            Controls.Add(layout);
        }

        private void BuildJobRows()
        {
            _jobsHost.SuspendLayout();
            _jobsHost.Controls.Clear();
            _jobsHost.RowStyles.Clear();
            _rows.Clear();

            if (_config == null || _config.Jobs.Count == 0)
            {
                var none = new Label
                {
                    Text = "등록된 프로젝트가 없습니다 — [설정 파일 열기]로 batch.config에 [job:이름] 섹션을 추가하세요.",
                    AutoSize = true, ForeColor = ErrColor, Dock = DockStyle.Fill,
                };
                _jobsHost.Controls.Add(none);
                _jobsHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _jobsHost.ResumeLayout();
                return;
            }

            foreach (var job in _config.Jobs)
            {
                var row = new JobRow { Job = job };
                var box = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(0, 2, 0, 4),
                    CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
                };
                box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

                // 1행: ☑ 이름 + 모델 파일명
                var line1 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
                row.Enabled = new CheckBox
                {
                    Text = job.Name,
                    Checked = job.Enabled,
                    AutoSize = true,
                    Font = new Font(Font, FontStyle.Bold),
                };
                line1.Controls.Add(row.Enabled);

                bool exists = false;
                try { exists = !string.IsNullOrWhiteSpace(job.ModelPath) && File.Exists(job.ModelPath); } catch { }
                row.ModelLabel = new Label
                {
                    AutoSize = true,
                    Padding = new Padding(4, 4, 0, 0),
                    ForeColor = exists ? Color.DimGray : ErrColor,
                    Text = exists
                        ? Path.GetFileName(job.ModelPath)
                        : (string.IsNullOrWhiteSpace(job.ModelPath) ? "(model 경로 없음 — 설정 파일 수정)" : "파일 없음: " + job.ModelPath),
                };
                _tip.SetToolTip(row.ModelLabel, job.ModelPath +
                    (string.IsNullOrWhiteSpace(job.ProjectNo) ? "\n(project: oasis.config 기본값)" : "\nproject=" + job.ProjectNo));
                line1.Controls.Add(row.ModelLabel);
                box.Controls.Add(line1);

                // 2행: 공종 체크박스
                var line2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(18, 0, 0, 0) };
                foreach (var d in BatchDisciplineInfo.Ordered)
                {
                    var cb = new CheckBox
                    {
                        Text = BatchDisciplineInfo.Label(d),
                        Checked = job.Disciplines.Contains(d),
                        AutoSize = true,
                        Margin = new Padding(0, 0, 8, 0),
                    };
                    row.Disciplines[d] = cb;
                    line2.Controls.Add(cb);
                }
                box.Controls.Add(line2);
                box.RowCount = 2;
                box.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                box.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                // 공종 체크 없이 job만 켜는 실수 방지: job 체크 시 공종이 하나도 없으면 안내
                row.Enabled.CheckedChanged += (s, e) =>
                {
                    if (row.Enabled.Checked && !row.Disciplines.Values.Any(c => c.Checked))
                        SetStatus($"[{job.Name}] 공종을 하나 이상 체크하세요.", WarnColor);
                };

                _jobsHost.Controls.Add(box);
                _jobsHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _rows.Add(row);
            }
            _jobsHost.RowCount = _rows.Count;
            _jobsHost.ResumeLayout();
        }

        // ------------------------------------------------------------------
        // Config
        // ------------------------------------------------------------------

        private void ReloadConfig()
        {
            try
            {
                _config = BatchConfig.Load();
                _lblConfigPath.Text = "설정: " + _config.SourcePath;
                _lblOutput.Text = "저장 폴더: " + (string.IsNullOrWhiteSpace(_config.OutputFolder)
                    ? "(모델과 같은 폴더)" : _config.OutputFolder) + "  ·  파일명: " + _config.FileNamePattern + ".nwd";
                _tip.SetToolTip(_lblOutput, "batch.config [settings] outputFolder / fileNamePattern");
                SetStatus("대기 중", Color.Gray);
            }
            catch (Exception ex)
            {
                _config = null;
                _lblConfigPath.Text = "설정 읽기 실패: " + ex.Message;
                SetStatus("batch.config를 읽지 못했습니다 — [설정 파일 열기]로 확인", ErrColor);
            }
            BuildJobRows();
        }

        /// <summary>체크 상태를 config에 되써 넣고 저장 — "어제 조합 기억".</summary>
        private void PersistSelection()
        {
            if (_config == null) return;
            foreach (var row in _rows)
            {
                row.Job.Enabled = row.Enabled.Checked;
                row.Job.Disciplines = new HashSet<BatchDiscipline>(
                    row.Disciplines.Where(kv => kv.Value.Checked).Select(kv => kv.Key));
            }
            try { _config.Save(); }
            catch (Exception ex) { ErrorLog.Append("batch.config 저장", ex); }
        }

        private void OpenConfigFile()
        {
            string path = _config?.SourcePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                // Load()가 없으면 템플릿을 만들어 두므로 한 번 더 시도
                try { path = BatchConfig.Load().SourcePath; } catch { path = BatchConfig.UserConfigPath; }
            }
            OpenTextFile(path);
        }

        private static void OpenTextFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    MessageBox.Show("파일이 아직 없습니다:\n" + path, "일괄 갱신", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("파일을 열지 못했습니다:\n" + path + "\n" + ex.Message, "일괄 갱신", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void OpenFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            try { Process.Start("explorer.exe", "\"" + folder + "\""); } catch { }
        }

        // ------------------------------------------------------------------
        // Run
        // ------------------------------------------------------------------

        /// <summary>[실행] — 도크 탭·독립 창·자동 실행 공통 진입.</summary>
        public void StartRun()
        {
            if (_running) return;
            if (_config == null)
            {
                SetStatus("설정을 읽지 못해 실행할 수 없습니다.", ErrColor);
                return;
            }

            var selected = _rows.Where(r => r.Enabled.Checked).ToList();
            if (selected.Count == 0)
            {
                SetStatus("실행할 프로젝트를 체크하세요.", ErrColor);
                return;
            }
            var noDiscipline = selected.FirstOrDefault(r => !r.Disciplines.Values.Any(c => c.Checked));
            if (noDiscipline != null)
            {
                SetStatus($"[{noDiscipline.Job.Name}] 공종을 하나 이상 체크하세요.", ErrColor);
                return;
            }

            PersistSelection();

            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null)
            {
                SetStatus("Navisworks 활성 문서가 없습니다.", ErrColor);
                return;
            }

            // 현재 열린 모델이 교체된다 — 사람이 보고 있는 세션(도크 탭)에서는 확인. 자동 실행은 생략.
            int openModels = 0;
            try { openModels = doc.Models.Count; } catch { }
            if (!AutoRun && openModels > 0)
            {
                var ans = MessageBox.Show(
                    "현재 열려 있는 모델이 배치 모델로 교체됩니다.\n저장하지 않은 변경 사항은 사라집니다.\n\n계속할까요?",
                    "일괄 갱신", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ans != DialogResult.Yes) return;
            }

            RunAll(doc, selected);
        }

        private void RunAll(Autodesk.Navisworks.Api.Document doc, List<JobRow> selected)
        {
            _running = true;
            SetButtons(false);
            _progress.Visible = true;
            _txtResult.Clear();
            _btnOpenFolder.Enabled = false;
            _lastOutputFolder = null;
            LastFailedJobs = 0;

            var runner = new BatchRunner
            {
                Progress = text =>
                {
                    SetStatus(text, Color.Black);
                    Application.DoEvents();
                },
            };
            var results = new List<BatchJobResult>();
            var swAll = Stopwatch.StartNew();
            var referenceDate = _dtpReference.Value.Date;

            try
            {
                foreach (var row in selected)
                {
                    var set = new HashSet<BatchDiscipline>(
                        row.Disciplines.Where(kv => kv.Value.Checked).Select(kv => kv.Key));
                    BatchJobResult r;
                    try
                    {
                        r = runner.RunJob(doc, row.Job, set, referenceDate, _config);
                    }
                    catch (Exception ex)
                    {
                        // RunJob이 내부에서 다 잡지만, 혹시 모를 예외로 나머지 job까지 죽지 않게
                        ErrorLog.Append("배치 job 예외", ex, "job: " + row.Job.Name);
                        r = new BatchJobResult { Job = row.Job, Error = ex.GetType().Name + ": " + ex.Message };
                    }
                    results.Add(r);
                    AppendResult(FormatJob(r));
                    if (r.Saved && _lastOutputFolder == null)
                    {
                        try { _lastOutputFolder = Path.GetDirectoryName(r.OutputPath); } catch { }
                    }
                    Application.DoEvents();
                }
            }
            finally
            {
                _running = false;
                _progress.Visible = false;
                SetButtons(true);
            }

            int saved = results.Count(r => r.Saved);
            LastFailedJobs = results.Count - saved;
            _btnOpenFolder.Enabled = _lastOutputFolder != null;
            string summary = $"완료 — {saved}/{results.Count} 프로젝트 저장 · {swAll.Elapsed.TotalMinutes:0}분 {swAll.Elapsed.Seconds}초 · {DateTime.Now:HH:mm}";
            SetStatus(summary, LastFailedJobs == 0 ? OkColor : (saved > 0 ? WarnColor : ErrColor));
            AppendResult(summary);

            try { RunFinished?.Invoke(); } catch { }
        }

        private static string FormatJob(BatchJobResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"■ {r.Job?.Name}  ({r.TotalMs / 1000.0:0}s)");
            foreach (var s in r.Steps)
            {
                sb.AppendLine("    " + s.Summary());
                if (!string.IsNullOrEmpty(s.ScopeNote))
                    sb.AppendLine("      ※ " + s.ScopeNote);
            }
            sb.AppendLine(r.Saved ? "    저장: " + r.OutputPath : "    ✕ " + r.Error);
            return sb.ToString();
        }

        private void AppendResult(string text)
        {
            _txtResult.AppendText(text.TrimEnd() + Environment.NewLine + Environment.NewLine);
        }

        private void SetStatus(string text, Color color)
        {
            _lblStatus.ForeColor = color;
            _lblStatus.Text = text;
        }

        private void SetButtons(bool enabled)
        {
            _btnRun.Enabled = enabled;
            _btnReload.Enabled = enabled;
            _btnOpenConfig.Enabled = enabled;
            _dtpReference.Enabled = enabled;
            foreach (var row in _rows)
            {
                row.Enabled.Enabled = enabled;
                foreach (var cb in row.Disciplines.Values) cb.Enabled = enabled;
            }
        }
    }
}
