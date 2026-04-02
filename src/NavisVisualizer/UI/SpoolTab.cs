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
    public class SpoolTab : UserControl
    {
        private readonly MainDockablePanel _main;

        private List<SpoolData> _spools = new List<SpoolData>();
        private Dictionary<SpoolStage, ColorSetting> _colorSettings;

        private Button   _btnLoad;
        private Label    _lblFile;
        private DateTimePicker _dtpReference;
        private TextBox  _txtSearch;
        private ListView _listView;
        private Button   _btnApply;
        private Button   _btnReset;
        private Button   _btnViewpoint;
        private Button   _btnNwd;
        private Label    _lblStats;
        private ProgressBar _progressBar;

        private Dictionary<SpoolStage, (Panel colorBox, Button colorBtn, ComboBox transparencyBox, CheckBox check)> _colorRows
            = new Dictionary<SpoolStage, (Panel, Button, ComboBox, CheckBox)>();

        public SpoolTab(MainDockablePanel main)
        {
            _main = main;
            _colorSettings = CloneDefaults(ColorSetting.SpoolDefaults);
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

            _btnLoad = new Button { Text = "Spool Excel", Dock = DockStyle.Fill, Height = 30 };
            _btnLoad.Click += BtnLoad_Click;
            _lblFile  = new Label { Text = "(파일 없음)", Dock = DockStyle.Fill, ForeColor = Color.Gray, AutoSize = false, Height = 18 };

            // Reference date picker
            var datePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, AutoSize = false };
            var dateLabel = new Label { Text = "기준일:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
            _dtpReference = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today,
                Width = 110,
            };
            _dtpReference.ValueChanged += (s, e) =>
            {
                if (_spools.Count > 0) FilterList();
            };
            datePanel.Controls.Add(dateLabel);
            datePanel.Controls.Add(_dtpReference);

            var colorPanel = BuildColorPanel();

            _txtSearch = new TextBox { Dock = DockStyle.Fill, Text = "" };
            _txtSearch.TextChanged += (s, e) => FilterList();

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                Height = 200,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details,
            };
            _listView.Columns.Add("Spool ID", 140);
            _listView.Columns.Add("ISO No", 120);
            _listView.Columns.Add("단계", 80);
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 65, AutoSize = true };
            _btnApply     = new Button { Text = "적용",              Width = 80  };
            _btnReset     = new Button { Text = "전체 초기화",       Width = 90  };
            _btnViewpoint = new Button { Text = "Viewpoint 저장",    Width = 120 };
            _btnNwd       = new Button { Text = "NWD Export",        Width = 110 };
            _btnApply.Click     += BtnApply_Click;
            _btnReset.Click     += BtnReset_Click;
            _btnViewpoint.Click += BtnViewpoint_Click;
            _btnNwd.Click       += BtnNwd_Click;
            btnPanel.Controls.AddRange(new Control[] { _btnApply, _btnReset, _btnViewpoint, _btnNwd });

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false };
            _lblStats    = new Label       { Dock = DockStyle.Fill, Text = "로드된 데이터 없음", AutoSize = false, Height = 36 };

            layout.Controls.Add(_btnLoad);
            layout.Controls.Add(_lblFile);
            layout.Controls.Add(datePanel);
            layout.Controls.Add(new Label { Text = "Fabrication", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanel.fabPanel);
            layout.Controls.Add(new Label { Text = "Install", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(colorPanel.instPanel);
            layout.Controls.Add(_txtSearch);
            layout.Controls.Add(new Label { Text = "Spool 목록", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 18 });
            layout.Controls.Add(_listView);
            layout.Controls.Add(btnPanel);
            layout.Controls.Add(_progressBar);
            layout.Controls.Add(_lblStats);

            Controls.Add(layout);
        }

        private (Panel fabPanel, Panel instPanel) BuildColorPanel()
        {
            var fabPanel = BuildStageColorPanel(
                new[] { SpoolStage.NotStarted }.Concat(SpoolStageInfo.FabricationStages).ToArray());
            var instPanel = BuildStageColorPanel(SpoolStageInfo.InstallStages);
            return (fabPanel, instPanel);
        }

        private Panel BuildStageColorPanel(SpoolStage[] stages)
        {
            var panel  = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

            foreach (var stage in stages)
            {
                var setting = _colorSettings[stage];
                string label = SpoolStageInfo.Labels[stage];

                var chk             = new CheckBox { Text = label, Checked = true, AutoSize = true };
                var colorBox        = new Panel    { Width = 36, Height = 20, BackColor = setting.DisplayColor, BorderStyle = BorderStyle.FixedSingle };
                var colorBtn        = new Button   { Text = "▼", Width = 22, Height = 20, FlatStyle = FlatStyle.Flat };
                colorBtn.FlatAppearance.BorderSize = 0;
                var transparencyBox = new ComboBox { Width = 65, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var t in new[] { "0%", "20%", "40%", "60%", "70%", "80%", "90%", "100%" })
                    transparencyBox.Items.Add(t);
                transparencyBox.Text = $"{(int)(setting.Transparency * 100)}%";

                var capturedStage = stage;
                colorBtn.Click += (s, e) =>
                {
                    using (var dlg = new ColorDialog { Color = _colorSettings[capturedStage].DisplayColor })
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            _colorSettings[capturedStage].DisplayColor = dlg.Color;
                            colorBox.BackColor = dlg.Color;
                        }
                    }
                };
                transparencyBox.SelectedIndexChanged += (s, e) =>
                {
                    if (double.TryParse(transparencyBox.Text.Replace("%", ""), out double pct))
                        _colorSettings[capturedStage].Transparency = pct / 100.0;
                };

                _colorRows[stage] = (colorBox, colorBtn, transparencyBox, chk);
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
            using (var dlg = new OpenFileDialog { Title = "Spool Excel 로드", Filter = "Excel (*.xlsx)|*.xlsx" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    _spools = ExcelLoader.LoadSpool(dlg.FileName);
                    _lblFile.Text = Path.GetFileName(dlg.FileName);
                    FilterList();
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
            if (doc == null || _spools.Count == 0)
            {
                MessageBox.Show("Excel을 먼저 로드하고 모델을 열어주세요.");
                return;
            }
            if (!_main.Searcher.IsIndexBuilt)
                BuildIndex();

            var activeSettings = new Dictionary<SpoolStage, ColorSetting>();
            foreach (var kv in _colorRows)
                if (kv.Value.check.Checked)
                    activeSettings[kv.Key] = _colorSettings[kv.Key];

            var referenceDate = _dtpReference.Value;
            var result = _main.OverrideEngine.ApplySpool(doc, _spools, activeSettings, referenceDate);
            UpdateStats(result.MatchedCount, result.UnmatchedCount);
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
            string name = $"Spool_{DateTime.Now:yyyyMMdd_HHmm}";
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
            var tag = _listView.SelectedItems[0].Tag as SpoolData;
            if (tag == null) return;

            var doc = _main.GetDocument();
            if (doc == null || !_main.Searcher.IsIndexBuilt) return;

            var found = _main.Searcher.FindBySpoolIds(new[] { tag.SpoolId });
            var items = found.Values.SelectMany(v => v).ToList();
            if (items.Count == 0) return;

            var collection = new Autodesk.Navisworks.Api.ModelItemCollection();
            collection.AddRange(items);
            doc.CurrentSelection.CopyFrom(collection);
            doc.ActiveView.FocusOnCurrentSelection();
        }

        private void FilterList()
        {
            string keyword = _txtSearch?.Text?.Trim().ToUpperInvariant() ?? "";
            var referenceDate = _dtpReference.Value;

            var filtered = string.IsNullOrEmpty(keyword)
                ? _spools
                : _spools.Where(s => s.SpoolId.ToUpperInvariant().Contains(keyword)
                    || (s.IsoNo ?? "").ToUpperInvariant().Contains(keyword)).ToList();

            _listView.Items.Clear();

            foreach (var spool in filtered)
            {
                var stage = spool.GetStageAtDate(referenceDate);
                string stageLabel = SpoolStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();

                var item = new ListViewItem(spool.SpoolId);
                item.SubItems.Add(spool.IsoNo ?? "");
                item.SubItems.Add(stageLabel);
                item.Tag = spool;
                if (_colorSettings.TryGetValue(stage, out var setting))
                    item.ForeColor = setting.DisplayColor;
                _listView.Items.Add(item);
            }
        }

        private void UpdateStats(int matched = 0, int unmatched = 0)
        {
            var referenceDate = _dtpReference.Value;
            var counts = _spools
                .GroupBy(s => s.GetStageAtDate(referenceDate))
                .ToDictionary(g => g.Key, g => g.Count());

            var parts = new List<string>();
            // Show counts in reverse order (most progressed first)
            var allStages = new[] { SpoolStage.NotStarted }.Concat(SpoolStageInfo.OrderedStages).Reverse();
            foreach (var stage in allStages)
            {
                if (counts.TryGetValue(stage, out int cnt) && cnt > 0)
                    parts.Add($"{SpoolStageInfo.Labels[stage]} {cnt}");
            }

            _lblStats.Text = string.Join("  ", parts)
                           + (matched > 0 ? $"\n매칭 {matched} / 미매칭 {unmatched}" : "");
        }

        private Dictionary<SpoolStage, ColorSetting> CloneDefaults(Dictionary<SpoolStage, ColorSetting> defaults)
        {
            var clone = new Dictionary<SpoolStage, ColorSetting>();
            foreach (var kv in defaults)
                clone[kv.Key] = kv.Value.Clone();
            return clone;
        }
    }
}
