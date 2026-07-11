using System;
using System.Windows.Forms;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// 키 입력마다 무거운 작업(수만 행 리스트 재생성 등)이 도는 것을 막는 지연 실행기
    /// (성능 audit P0-1). 마지막 Trigger 후 delayMs가 지나야 action이 1회 실행된다 —
    /// "101780"을 타이핑하면 종전엔 6회 전체 갱신, 이제는 입력 멈춘 뒤 1회.
    /// WinForms Timer 기반이라 액션은 항상 UI 스레드에서 실행된다.
    /// </summary>
    public sealed class Debouncer : IDisposable
    {
        private readonly Timer _timer;
        private readonly Action _action;

        public Debouncer(Action action, int delayMs = 300)
        {
            _action = action;
            _timer = new Timer { Interval = delayMs };
            _timer.Tick += (s, e) => { _timer.Stop(); _action(); };
        }

        /// <summary>지연 타이머 재시작 — 연속 입력 동안은 실행되지 않는다.</summary>
        public void Trigger() { _timer.Stop(); _timer.Start(); }

        /// <summary>대기 중인 지연 실행을 취소하고 지금 즉시 1회 실행 (Enter 등 명시 확정용).</summary>
        public void Flush() { _timer.Stop(); _action(); }

        public void Dispose() => _timer.Dispose();
    }
}
