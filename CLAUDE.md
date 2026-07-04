# CLAUDE.md

프로젝트 개요·아키텍처는 `ARCHITECTURE.md` 참조. 이 문서는 **향후 고려사항 / 설계 메모**를 정리한다.

## 향후 고려사항

### 1. Federated NWD 모델 스코핑 (우선순위: 높음)

**배경**
현재 `ModelItemSearcher.BuildIndex`는 `doc.Models` 전체를 순회한다. 실제 현장에서는 Equipment / Piping / ELEC가 **별도 RVM → 개별 NWD**로 추출되어 Navisworks에서 federated 상태로 올라오는 구조가 일반적이다.

```
Document
├── Models[0] "Equipment.nwd"   ← Equipment 탐색 대상
├── Models[1] "Piping.nwd"      ← Spool / Hydrotest 탐색 대상
└── Models[2] "ELEC.nwd"        ← EIT Tray 탐색 대상
```

이 구조에서는 Spool 인덱싱 시 ELEC·Equipment 트리를 포함해 전체를 walk할 필요가 없다.

**개선안**
- `ModelItemSearcher.BuildIndex(Document doc, Predicate<Model> modelFilter = null)` 시그니처 확장 (null이면 전체 — 기존 동작 유지)
- 각 탭이 자기 대상 모델을 식별하는 필터 제공:
  - EquipmentTab: `m => m.FileName에 "EQ" 포함`
  - Spool / HydrotestTab (TagSearcher 공유 사용): `m => "PIPE"/"PIPING" 포함`
  - EitTrayTab: `m => "ELEC"/"EIT" 포함`
- 필터로 한 건도 못 찾으면 **전체 모델로 자동 fallback** + 로그

**기대 효과**
인덱스 빌드 시간이 N(federated 모델 수)분의 1로 수렴. ELEC 모델이 클 때 Spool/Equipment 작업에 영향 없음.

**트레이드오프**
- 모델 식별을 파일명 키워드에 의존하면 파일명 규약이 깨질 때 fallback 발동 → 실질적 속도 저하. DisplayName 패턴 기반 자동 감지나 사용자 지정 드롭다운을 선택지로 열어둘 것.
- TagSearcher를 Spool / Hydrotest / EIT Tray가 공유하는데, 각자 대상 모델이 다르면 단일 인덱스로는 최적화 불가 → TagSearcher를 `PipingTagSearcher` + `ElecTagSearcher`로 한 번 더 분리하는 옵션도 고려.

**결정 보류 이유**
사용자 측 파일명·모델 구조 규약이 확정되지 않아, 현 시점에서 키워드를 하드코딩하기보다 규약 확정 후 적용하기로 함.

### 2. WalkAndIndex 조기 정지 (우선순위: 낮음)

**[해결됨] digit 보유 파일 노드로 인한 조기 정지**
federated 구조에서 `MEBTray1.nwc`(파일명에 digit)가 `/SM/MEB/ELEC` → `/.../PCVTRAY`(digit 없는 범주) 위에 있으면, "tag-like인데 자식에 digit 없음 → STOP"이 depth 1에서 발동해 하위 ELEC 트레이가 통째로 미인덱싱 → 매칭 0건이 되던 문제. 현재 `WalkAndIndex`는 tag-like 노드라도 **구조 컨테이너(geometry 없고 자식 있는 자식)**가 있으면 계속 내려가고, 자식이 전부 geometry leaf일 때만 정지하도록 수정됨.

**[잔여] 과다 방문**
`/CM/PDA/ELEC/PCVTRAY-STW`처럼 digit 없는 범주 노드 바로 아래에 geometry가 직접 붙으면, 그 노드는 애초에 tag-like가 아니라 정지 로직을 안 타고 geometry까지 방문. 단일 NWD에서 최적화가 필요하면 `BuildIndexForTrayIds`(Equipment의 `BuildIndexForTags`와 동일 방식)를 추가하는 방안 검토.

