using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Navisworks.Api.Automation;

namespace NavisBatch
{
    /// <summary>
    /// 바탕화면 '딸각' 러너 (CLAUDE.md §19).
    ///   1. Navisworks Simulate를 Automation API로 기동 (기본: 화면에 보이게)
    ///   2. 플러그인 진입점 NavisVisualizer.Batch.HDHHI_OE 실행 → 일괄 갱신 창(프로젝트·공종 체크 → 실행)
    ///   3. 창을 닫으면 반환. 종료 코드 = 저장 실패 job 수 (0 = 전부 성공)
    ///
    /// 인자 (전부 선택):
    ///   --hidden   Navisworks 창을 숨기고 끝나면 닫는다 (무인 스케줄용). 기본은 보이게 두고 끝나도 안 닫음
    ///              — 결과 모델이 그대로 떠 있어 바로 확인할 수 있다.
    ///   auto       기억된 선택으로 창이 뜨자마자 실행 (플러그인에 전달)
    ///   close      실행 후 창 자동 닫기 (플러그인에 전달)
    ///   예) 작업 스케줄러 무인 실행:  NavisBatch.exe --hidden auto close
    ///
    /// 빌드별 시그니처 차이(L4)에 대비해 Visible은 리플렉션으로 건드린다 — 없으면 그냥 보이는 채로.
    /// </summary>
    internal static class Program
    {
        private const string PluginId = "NavisVisualizer.Batch.HDHHI_OE";

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            bool hidden = args.Any(a => a.Equals("--hidden", StringComparison.OrdinalIgnoreCase)
                                     || a.Equals("/hidden", StringComparison.OrdinalIgnoreCase));
            string[] pluginArgs = args
                .Where(a => !a.Equals("--hidden", StringComparison.OrdinalIgnoreCase)
                         && !a.Equals("/hidden", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Console.WriteLine("NavisVisualizer 일괄 갱신");
            Console.WriteLine("Navisworks Simulate 기동 중… (첫 기동은 30초 정도 걸릴 수 있습니다)");

            NavisworksApplication app = null;
            try
            {
                app = new NavisworksApplication();
                if (hidden) TrySetVisible(app, false);

                Console.WriteLine("플러그인 실행: " + PluginId +
                                  (pluginArgs.Length > 0 ? "  인자: " + string.Join(" ", pluginArgs) : ""));
                int rc = app.ExecuteAddInPlugin(PluginId, pluginArgs);

                if (rc == 0) Console.WriteLine("완료.");
                else if (rc > 0) Console.WriteLine($"완료 — 저장 실패 {rc}건. 창의 결과 또는 batch.log를 확인하세요.");
                else Console.WriteLine("플러그인이 오류로 끝났습니다 (error.log 확인).");

                if (hidden)
                {
                    app.Dispose();   // 숨김 모드: Navisworks 종료
                    app = null;
                }
                else
                {
                    // 보이는 모드: Navisworks를 열어 둔다 — 색칠된 모델을 바로 볼 수 있게.
                    // (러너 종료로 Navisworks가 같이 닫히면 Windows 실측 후 대기 옵션 추가 — §19)
                    Console.WriteLine("Navisworks는 열어 둡니다.");
                }
                return rc;
            }
            catch (Exception ex)
            {
                string log = AppendErrorLog(ex);
                Console.Error.WriteLine("오류: " + ex.GetType().Name + ": " + ex.Message);
                Console.Error.WriteLine("상세: " + log);
                Console.WriteLine();
                Console.WriteLine("확인 사항: Navisworks Simulate 2022 설치·라이선스, 플러그인 배포(deploy.bat), " +
                                  "Autodesk.Navisworks.Automation.dll 존재 여부.");
                Console.WriteLine("아무 키나 누르면 닫힙니다.");
                try { Console.ReadKey(true); } catch { }
                try { app?.Dispose(); } catch { }
                return -1;
            }
        }

        private static void TrySetVisible(NavisworksApplication app, bool visible)
        {
            try
            {
                PropertyInfo p = app.GetType().GetProperty("Visible");
                if (p != null && p.CanWrite) p.SetValue(app, visible, null);
                else Console.WriteLine("(이 빌드의 Automation API에 Visible 속성이 없어 창을 숨기지 못했습니다)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("(창 숨기기 실패 — 보이는 채로 진행: " + ex.Message + ")");
            }
        }

        /// <summary>플러그인의 ErrorLog와 같은 파일(%APPDATA%\NavisVisualizer\error.log)에 남긴다.</summary>
        private static string AppendErrorLog(Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NavisVisualizer");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "error.log");
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] NavisBatch 러너\n{ex}\n{new string('-', 70)}\n",
                    Encoding.UTF8);
                return path;
            }
            catch
            {
                return "(에러 로그 기록 실패)";
            }
        }
    }
}
