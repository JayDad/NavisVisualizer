using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace NavisVisualizer.Services
{
    /// <summary>
    /// 경량 성능 계측 로그 (성능 audit P0 — marquee 진행바만으로는 인덱스/색칠/리스트 갱신 중
    /// 어느 단계가 병목인지 구분할 수 없음). 주요 작업(데이터 로드·인덱스 빌드·색상 적용·
    /// 리스트 갱신·범위 판정·속성 쓰기)이 소요시간과 건수를 기록하고, 고급 진단 탭에서
    /// CSV로 출력한다. Autodesk 비의존 — 리눅스 컴파일 검증 가능 그룹.
    /// 링 버퍼(최대 500건) — 넘치면 오래된 항목부터 버린다. UI 스레드 전용 플러그인이지만
    /// 방어적으로 lock을 건다.
    /// </summary>
    public static class PerfLog
    {
        public class Entry
        {
            public DateTime At;
            public string Action;
            public long Ms;
            public int Rows;    // 데이터 행 수 (-1 = 해당 없음)
            public int Items;   // 모델 아이템/인덱스 건수 (-1 = 해당 없음)
            public string Note;
        }

        private const int MaxEntries = 500;
        private static readonly object _sync = new object();
        private static readonly List<Entry> _entries = new List<Entry>();

        public static void Record(string action, long ms, int rows = -1, int items = -1, string note = null)
        {
            lock (_sync)
            {
                _entries.Add(new Entry
                {
                    At = DateTime.Now,
                    Action = action,
                    Ms = ms,
                    Rows = rows,
                    Items = items,
                    Note = note ?? "",
                });
                if (_entries.Count > MaxEntries)
                    _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }
        }

        /// <summary>using 패턴 측정 스코프 — Dispose 시 Record. Rows/Items/Note는 스코프 안에서 채운다.</summary>
        public static Scope Time(string action) => new Scope(action);

        public sealed class Scope : IDisposable
        {
            private readonly Stopwatch _sw = Stopwatch.StartNew();
            private readonly string _action;
            public int Rows = -1;
            public int Items = -1;
            public string Note;
            internal Scope(string action) { _action = action; }
            public void Dispose()
            {
                _sw.Stop();
                Record(_action, _sw.ElapsedMilliseconds, Rows, Items, Note);
            }
        }

        public static int Count { get { lock (_sync) return _entries.Count; } }

        public static void Clear() { lock (_sync) _entries.Clear(); }

        /// <summary>CSV 본문(헤더 포함) — Excel에서 바로 열리는 형식.</summary>
        public static string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("시각,작업,소요(ms),데이터행,모델건수,비고");
            lock (_sync)
            {
                foreach (var e in _entries)
                {
                    sb.AppendLine(
                        $"{e.At:HH:mm:ss.fff},\"{e.Action}\",{e.Ms}," +
                        $"{(e.Rows >= 0 ? e.Rows.ToString() : "")}," +
                        $"{(e.Items >= 0 ? e.Items.ToString() : "")}," +
                        $"\"{(e.Note ?? "").Replace("\"", "\"\"")}\"");
                }
            }
            return sb.ToString();
        }
    }
}