### 3. Cable Stage 날짜화 (EIT Tray)

현재 `EitTrayData.GetStageAtDate`는 Tray install date만 날짜로 쓰고 Cable Pulling / Completed는 *현재 상태*로 판정한다. 입력 데이터에 `Cable Pull date` / `Cable Complete date` 컬럼이 추가되면 `Dictionary<EitStage, DateTime?> StageDates` 패턴으로 교체 (Spool/Hydrotest와 동일 구조).

### 4. Cable "보이는 것만" 필터 — 단면(Clip Plane) 보정 (Windows 검증 필요)

`CableTab`의 `보이는 것만` 체크박스는 노드별 박스(점 마커)의 `BoundingBox().Center`가 화면에 보이는지로 리스트를 거른다. 단면(clip plane)은 플러그인이 만들지 않고 Navisworks 기본 Sectioning으로 자른 것을 읽기만 한다. 판정 = **비숨김(`SectionService.IsEffectivelyHidden`, 조상까지 검사) AND 활성 단면 평면 내부**. 노드에 박스 여러 개면 하나라도 보이면 visible.

- 단면 평면은 관리형 API에 노출되지 않아 `SectionService`가 COM을 late-binding(IDispatch)으로 읽는다. `UserDataService`와 동일 패턴.

**Navisworks 2022 실측으로 확정된 COM 경로 (중요 — 다시 헤매지 말 것):**
```
ComApiBridge.State (InwOpState10)
  .CurrentView                       → InwOpView
  .ClippingPlanes()                  → InwOpClipPlaneColl   (.Count, .Item(i) 1-based)
  Item(i)                            → InwOaClipPlane
       .Enabled (bool)
       .Plane                        → InwLPlane3f
```
- **`InwLPlane3f` 멤버 (메서드, params=0)**: `GetNormal()` → 단위 법선벡터(InwLUnitVec3f, 성분 `data1/data2/data3`), `distance()` → 평면의 원점기준 부호거리. (그 외 `DistanceEx`, `SetValue(2)`, `Copy`.)
- **주의**: COM 객체는 `GetTypeInfo`로 베이스(`InwBase`: nwReadOnly/nwHandle/nwID/Xtension/ObjectName…)만 노출되고, geometry 멤버(`distance`,`GetNormal`)는 **이름 late-binding(GetIDsOfNames)으로만** 잡힌다. `distance`는 PROPERTYGET이 아니라 **INVOKE_FUNC**이므로 메서드 호출로 읽어야 함(`Invoke`가 property→method 순으로 시도하므로 OK).
- 평면식: `Eval(p) = n·p - distance`(점의 평면기준 부호거리). keep쪽은 `SectionService.KeepPositiveSide`로 보정. 단면 안/밖이 반대면 이 플래그만 뒤집기.
- 부호/단위 의심 시 Tools 탭 **`Clip Plane 덤프`** (단계 추적 + `GetNormal`/`distance` 값 + IDispatch 멤버 열거).
- 단면 변경 자동 감지 이벤트는 안 걸어둠 → 체크박스 토글/검색/`새로고침` 버튼 시점에 재평가.

### 5. Cable Node Box 중복 검사 (Tools 탭)

노드당 `-BOX`가 2개 이상이면 박스 생성 매크로 오류 의심. `ModelItemSearcher.GetEntriesWithMultipleItems`로 box 인덱스에서 key당 item>1을 뽑아 CSV 출력.

### 6. SQL Server 실적 데이터 소스 — Progress Input 이원화 (우선순위: 높음, 확장설계안)

**배경**
현재 실적 입력은 Excel 단일 소스. SQL Server에서 직접 실적을 읽는 옵션을 추가하되 Excel import와 **공존**해야 한다 — 평소엔 서버 데이터로 보다가 필요할 때 Excel import본으로 전환하는 시나리오. 공종(모듈)별 SQL 데이터 구성(테이블/뷰/컬럼)은 추후 별도 확정 예정이므로, 지금은 구성이 어떻게 오든 수용 가능한 구조만 잡아둔다.

