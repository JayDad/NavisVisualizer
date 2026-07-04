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

**데이터 보관: 탭별 소스 슬롯 2개**
```csharp
enum ProgressSource { Excel, Server }

class DataSourceSlot<T>
{
    public List<T> Data;        // null = 미로드
    public string Label;        // 파일명 또는 "서버명.DB (쿼리시각)"
    public DateTime? LoadedAt;
}
// 탭 필드: _excelSlot, _serverSlot, _activeSource
// 기존 _spools 등은 "활성 슬롯의 Data"를 반환하는 프로퍼티로 치환
```
두 슬롯 모두 메모리에 유지 → 라디오 전환 시 재로드 없이 즉시 스위칭.

**UI: Progress Input 그룹 (탭 공통 → `ProgressInputPanel` UserControl로 추출)**
현재 `_btnLoad` + `_lblFile` 자리를 GroupBox로 교체. 5개 탭이 중복 구현하지 말고 공용 컨트롤 1개(`SourceChanged` / `ExcelLoadRequested` / `ServerLoadRequested` 이벤트)로:

```
┌ 실적 데이터 (Progress Input) ─────────────────────────────────┐
│ ◉ Excel    [Excel Import]       ● Spool.xlsx · 1,234건 · 14:02 │
│ ○ Server   [Load Server Data]   ○ (미로드)                     │
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

**가시화 버튼 명칭 변경 (전 탭 공통)**
| 현재 | 변경안 | 이유 |
|------|--------|------|
| `적용` | `가시화 적용` | 무엇을 적용하는지 명시 |
| `전체 초기화` | `가시화 해제` | "초기화"가 데이터/설정 리셋으로 오독됨. 실제 동작은 색상 override 제거 = 모델 원상복구 |

**단계 나누기**
- **Phase 1 (SQL 구성 확정 전 착수 가능)**: RowMapper 추출 리팩터링 + `DataSourceSlot` 도입 + `ProgressInputPanel` UI (Server 버튼은 "구성 대기" 비활성) + 버튼 명칭 변경
- **Phase 2 (공종별 구성 수령 후)**: `SqlServerLoader` 구현 + 서버 설정 UI + 모듈별 쿼리/컬럼 매핑

**트레이드오프 / 결정 보류**
- Cable Pull 탭은 행→노드/케이블 다대다 재구성 로직이 로더에 얽혀 있어 RowMapper 추출 난도가 높음 → Phase 1은 Spool/Hydrotest/Equipment/EIT 4개 먼저, Cable은 구성 수령 후 판단
- 서버 인증 방식(Windows 통합 vs SQL 계정)과 접속 정보 배포 방식은 현장 IT 정책 확인 필요

## 개발 규칙

- Navisworks Simulate 2022 / .NET Framework 4.8 타겟. `Autodesk.Navisworks.*` DLL은 Windows 설치 경로 참조.
- 리눅스/맥에서는 `dotnet` 빌드 불가 (COM interop + Windows-only DLL). Windows에서만 컴파일 검증.
- 새 탭 추가 시 그룹 결정:
  - "digit 포함 DisplayName" 매칭 → `TagSearcher` 재사용
  - 그 외 매칭 전략 → 새 `ModelItemSearcher` 인스턴스 + `ColorOverrideEngine` 생성자에 추가
