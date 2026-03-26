using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;

namespace NavisVisualizer.UI
{
    public class HydrotestTab : UserControl
    {
        private readonly MainDockablePanel _main;

        private List<TestPackageData> _packages = new List<TestPackageData>();
        private Dictionary<HydrotestStatus, ColorSetting> _colorSettings;

        private Button _btnLoad;
        private Label _lblFile;
        private ListView _listView;
        private Button _btnApply;
        private Button _btnReset;
        private Button _btnViewpoint;
        private Button _btnNwd;
        private Label _lblStats;
        private ProgressBar _progressBar;

        private Dictionary<HydrotestStatus, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<HydrotestStatus, (Panel, Button, ComboBox, CheckBox)>();

        public HydrotestTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.HydrotestDefaults);
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

            _btnLoad = new Button { Text = "📂 Hydrotest Excel 로드", Dock = DockStyle.Fill, Height = 30 };
            _btnLoad.Click += BtnLoad_Click;
            _lblFile = new Label { Text = "(파일 없음)", Dock = DockStyle.Fill, ForeColor = Color.Gray, AutoSize = false, Height = 18 };

            var colorPanel = BuildColorPanel();

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                Height = 200,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details,
            };
            _listView.Columns.Add("PKG ID", 100);
            _listView.Columns.Add("상태", 80);
            _listView.Columns.Add("Spool 수", 60);
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 65, AutoSize = true };
            _btnApply     = new Button { Text = "적용",           Width = 80  };
            _btnReset     = new Button { Text = "전체 초기화",    Width = 90  };
            _btnViewpoint = new Button { Text = "📷 Viewpoint 저장", Width = 120 };
            _btnNwd       = new Button { Text = "💾 NWD Export",  Width = 110 };
            _btnApply.Click     += BtnApply_Click;
            _btnReset.Click     += BtnReset_Click;
            _btnViewpoint.Click += BtnViewpoint_Click;
            _btnNwd.Click       += BtnNwd_Click;
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnReset, _btnViewpoint, _btnNwd });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };
            _lblStats    = new Label       { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false, Height = 18 };

            layout.Controls.Add(_btnLoad);
            layout.Controls.Add(_lblFile);
            layout.Controls.Add(new Label { Text = "상태 & 색상", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanel);
            layout.Controls.Add(new Label { Text = "PKG 목록",   Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(_listView);
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(_lblStats);

            Controls.Add(layout);
        }

        private Panel BuildColorPanel()
        {
            var panel  = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

            var statuses = new[] { HydrotestStatus.NotStarted, HydrotestStatus.Completed, HydrotestStatus.Recovery };
            var labels   = new[] { "미착수", "완료", "복구" };

            for (int i = 0; i < statuses.Length; i++)
            {
                var status  = statuses[i];
                var setting = _colorSettings[status];

                var chk            = new CheckBox { Text = labels[i], Checked = true, AutoSize = true };
                var colorBox       = new Panel    { Width = 36, Height = 20, BackColor = setting.DisplayColor, BorderStyle = BorderStyle.FixedSingle };
                var colorBtn       = new Button   { Text = "▼", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
                colorBtn.FlatAppearance.BorderSize = 0;
                var transparencyBox = new ComboBox { Width = 65, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%", "100%" })
                    transparencyBox.Items.Add(t);
                transparencyBox.Text = $"{(int)(setting.Transparency * 100)}%";

                var capturedStatus = status;
                colorBtn.Click += (s, e) =>
                {
                    using (var dlg = new ColorDialog { Color = _colorSettings[capturedStatus].DisplayColor })
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            _colorSettings[capturedStatus].DisplayColor = dlg.Color;
                            colorBox.BackColor = dlg.Color;
                        }
                    }
                };
                transparencyBox.SelectedIndexChanged += (s, e) =>
                {
                    if (double.TryParse(transparencyBox.Text.Replace("%", ""), out double pct))
                        _colorSettings[capturedStatus].Transparency = pct / 100.0;
                };

                _colorRows[status] = (colorBox, colorBtn, transparencyBox, chk);
                layout.Controls.Add(chk);
                layout.Controls.Add(colorBox);
                layout.Controls.Add(colorBtn);
                layout.Controls.Add(transparencyBox);
            }

            panel.Controls.Add(layout);
            return panel;
        }

        private void BtnLoad_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Title = "Hydrotest Excel 로드", Filter = "Excel (*.xlsx)|*.xlsx" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    _packages = ExcelLoader.LoadHydrotest(dlg.FileName);
                    _lblFile.Text = Path.GetFileName(dlg.FileName);
                    RefreshList();
                    UpdateStats();
                    if (!_main.Searcher.IsIndexBuilt)
                        BuildIndex();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Excel 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BuildIndex()
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _progressBar.Visible = true;
            _main.Searcher.BuildIndex(doc, (current, total) =>
            {
                if (total > 0)
                {
                    _progressBar.Value = Math.Min((int)((double)current / total * 100), 100);
                    Application.DoEvents();
                }
            });
            _progressBar.Visible = false;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null || _packages.Count == 0)
            {
                MessageBox.Show("Excel을 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (!_main.Searcher.IsIndexBuilt)
                BuildIndex();

            var activeSettings = new Dictionary<HydrotestStatus, ColorSetting>();
            foreach (var kv in _colorRows)
                if (kv.Value.check.Checked)
                    activeSettings[kv.Key] = _colorSettings[kv.Key];

            var result = _main.OverrideEngine.ApplyHydrotest(doc, _packages, activeSettings);
            UpdateStats(result.MatchedCount, result.UnmatchedCount, result.UnmatchedIds);
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            _main.OverrideEngine.Reset(doc);
            _lblStats.Text = "전체 초기화 완료";
        }

        private void BtnViewpoint_Click(object sender, EventArgs e)
        {
            var doc = _main.GetDocument();
            if (doc == null) return;
            string name = $"Hydrotest_{DateTime.Now:yyyyMMdd_HHmm}";
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

        private void ListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;
            var tag = _listView.SelectedItems[0].Tag as TestPackageData;
            if (tag == null || tag.SpoolIds.Count == 0) return;

            var doc = _main.GetDocument();
            if (doc == null || !_main.Searcher.IsIndexBuilt) return;

            var found = _main.Searcher.FindBySpoolIds(tag.SpoolIds);
            var items = found.Values.SelectMany(v => v).ToList();
            if (items.Count == 0) return;

            var collection = new Autodesk.Navisworks.Api.ModelItemCollection();
            collection.AddRange(items);
            doc.CurrentSelection.CopyFrom(collection);
        }

        private void RefreshList()
        {
            _listView.Items.Clear();
            var statusLabels = new Dictionary<HydrotestStatus, string>
            {
                [HydrotestStatus.NotStarted] = "미착수",
                [HydrotestStatus.Completed]  = "완료",
                [HydrotestStatus.Recovery]   = "복구",
            };

            foreach (var pkg in _packages)
            {
                var item = new ListViewItem(pkg.TestPkgId);
                item.SubItems.Add(statusLabels[pkg.Status]);
                item.SubItems.Add(pkg.SpoolIds.Count.ToString());
                item.Tag = pkg;
                item.ForeColor = _colorSettings[pkg.Status].DisplayColor;
                _listView.Items.Add(item);
            }
        }

        private void UpdateStats(int matched = 0, int unmatched = 0, List<string> unmatchedIds = null)
        {
            int completed  = _packages.Count(p => p.Status == HydrotestStatus.Completed);
            int notStarted = _packages.Count(p => p.Status == HydrotestStatus.NotStarted);
            int recovery   = _packages.Count(p => p.Status == HydrotestStatus.Recovery);
            _lblStats.Text = $"완료 {completed}  미착수 {notStarted}  복구 {recovery}"
                           + (matched > 0 ? $"  | 매칭 {matched} / 미매칭 {unmatched}" : "");
        }

        private Dictionary<HydrotestStatus, ColorSetting> CloneDefaults(Dictionary<HydrotestStatus, ColorSetting> defaults)
        {
            var clone = new Dictionary<HydrotestStatus, ColorSetting>();
            foreach (var kv in defaults)
                clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
