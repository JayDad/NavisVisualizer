using System;
using System.Diagnostics;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;

namespace NavisVisualizer.Services
{
    public class ExportService
    {
        public void SaveViewpoint(Document doc, string name)
        {
            try
            {
                Viewpoint currentVp = doc.CurrentViewpoint.ToViewpoint();
                var savedVp = new SavedViewpoint(currentVp) { DisplayName = name };
                doc.SavedViewpoints.AddCopy(savedVp);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Viewpoint 저장 실패: {ex.Message}");
                throw;
            }
        }

        public bool ExportNwd(Document doc, string outputPath)
        {
            try
            {
                Autodesk.Navisworks.Api.Application.ActiveDocument.SaveFile(outputPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NWD 자동 저장 실패: {ex.Message}");
                MessageBox.Show(
                    $"자동 NWD 저장에 실패했습니다.\n\n" +
                    $"Navisworks에서 수동으로 저장해 주세요:\n" +
                    $"File > Save As > {outputPath}\n\n" +
                    $"오류: {ex.Message}",
                    "NWD Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
        }

        public void ExportNwdWithDialog(Document doc)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "NWD 파일로 저장";
                dlg.Filter = "Navisworks Document (*.nwd)|*.nwd";
                dlg.FileName = $"NavisVisualizer_{DateTime.Now:yyyyMMdd_HHmm}.nwd";

                if (dlg.ShowDialog() == DialogResult.OK)
                    ExportNwd(doc, dlg.FileName);
            }
        }
    }
}
