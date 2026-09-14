using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Loaders;
using NavisVisualizer.Models;
using NavisVisualizer.Searchers;
using NavisVisualizer.Visualizers;

namespace NavisVisualizer.Services
{
    /// <summary>배치 한 job 안에서 공종 하나의 결과.</summary>
    public class BatchStepResult
    {
        public BatchDiscipline Discipline;
        public int Rows = -1;          // OASIS 행 수
        public int Matched = -1;
        public int Unmatched = -1;
        public long Ms;
        public string ScopeNote = "";
        public string Error;           // null = 성공

        public bool Success => Error == null;

        public string Summary()
        {
            string label = BatchDisciplineInfo.Label(Discipline);
            if (!Success) return $"{label}: 실패 — {Error}";
            return $"{label}: OASIS {Rows:N0}건 · 매칭 {Matched:N0} / 미매칭 {Unmatched:N0} · {Ms / 1000.0:0.0}s";
        }
    }

    /// <summary>배치 job(모델 1개) 전체 결과.</summary>
    public class BatchJobResult
    {
        public BatchJob Job;
        public List<BatchStepResult> Steps = new List<BatchStepResult>();
        public string OutputPath;      // 저장 성공 시 경로
        public string Error;           // job 수준 오류 (모델 열기/저장 실패 등). null = 정상
        public long TotalMs;

        public bool Saved => OutputPath != null && Error == null;
        public bool AnyStepSucceeded => Steps.Any(s => s.Success);
    }

    /// <summary>
    /// 일괄 갱신 코어 (CLAUDE.md §21). job마다 "모델 열기 → OASIS 로드 → 인덱스 → 색 적용 →
    /// 다른 이름 저장"을 UI 없이 수행한다. 각 공종 탭의 [OASIS 로드]+[가시화 적용]과 **같은
    /// 함수**(SqlLoader / ModelItemSearcher / ColorOverrideEngine)를 호출하는 얇은 층이라 탭 코드는
    /// 손대지 않는다.
    ///
    /// 설계 결정:
    /// - 색은 ColorSetting.*Defaults 전 단계 (탭의 세션 색은 메모리뿐이라 배치가 알 수 없음 —
    ///   미착수 빨강 기본값이 여기서 곧바로 효과를 냄).
    /// - job마다 searcher 5개 + 엔진을 **새로 만든다**: Navisworks는 Document 인스턴스를 재사용하며
    ///   파일만 갈아끼우므로, 이전 job의 painted 컬렉션(ModelItem)을 든 엔진으로 ResetModule을
    ///   부르면 죽은 핸들을 리셋하게 된다. 도크 패널의 searcher/엔진과도 공유하지 않는다(격리).
    /// - 공종 하나가 실패해도 나머지 공종은 계속 → 하나라도 성공했으면 저장. 전부 실패면 저장 생략.
    /// - Navisworks API는 UI(STA) 스레드 전용이라 동기 실행. 진행 문구는 Progress 콜백으로.
    /// - 대화상자(MessageBox) 절대 없음 — 무인 실행에서 모달은 곧 정지. 오류는 결과 객체 + ErrorLog.
    /// </summary>
    public class BatchRunner
    {
        /// <summary>진행 단계 문구 (UI가 라벨에 표시). UI 스레드에서 호출됨.</summary>
        public Action<string> Progress { get; set; }

        /// <summary>
        /// 인덱스 빌드 직전 스코프(모델 파일 매핑) 게이트 — §19의 ScopeGate와 같은 역할. 화면이 있으면
        /// `ScopeGate.EnsureMapped`(미지정 시 파일 지정 프롬프트), 무인 실행이면 매핑 여부만 판정해
        /// 미지정 공종을 건너뛴다(프롬프트 = 정지). null이면 매핑 여부만 판정.
        /// </summary>
        public Func<Document, NwdScope, bool> EnsureScopeMapped { get; set; }

        private readonly ExportService _export = new ExportService();

