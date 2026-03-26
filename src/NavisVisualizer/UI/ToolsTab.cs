using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NavisVisualizer.UI
{
    public class ToolsTab : UserControl
    {
        private readonly MainDockablePanel _main;
        private Button _btnDump;
        private Label _lblStatus;

        public ToolsTab(MainDockablePanel main)
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

            layout.Controls.Add(new Label
            {
                Text = "Property Dumper",
                Font = new Font(Font, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Height = 20
            });

            layout.Controls.Add(new Label
            {
                Text = "1. Navisworks에서 아이템 선택\n2. 아래 버튼 클릭\n3. 바탕화면에 CSV 저장됨",
                Dock = DockStyle.Fill,
                Height = 50,
                ForeColor = Color.Gray
            });

            _btnDump = new Button
            {
                Text = "선택 아이템 속성 덤프 (CSV)",
                Dock = DockStyle.Fill,
                Height = 35
            };
            _btnDump.Click += BtnDump_Click;

            _lblStatus = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                Height = 18,
                AutoSize = false
            };

            layout.Controls.Add(_btnDump);
            layout.Controls.Add(_lblStatus);

            Controls.Add(layout);
        }

        private void BtnDump_Click(object sender, EventArgs e)
        {
            try
            {
                var doc = _main.GetDocument();
                if (doc == null)
                {
                    MessageBox.Show("No document open.");
                    return;
                }

                var selection = doc.CurrentSelection.SelectedItems;
                if (selection.Count == 0)
                {
                    MessageBox.Show("Select items first (1~10 recommended).");
                    return;
                }

                var lines = new List<string>();
                lines.Add("ItemName,Category,PropertyName,Value,DataType");

                foreach (Autodesk.Navisworks.Api.ModelItem item in selection)
                {
                    string itemName = (item.DisplayName ?? "(unnamed)").Replace("\"", "'");

                    foreach (Autodesk.Navisworks.Api.PropertyCategory cat in item.PropertyCategories)
                    {
                        string catDisplay = (cat.DisplayName ?? "").Replace("\"", "'");
                        string catName = (cat.Name ?? "").Replace("\"", "'");

                        foreach (Autodesk.Navisworks.Api.DataProperty prop in cat.Properties)
                        {
                            string propDisplay = (prop.DisplayName ?? "").Replace("\"", "'");
                            string propName = (prop.Name ?? "").Replace("\"", "'");
                            string value = (prop.Value?.ToString() ?? "(null)").Replace("\"", "'");
                            string dataType = prop.Value?.DataType.ToString() ?? "Unknown";

                            lines.Add(
                                $"\"{itemName}\",\"{catDisplay} [{catName}]\",\"{propDisplay} [{propName}]\",\"{value}\",\"{dataType}\"");
                        }
                    }
                }

                string outPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"PropertyDump_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllLines(outPath, lines);

                _lblStatus.Text = $"Saved: {selection.Count} items, {lines.Count - 1} props";
                MessageBox.Show(
                    $"Property dump complete!\n\n"
                    + $"Items: {selection.Count}\n"
                    + $"Properties: {lines.Count - 1}\n"
                    + $"File: {outPath}",
                    "Property Dumper",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Property Dumper",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
