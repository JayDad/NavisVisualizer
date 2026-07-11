# UX Audit 검토 결과 (2026-07)

master 기준 UX audit 리포트를 항목별로 검토한 판정 기록. **이번에 구현한 것 / 변형 채택한 것 /
보류·기각한 것**과 그 근거를 남긴다. 구현분은 전부 Windows 실기 검증 대기 (이 환경은 Autodesk
DLL 부재로 컴파일 불가 — 개발 규칙 참조).

## 요약

| Audit 항목 | 판정 | 비고 |
|---|---|---|
| P0-1 데이터 상태 ↔ 3D 적용 상태 분리 | **구현 (경량형)** | `ApplyStatePanel` — 아래 상세 |
| P0-2 "적용" 버튼 명칭 구분 | **구현 (일부 변형)** | 가시화 적용 / 범위 적용 / 가시화 해제 |
| P0-3 긴 작업 단계·진행 표시 | **부분 구현** | 단계 문구 병기. 건수/경과/중단은 보류 |
| P1 Overview/Preflight 첫 화면 | **구현 (후속 반영, 2026-07)** | `OverviewTab` + `ScopePreflight` — 아래 상세 |
| P1 색상 설정 접기 | **구현** | `ColorEditCollapse`, 기본 접힘 |
| P1 반응형 레이아웃 | 보류 | Windows DPI 실측과 함께 진행해야 안전 |
| P1 리스트·통계·필터 통합 | 보류 | 검증된 현행 구조 유지, 실사용 피드백 후 |
| P1 Sub-system 선택 UX 재설계 | 대부분 보류 | 검색폭 88→120, 탭 색상해제 버튼만 반영 |
| P2 Tools 분리 | **부분 구현** | 탭명 "고급 진단" (이미 마지막 탭) |
| P2 저장·완료 알림 개선 | **구현** | `SaveNotifier` — 비모달 + 파일/폴더 열기 |
| P2 Empty state/오류 체크리스트화 | 보류 | Preflight와 같은 축 — 후속 |
| QW7 EIT 기준일 비활성 영역 숨김 | **구현** | 행 제거 (날짜 컬럼 생기면 복원) |

## 구현 상세

### P0-1. 3D 적용 상태 표시 — `UI/ApplyStatePanel.cs` (신규)

Audit가 지적한 핵심(소스 전환 시 리스트는 즉시 바뀌는데 3D는 이전 소스 기준으로 남고, 경고가
통계 라벨에 덮어써짐)을 전용 컨트롤로 해결:

- 상태 3단: `3D: 미적용`(회색) → `3D: {기준} · HH:mm 적용됨`(녹색) → `⚠ 3D 업데이트 필요 (사유)`(주황)
- 업데이트 필요 상태에서 [가시화 적용] 버튼 배경 강조 (Primary action 유도)
- 배치: 탭 상단 고정 상태바 대신 **가시화 버튼 행 우측** — 버튼과 상태가 한 시선에 있고,
  기존 레이아웃(세로 스크롤 TableLayoutPanel)에 행 추가 없이 들어감
- MarkStale 트리거 (탭별 배선): 데이터 소스 전환/재로드 · 기준일 변경 · 단계 체크 변경 ·
  (Sub-system) 선택 변경·시각화 모드 변경. 색/투명도 변경은 증분 반영이 즉시 되므로 stale 아님
- 6개 탭 전부 배선: Spool / Hydrotest / Equipment / EIT Tray / Cable(형상) / Sub-system
- 기존 `_lblStats` 경고 문구(⚠ 3곳)는 전부 제거 — **통계 라벨은 통계만**

Audit 원안 대비 축소한 것: 7단계 상태 모델(Not loaded/Loaded/Ready/Applying/Applied/Pending/Failed)
중 로드 상태는 `DataSourcePanel`이 이미 표시(●/○/✕)하므로 중복 표시하지 않았고, Applying은
진행바+단계 문구가 담당. **탭 제목 ● 배지**는 보류 — 탭 페이지 텍스트가 이미 건수 표시에 쓰이고,
MainDockablePanel↔탭 간 결합이 늘어 후속으로.

### P0-2. 버튼 명칭 (전 탭 통일)

| 구 | 신 | 근거 |
|---|---|---|
| `적용` (Spool/Hydro/Equip/Sub-system) | `가시화 적용` | EIT·Cable과 통일. CLAUDE.md §6 기존 결정 준수 |
| ScopePanel `적용` | `범위 적용` | 가시화 적용과 오인 방지 (audit P0-2 핵심) |
| `공종 초기화` | `이 탭 가시화 해제` | "초기화"의 데이터 리셋 오독 제거 (§6 취지) + 대상 명시 |
| `전체 초기화` | `전체 가시화 해제` | 동일. 동작(색 제거+숨김 복원)을 정확히 표현 |
| `매칭 Status 출력` | `매칭 Status 엑셀 출력` | §6에 이미 결정돼 있었으나 미반영이던 항목 |