**핵심 설계 원칙: 두 소스가 같은 데이터 모델로 수렴**
`ExcelLoader`는 ExcelDataReader로 `DataSet`을 만든 뒤 행→모델 매핑을 한다. SQL도 `SqlDataAdapter.Fill` → `DataTable`이므로 **두 경로가 DataTable에서 합류**한다:

```
Excel 파일 ──ExcelDataReader──▶ DataTable ─┐
                                           ├─▶ RowMapper (헤더 키워드 매핑, 기존 로직 추출) ─▶ List<SpoolData> 등
SQL Server ──SqlDataAdapter──▶ DataTable ─┘
```

- `ExcelLoader.LoadXxx`의 "헤더 탐지 + 행→모델 변환" 부분을 모듈별 `RowMapper`로 추출, `ExcelLoader`와 신규 `SqlServerLoader`가 공유
- SQL 뷰 컬럼명을 Excel 헤더 키워드와 동일하게 맞추도록 요청하면 매핑 코드 제로 — 다르면 모듈별 컬럼 매핑만 `SqlServerLoader`에 추가
- 다운스트림(Stage 계산 → Searcher → ColorOverrideEngine)은 `List<XxxData>`만 받으므로 **전혀 수정 없음**
- 드라이버는 .NET Framework 4.8 내장 `System.Data.SqlClient` 사용 (신규 NuGet 없음 → Navisworks 플러그인 어셈블리 바인딩 리스크 회피)

**명칭: 서버 소스의 사용자 표기는 "OASIS"**
OASIS가 기간계 시스템이고, 실적이 OASIS → SQL Server view table로 적재되는 구조. 사용자는 "서버"보다 OASIS를 인지하므로 UI 표기는 전부 OASIS로: 라디오 `OASIS`, 버튼 `[Load OASIS Data]`, 미구성 라벨 `— (OASIS 구성 대기)`. 내부 코드 식별자도 `Oasis`로 통일 (연결 대상이 SQL Server라는 사실은 구현 세부 — `SqlServerLoader` 클래스명은 유지).

**데이터 보관: 탭별 소스 슬롯 2개**
```csharp
enum ProgressSource { Excel, Oasis }

class DataSourceSlot<T>
{
    public List<T> Data;        // null = 미로드
    public string Label;        // 파일명 또는 "서버명.DB (쿼리시각)"
    public DateTime? LoadedAt;
}
// 탭 필드: _excelSlot, _serverSlot, _activeSource
// 기존 _spools 등은 "활성 슬롯의 Data"를 반환하는 프로퍼티로 치환
```
두 슬롯 모두 메모리에 유지 → 라디오 전환 시 재로드/재파싱 없이 즉시 스위칭. 한 소스의 로드는 다른 슬롯을 절대 건드리지 않는다 (같은 소스 재로드 시에만 해당 슬롯 갱신).

- **매칭 추적(`_matchedIds`/`_unmatchedIds`)도 슬롯별 보관** — 소스 전환 시 그 소스로 마지막 적용했을 때의 매칭 O/X가 그대로 복원되어야 함 (전환으로 매칭 표시가 사라지거나 타 소스 결과와 섞이면 안 됨)
- **`가시화 적용` = 사실상 그래픽 변경만**: 모델 인덱스는 모델 트리에서 만들므로 소스와 무관 — `NeedsRebuild`(모델 변경) 시에만 재빌드, 소스 전환은 재빌드 사유 아님. Stage 계산·매칭 조회는 메모리 연산이라 즉시 수준, 시간은 색상 override 배치가 씀
- **Equipment 예외**: `BuildIndexForTags`가 *데이터의 태그 목록*으로 인덱스를 만들므로, 활성 소스의 태그 중 인덱스에 없는 태그가 있으면 재빌드 필요. 적용 시 "활성 데이터 태그 ⊄ 인덱스 키" 체크 추가 (같은 공종 데이터라 태그 대부분 겹침 → 실제 재빌드는 드묾)

