using System;
using System.Collections.Generic;
using System.Windows.Forms;
using NavisVisualizer.Services;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// "현황 집계 범위" group: mutually exclusive scope radios + an [적용] button.
    /// Radios only select — nothing is recomputed until [적용] raises
    /// <see cref="ApplyRequested"/> (deliberate: scope judgement cost is paid only on
    /// an explicit user action). The scope actually reflected on screen is shown in
    /// the group title "(현재: …)", so a radio that was clicked but not yet applied
    /// cannot be mistaken for the active one.
    /// </summary>
    public class ScopePanel : UserControl
    {
        /// <summary>Fired when the user presses [적용]. The tab runs the judgement.</summary>
        public event EventHandler ApplyRequested;

        private readonly GroupBox _group;
        private readonly Dictionary<MatchScope, RadioButton> _radios
            = new Dictionary<MatchScope, RadioButton>();

        /// <summary>Scope currently checked in the radios (may not be applied yet).</summary>
        public MatchScope SelectedScope
        {
            get
            {
                foreach (var kv in _radios)
                    if (kv.Value.Checked) return kv.Key;
                return MatchScope.FullModel;
            }
        }

        /// <summary>Scope last applied — what the list/stats on screen are based on.</summary>
        public MatchScope CurrentScope { get; private set; } = MatchScope.FullModel;

        public ScopePanel()
        {
            Height = 52;

            _group = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = Title(MatchScope.FullModel),
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = true,
                Padding = new Padding(4, 0, 4, 0),
            };

            foreach (var scope in MatchScopeInfo.Ordered)
            {
                var radio = new RadioButton
                {
                    Text = MatchScopeInfo.Label(scope),
                    AutoSize = true,
                    Checked = scope == MatchScope.FullModel,
                    Margin = new Padding(3, 3, 8, 0),
                };
                _radios[scope] = radio;
                flow.Controls.Add(radio);
            }

            var btnApply = new Button
            {
                Text = "적용",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 0, 8, 0),
            };
            btnApply.Click += (s, e) => ApplyRequested?.Invoke(this, EventArgs.Empty);
            flow.Controls.Add(btnApply);

            _group.Controls.Add(flow);
            Controls.Add(_group);
        }

        /// <summary>Record the applied scope and reflect it in the group title.</summary>
        public void SetCurrentScope(MatchScope scope)
        {
            CurrentScope = scope;
            _group.Text = Title(scope);
        }

        /// <summary>Back to the default state (radio + title). Does not raise events.</summary>
        public void ResetToFullModel()
        {
            _radios[MatchScope.FullModel].Checked = true;
            SetCurrentScope(MatchScope.FullModel);
        }

        private static string Title(MatchScope scope) =>
            $"현황 집계 범위 (현재: {MatchScopeInfo.Label(scope)})";
    }
}