버튼명을 참조하던 안내문("먼저 적용(가시화)을 실행하세요" 등)도 `[가시화 적용]`으로 일괄 갱신.

Audit 원안 중 **미채택**:
- `3D 가시화 업데이트`: §6에서 확정된 `가시화 적용`과 충돌 + 더 김. "업데이트 필요" 신호는
  ApplyStatePanel이 담당하므로 버튼명까지 바꿀 실익이 낮다고 판단
- `체크 단계 외 숨김` → `선택 단계만 3D에 표시`: 실제 동작이 "숨김 토글"(재클릭 = 전체 보기)이라
  '표시'로 바꾸면 토글 복귀 문구와 어긋남. 유지
- Reset/Export의 더보기 메뉴 이동: 3행 버튼 배치(§10 확정)를 유지. 후속 검토

### P0-3. 긴 작업 단계 표시 (부분)

- 인덱스 빌드: `모델 태그 인덱스 생성 중…`, 색칠: `색상 적용 중…` 문구를 marquee와 병기
  (기존 `Application.DoEvents()` 시점 활용 — 스레딩 변경 없음)
- Hydrotest/Equipment/Sub-system은 색칠 구간에 진행바 자체가 없던 것도 이번에 추가
  (Spool §10과 동일한 try/finally marquee)

**보류**: 처리 건수/경과 시간/중단 버튼. Navisworks API가 UI 스레드 구속이라 백그라운드 처리
불가 → 건수 표시는 `ColorOverrideEngine`/`ModelItemSearcher`에 진행 콜백(N건마다 DoEvents+라벨
갱신)을 심어야 하고, 중단은 "stage 배치 단위 안전 경계" 설계가 필요. 다음 단계 후보로 기록.
(참고: 콜백 삽입 위치 — `WalkAndIndex` 노드 카운터, `Apply*`의 stage 배치 루프.)

### P1. 색상 편집 접기 — `UI/ColorEditCollapse.cs` (신규)

- 기본 접힘: 단계 **체크박스 + 색 스와치**만 노출, ▼(색 변경)·투명도 콤보는
  `색상·투명도 편집 펼치기 ▾` 링크로 토글
- 탭별 빌더를 고치지 않고 패널 트리를 걸어 ComboBox·"▼" 버튼만 Visible 토글하는 방식 —
  6개 탭 공통 적용에 구조 변경 최소화. 절대폭 컬럼이라 숨겨도 레이아웃 안 흔들림
- **보류**: 회사 표준 색 Preset / 사용자 Preset 저장 / 색각 이상 팔레트 — 설정 저장 인프라
  (`%APPDATA%` json)와 함께 별도 과제

### P2. 저장 완료 알림 — `UI/SaveNotifier.cs` (신규)

- 모든 CSV 저장 완료(매칭 Status/비교 출력/Template/Sub-system 리포트·상세/Cable clash)를
  모달 MessageBox → **비모달 소형 창**으로: 파일명·경로 + `[파일 열기] [폴더 열기] [닫기]`(ESC)
- 오류·확인 필수 상황은 MessageBox 유지. 고급 진단(Tools) 탭 출력물은 진단용이라 현행 유지
- **보류**: 저장 폴더 사용자 설정/다른 이름으로 저장 — 현행 Desktop 규약 유지 (설정 인프라 과제와 함께)

### 기타 구현

- **Tools → "고급 진단"** 탭명 변경 (이미 마지막 탭 위치). 내용물이 전부 진단·개발 기능임을 확인
  (Property/Tree Dumper, COM 쓰기 테스트, box 중복 검사, Clip Plane/Vertex 덤프). 설정 게이팅으로
  숨기는 것은 설정 인프라가 없어 보류 — 이름으로 위계만 표시
- **EIT Tray 기준일 행 제거** (비활성 컨트롤 노출 중단). 날짜 컬럼 확보 시(§3) Spool 패턴으로 복원
- **Sub-system**: 검색창 88→120px, `이 탭 가시화 해제` 버튼 신설(§10 잔여 해소 —
  `ResetModule(VisualModule.SubSystem)`), 색칠 구간 진행바

### P1. Overview 탭 — `UI/OverviewTab.cs` + `Services/ScopePreflight.cs` (후속 반영, 신규)

첫 심사에서 보류했던 항목을 사용자 요청으로 구현. 첫 번째 탭으로 배치:

