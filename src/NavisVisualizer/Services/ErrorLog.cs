using System;
using System.IO;
using System.Text;

namespace NavisVisualizer.Services
{
    /// <summary>
    /// 예외 상세를 파일로 남기는 최소 에러 로그 — %APPDATA%\NavisVisualizer\error.log
    /// (oasis.config와 같은 폴더). Navisworks 내부 오류는 지역화된 한 줄 메시지
    /// ("...에러가 발생하였습니다")만 노출돼 원인 특정이 불가능하므로, 예외 타입·
    /// 내부 예외·스택 전체를 기록해 재현 시 진단 가능하게 한다 (L5 — 덤프 먼저).
    /// Autodesk 비의존.
    /// </summary>
    public static class ErrorLog
    {
        /// <summary>예외를 기록하고 로그 파일 경로를 반환한다 (기록 실패 시 안내 문자열).</summary>
        public static string Append(string context, Exception ex, string extra = null)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NavisVisualizer");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "error.log");

                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}");
                if (!string.IsNullOrEmpty(extra))
                    sb.AppendLine(extra);
                sb.AppendLine(ex.ToString());   // 타입 + 메시지 + inner + 스택 전부
                sb.AppendLine(new string('-', 70));
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch
            {
                return "(에러 로그 기록 실패)";
            }
        }
    }
}