        public BatchJobResult RunJob(Document doc, BatchJob job, ISet<BatchDiscipline> disciplines,
            DateTime referenceDate, BatchConfig config)
        {
            var sw = Stopwatch.StartNew();
            var result = new BatchJobResult { Job = job };

            try
            {
                if (doc == null)
                {
                    result.Error = "활성 문서가 없습니다 (Navisworks 문서 객체 없음).";
                    return result;
                }
                if (string.IsNullOrWhiteSpace(job.ModelPath) || !File.Exists(job.ModelPath))
                {
                    result.Error = $"모델 파일이 없습니다: {job.ModelPath}\n(batch.config의 model 경로를 확인하세요)";
                    return result;
                }
                if (disciplines == null || disciplines.Count == 0)
                {
                    result.Error = "선택된 공종이 없습니다.";
                    return result;
                }

                // 1) 모델 열기 — 현재 문서 내용을 교체한다 (호출부가 미리 확인받음).
                Report($"[{job.Name}] 모델 여는 중… {Path.GetFileName(job.ModelPath)}");
                var swOpen = Stopwatch.StartNew();
                try
                {
                    doc.OpenFile(job.ModelPath);
                }
                catch (Exception ex)
                {
                    ErrorLog.Append("배치 모델 열기", ex, $"job: {job.Name}\n모델: {job.ModelPath}");
                    result.Error = $"모델 열기 실패: {ex.GetType().Name}: {ex.Message}";
                    return result;
                }
                if (doc.Models.Count == 0)
                {
                    result.Error = "모델 열기 후 모델이 비어 있습니다 (파일 손상 또는 열기 취소).";
                    return result;
                }
                PerfLog.Record("배치 모델 열기", swOpen.ElapsedMilliseconds, items: doc.Models.Count,
                    note: Path.GetFileName(job.ModelPath));
                // 같은 Document 인스턴스에 파일만 갈아끼웠으므로 문서별 매핑 캐시를 비운다
                // (도크 패널이 없는 독립 실행에서는 FileNameChanged 무효화 경로가 없다).
                ScopeMappingService.InvalidateCache();

                // 2) OASIS 연결 설정 — job별 프로젝트 필터 override
                SqlConnectionSettings settings;
                try
                {
                    settings = SqlConnectionSettings.Load();
                    if (!string.IsNullOrWhiteSpace(job.ProjectNo))
                        settings.ProjectNo = job.ProjectNo.Trim();
                }
                catch (Exception ex)
                {
                    result.Error = "OASIS 연결 설정 오류: " + ex.Message;
                    return result;
                }

                // 3) 공종별 로드 → 인덱스 → 적용 (job 전용 searcher/엔진 — 격리)
                var spoolSearcher = new ModelItemSearcher();
                var hydroSearcher = new ModelItemSearcher();
                var elecSearcher = new ModelItemSearcher();
                var equipSearcher = new ModelItemSearcher();
                var cableSearcher = new ModelItemSearcher();
                var engine = new ColorOverrideEngine(
                    spoolSearcher, hydroSearcher, elecSearcher, equipSearcher, cableSearcher);

                // 색칠 순서: Hydrotest(PKG 상위) → Spool(하위) — 스풀 색이 PKG 색에 덮이지 않게 (RunOrder 주석)
                foreach (var d in BatchDisciplineInfo.RunOrder)
                {
                    if (!disciplines.Contains(d)) continue;
                    var step = new BatchStepResult { Discipline = d };
                    var swStep = Stopwatch.StartNew();
                    try
                    {
                        switch (d)
                        {
                            case BatchDiscipline.Spool:
                                RunSpool(doc, job, settings, referenceDate, spoolSearcher, engine, step);
                                break;
                            case BatchDiscipline.Hydrotest:
                                RunHydrotest(doc, job, settings, referenceDate, hydroSearcher, engine, step);
                                break;
                            case BatchDiscipline.Equipment:
                                RunEquipment(doc, job, settings, referenceDate, equipSearcher, engine, step);
                                break;
                            case BatchDiscipline.EitTray:
                                RunEitTray(doc, job, settings, elecSearcher, engine, step);
                                break;
                            case BatchDiscipline.Cable:
                                RunCable(doc, job, settings, referenceDate, cableSearcher, engine, step);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorLog.Append($"배치 {BatchDisciplineInfo.Label(d)}", ex,
                            $"job: {job.Name}\n모델: {job.ModelPath}");
                        step.Error = $"{ex.GetType().Name}: {ex.Message}";
                    }
                    step.Ms = swStep.ElapsedMilliseconds;
                    result.Steps.Add(step);
                    Report($"[{job.Name}] {step.Summary()}");
                }

                if (!result.AnyStepSucceeded)
                {
                    result.Error = "적용에 성공한 공종이 없어 저장을 생략했습니다.";
                    return result;
                }

                // 4) 다른 이름으로 저장 — 대화상자 없는 경로
                string outputPath = config.BuildOutputPath(job, DateTime.Now);
                Report($"[{job.Name}] 저장 중… {Path.GetFileName(outputPath)}");
                try
                {
                    string dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }
                catch { /* 생성 실패는 아래 precheck가 잡는다 */ }

                if (_export.ExportNwdSilent(doc, outputPath, out string saveError))
                    result.OutputPath = outputPath;
                else
                    result.Error = "저장 실패: " + saveError;
                return result;
            }
            finally
            {
                result.TotalMs = sw.ElapsedMilliseconds;
                PerfLog.Record("배치 job", result.TotalMs, rows: result.Steps.Count,
                    note: $"{job.Name} · {(result.Saved ? "저장" : "미저장")}" +
                          (result.Error != null ? " · " + result.Error : ""));
                AppendBatchLog(result, referenceDate);
            }
        }

        // ---- 공종별 단계 (각 탭의 OASIS 로드 + BuildIndex + BtnApply_Click 과 동일 호출) ----

        private void RunSpool(Document doc, BatchJob job, SqlConnectionSettings settings, DateTime refDate,
            ModelItemSearcher searcher, ColorOverrideEngine engine, BatchStepResult step)
        {
            Report($"[{job.Name}] Spool — OASIS 조회 중…");
            var list = SqlLoader.LoadSpool(settings);
            step.Rows = list.Count;
            if (list.Count == 0) { step.Error = "OASIS 데이터 0건"; return; }

            if (!ScopeReady(doc, NwdScope.Spool, step)) return;
            Report($"[{job.Name}] Spool — 모델 태그 인덱스 생성 중… ({list.Count:N0}건)");
            searcher.Reset();
            searcher.BuildIndexForTags(doc, new HashSet<string>(list.Select(s => s.SpoolId)), NwdScope.Spool);
            step.ScopeNote = ScopeNoteOf(searcher);

            Report($"[{job.Name}] Spool — 색상 적용 중…");
            var r = engine.ApplySpool(doc, list, ColorSetting.SpoolDefaults, refDate);
            step.Matched = r.MatchedCount;
            step.Unmatched = r.UnmatchedCount;
        }

        private void RunHydrotest(Document doc, BatchJob job, SqlConnectionSettings settings, DateTime refDate,
            ModelItemSearcher searcher, ColorOverrideEngine engine, BatchStepResult step)
        {
            Report($"[{job.Name}] Hydrotest — OASIS 조회 중…");
            var list = SqlLoader.LoadHydrotest(settings);
            step.Rows = list.Count;
            if (list.Count == 0) { step.Error = "OASIS 데이터 0건"; return; }

            if (!ScopeReady(doc, NwdScope.Hydrotest, step)) return;
            Report($"[{job.Name}] Hydrotest — 모델 태그 인덱스 생성 중…");
            searcher.Reset();
            searcher.BuildIndex(doc, NwdScope.Hydrotest);
            step.ScopeNote = ScopeNoteOf(searcher);

            Report($"[{job.Name}] Hydrotest — 색상 적용 중…");
            var r = engine.ApplyHydrotest(doc, list, ColorSetting.HydrotestDefaults, refDate);
            step.Matched = r.MatchedCount;
            step.Unmatched = r.UnmatchedCount;
        }

        private void RunEquipment(Document doc, BatchJob job, SqlConnectionSettings settings, DateTime refDate,
            ModelItemSearcher searcher, ColorOverrideEngine engine, BatchStepResult step)
        {
            Report($"[{job.Name}] Equipment — OASIS 조회 중…");
            var list = SqlLoader.LoadEquipment(settings);
            step.Rows = list.Count;
            if (list.Count == 0) { step.Error = "OASIS 데이터 0건"; return; }

            if (!ScopeReady(doc, NwdScope.Equipment, step)) return;
            Report($"[{job.Name}] Equipment — 모델 태그 인덱스 생성 중… ({list.Count:N0}건)");
            searcher.Reset();
            searcher.BuildIndexForTags(doc, new HashSet<string>(list.Select(e => e.TagNo)), NwdScope.Equipment);
            step.ScopeNote = ScopeNoteOf(searcher);

            Report($"[{job.Name}] Equipment — 색상 적용 중…");
            var r = engine.ApplyEquipment(doc, list, ColorSetting.EquipmentDefaults, refDate);
            step.Matched = r.MatchedCount;
            step.Unmatched = r.UnmatchedCount;
        }

        /// <summary>EIT Tray는 날짜 컬럼이 없어 기준일 무관 (%기반 현재 상태 — CLAUDE.md §9).</summary>
        private void RunEitTray(Document doc, BatchJob job, SqlConnectionSettings settings,
            ModelItemSearcher searcher, ColorOverrideEngine engine, BatchStepResult step)
        {
            Report($"[{job.Name}] EIT Tray — OASIS 조회 중…");
            var list = SqlLoader.LoadEitTray(settings);
            step.Rows = list.Count;
            if (list.Count == 0) { step.Error = "OASIS 데이터 0건"; return; }

            if (!ScopeReady(doc, NwdScope.EitTray, step)) return;
            Report($"[{job.Name}] EIT Tray — 모델 태그 인덱스 생성 중… ({list.Count:N0}건)");
            searcher.Reset();
            var trayIds = new HashSet<string>(
                list.Select(t => EitTrayData.NormalizeId(t.TrayNumber)),
                StringComparer.OrdinalIgnoreCase);
            // 스코프 미지정이면 전체 트리를 훑지 않고 0건 + 노트 (§19 — 전 탭 공통 동작)
            searcher.BuildIndexForTags(doc, trayIds, NwdScope.EitTray);
            step.ScopeNote = ScopeNoteOf(searcher);

            Report($"[{job.Name}] EIT Tray — 색상 적용 중…");
            var r = engine.ApplyEit(doc, list, ColorSetting.EitDefaults);
            step.Matched = r.MatchedCount;
            step.Unmatched = r.UnmatchedCount;
        }

        private void RunCable(Document doc, BatchJob job, SqlConnectionSettings settings, DateTime refDate,
            ModelItemSearcher searcher, ColorOverrideEngine engine, BatchStepResult step)
        {
            Report($"[{job.Name}] Cable — OASIS 조회 중…");
            var list = SqlLoader.LoadCable(settings);
            step.Rows = list.Count;
            if (list.Count == 0) { step.Error = "OASIS 데이터 0건"; return; }

            if (!ScopeReady(doc, NwdScope.Cable, step)) return;
            Report($"[{job.Name}] Cable — 모델 태그 인덱스 생성 중… ({list.Count:N0}건)");
            searcher.Reset();
            searcher.BuildIndexForTags(doc, new HashSet<string>(list.Select(c => c.CableNo)), NwdScope.Cable);
            step.ScopeNote = ScopeNoteOf(searcher);

            // CableLineTab과 동일: 진척 신호가 전무한 맨 목록이면 단색 하이라이트 모드
            bool highlightMode = !list.Any(c => c.HasProgressSignal);
            Report($"[{job.Name}] Cable — 색상 적용 중…{(highlightMode ? " (하이라이트 모드)" : "")}");
            var r = engine.ApplyCableLines(doc, list, ColorSetting.CableLineDefaults, refDate,
                highlightMode ? ColorSetting.CableLineHighlight : null);
            step.Matched = r.MatchedCount;
            step.Unmatched = r.UnmatchedCount;
            if (highlightMode) step.ScopeNote = ("하이라이트 모드(진척 신호 없음) " + step.ScopeNote).Trim();
        }

        private void Report(string text)
        {
            try { Progress?.Invoke(text); } catch { }
        }

        /// <summary>
        /// 스코프(모델 파일) 매핑 게이트. 미지정이면 이 공종을 건너뛰고 사유를 남긴다 — 전체 모델
        /// fallback은 폐지됐으므로(§19) 여기서 멈추지 않으면 조용히 0건이 된다.
        /// </summary>
        private bool ScopeReady(Document doc, NwdScope scope, BatchStepResult step)
        {
            bool mapped;
            try
            {
                mapped = EnsureScopeMapped != null
                    ? EnsureScopeMapped(doc, scope)
                    : ScopeMappingService.Resolve(doc, scope).IsMapped;
            }
            catch (Exception ex)
            {
                step.Error = "모델 파일 매핑 해석 실패: " + ex.Message;
                return false;
            }
            if (mapped) return true;
            step.Error = "모델 파일(스코프) 미지정 — Navisworks 플러그인 Overview 탭에서 이 공종의 파일을 지정하세요";
            return false;
        }

        private static string ScopeNoteOf(ModelItemSearcher searcher)
        {
            string note = searcher.LastScopeNote ?? "";
            if (searcher.LastScopeUnmapped)
                note = ("스코프 미지정(0건) " + note).Trim();
            return note;
        }

        /// <summary>%APPDATA%\NavisVisualizer\batch.log — 매일 돌린 이력이 남아야 "어제 왜 안 됐지"를 답할 수 있다.</summary>
        public static string BatchLogPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NavisVisualizer", "batch.log");

        private static void AppendBatchLog(BatchJobResult r, DateTime referenceDate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BatchLogPath));
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] job={r.Job?.Name} 기준일={referenceDate:yyyy-MM-dd} " +
                              $"모델={r.Job?.ModelPath} 소요={r.TotalMs / 1000.0:0.0}s");
                foreach (var s in r.Steps)
                {
                    sb.AppendLine("  " + s.Summary() +
                                  (string.IsNullOrEmpty(s.ScopeNote) ? "" : $"  [스코프: {s.ScopeNote}]"));
                }
                sb.AppendLine(r.Saved ? $"  저장: {r.OutputPath}" : $"  미저장: {r.Error}");
                File.AppendAllText(BatchLogPath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* 로그 실패는 배치 실패가 아니다 */ }
        }
    }
}
