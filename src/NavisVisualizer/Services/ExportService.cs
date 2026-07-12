using System;
using System.Diagnostics;
using System.IO;
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
                // 호출부(탭)가 ex.Message로 안내하므로 여기서는 상세만 남기고 rethrow.
                ErrorLog.Append("Viewpoint 저장", ex, $"이름: {name}");
                throw;
            }
        }

        /// <summary>
        /// NWD 저장. 실패 원인 특정이 안 되던 문제(2026-07 사용자 보고 — Navisworks가
        /// "…에러가 발생하였습니다" 류의 지역화 한 줄만 보여줌) 대응:
        /// ① 흔한 환경 원인(대상 파일 잠김·폴더 쓰기 불가)은 저장 전에 걸러 명확한 메시지로,
        /// ② SaveFile 예외는 타입·내부 예외까지 대화상자에 표시하고 전체 스택을
        ///    %APPDATA%\NavisVisualizer\error.log에 기록해 재현 시 진단 가능하게.
        /// </summary>
        public bool ExportNwd(Document doc, string outputPath)
        {
            string precheck = PrecheckSavePath(doc, outputPath);
            if (precheck != null)
            {
                MessageBox.Show(precheck, "NWD Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                doc.SaveFile(outputPath);
                long bytes = 0;
                try { bytes = new FileInfo(outputPath).Length; } catch { }
                PerfLog.Record("NWD Export", sw.ElapsedMilliseconds,
                    note: $"{Path.GetFileName(outputPath)} · {bytes / 1024 / 1024:N0}MB");
                return true;
            }
            catch (Exception ex)
            {
                string logPath = ErrorLog.Append("NWD Export", ex,
                    $"대상: {outputPath}\n문서: {SafeDocName(doc)}\n소요: {sw.ElapsedMilliseconds}ms");
                var inner = ex.InnerException;
                MessageBox.Show(
                    "NWD 저장에 실패했습니다.\n\n" +
                    $"대상: {outputPath}\n" +
                    $"오류: {ex.GetType().Name}: {ex.Message}" +
                    (inner != null ? $"\n내부 오류: {inner.GetType().Name}: {inner.Message}" : "") +
                    $"\n\n상세 로그: {logPath}\n" +
                    "(재현 시 이 로그 파일을 전달해 주세요)\n\n" +
                    "우선 Navisworks에서 수동으로 저장해 보세요: File > Save As",
                    "NWD Export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
        }

        /// <summary>
        /// 저장 전 환경 점검. 문제 있으면 사용자 안내 문자열, 없으면 null.
        /// Navisworks 내부 오류로 넘어가기 전에 흔한 원인을 명확한 문구로 분리한다.
        /// 점검 자체가 실패하면(예: 네트워크 드라이브 지연) 저장을 막지 않는다 — 보수적 통과.
        /// </summary>
        private static string PrecheckSavePath(Document doc, string outputPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return $"저장 폴더가 존재하지 않습니다:\n{dir}";

                // 폴더 쓰기 권한 — 임시 파일 생성으로 실측 (ACL 조회보다 확실)
                string probe = Path.Combine(dir, $".nv_write_test_{Guid.NewGuid():N}.tmp");
                try
                {
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                }
                catch (Exception)
                {
                    return $"저장 폴더에 쓰기 권한이 없습니다:\n{dir}\n\n다른 폴더를 선택하세요.";
                }

                // 대상 파일 잠김 (다른 뷰어/탐색기 미리보기가 열고 있는 경우가 흔함)
                if (File.Exists(outputPath))
                {
                    string docFile = SafeDocName(doc);
                    if (!string.IsNullOrEmpty(docFile) &&
                        string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(docFile),
                            StringComparison.OrdinalIgnoreCase))
                        return "현재 열려 있는 파일과 같은 경로입니다.\n다른 이름으로 저장하세요.";

                    try
                    {
                        using (File.Open(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    }
                    catch (IOException)
                    {
                        return $"대상 파일이 다른 프로그램에서 사용 중입니다:\n{outputPath}\n\n해당 파일을 닫거나 다른 이름으로 저장하세요.";
                    }
                }
            }
            catch
            {
                // 점검 실패는 저장 실패가 아니다 — SaveFile 시도로 진행.
            }
            return null;
        }

        private static string SafeDocName(Document doc)
        {
            try { return doc?.FileName ?? ""; } catch { return ""; }
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