**UI: Progress Input 그룹 (탭 공통 → `ProgressInputPanel` UserControl로 추출)**
현재 `_btnLoad` + `_lblFile` 자리를 GroupBox로 교체. 5개 탭이 중복 구현하지 말고 공용 컨트롤 1개(`SourceChanged` / `ExcelLoadRequested` / `ServerLoadRequested` 이벤트)로:

```
┌ 실적 데이터 (Progress Input) ─────────────────────────────────┐
│ ◉ Excel    [Excel Import]       ● Spool.xlsx · 1,234건 · 14:02 │
│ ○ OASIS    [Load OASIS Data]    ○ (미로드)                     │
└────────────────────────────────────────────────────────────────┘
```

- **라디오버튼** (체크박스 아님 — 가시화 기준 소스는 한 번에 하나이므로 상호배타)
- 상태 표시: `○ (미로드)` 회색 / `● 라벨·건수·시각` 검정 / `✕ 로드 실패` 빨강
- 라디오는 해당 슬롯이 로드된 경우에만 활성화. 로드 성공 시 그 소스를 자동으로 활성 소스로 전환
- 소스 전환 시: 리스트/통계/매칭 추적 즉시 갱신. 색상이 이미 적용된 상태면 "⚠ 화면 색상은 {이전 소스} 기준 — 가시화 적용 필요" 표시 (자동 재적용은 안 함 — 대형 모델에서 의도치 않은 수 초 블로킹 방지)
- 서버 로드는 네트워크 대기가 있으므로 기존 `_progressBar`(marquee) 재사용 + 버튼 비활성화, `SqlCommand.CommandTimeout` 명시

**서버 연결 구성 (공종별 구성 확정 전까지 열어둘 부분)**
- 연결 정보(서버/DB/인증)는 전 모듈 공유, 모듈별로는 쿼리(뷰 이름)만 다르다고 가정
- `%APPDATA%\NavisVisualizer\server.json` + Tools 탭에 "서버 설정" 버튼(연결 테스트 포함)
- 공종별 구성이 뷰가 아니라 조인/파라미터(프로젝트 코드 등)로 오면 모듈별 쿼리 빌더로 확장 — `SqlServerLoader.LoadXxx(ServerConfig)` 시그니처는 동일 유지

**공종별 OASIS 버튼 활성화 게이팅**
구성은 공종별로 순차 확정되므로 활성화도 **탭(공종) 단위**로 게이팅한다. 전역 on/off 아님 — Spool 구성만 확정된 시점엔 Spool 탭 OASIS 버튼만 살아있어야 함.
- `server.json`의 `modules` 섹션에 해당 공종 엔트리(뷰/쿼리)가 존재할 때만 그 탭의 `[Load OASIS Data]` 버튼 + OASIS 라디오 활성화
- 미구성 탭은 버튼 비활성 + 상태 라벨 `— (OASIS 구성 대기)` 회색 표시. 버튼을 숨기지 않고 비활성으로 두는 이유: 기능이 존재한다는 것을 사용자가 인지하도록
- `ProgressInputPanel`에 `ServerConfigured` bool 프로퍼티 하나로 노출 — 각 탭이 config 로드 후 세팅
- Phase 1에서는 `modules` 섹션이 비어 있으므로 전 탭 자동 비활성 (별도 코드 불필요, 게이팅 로직 자체가 Phase 1 범위)

**가시화 버튼 명칭 + 위치 변경 (전 탭 공통)**
| 현재 | 변경안 | 이유 |
|------|--------|------|
| `적용` | `가시화 적용` | 무엇을 적용하는지 명시 |
| `전체 초기화` | `가시화 해제` | "초기화"가 데이터/설정 리셋으로 오독됨. 실제 동작은 색상 override 제거 = 모델 원상복구 |