- **공종 현황 표** — 6개 공종 탭이 `IOverviewSource.GetOverviewStatus()`(인메모리 스냅샷)로
  {데이터 소스·건수 / 인덱스 건수 / 3D 적용 상태 / 매칭·미매칭 / 인덱스 스코프 노트}를 노출.
  3D 상태·stale 여부는 `ApplyStatePanel`(P0-1) 상태를 그대로 재사용 — 정보원이 하나.
  행 더블클릭 = 해당 탭으로 이동. 미매칭>0 빨강, stale 주황, 스코프 fallback 주황.
- **NWD Preflight 표** — `ScopePreflight.Probe`가 공종별 스코프 체인(SPL→HYDROPKG / HYDROPKG /
  MEQ / EIT / CABLE)의 대상 파일 발견 여부를 **인덱스 빌드 없이** 판정.
  `ResolveScopeRoots`의 2단계 매칭(① Model.FileName/RootItem DisplayName ② 파일 노드 depth≤3
  얕은 하강)을 읽기 전용으로 미러링 — searcher의 `LastScopeNote`/`LastScopeFellBack`을 건드리지
  않는 별도 구현 (사전 점검이 실제 빌드 진단값을 덮어쓰면 안 되므로). 파일 노드만 따라가
  geometry walk가 없어 대형 모델에서도 즉시 수준.
- **갱신 정책**: 상태 캐시 없음(레슨런 L2 취지 — 라이브 상태는 캐시 금지). Overview 탭이
  선택될 때 자동 재조회(`TabControl.SelectedIndexChanged`) + [새로고침] 버튼.
- audit 원안의 "Action 버튼(열기/업데이트/OASIS 로드)" 열은 미채택 — 행 더블클릭 이동으로 대체
  (그 탭에 가면 어떤 액션이 필요한지 ApplyStatePanel·DataSourcePanel이 이미 보여줌).

## 보류 항목 상세 (후속 우선순위 제안)

1. **진행 콜백 + 중단** (P0-3 잔여) — 위 설계 메모 참조.
2. **반응형/DPI (P1)** — 절대폭이 전면에 깔려 있어(DataSourcePanel 100/105/18px 등) 일괄 수정은
   Windows 125/150% 실측과 병행해야 안전. Linux에서 맹수정하면 검증 불가 리스크만 커짐.
3. **리스트·통계·필터 통합 (P1)** — 전체/매칭/미매칭 TabControl → Segmented 필터 + KPI 클릭
   필터링. 동작엔 문제 없는 영역이라 실사용 피드백 후. (ListView를 탭 간 이동시키는 현재 구현은
   특이하지만 동작 검증됨.)
4. **Sub-system 선택 UX (P1)** — dual-list는 2026-07에 체크박스에서 전환한 지 얼마 안 된 사용자
   결정(§11)이라 재재설계는 실사용 후. Quick Filter(MCC 지연은 이미 있음 — Punch/ITR 미완료 추가),
   Detail Pane은 상세 현황 창이 대체 중.
5. **Empty state 체크리스트 / 오류 메시지 구체화 (P2)** — Overview/Preflight가 정보원 확보됨.
   각 탭의 "데이터를 먼저 로드하고 모델을 열어주세요"류 메시지를 조건별 체크리스트로 바꾸는
   것은 `ScopePreflight` + `GetOverviewStatus` 재사용으로 후속 가능.
6. **UI 언어 통일 (P2)** — 고급 진단 탭 영어 혼용. 낮은 우선순위.

## Windows 검증 게이트 (이번 변경분)

1. `ApplyStatePanel`이 FlowLayoutPanel 버튼 행에서 세로 정렬·잘림 없이 표시되는지 (Padding 8,6)
2. `ColorEditCollapse` 기본 접힘 상태에서 색 스와치·체크박스 클릭 동작, 펼침/접힘 토글
3. `SaveNotifier` 비모달 창이 Navisworks 메인 창 위에 정상 표시·포커스 복귀하는지,
   `explorer /select` 경로(공백 포함) 동작
4. Sub-system `이 탭 가시화 해제`가 다른 공종 색을 건드리지 않는지 (`_paintedByModule` 경로)
5. 단계 문구가 marquee 동안 실제로 갱신되어 보이는지 (DoEvents 타이밍)
6. Overview: federated NWD에서 Preflight 판정이 실제 인덱스 빌드의 `인덱스 스코프` 노트와
   일치하는지 (`ScopePreflight`가 ResolveScopeRoots를 미러하므로 어긋나면 미러 누락 의심),
   문서 미열림/문서 전환 직후 새로고침 시 예외 없이 "모델 미열림" 표시되는지,
   행 더블클릭 탭 이동 동작
