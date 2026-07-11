using System;
using System.Drawing;
using System.Windows.Forms;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// "3D 적용 상태" 표시기 (UX audit P0-1). 리스트/통계는 항상 현재 데이터 기준으로
    /// 즉시 갱신되지만 3D 색상은 마지막 [가시화 적용] 시점 기준이라 둘이 어긋날 수 있다 —
    /// 그 어긋남을 통계 라벨에 경고 문구로 얹지 않고(통계는 통계만) 이 전용 라벨이
    /// 항상 한 자리에서 알려준다. 상태 3단:
    ///   미적용(회색) → {기준} 적용됨 · 시각(녹색) → ⚠ 3D 업데이트 필요 + 사유(주황).
    /// 업데이트 필요 상태에서는 연결된 [가시화 적용] 버튼 배경도 강조한다.
    /// 소스/기준일/단계 체크가 바뀔 때 탭이 MarkStale을 호출한다 — 한 번도 적용 전이면
    /// 3D는 아직 아무 기준도 아니므로 no-op ("미적용" 유지).
    /// </summary>
    public class ApplyStatePanel : Label
    {
        private static readonly Color AppliedColor = Color.FromArgb(0, 120, 40);
        private static readonly Color StaleColor = Color.FromArgb(190, 90, 0);
        private static readonly Color StaleButtonBack = Color.FromArgb(255, 232, 170);

        private Button _applyButton;
        private bool _appliedOnce;

        /// <summary>Overview 탭 노출용 — 마지막 적용 이후 어긋남이 표시된 상태인가.</summary>
        public bool IsStale { get; private set; }

        /// <summary>Overview 탭 노출용 — 적용 이력이 있는가 (해제 시 false 복귀).</summary>
        public bool IsApplied => _appliedOnce;

        public ApplyStatePanel()
        {
            AutoSize = true;
            // FlowLayoutPanel 버튼 행(버튼 높이 ~23px)에 넣었을 때 세로 중앙 부근에 오도록.
            Padding = new Padding(8, 6, 0, 0);
            ForeColor = Color.Gray;
            Text = "3D: 미적용";
        }

        /// <summary>업데이트 필요 시 배경을 강조할 [가시화 적용] 버튼 연결.</summary>
        public void AttachApplyButton(Button button)
        {
            _applyButton = button;
        }

        /// <summary>가시화 적용 직후 호출 — 무엇 기준으로 칠해졌는지 기록 (예: "OASIS · 기준일 07-11").</summary>
        public void SetApplied(string basis)
        {
            _appliedOnce = true;
            IsStale = false;
            ForeColor = AppliedColor;
            Text = $"3D: {basis} · {DateTime.Now:HH:mm} 적용됨";
            Highlight(false);
        }

        /// <summary>데이터/설정이 3D와 어긋났을 때 호출 — 사유와 함께 업데이트 필요 표시.</summary>
        public void MarkStale(string reason)
        {
            if (!_appliedOnce) return;
            IsStale = true;
            ForeColor = StaleColor;
            Text = $"⚠ 3D 업데이트 필요 ({reason})";
            Highlight(true);
        }

        /// <summary>가시화 해제(초기화) 후 — 미적용 상태로 복귀.</summary>
        public void SetCleared()
        {
            _appliedOnce = false;
            IsStale = false;
            ForeColor = Color.Gray;
            Text = "3D: 미적용";
            Highlight(false);
        }

        private void Highlight(bool on)
        {
            if (_applyButton == null) return;
            if (on)
            {
                _applyButton.UseVisualStyleBackColor = false;
                _applyButton.BackColor = StaleButtonBack;
            }
            else
            {
                _applyButton.BackColor = SystemColors.Control;
                _applyButton.UseVisualStyleBackColor = true;
            }
        }
    }
}