- 위치: 핵심 기능이므로 하단 버튼 행에서 **Progress Input 그룹 직하단**으로 승격, 2버튼 전용 행(강조 스타일). 데이터 로드/전환 → 가시화가 한 시선 흐름, 소스 전환 경고(⚠)와 인접해 "다시 적용" 동선 단축
- 보조 기능(`속성 쓰기` / `Viewpoint 저장` / `NWD Export`)은 기존 하단 행 유지 — 출력/배포용이라 사용 빈도 낮음
- 전 공종 동일 배치 (`ProgressInputPanel` + 가시화 버튼 행을 한 세트로 두는 것도 고려 — 탭별 배치 편차 원천 차단)

**리스트 영역 UI 카피 (전 탭 공통)**
- 매칭/미매칭 필터 탭 옆에 회색 remark 추가: `※ 실적 데이터와 매칭 여부를 뜻함`
- `매칭 Status 출력` → `매칭 Status 엑셀 출력` (실제 출력물은 Excel에서 바로 열리는 CSV — 사용자 관점 명칭)

**단계 나누기**
- **Phase 1 (SQL 구성 확정 전 착수 가능)**: RowMapper 추출 리팩터링 + `DataSourceSlot` 도입 + `ProgressInputPanel` UI (Server 버튼은 "구성 대기" 비활성) + 버튼 명칭 변경
- **Phase 2 (공종별 구성 수령 후)**: `SqlServerLoader` 구현 + 서버 설정 UI + 모듈별 쿼리/컬럼 매핑

**트레이드오프 / 결정 보류**
- Cable Pull 탭은 행→노드/케이블 다대다 재구성 로직이 로더에 얽혀 있어 RowMapper 추출 난도가 높음 → Phase 1은 Spool/Hydrotest/Equipment/EIT 4개 먼저, Cable은 구성 수령 후 판단
- 서버 인증 방식(Windows 통합 vs SQL 계정)과 접속 정보 배포 방식은 현장 IT 정책 확인 필요

### 7. 매칭 현황 집계 범위(Scope) 필터 — 전 공종 확장 (채택)

**배경**
현재 clipping/가시성 기준 필터는 Cable Pull 탭의 `보이는 것만` 체크박스에만 존재 (비숨김 + 활성 clip plane 내부 판정, `SectionService` 재사용 — 4번 항목). 다른 공종의 매칭 리스트/현황도 clipping area 등 범위 기준으로 좁혀 보고 싶다는 요구. 단, 기준이 여러 개(숨김/clipping/선택)라 사용자가 헷갈리지 않도록 명시적 선택 UI가 필요.

**검토안: "매칭 집계 범위" 라디오 그룹 (상호배타)**
- ◉ **전체 모델** (default — 현행 NWD 파일 기준 그대로, 기존 사용에 영향 없음)
- ○ **숨김 제외** — hidden 처리 항목 제외 (`SectionService.IsEffectivelyHidden`, 조상까지)
- ○ **Clipping 영역** — 활성 단면 평면 내부만 (clip plane COM 판정 재사용)
- ○ **선택 항목** — 현재 3D 선택 기준

**설계 주의**
- 범위는 **리스트/통계 집계에만** 우선 적용, 색칠(가시화) 범위 연동은 별도 검토 — "집계는 좁혔는데 색은 전체에 칠해짐" 혼동 방지를 위해 현황 라벨에 `(Clipping 영역 기준)` 등 범위 병기
- 판정 위치: Cable은 box 마커 중심점이었고, Spool/Equipment 등은 매칭 노드의 `BoundingBox().Center`로 동일 판정 가능
- Cable 탭 기존 `보이는 것만` 체크박스와의 관계 정리 필요 — 라디오 그룹으로 흡수(체크박스 제거)가 일관적
- `매칭 Status 엑셀 출력`도 선택된 범위를 따름 + CSV 헤더에 범위 표기

