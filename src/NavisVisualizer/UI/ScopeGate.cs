using System;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using NavisVisualizer.Searchers;
using NavisVisualizer.Services;

namespace NavisVisualizer.UI
{
    /// <summary>
    /// 인덱스 빌드 직전의 스코프 게이트 (2026-09 — 전체 모델 자동 fallback 폐지의 짝).
    /// 스코프가 미지정이면 멈추고 사용자에게 셋 중 하나를 묻는다:
    ///   [파일 직접 지정…] → ScopeMappingDialog / [전체 모델에서 찾기] → AllModels 저장 / [취소]
    /// 매핑돼 있으면 아무것도 묻지 않는다 — 자동 인식이 맞는 문서(Trion)에선 종전과 동일한 흐름.
    /// 한 번 정하면 문서별로 저장되어 다음 적용부터 조용히 통과.
    /// </summary>
    public static class ScopeGate
    {
        /// <summary>스코프 → 사용자 표시 공종명 (Overview 표·대화상자 제목 공통).</summary>
        public static string TabLabelOf(NwdScope scope)
        {
            if (scope == null) return "-";
            if (scope == NwdScope.Structure) return "Structure";
            if (scope == NwdScope.Spool) return "Spool";
            if (scope == NwdScope.Hydrotest) return "Hydrotest";
            if (scope == NwdScope.Equipment) return "Equipment";
            if (scope == NwdScope.EitTray) return "EIT Tray";
            if (scope == NwdScope.Eit) return "EIT EQ (Sub-system)";
            if (scope == NwdScope.Cable) return "Cable";
            return scope.Key;
        }

        /// <summary>
        /// 스코프가 매핑돼 있으면 true. 미지정이면 안내 대화상자 → 사용자의 선택으로 매핑을 만든 뒤
        /// 다시 해석한 결과를 반환한다 (취소 = false — 호출부는 빌드를 건너뛰거나 0건으로 진행).
        /// </summary>
        public static bool EnsureMapped(IWin32Window owner, Document doc, NwdScope scope, string tabLabel = null)
        {
            if (doc == null || scope == null) return false;
            tabLabel = tabLabel ?? TabLabelOf(scope);

            ScopeResolution res;
            try { res = ScopeMappingService.Resolve(doc, scope); }
            catch (Exception ex)
            {
                MessageBox.Show(owner, $"모델 파일 해석 실패:\n{ex.Message}", "모델 파일 지정", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (res.IsMapped) return true;

            using (var prompt = new UnmappedPrompt(doc, scope, tabLabel, res))
            {
                var choice = prompt.ShowDialog(owner);
                switch (choice)
                {
                    case DialogResult.Yes:      // 파일 직접 지정…
                        return OpenMappingDialog(owner, doc, scope, tabLabel);
                    case DialogResult.Retry:    // 전체 모델에서 찾기
                        try
                        {
                            ScopeMappingService.SetSelection(doc, scope,
                                new ScopeSelection { Mode = ScopeSelectionMode.AllModels });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(owner, $"매핑 저장 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return false;
                        }
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>매핑 대화상자를 열고, 확인 후 매핑이 잡혔는지 반환 (Overview·게이트 공용).</summary>
        public static bool OpenMappingDialog(IWin32Window owner, Document doc, NwdScope scope, string tabLabel = null)
        {
            if (doc == null || scope == null) return false;
            using (var dlg = new ScopeMappingDialog(doc, scope, tabLabel ?? TabLabelOf(scope)))
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
                return dlg.Result != null && dlg.Result.IsMapped;
            }
        }

        /// <summary>"파일을 못 찾았음 — 어떻게 할까요?" 3택 프롬프트. Yes=직접 지정, Retry=전체 모델, Cancel.</summary>
        private class UnmappedPrompt : Form
        {
            public UnmappedPrompt(Document doc, NwdScope scope, string tabLabel, ScopeResolution res)
            {
                string docName;
                int fileCount = 0;
                try { docName = NwdScope.StripDirectory(doc.FileName ?? "(무제)"); } catch { docName = "(무제)"; }
                try { fileCount = ScopeMappingService.EnumerateFileNodes(doc).Count; } catch { }

                Text = $"모델 파일 지정 필요 — {tabLabel}";
                Width = 520;
                Height = 250;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                MinimizeBox = false;
                MaximizeBox = false;
                Font = new Font("Malgun Gothic", 9f);

                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14) };
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                string reason = res.Mode == ScopeSelectionMode.Files
                    ? $"직접 지정했던 파일이 이 문서에 없습니다: {string.Join(", ", res.MissingFiles)}"
                    : $"자동 인식 별칭({scope.ChainAliasLabel()})이 파일명에 없습니다.";
                var msg = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = $"'{tabLabel}' 공종이 쓸 모델 파일을 찾지 못했습니다.\n\n" +
                           $"문서: {docName} (파일 {fileCount}개)\n사유: {reason}\n\n" +
                           "파일을 직접 지정하면 다음부터는 묻지 않습니다. 전체 모델 검색은 모든 파일을 순회하므로 느립니다.",
                };

                var btnRow = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Top, AutoSize = true };
                var btnCancel = new Button { Text = "취소", Width = 80, Height = 30, DialogResult = DialogResult.Cancel };
                var btnAll = new Button { Text = "전체 모델에서 찾기 (느림)", Width = 170, Height = 30, DialogResult = DialogResult.Retry };
                var btnPick = new Button { Text = "파일 직접 지정…", Width = 140, Height = 30, DialogResult = DialogResult.Yes };
                btnPick.Font = new Font(Font, FontStyle.Bold);
                btnRow.Controls.Add(btnCancel);
                btnRow.Controls.Add(btnAll);
                btnRow.Controls.Add(btnPick);
                AcceptButton = btnPick;
                CancelButton = btnCancel;

                layout.Controls.Add(msg);
                layout.Controls.Add(btnRow);
                Controls.Add(layout);
            }
        }
    }
}
