using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NavisVisualizer.UI
{
    public class ToolsTab : UserControl
    {
        private readonly MainDockablePanel _main;
        private Button _btnDump;
        private Button _btnTree;
        private NumericUpDown _nudDepth;
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

            // --- Property Dumper ---
            layout.Controls.Add(new Label
            {
                Text = "Property Dumper",
                Font = new Font(Font, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Height = 20
            });

            layout.Controls.Add(new Label
            {
                Text = "Select items -> dump properties to CSV",
                Dock = DockStyle.Fill,
                Height = 20,
                ForeColor = Color.Gray
            });

            _btnDump = new Button
            {
                Text = "Selected Item Properties (CSV)",
                Dock = DockStyle.Fill,
                Height = 30
            };
            _btnDump.Click += BtnDump_Click;
            layout.Controls.Add(_btnDump);

            // --- Separator ---
            layout.Controls.Add(new Label { Height = 10, Dock = DockStyle.Fill });

            // --- Tree Dumper ---
            layout.Controls.Add(new Label
            {
                Text = "Model Tree Dumper",
                Font = new Font(Font, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Height = 20
            });

            layout.Controls.Add(new Label
            {
                Text = "Export full model tree structure to CSV",
                Dock = DockStyle.Fill,
                Height = 20,
                ForeColor = Color.Gray
            });

            var depthPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28 };
            depthPanel.Controls.Add(new Label { Text = "Max Depth:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            _nudDepth = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 20,
                Value = 5,
                Width = 50
            };
            depthPanel.Controls.Add(_nudDepth);
            layout.Controls.Add(depthPanel);

            _btnTree = new Button
            {
                Text = "Full Model Tree (CSV)",
                Dock = DockStyle.Fill,
                Height = 30
            };
            _btnTree.Click += BtnTree_Click;
            layout.Controls.Add(_btnTree);

            // --- Status ---
            _lblStatus = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                Height = 18,
                AutoSize = false
            };
            layout.Controls.Add(_lblStatus);

            Controls.Add(layout);
        }

        // -------------------------------------------------------
        // Property Dumper
        // -------------------------------------------------------
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
                    string itemName = Esc(item.DisplayName ?? "(unnamed)");

                    foreach (Autodesk.Navisworks.Api.PropertyCategory cat in item.PropertyCategories)
                    {
                        string catInfo = $"{Esc(cat.DisplayName)} [{Esc(cat.Name)}]";

                        foreach (Autodesk.Navisworks.Api.DataProperty prop in cat.Properties)
                        {
                            string propInfo = $"{Esc(prop.DisplayName)} [{Esc(prop.Name)}]";
                            string value = Esc(prop.Value?.ToString() ?? "(null)");
                            string dataType = prop.Value?.DataType.ToString() ?? "Unknown";

                            lines.Add($"\"{itemName}\",\"{catInfo}\",\"{propInfo}\",\"{value}\",\"{dataType}\"");
                        }
                    }
                }

                string outPath = SaveToDesktop("PropertyDump", lines);
                _lblStatus.Text = $"Props: {selection.Count} items, {lines.Count - 1} rows";
                MessageBox.Show($"Saved: {outPath}", "Property Dumper",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Property Dumper",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -------------------------------------------------------
        // Model Tree Dumper
        // -------------------------------------------------------
        private void BtnTree_Click(object sender, EventArgs e)
        {
            try
            {
                var doc = _main.GetDocument();
                if (doc == null || doc.Models.Count == 0)
                {
                    MessageBox.Show("No model loaded.");
                    return;
                }

                int maxDepth = (int)_nudDepth.Value;
                var lines = new List<string>();
                lines.Add("Depth,Path,DisplayName,Type,ClassName,HasGeometry,ChildCount,CategoryNames");

                _lblStatus.Text = "Exporting tree...";
                Application.DoEvents();

                int totalCount = 0;
                foreach (Autodesk.Navisworks.Api.Model model in doc.Models)
                {
                    WalkTree(model.RootItem, 0, maxDepth, "", lines, ref totalCount);
                }

                string outPath = SaveToDesktop("ModelTree", lines);
                _lblStatus.Text = $"Tree: {totalCount} nodes exported";
                MessageBox.Show(
                    $"Model tree exported!\n\n"
                    + $"Nodes: {totalCount}\n"
                    + $"Max Depth: {maxDepth}\n"
                    + $"File: {outPath}",
                    "Model Tree Dumper",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Model Tree Dumper",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WalkTree(
            Autodesk.Navisworks.Api.ModelItem item,
            int depth,
            int maxDepth,
            string parentPath,
            List<string> lines,
            ref int count)
        {
            string name = item.DisplayName ?? "(unnamed)";
            string path = string.IsNullOrEmpty(parentPath) ? name : parentPath + "/" + name;
            string type = item.ClassDisplayName ?? "";
            string className = item.ClassName ?? "";
            bool hasGeo = item.HasGeometry;
            int childCount = item.Children.Count();

            // Collect category names (not material)
            var catNames = new List<string>();
            foreach (Autodesk.Navisworks.Api.PropertyCategory cat in item.PropertyCategories)
            {
                string catName = cat.DisplayName ?? cat.Name ?? "";
                if (catName != "" && !catName.Contains("LcOaExMaterial"))
                    catNames.Add(catName);
            }
            string cats = string.Join("; ", catNames);

            lines.Add($"{depth},\"{Esc(path)}\",\"{Esc(name)}\",\"{Esc(type)}\",\"{Esc(className)}\",{hasGeo},{childCount},\"{Esc(cats)}\"");
            count++;

            // Progress update every 1000 items
            if (count % 1000 == 0)
            {
                _lblStatus.Text = $"Exporting... {count} nodes";
                Application.DoEvents();
            }

            if (depth < maxDepth)
            {
                foreach (Autodesk.Navisworks.Api.ModelItem child in item.Children)
                {
                    WalkTree(child, depth + 1, maxDepth, path, lines, ref count);
                }
            }
            else if (childCount > 0)
            {
                // Mark that there are more children below max depth
                lines.Add($"{depth + 1},\"{Esc(path)}/...\",\"... ({childCount} children)\",\"\",\"\",false,0,\"\"");
            }
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        private static string Esc(string s) => (s ?? "").Replace("\"", "'");

        private static string SaveToDesktop(string prefix, List<string> lines)
        {
            string outPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllLines(outPath, lines);
            return outPath;
        }
    }
}