**성능 설계 (라디오 전환마다 status 재계산)**
비용의 본질: 판정 대상이 전체 모델 geometry(수백만)가 아니라 **매칭된 노드(리스트 행 수 = 수천 건)뿐**이라 전환당 작업량 자체가 작다. 범위별 비용:
- `전체 모델`: 판정 없음 (즉시). `선택 항목`: CurrentSelection 조회 1회 (즉시)
- `숨김 제외`: 노드당 조상 체인 `IsHidden` 검사 — Cable `보이는 것만`에서 이미 수천 건 실사용 중, 문제 없음
- `Clipping 영역`: clip plane COM 읽기는 **전환당 1회**(노드당 아님), 노드당은 `BoundingBox().Center`(Navisworks가 미리 계산해 둔 값 조회) + 평면식 산술 → 수천 건이면 밀리초~수백 ms 예상

안전장치:
- **범위별 판정 결과 캐시** (`Dictionary<scope, HashSet<nodeKey>>`) — 같은 범위로 되돌아오는 전환은 0 비용. 캐시 무효화는 `가시화 적용`/`새로고침`/모델 변경 시에만 (단면·숨김 변경 자동 감지 이벤트는 안 걸므로 — 4번 항목과 동일 정책)
- 첫 판정이 오래 걸리는 대형 케이스 대비: 기존 marquee `_progressBar` 재사용 (UI freeze 인상 방지)
- Windows 실측으로 확정 필요 (특히 만 건 이상 매칭 시 BoundingBox 일괄 조회)

### 8. 매칭 Status 엑셀 출력 — 리포트화 (검토 단계)

**배경**
현재 출력은 행 단위 CSV 리스트(항목·Stage·매칭 O/X)뿐. 생산관리자들은 리스트에 더해 **통계치가 리포트 형태로 정리된 출력물**을 원함. Excel 출력 전반의 개선 검토 필요.

**리포트에 담을 후보 (요구사항 수집 후 확정)**
- 헤더 블록: 공종 / 기준일 / 데이터 소스(Excel 파일명 or OASIS 쿼리시각) / 집계 범위(7번) / 출력 시각
- Stage별 건수·비율 (+ 누적 %), 매칭/미매칭 건수·매칭률
- 구역·시스템 단위 소계 (Spool: ISO 그룹별, Equipment: Sub System별 등 — 공종별 그룹 축 상이)
- 주간 증감(전주 대비)은 스냅샷 보관이 필요해서 범위가 큼 — 별도 판단

**구현 옵션**
| 방식 | 장점 | 단점 |
|------|------|------|
| CSV 상단에 요약 블록 추가 | 의존성 제로, 즉시 가능 | 서식 없음, 시트 분리 불가 |
| `.xlsx` 생성 (ClosedXML/EPPlus 등) | 다중 시트(요약+리스트)·서식·차트 | NuGet 추가 — Navisworks 플러그인 어셈블리 바인딩 검증 필요 |
| Excel 템플릿(.xltx)에 값만 채움 | 서식은 템플릿이 담당, 코드 단순 | 템플릿 파일 배포 관리 필요 |

**방향 제안**: 단기는 CSV 요약 블록(리스트 위에 통계 섹션), 본격 리포트는 `.xlsx` 라이브러리 검증 후 "요약 시트 + 상세 리스트 시트" 2시트 구성. 생산관리자 요구 항목(어떤 통계·어떤 그룹핑)을 먼저 수집해서 확정할 것.

## 개발 규칙

- Navisworks Simulate 2022 / .NET Framework 4.8 타겟. `Autodesk.Navisworks.*` DLL은 Windows 설치 경로 참조.
- 리눅스/맥에서는 `dotnet` 빌드 불가 (COM interop + Windows-only DLL). Windows에서만 컴파일 검증.
- 새 탭 추가 시 그룹 결정:
  - "digit 포함 DisplayName" 매칭 → `TagSearcher` 재사용
  - 그 외 매칭 전략 → 새 `ModelItemSearcher` 인스턴스 + `ColorOverrideEngine` 생성자에 추가
