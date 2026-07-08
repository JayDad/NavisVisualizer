using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NavisVisualizer.Searchers;

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

            // --- User Data Test ---
            layout.Controls.Add(new Label { Height = 10, Dock = DockStyle.Fill });
            layout.Controls.Add(new Label
            {
                Text = "User Data Test",
                Font = new Font(Font, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Height = 20
            });
            layout.Controls.Add(new Label
            {
                Text = "Select 1 item -> test COM property write",
                Dock = DockStyle.Fill,
                Height = 20,
                ForeColor = Color.Gray
            });
            var btnTestProp = new Button
            {
                Text = "Test Write Property (1 item)",
                Dock = DockStyle.Fill,
                Height = 30
            };
            btnTestProp.Click += BtnTestProp_Click;
            layout.Controls.Add(btnTestProp);

            // --- Cable Node Box 진단 ---
            layout.Controls.Add(new Label { Height = 10, Dock = DockStyle.Fill });
            layout.Controls.Add(new Label
            {
                Text = "Cable Node Box 진단",
                Font = new Font(Font, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Height = 20
            });
            layout.Controls.Add(new Label
            {
                Text = "노드당 박스 2개 이상(매크로 중복 의심) 검사",
                Dock = DockStyle.Fill,
                Height = 20,
                ForeColor = Color.Gray
            });
            var btnDupBox = new Button
            {
                Text = "Node Box 중복 검사 (CSV)",
                Dock = DockStyle.Fill,
                Height = 30
            };
            btnDupBox.Click += BtnDupBox_Click;
            layout.Controls.Add(btnDupBox);

            layout.Controls.Add(new Label
            {
                Text = "현재 뷰의 단면(Clip Plane) 평면 값 덤프 — 보이는 것만 필터 보정용",
                Dock = DockStyle.Fill,
                Height = 20,
                ForeColor = Color.Gray
            });
            var btnClipDump = new Button
            {
                Text = "Clip Plane 덤프",
                Dock = DockStyle.Fill,
                Height = 30
            };
            btnClipDump.Click += BtnClipDump_Click;
            layout.Controls.Add(btnClipDump);

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
                string outPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"ModelTree_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                _lblStatus.Text = "Exporting tree...";
                _btnTree.Enabled = false;
                Application.DoEvents();

                int totalCount = 0;
                using (var writer = new StreamWriter(outPath, false, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine("Depth,Path,DisplayName,Type,ClassName,HasGeometry,ChildCount,CategoryNames");

                    foreach (Autodesk.Navisworks.Api.Model model in doc.Models)
                    {
                        WalkTree(model.RootItem, 0, maxDepth, "", writer, ref totalCount);
                    }
                }

                _btnTree.Enabled = true;
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
                _btnTree.Enabled = true;
                MessageBox.Show($"Error: {ex.Message}", "Model Tree Dumper",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WalkTree(
            Autodesk.Navisworks.Api.ModelItem item,
            int depth,
            int maxDepth,
            string parentPath,
            StreamWriter writer,
            ref int count)
        {
            string name = item.DisplayName ?? "(unnamed)";
            string path = string.IsNullOrEmpty(parentPath) ? name : parentPath + "/" + name;
            string type = item.ClassDisplayName ?? "";
            string className = item.ClassName ?? "";
            bool hasGeo = item.HasGeometry;
            int childCount = item.Children.Count();

            // Collect category names (skip material)
            var catNames = new List<string>();
            foreach (Autodesk.Navisworks.Api.PropertyCategory cat in item.PropertyCategories)
            {
                string catName = cat.DisplayName ?? cat.Name ?? "";
                if (catName != "" && !catName.Contains("LcOaExMaterial"))
                    catNames.Add(catName);
            }
            string cats = string.Join("; ", catNames);

            writer.WriteLine($"{depth},\"{Esc(path)}\",\"{Esc(name)}\",\"{Esc(type)}\",\"{Esc(className)}\",{hasGeo},{childCount},\"{Esc(cats)}\"");
            count++;

            if (count % 5000 == 0)
            {
                _lblStatus.Text = $"Exporting... {count} nodes";
                writer.Flush();
                Application.DoEvents();
            }

            if (depth < maxDepth)
            {
                foreach (Autodesk.Navisworks.Api.ModelItem child in item.Children)
                {
                    WalkTree(child, depth + 1, maxDepth, path, writer, ref count);
                }
            }
            else if (childCount > 0)
            {
                writer.WriteLine($"{depth + 1},\"{Esc(path)}/...\",\"... ({childCount} children)\",\"\",\"\",false,0,\"\"");
            }
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        private void BtnTestProp_Click(object sender, EventArgs e)
        {
            try
            {
                var doc = _main.GetDocument();
                if (doc == null) { MessageBox.Show("No document open."); return; }

                var selection = doc.CurrentSelection.SelectedItems;
                if (selection.Count == 0) { MessageBox.Show("Select 1 item first."); return; }

                var item = selection.First();
                string result = _main.UserDataSvc.TestWriteOneProperty(item);
                MessageBox.Show(result, "User Data Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "User Data Test", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -------------------------------------------------------
        // Cable Node Box duplicate check
        // -------------------------------------------------------
        private void BtnDupBox_Click(object sender, EventArgs e)
        {
            try
            {
                var doc = _main.GetDocument();
                if (doc == null || doc.Models.Count == 0)
                {
                    MessageBox.Show("No model loaded.");
                    return;
                }

                _lblStatus.Text = "Building box index...";
                Application.DoEvents();
                if (_main.CableBoxSearcher.NeedsRebuild(doc))
                    _main.CableBoxSearcher.BuildIndexForBoxes(doc, NwdScope.Cable);

                var dups = _main.CableBoxSearcher.GetEntriesWithMultipleItems();
                int totalNodes = _main.CableBoxSearcher.IndexedCount;
                string scopeNote = _main.CableBoxSearcher.LastScopeNote ?? "-";

                if (dups.Count == 0)
                {
                    _lblStatus.Text = $"Box nodes: {totalNodes}, duplicates: 0";
                    MessageBox.Show(
                        $"중복 없음.\n\n인덱싱된 Node Box: {totalNodes}개\n노드당 박스는 모두 1개입니다.\n\n{scopeNote}",
                        "Node Box 중복 검사", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var lines = new List<string>();
                lines.Add("NodeKey,BoxCount,BoxNames");
                foreach (var kv in dups)
                {
                    var names = string.Join("; ", kv.Value.Select(i => Esc(i.DisplayName ?? "(unnamed)")));
                    lines.Add($"\"{Esc(kv.Key)}\",{kv.Value.Count},\"{names}\"");
                }

                string outPath = SaveToDesktop("CableNodeBox_Duplicates", lines);
                _lblStatus.Text = $"Box nodes: {totalNodes}, duplicates: {dups.Count}";
                MessageBox.Show(
                    $"노드당 박스 2개 이상: {dups.Count}개 발견 (전체 {totalNodes}개 중)\n\n저장: {outPath}",
                    "Node Box 중복 검사", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Node Box 중복 검사",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -------------------------------------------------------
        // Clip Plane dump (visibility filter calibration)
        // -------------------------------------------------------
        private void BtnClipDump_Click(object sender, EventArgs e)
        {
            try
            {
                var doc = _main.GetDocument();
                if (doc == null)
                {
                    MessageBox.Show("No document open.");
                    return;
                }

                string dump = _main.SectionSvc.DumpClipPlanes(doc);

                string outPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"ClipPlaneDump_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(outPath, dump, System.Text.Encoding.UTF8);

                _lblStatus.Text = "Clip plane dump saved";
                MessageBox.Show($"{dump}\n\n저장: {outPath}", "Clip Plane 덤프",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Clip Plane 덤프",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

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
