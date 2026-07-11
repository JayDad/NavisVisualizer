# CLAUDE.md

프로젝트 개요·아키텍처는 `ARCHITECTURE.md` 참조. 이 문서는 **향후 고려사항 / 설계 메모**를 정리한다.

## 향후 고려사항

### 1. Federated NWD 모델 스코핑 (구현됨 — Windows 검증 대기)

**구현 현황**: 파일명 규약 확정(2026-07)에 따라 `Searchers/NwdScope.cs`(키워드 매칭, Autodesk
비의존 — `NwdScopeTests` 존재) + `ModelItemSearcher` 스코프 지원(BuildIndex /
BuildIndexForTags / BuildIndexForBoxes에 `NwdScope` 파라미터, null = 전체 = 기존 동작)으로
구현. 상세는 ARCHITECTURE.md "NWD 파일 스코핑" 참조.

**확정 파일명 규약과 스코프 배정**
```
00-02_Trion_Topsides_Subsystem.nwd     ← federated 컨테이너 (어느 스코프에도 미매칭 — 의도)
├─ 01-02_Trion_TopsidesLQ_Str.nwc      ← 구조. 플러그인 없음 (추후 block no별 공정 시각화 후보)
├─ 02-02_Trion_Topsides_HYDROPKG.nwd   ← Hydrotest. SPL 파일 부재 시 스풀도 여기 존재
├─ (03-..._SPL.nwd — 있으면)           ← 배관 스풀
├─ 04-02_Trion_Topsides_MEQ.nwd        ← Mechanical Equipment
├─ 05-02_Trion_Topsides_EIT.nwd        ← EIT 소형기기/Tray/Tray Support
├─ 07_Trion_All_Cable.nwd              ← 케이블 루트 (cable no.별 모델링)
└─ 09-02_Trion_Topsides_PIPSupport.nwd ← 배관 서포트. 플러그인 없음
```
- `NwdScope.Spool` = SPL, **없으면 HYDROPKG로 체인 fallback** (`NwdScope.Fallback` —
  "SPL 있으면 스풀은 SPL에만, 없으면 HYDROPKG 안에" 규약의 코드화. SPL 존재 시 HYDROPKG는
  walk 안 함) / `NwdScope.Hydrotest` = HYDROPKG. 초기 구현의 SPL∪HYDROPKG 합집합 공유는
  "SPL이 있어도 둘 다 walk"라 의미가 흐려 우선순위 체인으로 교체 (2026-07 사용자 결정).
  대신 Spool/Hydrotest searcher가 분리되어, SPL 없는 문서에선 같은 HYDROPKG 인덱스를
  탭별로 각각 빌드 (파일 단위 walk + lazy 빌드라 비용 미미)
- `NwdScope.Equipment` = MEQ / `NwdScope.EitTray` = EIT (`ElecTagSearcher` 분리) /
  `NwdScope.Cable` = CABLE / `NwdScope.SubSystem` = MEQ·SPL·HYDROPKG (`SubSystemSearcher` 분리
  — 요소인 Equipment TAG·Piping PKG가 어느 배관 파일에 있든 커버하는 합집합 유지)
- 구 `TagSearcher` 공유 인스턴스는 `SpoolTagSearcher`/`HydroTagSearcher`/`ElecTagSearcher`/
  `SubSystemSearcher` 4개로 분리, `ColorOverrideEngine` 생성자도 6개 searcher를 받도록 변경

**동작 방식**
- 우선순위 체인 (`NwdScope.Fallback`): 앞 스코프로 대상 모델을 못 찾을 때만 다음 스코프
  시도 (Spool = SPL → HYDROPKG). 어느 단계가 잡혔는지는 `LastScopeNote`에
  "스코프 SPL 없음 → HYDROPKG: 02-02_..." 형식으로 기록
- 2단계 매칭: ① `Model.FileName`/RootItem DisplayName (개별 공종 nwd만 열거나 append 구성)
  ② federated NWD를 연 경우 트리 안 **파일 노드**(확장자 보유 DisplayName)만 얕게(depth≤3)
  따라가며 매칭 — geometry 트리는 안 내려감. 디렉터리명 오탐 방지 위해 파일명만 비교
- 3중 자동 fallback (규약 깨져도 동작 유지): 체인 전체 대상 모델 없음 / Equipment 스코프 내
  태그 미발견 / 스코프 인덱스 0건 → 전체 모델 재인덱싱. `LastScopeNote`/`LastScopeFellBack`로
  노출되어 각 탭 매칭 Status CSV `인덱스 스코프` 행 + Tools 탭 박스 중복 검사에서 확인 가능
- **하드 스코프(`BuildIndex(..., hardScope: true)` — 2026-07)**: EIT Tray 탭은 "EIT nwd에서만"이
  요구라 위 전체-모델 fallback을 끈다. 스코프 파일 미발견/인덱스 0건이어도 전체 트리를 walk하지
  않고 빈 인덱스 + 진단 노트("하드 스코프: 전체 fallback 안 함 — 파일명 규약 확인")를 남긴다
  (federated 매칭이 어긋날 때 전 트리 순회로 인한 지연 방지 — EIT 적용 최다 지연 대책의 짝).
  `EitTrayTab`·`SubSystemTab` EIT EQ 빌드 둘 다 hardScope(ElecTagSearcher 공유 인스턴스라 일관 유지).
  스코프 키워드 "EIT"는 granular 복수 파일명(`05-02-01_..._EIT_Tray`, `05-02-02_LQRooms_EIT_Tray`)을
  전부 부분일치로 수집 — `ResolveScopeRoots`가 전 모델 순회라 파일 수 무관.

**잔여 / 주의**
- **Windows 실측 검증 필요**: federated NWD를 열었을 때 하위 파일이 `doc.Models` 복수로
  풀리는지, 단일 Model 밑 파일 노드로 오는지 (양쪽 다 대응해 뒀지만 실측 확인).
  fallback 발동 여부는 매칭 Status CSV의 `인덱스 스코프` 행으로 확인
- Cable node box nwd 파일명 규약 확정 시 `NwdScope.Cable` 키워드 추가 (현재는 0건 fallback으로 동작)
- 파일명 규약 변경 시 `NwdScope`의 키워드 + `NwdScopeTests`를 같이 갱신할 것
- Str(구조)·PIPSupport는 플러그인 신설 시 각자 키워드(STR / PIPSUPPORT)로 스코프 추가

### 2. WalkAndIndex 조기 정지 (우선순위: 낮음)

**[해결됨] digit 보유 파일 노드로 인한 조기 정지**
federated 구조에서 `MEBTray1.nwc`(파일명에 digit)가 `/SM/MEB/ELEC` → `/.../PCVTRAY`(digit 없는 범주) 위에 있으면, "tag-like인데 자식에 digit 없음 → STOP"이 depth 1에서 발동해 하위 ELEC 트레이가 통째로 미인덱싱 → 매칭 0건이 되던 문제. 현재 `WalkAndIndex`는 tag-like 노드라도 **구조 컨테이너(geometry 없고 자식 있는 자식)**가 있으면 계속 내려가고, 자식이 전부 geometry leaf일 때만 정지하도록 수정됨.

**[해결됨 2026-07 — Windows 검증 대기] 과다 방문 (EIT Tray 가시화 적용이 가장 느렸던 원인)**
`/CM/PDA/ELEC/PCVTRAY-STW`처럼 digit 없는 범주 노드 바로 아래에 geometry가 직접 붙으면, 그 노드는 tag-like가 아니라서 정지 게이트를 안 타고 **geometry 서브트리 전체를 COM으로 순회**했다. EIT nwd(트레이+소형기기+서포트)는 이 구조가 많아 ElecTagSearcher 일반 walk(= EIT Tray 탭 첫 [적용])가 전 탭에서 가장 느렸다. `WalkAndIndex`의 하강 게이트("자식 중 tag-like 또는 구조 컨테이너가 있을 때만 하강, 전부 geometry면 정지")를 **비태그 노드에도 동일 적용**해 geometry 숲 진입을 차단 — 일반 walk를 쓰는 모든 searcher(Hydro/ElecTag/SubSystem)가 공통 수혜.
- **전제(기존 태그 노드 정지 규칙과 동일)**: 태그는 컴포지트 노드 이름이며 geometry 인스턴스 아래에는 없다. Navisworks에서 `HasGeometry`는 leaf 인스턴스에서만 true라 컴포지트는 구조 컨테이너로 계속 하강된다.
- **리스크(Windows 실측 대조 필요)**: 종전엔 digit 보유 **geometry 노드 이름도 인덱싱**됐다. 만약 어떤 모델이 태그를 컴포지트가 아닌 geometry leaf 이름에만 갖고 있으면 이제 미매칭이 된다 — 매칭 건수를 수정 전과 대조할 것. 인덱스 0건이면 기존 3중 fallback이 동작하나 부분 누락은 못 잡는다(§2 레벨 타겟과 동일 성격).
- **[전환됨 2026-07 — Windows 검증 대기] EIT Tray = 레벨 타겟으로 전환**: general walk가 여전히 느려(자식 스캔 비용) `EitTrayTab.BuildIndex`를 `BuildIndex`→`BuildIndexForTags(trayIdSet, EIT, hardScope: true)`로 교체. "매칭 깊이 인덱싱 → 하위 geometry 트리 무시 → 옆으로"라 general walk의 자식 스캔이 사라짐. Sub-system EIT EQ가 별개 사유 searcher로 분리되면서 ElecTagSearcher는 이제 EIT Tray 전용 → 공유 충돌 없음. 소스 전환 시 `_needsIndexRebuild`(레벨 타겟은 트레이 셋 기반). **리스크(ELEC 트리 깊이 불균일 — MEBTray 사례)**: 트레이가 여러 깊이에 섞이면 첫 매칭 깊이만 인덱싱 → 일부 미색칠. Windows에서 general walk 시절 매칭 건수와 대조 필수. 되돌리려면 `BuildIndex(doc, NwdScope.EitTray, hardScope: true)` 한 줄로 복귀.

**[변경됨] Spool 인덱스 = 레벨 타겟으로 전환 (2026-07 — 성능 극대화)**
2만 스풀 기준 general `WalkAndIndex`가 스풀마다 "하강할지" 판단하려고 자식(전부 geometry인 경우 끝까지)을 COM으로 스캔 → 첫 빌드 ~1분. `SpoolTab.BuildIndex`를 `BuildIndex(NwdScope.Spool)`에서 **`BuildIndexForTags(spoolIdSet, NwdScope.Spool)`**(Equipment와 동일 레벨 타겟)로 교체. 스풀 id가 처음 매칭되는 깊이만 `IndexAtDepth`로 인덱싱하고 그 노드의 geometry 자식은 아예 안 건드림 → 자식 스캔 비용 제거.
- **리스크(수용됨, Windows 검증 대기)**: `BuildIndexForTags`는 첫 매칭 깊이 하나만 인덱싱한다. 스풀이 **여러 깊이에 섞여** 모델링돼 있으면 다른 깊이의 스풀은 미인덱싱 → 그 스풀만 미매칭이 된다(색 안 칠해짐). general walk는 전 깊이를 훑어 이 문제가 없었음 — 속도와 맞바꾼 것. federated 스코프 자체를 못 찾거나 스코프 내 태그 0건이면 기존 3중 fallback(전체 재인덱싱)이 동작하나, **"일부 깊이만 누락"은 fallback이 안 잡는다**(0건이 아니므로). 실측 시 매칭 건수를 general walk 시절과 대조할 것.
- 되돌리려면 `SpoolTab.BuildIndex`를 `BuildIndex(doc, NwdScope.Spool)` 한 줄로 복귀. Hydrotest/EIT는 아직 general walk 유지(스풀만 건수가 압도적이라 우선 적용).
- **소스 전환 재빌드 필수**: 레벨 타겟 인덱스는 활성 소스 태그 셋으로 깊이를 찾으므로, Excel↔OASIS 전환 시 재빌드해야 한다. `NeedsRebuild(doc)`는 모델 변경만 감지(소스 바뀌어도 doc 동일) → SpoolTab에 `_needsIndexRebuild` 플래그 추가(Equipment와 동일 패턴): 소스 전환 시 true, BuildIndex 후 false, 전 NeedsRebuild 호출부에 OR. 누락 시 소스 전환 후 옛 소스 깊이 인덱스가 잔존해 신규 소스 스풀 미매칭 위험.

### 3. Cable Stage 날짜화 (EIT Tray)

현재 `EitTrayData.GetStageAtDate`는 Tray install date만 날짜로 쓰고 Cable Pulling / Completed는 *현재 상태*로 판정한다. 입력 데이터에 `Cable Pull date` / `Cable Complete date` 컬럼이 추가되면 `Dictionary<EitStage, DateTime?> StageDates` 패턴으로 교체 (Spool/Hydrotest와 동일 구조).

### 4. Cable "보이는 것만" 필터 — 단면(Clip Plane) 보정 (Windows 검증 필요)

`CableTab`의 `보이는 것만` 체크박스는 노드별 박스(점 마커)의 `BoundingBox().Center`가 화면에 보이는지로 리스트를 거른다. 단면(clip plane)은 플러그인이 만들지 않고 Navisworks 기본 Sectioning으로 자른 것을 읽기만 한다. 판정 = **비숨김(`SectionService.IsEffectivelyHidden`, 조상까지 검사) AND 활성 단면 평면 내부**. 노드에 박스 여러 개면 하나라도 보이면 visible.

- **단면 BOX는 COM `ClippingPlanes()`에 안 들어온다** (그 컬렉션은 Planes 모드 평면만; 박스 걸어도 `Count`=1 = 잔여 평면 1개뿐 → "박스==단일 평면" 버그의 원인). 박스는 **관리형 `View.GetClippingPlanes()`가 JSON으로 노출**(`OrientedBox`)하므로 이걸 우선 읽는다:
  - `SectionService.GetActiveClipPlanes` = ① 관리형 JSON에서 enabled OrientedBox → 6개 반평면(안쪽 법선, `KeepPositiveSide=true`와 일치)으로 변환, ② 박스 아니면(Planes 모드/무단면) 기존 COM 경로. → Planes 모드는 무변경, 박스 능력만 추가.
  - `View.GetClippingPlanes()`는 **리플렉션으로 호출**(빌드별 시그니처 차이 시 컴파일 깨짐 대신 null → COM fallback). JSON 파싱은 `JavaScriptSerializer`(`System.Web.Extensions`).
  - **현재 축정렬(Rotation≈0) 박스만 확정 처리**. 회전 박스는 Rotation 규약 미확정이라 null 반환(잘못된 볼륨 배포 방지) → COM fallback. 회전 박스/포맷 확인은 Tools 탭 `Clip Plane 덤프` 최상단의 **원본 JSON**으로.
  - 단위 가정: 박스 좌표가 관리형 `BoundingBox().Center`와 같은 모델 단위 — Windows 실측 확인 필요.
- Planes 모드 단면 평면은 관리형에 (평면 리스트로는) 안 나와 `SectionService`가 COM을 late-binding(IDispatch)으로 읽는다. `UserDataService`와 동일 패턴.

**Navisworks 2022 실측으로 확정된 COM 경로 (Planes 모드 — 박스는 위 관리형 JSON 사용):**
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

### 6. SQL Server 실적 데이터 소스 — Progress Input 이원화 (일부 구현됨 — 잔여는 9번 참조)

> **구현 현황**: OASIS(SQL) 로드는 `SqlLoader` + `DataSourcePanel`(Excel/OASIS 이중 소스 + 적용 기준 라디오 + 비교 출력)로 Spool/Hydrotest/Equipment 3개 탭에 구현·머지됨. 아래 설계안 중 **가시화 버튼 명칭·위치 변경**과 **공종별 게이팅(server.json modules)**, EIT/Cable 이원화는 미적용 — 잔여 항목은 9번에 정리.

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

**상단 데이터 로드 UI 카피 (구현됨)**
- Excel 로드 버튼 문구 전 탭 `Excel Import`로 통일 (탭 이름이 공종을 이미 말해줌)
- 그 우측에 `[Template 출력]` 버튼 — 공종별 입력 양식 CSV를 바탕화면에 저장 (`Loaders/InputTemplate.cs`).
  헤더는 ExcelLoader 탐지 키워드와 1:1 — **로더의 FindColumn 후보를 바꾸면 InputTemplate도 같이 갱신할 것**.
  CSV는 ExcelDataReader가 못 읽으므로 안내문에 "작성 후 .xlsx로 저장" 명시 (안내문 행은 헤더 자동 탐지에 안 걸림)

**단계 나누기**
- **Phase 1 (SQL 구성 확정 전 착수 가능)**: RowMapper 추출 리팩터링 + `DataSourceSlot` 도입 + `ProgressInputPanel` UI (Server 버튼은 "구성 대기" 비활성) + 버튼 명칭 변경
- **Phase 2 (공종별 구성 수령 후)**: `SqlServerLoader` 구현 + 서버 설정 UI + 모듈별 쿼리/컬럼 매핑

**트레이드오프 / 결정 보류**
- Cable Pull 탭은 행→노드/케이블 다대다 재구성 로직이 로더에 얽혀 있어 RowMapper 추출 난도가 높음 → Phase 1은 Spool/Hydrotest/Equipment/EIT 4개 먼저, Cable은 구성 수령 후 판단
- 서버 인증 방식(Windows 통합 vs SQL 계정)과 접속 정보 배포 방식은 현장 IT 정책 확인 필요

### 7. 매칭 현황 집계 범위(Scope) 필터 — 전 공종 확장 (구현됨 — Windows 검증 대기)

**그룹 명칭은 "현황 집계 범위"** (구 "매칭 집계 범위" — 매칭 리스트뿐 아니라 현황 전반을 좁힌다는 의미). 라디오 순서: **전체 모델 · 선택 항목 · 숨김 제외 · Clipping 영역**.

**구현 현황**: `Services/ScopeFilter.cs`(판정) + `UI/ScopePanel.cs`(라디오 그룹 UserControl) 공용 컴포넌트로 구현, 5개 탭 전부 배선 완료. Cable 탭 `보이는 것만` 체크박스 + `새로고침` 버튼은 라디오 그룹으로 흡수(제거). 구현 중 확정한 세부:
- **미매칭은 범위와 무관한 고정 전역 수치** (Spool/Hydrotest/Equipment/EIT):
  - 이들 탭의 미매칭 = **실적 데이터(Excel/OASIS)에는 있으나 모델에서 못 찾은 행** = 노드/위치가 없음 → 선택·단면·숨김 어떤 범위로도 판정 불가. 따라서 `InScope`는 미매칭을 항상 통과시키고, 카운트는 **항상 전체 미매칭 수**.
  - **표시 분리**: 미매칭을 스코프된 `매칭` 옆에 나란히 두면 "선택한 것 중 매칭 N/미매칭 M"으로 오독됨(M은 전역인데). 그래서 통계 라벨에서 미매칭을 빼고, **우측 코너 별도 라벨**(`_lblUnmatched`, "미매칭 N건", 회색)로 고정 표시. 탭 헤더 `미매칭 (N)`과 CSV/현황 출력도 전체 미매칭 그대로.
  - (초기엔 선택/Clipping에서 미매칭을 0으로 줄이는 안을 넣었다가, "미매칭=모델없음"의 본질을 흐린다는 판단으로 철회 — 미매칭은 데이터 품질 지표로 스코프와 직교.)
  - **Cable은 예외**: Cable의 미매칭 = box(위치 있음)인데 데이터 매칭 안 된 노드 → 위치가 있어 범위 판정이 가능하므로 매칭과 함께 스코프됨(코너 분리 불필요, 기존 유지).
- **[적용]은 항상 재계산** — 단면/숨김/선택은 이벤트 없이 실시간 변하므로 판정 결과를 **캐시하지 않는다**. (구 구현은 다른 범위로 전환했다 되돌아오면 캐시된 옛 판정이 되살아나 단면 해제 후에도 숫자가 안 바뀌는 버그가 있었음.) 판정 비용은 매칭 노드 수(수천) 수준이라 매 클릭 재계산이 부담 없음.
- `선택 항목` 판정은 선택된 노드의 조상/자손 양방향 포함. `HashSet<ModelItem>` 동등성 의존 — **Windows 실측 검증 필요**
- CSV 출력: 첫 행에 `집계 범위,{라벨}` + 범위 내 행만 출력
- Cable만 범위 판정을 매칭 여부와 무관하게 전 노드에 적용 (box 존재 자체가 판정 대상 — 기존 보이는 것만과 동일 의미론)
- **Clipping 영역은 여전히 "비숨김 AND 단면 내부"** (기존 `보이는 것만` 의미 유지). 숨김을 무시한 순수 기하 단면만 원하면 `ScopeFilter.Compute`의 ClippingVolume 분기에서 `IsEffectivelyHidden` 게이트만 제거하면 됨.

**배경**
현재 clipping/가시성 기준 필터는 Cable Pull 탭의 `보이는 것만` 체크박스에만 존재 (비숨김 + 활성 clip plane 내부 판정, `SectionService` 재사용 — 4번 항목). 다른 공종의 매칭 리스트/현황도 clipping area 등 범위 기준으로 좁혀 보고 싶다는 요구. 단, 기준이 여러 개(숨김/clipping/선택)라 사용자가 헷갈리지 않도록 명시적 선택 UI가 필요.

**UI: "현황 집계 범위 (현재: xxx)" 라디오 그룹 + [적용] 버튼**
- ◉ **전체 모델** (default — 현행 NWD 파일 기준 그대로, 기존 사용에 영향 없음)
- ○ **선택 항목** — 현재 3D 선택 기준
- ○ **숨김 제외** — hidden 처리 항목 제외 (`SectionService.IsEffectivelyHidden`, 조상까지)
- ○ **Clipping 영역** — 비숨김 AND 활성 단면 평면 내부 (clip plane COM 판정 재사용)

**적용 버튼 방식 (확정)**: 라디오는 선택만 하고, 그룹 내 `[적용]` 버튼을 눌러야 재집계 실행. 라디오 클릭 자체는 아무 계산도 하지 않음 → 전환 성능 이슈 원천 차단. 현재 화면의 집계 기준은 그룹 제목 `(현재: 전체 모델)`에 항상 표시 — 라디오 선택과 실제 반영 상태가 달라도 사용자가 혼동하지 않음. 현황 라벨에도 `(Excel · Clipping 영역 기준)`처럼 소스+범위 병기.

**설계 주의**
- 범위는 **리스트/통계 집계에만** 우선 적용, 색칠(가시화) 범위 연동은 별도 검토 — "집계는 좁혔는데 색은 전체에 칠해짐" 혼동 방지를 위해 현황 라벨에 `(Clipping 영역 기준)` 등 범위 병기
- 판정 위치: Cable은 box 마커 중심점이었고, Spool/Equipment 등은 매칭 노드의 `BoundingBox().Center`로 동일 판정 가능
- Cable 탭 기존 `보이는 것만` 체크박스와의 관계 정리 필요 — 라디오 그룹으로 흡수(체크박스 제거)가 일관적
- `매칭 Status 엑셀 출력`도 선택된 범위를 따름 + CSV 헤더에 범위 표기

**성능 설계**
재계산은 `[적용]` 클릭 시에만 실행. 비용의 본질: 판정 대상이 전체 모델 geometry(수백만)가 아니라 **매칭된 노드(리스트 행 수 = 수천 건)뿐**이라 1회 작업량 자체가 작다. 범위별 비용:
- `전체 모델`: 판정 없음 (즉시). `선택 항목`: CurrentSelection 조회 1회 (즉시)
- `숨김 제외`: 노드당 조상 체인 `IsHidden` 검사 — Cable `보이는 것만`에서 이미 수천 건 실사용 중, 문제 없음
- `Clipping 영역`: clip plane COM 읽기는 **전환당 1회**(노드당 아님), 노드당은 `BoundingBox().Center`(Navisworks가 미리 계산해 둔 값 조회) + 평면식 산술 → 수천 건이면 밀리초~수백 ms 예상

안전장치:
- **판정 결과는 캐시하지 않고 [적용]마다 재계산** — 단면·숨김·선택은 자동 감지 이벤트가 없어 캐시하면 옛 상태가 되살아난다(단면 해제 후에도 숫자 그대로 = 구 버그). 매칭 노드 수(수천) 수준이라 매 클릭 재판정이 즉시급. (구 `Dictionary<scope, HashSet>` 캐시는 제거됨.)
- 첫 판정이 오래 걸리는 대형 케이스 대비: 기존 marquee `_progressBar` 재사용 (UI freeze 인상 방지)
- Windows 실측으로 확정 필요 (특히 만 건 이상 매칭 시 BoundingBox 일괄 조회)

### 8. 매칭 Status 엑셀 출력 — 리포트화 (검토 단계 — 단기안은 Sub-system 탭에 첫 구현)

> **구현 현황**: 아래 "단기 = CSV 요약 블록" 방향이 Sub-system 탭 `[현황 리포트 출력]`으로
> 첫 구현됨 (헤더 블록 + Sub-system별 요약 + 상세 리스트 — 11번 참조). 기존 4개 탭의
> `매칭 Status 출력`은 아직 행 단위 리스트 그대로 — 요약 블록 이식은 요구 항목 확정 후.

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

### 9. OASIS(SQL) 연동 잔여 항목

Spool / Hydrotest / Equipment 탭은 OASIS 로드 구현 완료 (`SqlLoader`). **실제 DB는 `[Navis]`
스키마 아래 BASE TABLE 10개**로 확정됨 (2026-07 사용자 실측 — 상세는 `docs/SQL_DB_CONNECTION_ANALYSIS.md`
부록). CSV 파일명이 `Navis_XXX`라 스키마.테이블(`[Navis].[XXX]`)과 헷갈리지만 실객체는 후자 —
기존 `SqlLoader`의 `FROM [Navis].[Piping_Spool]` 방식이 정확. 10개 = All_EQ, All_Support,
EIT_Cable, EIT_EQ, EIT_Route, EIT_Tray, Mech_EQ, Piping_HydrotestPKG, Piping_Spool, System_Summary.
남은 것:

- **Spool `FIT-UP`**: **[해결됨]** 설치 fit-up 단계(`Setting`↔`Welding` 사이)를 `SpoolStage.FitUpInstall`로
  추가(라벨 "FIT-UP"). 제작 `FitUp`("F/up")과 별개. 동시에 `Welding`의 **표시 라벨을 "Install"로** 변경
  (Welding+Flange Connection = 설치 완료 의미 — enum 멤버명 `Welding`은 DB 컬럼·캐시키 안정성 위해 유지).
  적용: enum/OrderedStages/Labels/ColumnMap/InstallStages/SpoolDefaults + `SqlLoader.LoadSpool`
  SELECT(`[FIT-UP]`)·매핑 + 테스트 count(15→16). Excel엔 FIT-UP 컬럼 없음 → 역순 스캔이 직전 단계로 자동 인식(무해).
- **Spool/Hydrotest 로더**: 실제 컬럼과 대조 완료 — 기존 SELECT 그대로 일치(수정 불필요).
  Hydrotest는 `System`/`Sub-System` 둘 다 있고 로더는 세밀한 `Sub-System` 사용(기존 결정 유지).
- **Equipment**: **[해결됨]** Mech_EQ 실제 컬럼 대조 완료 — 로더 기대 컬럼(`RFQ NO` 공백 형식 포함)
  전부 일치. **All_EQ는 사용자 결정으로 제외**(2026-07) → `LoadEquipment`는 `[Navis].[Mech_EQ]` 단독
  조회로 단순화(병합 루프 제거, 선행 `/` 정규화는 방어적으로 유지). All_EQ를 다시 살리려면 테이블 순회
  복원 + All_EQ 컬럼명 검증 필요. Mech_EQ 신규 컬럼(RFQ DES/L·W·H/Weight/SYSTEM/AITR·Punch 수치)은
  미사용 — 표시 확장 기회.
- **EIT Tray**: 진척 테이블 `[Navis].[EIT_Tray]` **존재 확인**(`BRANCH NO.`/`TRAY Install %`/`PJTNO`).
  단 **날짜 컬럼 없음**(`Tray install date` 부재) → 기준일 필터 불가, %기반 현재상태 판정으로 로더 설계 필요.
  `BRANCH NO.` 선행 `/` → `NormalizeId` 적용. (이번 3-탭 범위 밖.)
  **후행 `.` 장식(2026-07 실측)**: DB TRAY ID가 `X.`처럼 끝에 '.'이 붙은 행이 있어 매칭 실패 —
  `EitTrayData.NormalizeId`가 후행 '.'도 제거하도록 수정(+ Sql/Excel 로더 중복 제거를 정규화 키로).
  모델 DisplayName엔 이 장식이 없다는 가정 — 모델 쪽에도 붙어 있으면 인덱스 키 정규화 확장 필요.
- **Cable**: **[부분 해결 — Cable(형상) 탭 개통]** `EIT_Cable` 컬럼 철자 실측 확정(2026-07 사용자
  제공: 날짜 4개는 ` DATE` 접미사 — `PULLING START/END DATE`, `FROM/TO CONN DATE`) →
  `SqlLoader.LoadCable` 철자 교체 + 표시 필드(FROM/TO MODULE·EQUIP, TYPE/CORE/SIZE, OUT DIA,
  TRAY SYS, SYSTEM, Pulling %) 매핑 + `CableLineTab` DataSourcePanel 듀얼소스 배선 완료.
  `PULLING LTH`는 샘플상 포설 실적 길이로 보이나(0/189=0%, 37/37=100%) 오너 확정 전까지 표시 전용
  유지(§13-6). 프로젝트 컬럼 없음 → 전체 로드. 미사용: INSTALL_MODULE, SYSTEM DES, SUB-SYSTEM(+DES).
  **노드단위 집계는 폐기 — Cable(Node) 탭 삭제(2026-07 사용자 결정)**: `EIT_Route`에 홉 순서
  (SEQ)·NodeId 변환이 없어 개통 불가였고 형상 탭이 역할을 대체. `ApplyCable`/`CableNodeData`/
  `CableStage`/`LoadCablePull`/노드 필터포커스·박스숨김 일괄 제거. Tools 탭 box 중복 검사(§5)와
  `CableBoxSearcher`는 유지(박스 생성 매크로 QA — 탭과 무관).
- **EIT_EQ**: `[Navis].[EIT_EQ]` 컬럼 확장됨 — `WRKDTE`→`INSTALL DTE`(설치 실적일), `SUB-SYSTEM` +
  AITR/Punch 수치 보유. **Sub-system 요소로 편입됨(2026-07 — §11)**: 단일 단계(미착수/설치완료) +
  ElecTagSearcher 재사용. 전용 탭 신설은 여전히 미정(AITR/Punch 수치 미사용).
- **System_Summary = SubSystem_Master 대체**: **[해결됨]** 실 DB엔 `SubSystem_Master` 없음 —
  `LoadSubSystemMaster`를 `[Navis].[System_Summary]` 실측 스키마(2026-07 사용자 제공)로 재매핑 완료.
  매핑: `Sub-System`→SubSystemNo / `Sub-System Des`→Description / `MCC Plan` 동일 /
  `WD Actual`→Walkdown / `Partial MCC Actual`→P-MCC / `MCC Actual`→MCC / `PCC Actual`→PCC /
  `A-ITR Total·Complete` 등 ITR 3종 / `A·B Punch Total·Closed`. 프로젝트 필터 = `PJTNO`(기존 유지).
  **미사용 컬럼(확장 후보)**: `Area`/`System`/`System Des`(상위 그룹핑 축), `PCC Plan`/`MCC Fcst`
  (계획·예측일 — 현재 지연 판정은 MCC Plan만 사용), `%` 계열(Total/Complete 수치로 대체 가능해 제외).
  §11의 CREATE TABLE 계약은 폐기 — 실 테이블이 이미 존재.

### 10. 공종(모듈)별 초기화 — 메커니즘 구현됨 (Spool 배선, 나머지 탭 잔여)

**구현 현황(2026-07)**: `ColorOverrideEngine`에 `_paintedByModule`(모듈별 누적 painted 합집합) +
`ResetModule(doc, module)`(그 누적분만 `ResetPermanentMaterials` — 다른 공종 색 유지) +
`AccumulatePainted` 추가. **`ApplySpool`이 색칠 전 `ResetModule(Spool)` 호출** → 재적용 시
이전 적용 잔존(체크 해제 단계·기준일 변경으로 빠진 스풀)까지 정확히 원복 후 현재 활성 집합만
재도색. 이로써 **재적용 성능 저하(override 누적 → 투명 재처리)와 "체크 해제 stage 색 잔존"
버그 동시 해결**. SpoolTab `BtnApply_Click`에 색칠 진행바(marquee)도 추가(색칠 수 초간 UI 프리즈
"뻗은 느낌" 제거).

**구현 확대(2026-07)**:
- **Spool/Hydrotest/Equipment 3탭 모두** `Apply*`에 `ResetModule`+`AccumulatePainted` 이식 완료 —
  세 탭 다 재적용 누적(투명 재처리) 문제 해결. (Cable/Sub-system은 아직 잔여.)
- **`[공종 초기화]` 버튼 신설** — 3탭 각각 버튼 3행 배치:
  1행(가시화) `[적용][체크 단계 외 숨김]` / 2행(초기화) `[공종 초기화][전체 초기화]` /
  3행(출력) `[속성 쓰기]?[Viewpoint 저장][NWD Export]`. `공종 초기화`는 그 탭 색만
  `ResetModule`로 제거(다른 공종 유지) + 그 탭 숨김 복원. `전체 초기화`는 여전히 전역
  `ResetAllPermanentMaterials`.

**잔여**:
- ~~Cable(Node) 탭 `ApplyCable` 이식~~ — **탭 삭제(2026-07)로 소멸** (§9 Cable 참조).
  (Sub-system은 2026-07 `ApplySubSystem`에 이식 완료 — §11. Sub-system/Cable(형상)/EIT Tray의
  `공종 초기화` 버튼 배치는 잔여.)

**설계 메모(유지)**:
- API: `DocumentModels.ResetPermanentMaterials(ModelItemCollection)` (아이템 단위 리셋).
- **최신 캐시가 아니라 누적 painted 셋을 리셋** — 각 Apply는 활성 stage 전체를 재도색하므로,
  직전 Apply의 누적 painted 전체를 리셋하면 체크 해제/기준일 변경 잔존이 정확히 제거됨.
- Cable 모듈 리셋은 색 + `RestoreHiddenCableBoxes` + 필터 포커스 + cable 전용 캐시까지 필요(별도).
- Spool↔Hydrotest 겹침은 유리: Spool(하위 노드)만 리셋하면 상위 PKG 오버라이드가 다시 드러남.

### 11. Sub-system 탭 — 구현됨 (Windows 검증 대기) + 확장 잔여

**구현 현황**: `UI/SubSystemTab.cs` + `SqlLoader.LoadSubSystemElements`/`LoadSubSystemMaster` +
`ColorOverrideEngine.ApplySubSystem`(`VisualModule.SubSystem`) + `SubSystemElement`/
`ProgressStatus`/`SubSystemStage`/`SubSystemMasterData`(DataModels). OASIS 전용 — 요소는
기존 검증 쿼리(LoadEquipment/LoadHydrotest) 재사용 (Equipment `SUB-SYSTEM`→TAG NO /
Piping `Sub-System`→PKGNO, 미지정 행 제외 + 건수 보고). 매칭은 개발 규칙대로 TagSearcher
재사용 (Equipment 태그도 digit 포함 정확 일치라 전체 워크 인덱스로 조회됨 —
EquipmentSearcher의 레벨 타겟 인덱스는 건드리지 않음).

- **마스터 기준 목록**: `[Navis].[System_Summary]` 로드 성공 시 좌측 목록 = 마스터 ∪
  요소 파생 (요소 0건은 회색, 마스터 외 요소 그룹은 "(마스터 외)" + 건수 진단).
  **테이블 미구성이면 요소 파생 목록으로 자동 fallback** + 단계 모드 비활성.
- **가시화 2모드**: ① Sub-system 단계별 — 선택한 sub-system의 요소 전체가 그 sub-system의
  마일스톤 단계색(Walkdown/P-MCC/MCC/PCC + 미착수, 기준일 역순 스캔)을 받음
  (기본, 마스터 필요). **별도 RFCC 단계 없음** — MCC(또는 Partial MCC)가 Ready for
  Commissioning 의미. **마일스톤은 순차 아님** — P-MCC 없이 바로 MCC 가능. 역순 스캔이
  날짜 보유 여부만 보므로 스킵 자동 허용 (enum 순서 = 달성 수준 랭킹, 시간 순서 아님).
  ② 요소 진행상태별 — 미착수/진행중/완료 3단계 정규화.
- **MCC 지연 감지 (색으로는 표시 안 함)**: 마스터에 `MCC Plan`(계획일)이 있고 기준일까지
  도래했는데 P-MCC/MCC 실적이 미입력(실적 단계 < P-MCC)이면 `IsDelayed` = true.
  **지연은 별도 색(빨강)으로 칠하지 않는다** — 지연이어도 달성 단계(Walkdown 등)가 있어
  stage와 직교하기 때문. 대신 ⓐ 좌/우 테이블 `MCC계획` 컬럼에 "지연 Nd" 텍스트,
  ⓑ `[MCC 지연 담기]` 버튼(검색 옆)으로 지연 sub-system을 선택 박스에 일괄 담기,
  ⓒ 현황 라벨·리포트에 지연 개수/일수로만 노출. 3D 색은 실제 달성 단계 그대로.
- **상세 현황 별도 창**: 선택 박스 하단 `[선택 Sub-system 상세 현황 보기…]` → 비모달 Form.
  선택된 sub-system의 **공종(Equipment/Piping)·요소별 status**를 한 그리드에 나열
  (sub-system→공종→요소 정렬, 검색 필터, 행 더블클릭 시 3D 선택·포커스, `[CSV 출력]`
  으로 엑셀 저장). 다공종 상세 요소 현황을 한 창에서 전부 표시.
  (초기 구현의 "sub-system별 팔레트 고유색" 모드는 단계별 모드로 대체되어 제거 —
  `SubSystemPalette` 삭제됨.)
- **선택 UI (dual-list)**: 좌측 검색(코드+설명)+status 테이블(단계/ITR/Punch/요소 병기)
  ↔ [▶ ◀ ▶▶ ◀◀] 화살표 ↔ 우측 선택 누적 테이블(단계색 스와치) + 하단 개수 라벨.
  다중 선택 + 더블클릭 지원. 체크박스 방식에서 전환됨.
- CSV 현황 리포트(8번 단기안): 헤더 블록 + 요약(Description/단계/ITR/Punch 병기) + 상세.

**마스터 테이블 = `[Navis].[System_Summary]` (실측 스키마 — 구 SubSystem_Master 계약 폐기)**
```
Area, System, System Des, Sub-System, Sub-System Des,
PCC Plan, PCC Actual, MCC Plan, MCC Fcst, WD Actual, Partial MCC Actual, MCC Actual,
A-ITR Total, A-ITR Complete, A-ITR%, B-ITR Total, B-ITR Complete, B-ITR%,
C-ITR Total, C-ITR Complete, C-ITR%,
A Punch Total, A Punch Closed, A Punch %, B Punch Total, B Punch Closed, B Punch %, PJTNO
```
- 마일스톤 실적일 = `WD/Partial MCC/MCC/PCC Actual`, 지연 판정 기준 = `MCC Plan` (Fcst·PCC Plan 미사용).
- 미사용 컬럼: `Area`/`System`/`System Des`(상위 그룹핑 확장 후보), `MCC Fcst`/`PCC Plan`, `%` 계열.
- 컬럼명을 바꾸면 `SqlLoader.LoadSubSystemMaster`의 SELECT만 같이 수정 (명시 매핑이라 즉시 오류로 드러남).
- ITR/Punch가 수치가 아니라 %로 오면 GetInt 대신 ParsePercentage 계열 추가 검토 (§2.2 스케일 함정 참조).
- 화면 표기는 전부 "완료(종결)/전체" — 좌측 테이블 A-ITR/B-ITR/C-ITR/P.A/P.B 컬럼 + 리포트 요약.
- `MCC Plan` 미보유(null)면 지연 판정 안 함 — 계획일 없는 sub-system은 지연 대상에서 제외.
- 마일스톤 날짜 성격은 실 컬럼명으로 확정 — `MCC Plan`만 계획, 나머지는 `Actual` 접미사(실적일).

**확장 잔여 (결정/데이터 대기)**
- **마스터 로더 Windows 실측 검증**: `System_Summary` 재매핑(2026-07) 후 실 서버 대상
  로드·지연 판정 확인. 날짜 컬럼이 varchar로 오면 `GetDate` 문자열 파싱 경로로 처리됨(yyyy-MM-dd 확인됨).
- **Spool 단위 sub-system**: 현재 배관은 PKG 노드 색칠이 하위 스풀을 커버. 개별 스풀
  granularity가 필요해지면 `Piping_Spool`에 Sub-System 컬럼 계약 확정 후
  `LoadSubSystemElements`에 추가 (Discipline enum 확장).
- **EIT EQ / Cable 편입**: **[구현됨 2026-07 — Windows 검증 대기]** 요소 4공종으로 확장.
  `LoadSubSystemElements`가 `EIT_EQ`(TAG NO/INSTALL DTE 단일 단계 미착수·설치완료)·
  `EIT_Cable`(CABLE NO/날짜 4종, SUB-SYSTEM 실측 확정)을 공종별 try/catch로 편입
  (컬럼 미구성 시 그 공종만 제외 + 라벨 사유). **EIT Tray는 편입 안 함 — EIT_Tray에
  Sub-system 매핑 컬럼이 없음(2026-07 사용자 확정). 컬럼 추가 시 EIT EQ 패턴으로 재편입.**
  ApplySubSystem에 §10 ResetModule/AccumulatePainted도 이식(선택 축소 재적용 잔존 해결).
- **공종별 1:1 스코프 + 레벨 타겟 (2026-07 재설계 — Windows 검증 대기)**: 구 설계는 Equipment+Piping을
  한 `SubSystemSearcher`(union 스코프 MEQ·SPL·HYDROPKG) **general walk**로 묶어 부정확·저속이었다.
  **각 공종이 자기 nwd 하나만 자기 태그 셋으로 레벨 타겟**하도록 분리 — Equipment=MEQ / Piping=HYDROPKG /
  EIT EQ=EIT / Cable=CABLE, 전부 `BuildIndexForTags(..., hardScope: true)`(그 nwd에서만, general walk 없음).
  각 공종이 별개 태그 셋이라 4개 **사유(私有) searcher**를 `SubSystemTab`이 소유(다른 탭·서로 공유 불가) —
  엔진 `ApplySubSystem`은 `Func<Discipline, Searcher>` 리졸버(`SearcherFor`)를 주입받아 엔진이 searcher를
  안 들고 있음. 구 `SubSystemSearcher`/`SubSystemCableSearcher`/`SearcherForSubSystem`/`NwdScope.SubSystem`
  모두 제거. 인덱스 최신성은 탭의 `IndexStale(doc)`(데이터 재로드 `_needsIndexRebuild` + 모델 sig)로 판정.
  리포트 `인덱스 스코프` 행은 공종별 노트 결합(`ScopeNotes`) — 공종마다 fallback 여부 개별 확인.
  **§2 리스크(수용)**: 레벨 타겟은 첫 매칭 깊이만 인덱싱 — 요소가 여러 깊이에 섞이면 일부 미매칭.
  Spool/Equipment/Cable이 이미 수용한 리스크와 동일. Windows에서 general walk 시절과 매칭 건수 대조.
- **Excel 소스**: 미지원 (OASIS 전용). 필요 시 DataSourcePanel 이중 소스로 확장 —
  Excel에 Sub-system 컬럼 계약이 먼저.
- **집계 범위(ScopePanel) 미배선** — 필요 시 7번 공용 컴포넌트 그대로 연결 가능.
- **팔레트 색 사용자 지정 없음** (자동 배정만). 색은 선택 순서 기준 배정, 세션 내 유지.
- **선택 축소 후 재적용 시 이전 색 잔존**: **[해결됨 2026-07]** `ApplySubSystem`에 §10
  ResetModule/AccumulatePainted 이식 — 재적용 시 직전 누적분 원복 후 현재 선택만 재도색.
- 우측 테이블 `매칭` 열과 리포트의 매칭 O/X는 **마지막 [적용] 스냅샷** 기준 — 적용에
  포함되지 않았던 sub-system은 "-" 표시 (미적용 상태에서 O로 찍히는 기존 탭 결함을 답습하지 않음).

### 12. Cable 경로 추출 + Clipping 볼륨 통과 판정 (clash) — 설계 기록 (진단만 구현됨)

**목표**
특정 clipping 볼륨(단면 박스/영역)을 **지나가는 케이블 리스트 추출**. 케이블 한 가닥이
**하나의 component**로 구불구불 모델링된 경우, 중심점 1개(`BoundingBox().Center`)로는
판정 불가 → **실제 형상을 볼륨과 대봐야** 함.

**구현 현황**: `Services/GeometryProbe.cs` + Tools 탭 `Cable Vertex 진단`(선분 CSV/txt
덤프)까지 구현·머지됨(읽기 전용 진단). 실제 clash 배선(ScopeFilter 연결)은 **미구현** —
아래 설계대로 착수.

#### (A) 경로(형상) 추출 로직 — 실측 확정
- COM 경로: `ComApiBridge.ToInwOaPath(item)` → `path.Fragments()` → 프래그먼트마다
  `InwOaFragment3.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, cb)`
- 콜백 `cb` = `InwSimplePrimitivesCB` 구현(Line/Triangle/Point/SnapPoint 4개 — COM 콜백
  인터페이스라 시그니처 정확히 일치해야 바인딩됨). CCW 노출 위해 클래스는 `public`.
- **별도 index 버퍼 없음** — de-indexed 명시 정점을 그대로 줌.
- **케이블은 Line 프리미티브**(스윕 튜브 wireframe), Triangle 아님. 케이블당 ~79–88 선분 실측.
- **각 Line은 독립 선분(pair)** — 연속 폴리라인 아님. 순차로 이으면(v1→v2, v2→v3…)
  **가짜 선분** 생겨 형상 깨짐.
- wireframe = 단면 모서리(짧은 선분) + **route 레일(긴 선분)**. **route 중심선 아님** —
  하지만 clash엔 route 복원 불필요.
- **정점은 프래그먼트 로컬 좌표** → `frag.GetLocalToWorldMatrix()` 곱해 월드로(병진 실측
  ~수십 m). 규약: `world.k = Σ_j local_j·m[j*4+k] + m[12+k]` (row-vector×row-major).
  축정렬(회전≈0) **확정**, 회전 프래그먼트 규약 미확인.
- `InwSimpleVertex.coord` = float SAFEARRAY, 0/1-based는 lowerBound로 방어적 처리.
- 빌드별 시그니처 리스크 회피 위해 프래그먼트 순회는 `dynamic`(없으면 컴파일이 아닌
  런타임 예외로 degrade — L4).

#### (B) 볼륨 통과 판정 로직 (직육면체 = AABB)
**볼륨을 AABB/반평면으로 받아 각 케이블 선분과 교차 검사. 하나라도 교차 = 통과.**
- 선분 vs AABB **slab test**(Liang–Barsky, t∈[0,1]): 축마다 `d==0`이면 슬래브 밖일 때
  false, 아니면 `t1,t2=(min/max−p)/d` 정렬→`tMin=max(tMin,t1)`,`tMax=min(tMax,t2)`,
  `tMin>tMax`면 false. 끝까지 통과=교차. **정확 + 쌈(~30 float ops/선분), 점 샘플링 아님**
  (해석적이라 중간 관통도 잡음).
- **wireframe로 판정해도 정확한 이유**: route를 따라 달리는 **긴 세로 레일**이 볼륨을 반드시
  가로지름. (예외: 볼륨이 케이블 단면 ~0.4×0.5보다 작을 때 — 실무 clip은 구역 크기라 없음.)

#### (C) 비-직육면체(임의 형상) 볼륨 일반화 ← 핵심
볼륨이 박스가 아니어도 판정 **가능**. **선분-vs-볼륨 술어만 교체**(케이블 추출·캐시는 동일):
- **볼록(convex) 임의 볼륨** — 회전 박스(OBB) · Planes 모드 단면 · 절두체 · 임의 반평면
  집합: **Cyrus–Beck**(= slab의 일반화). 선분 t를 각 반평면으로 클리핑, 살아남는 t구간
  있으면 통과. `SectionService.GetActiveClipPlanes`가 이미 반평면 집합을 주므로 Planes/박스
  모두 이 경로로 흡수. 회전 박스는 선분 끝점을 **박스 로컬로 변환 후 slab**로도 가능.
- **비볼록(concave) 임의 볼륨** — L자 영역·임의 solid: 반평면 "모두 안쪽" 논리 깨짐. 두 방법:
  1. **볼록 분해**: 볼륨을 볼록 조각들로 나눠 각 조각에 Cyrus–Beck, 하나라도 통과=통과.
  2. **닫힌 메시 교차**: 볼륨이 닫힌 삼각형 메시면 **선분 vs 삼각형 교차**(경계 관통) +
     **내부점 판정**(ray cast/winding)으로 "선분이 solid와 교차" 완전 일반 판정. 비싸지만 범용.
- **Manage 가용 시**: 임의 메시 볼륨을 요소로 넣고 Clash Detective — 엔진이 임의 형상
  네이티브 처리. 단 **Manage 전용**(Simulate 불가, 4번·이전 논의).
- **아키텍처 분리(중요)**: ① 케이블→선분(고정, 1회 추출·캐시) ↔ ② 선분-vs-볼륨 술어
  (pluggable: **AABB=slab / convex=Cyrus–Beck / arbitrary=메시교차**). **볼륨 모양이 바뀌어도
  ①은 불변** — 술어 인터페이스 하나(`bool Intersects(seg, volume)`)로 두면 볼륨 종류 확장 자유.

#### (D) 성능
- **산술은 병목 아님**: 2만 케이블 × ~80선분 = 160만 선분 × slab ~30ops → 수십 ms.
- **병목 = COM 추출**(`GenerateSimplePrimitives` 마샬링, 케이블당 왕복 → 초 단위 가능).
- **형상은 정적**: 케이블 선분을 **1회 추출·캐시**, `[적용]`마다 캐시 선분에 볼륨 술어만
  재계산. **캐시하는 건 형상이지 판정 결과 아님**(L2 준수 — 판정은 라이브 볼륨으로 매번).
- **AABB 사전 배제**: 케이블 `BoundingBox`가 볼륨과 안 겹치면 선분 루프 스킵.
  **[버그였음 → 수정 2026-07]** 초기 구현은 pre-cull을 **세그먼트 추출 후**(추출된 세그먼트의
  AABB로) 수행해 첫 배치에서 2만 케이블 전부 COM 추출이 일어났다 — "clash 엄청 느림"의 원인.
  `CableClashService.PassesVolume`이 미캐시 케이블은 **관리형 `BoundingBox()`(COM 왕복 없는
  사전 계산값)로 먼저 배제**하고 볼륨과 겹치는 후보만 추출하도록 순서 교정. 배제된 케이블은
  캐시하지 않음(다른 볼륨에서 후보가 되면 그때 추출). 진단 카운터 `LastPreCulled` 신설 —
  clash CSV의 `bbox 사전배제/추출/세그AABB배제` 행에서 pre-cull이 먹는지 확인.

#### (E) 통합 지점 / Windows 검증
- `ScopeFilter` 케이블 분기의 `BoundingBox().Center` 판정을 **선분-vs-볼륨 clash로 교체**.
  통과 케이블 → 리스트/CSV/코너 카운트 반영.
- 검증: 좌표/단위 일치(COM 정점 ↔ 볼륨 공간), 회전 프래그먼트 행렬 규약, coord 0/1-based,
  wireframe 세로 레일 존재 가정.

### 13. Cable(형상) 탭 재정향 + Tray 탭 OASIS 연결 (구현됨 — Windows 검증 대기)

> 사용자 결정(2026-07): 아래 6개 결정 전부 추천대로 확정 → 한 번에 구현.
> **(A) 케이블 형상 중심 Cable 탭**(신설 `CableLineTab` — `07_Trion_All_Cable.nwd`의 cable-no
> 컴포넌트를 직접 매칭·하이라이트·단면 clash) + **(B) EIT nwd 전용 Tray 탭**(기존 `EitTrayTab`에
> OASIS 연결·§10·3행 버튼 마무리). "Windows 실측" 표시 항목은 리눅스 컴파일 불가라 실기 검증 후 확신 가능.

**확정 결정**
1. ~~기존 노드/박스 `Cable Pull` 탭 = 유지·개명 (`Cable(Node)`)~~ → **번복: 탭 삭제(2026-07
   사용자 결정, §9 Cable 참조)**. DB(EIT_Route)에 홉순서·NodeId 맵이 없어 개통 불가였고 형상
   탭이 대체. Cable 탭 = 형상 탭 단일. 추가로 형상 탭에 **Excel 케이블 리스트 필터**(`리스트
   필터 Import` — Cable No 목록 파일로 리스트+3D를 그 부분집합만 표시, 토글) 신설.
2. 형상 탭 stage = **신규 enum `CableLineStage`** 별도 정의(레거시 `CableStage` 불변 — 재정의는
   `CableDefaults`·`ApplyCable`·노드탭을 깸).
3. clash(단면 통과) 출력 = **리스트 + CSV만**(3D 색칠 안 함 — §7 "집계는 좁혔는데 색은 전체" 방지).
4. 겹침 완화 기본 = **숨김 기반 isolate**(2만 케이블에서 투명 dim은 프레임레이트 붕괴 — SpoolTab 실측).
5. Tray = **OASIS 듀얼소스 추가**(DataSourcePanel, Excel↔OASIS).
6. OASIS 진척 색 배선 = **Excel 우선**. `EIT_Cable`의 `PULLING LTH`/`FROM·TO CONN` 날짜가 실적/계획
   미확정이라 길이·%는 색에 배선 안 함(표시 전용). 데이터 오너 확정 후 stage 날짜 배선.

**핵심 통찰**: 형상 재정향이 EIT_Cable DB 차단(§9 "노드 route 부재")을 무관화 — cable-no를 컴포넌트에
직접 매칭하므로 노드 route가 애초에 불필요. Tray는 신규가 아니라 완성(기존 EitTrayTab이 이미 EIT
스코프·Install% 3단계).

**Cable(형상) 탭 아키텍처**
- **매칭**: 신규 `CableLineSearcher`(7번째 `ModelItemSearcher`) — `BuildIndexForTags(cableNoSet,
  NwdScope.Cable)` 레벨 타겟(2만 케이블은 스풀 규모 → general walk 자식 스캔 비용 회피). `CableBoxSearcher`
  재사용 불가(box 인덱스는 `-BOX` 접두 키). `NwdScope.Cable`(키워드 "CABLE") 재사용. 소스 전환 시
  `_needsIndexRebuild`(레벨 타겟은 태그 셋 기반, Spool 패턴).
- **Stage**: 날짜 기반 4단계 `미착수/포설중/포설완료/결선완료`(`GetStageAtDate` 역순 스캔). Pulling=PULLING
  START, Pulled=PULLING END, Terminated=`FROM CONN`·`TO CONN` **둘 다** 있을 때 Max(AND 게이트).
  **하이라이트 우선 모드**: 로드 데이터에 stage 날짜가 전무하면(맨 Excel 목록) stage 계산 우회 →
  매칭 케이블을 단색 solid 하이라이트로 칠함(안 그러면 전 케이블이 미착수=70% 투명 회색 = 하이라이트 반대).
- **clash**: 신규 `Services/CableClashService` — 케이블 world 세그먼트 **형상만 캐시**(모델당 정적, doc-id
  무효화), 판정은 매 [적용]마다 live `GetActiveClipPlanes`로 재계산(L2). AABB pre-cull + Cyrus–Beck
  세그먼트-vs-반평면(축정렬 박스 6반평면 + Planes 모드 한 구현). `GeometryProbe.ExtractWorldSegments`
  분리(컨테이너→geometry leaf 하강). ScopeFilter의 ClippingVolume 분기에 cableNo-keyed volume-judge
  델리게이트 주입(타 탭 null → BoundingBox 중점 유지). `이 단면 지나가는 케이블 추출` 버튼 + scope-aware CSV.
- **겹침 완화 UX**: 3행 버튼(가시화/초기화/출력). `체크 단계 외 숨김`·`선택 케이블만 보기`(SetHidden isolate,
  상호배타), `필터 포커스`(투명 dim, 작은 히트셋만). List↔3D 양방향 선택 sync(조상 walk로 cable-no 해석).
- **§10**: `VisualModule.CableLine` 신규 멤버(레거시 `Cable`과 캐시 충돌 방지). `ApplyCableLines`는 처음부터
  ResetModule/AccumulatePainted 채택. focus·isolate는 override/hide라 AccumulatePainted가 안 잡으므로
  ResetModule 전에 별도 해제.

**Tray 탭 마무리 (재작성 아님)**
- `EitTrayTab`에 `DataSourcePanel`(Excel↔OASIS) + `SqlLoader.LoadEitTray`(`[Navis].[EIT_Tray]` —
  `BRANCH NO.`/`TRAY Install %`/`PJTNO`, **날짜 컬럼 없음 → %기반 현재상태**, `dtpReference` 비활성 유지).
  `%` 스케일 파서(`85`/`0.85`/`"85%"`→0.85) 필수(§2.2). SourceComparer는 Stage/Install%만 diff.
- `ApplyEit`에 §10 ResetModule/AccumulatePainted 이식(재적용 누적 버그) + `cache.Clear()` 유지.
- 3행 버튼(`체크 단계 외 숨김`·`공종 초기화`) + 낡은 주석(`Cable 포설중`) 삭제.

**Windows 검증 게이트(착수 직후)**
1. **[최우선]** cable 컴포넌트 DisplayName이 Excel/OASIS CABLE NO와 trim/case 후 정확 일치인가(장식
   문자 있으면 레벨 타겟 0 매칭 → `NormalizeCableNo` 추가). Tools `Cable Vertex 진단`으로 실 DisplayName 덤프.
2. 케이블 모델링 깊이 단일/혼재(레벨 타겟은 첫 깊이만 — 혼재 시 일부 미색칠, general walk 베이스라인과 대조).
3. 좌표/단위 일치(추출 world 세그 ↔ clip-box JSON) — 덤프 `AnySegmentClips` 열로 술어 calibration.
4. 컨테이너 vs leaf 하강 깊이. 회전 박스는 `GetActiveClipPlanes` null→COM fallback(축정렬만 우선).
5. `TRAY Install %` 스케일·`PJTNO` 필터, 선택 sync `HashSet<ModelItem>` 동등성(§7 미검증).

**Phase**: 0(진단 refactor·DisplayName 실측) → 1(Excel-only 출하: 형상 탭·clash·Tray 마무리) →
2(OASIS 로더 `LoadCable`/`LoadEitTray`·회전 박스). `LoadCable` 철자는 실 스키마로 확정·배선 완료
(2026-07 — §9 Cable 참조), Phase 2 잔여는 회전 박스뿐.

### 14. UX Audit 반영 (2026-07 — 구현됨, Windows 검증 대기)

master 기준 UX audit의 항목별 판정·근거·보류 목록은 **`docs/UX_AUDIT_REVIEW.md`** 참조. 반영분:

- **3D 적용 상태 표시 `UI/ApplyStatePanel.cs`** (audit P0-1): 가시화 버튼 행에 `3D: 미적용 /
  {기준}·시각 적용됨 / ⚠ 3D 업데이트 필요(사유)` 라벨 + stale 시 [가시화 적용] 버튼 배경 강조.
  6개 탭 전부 배선 — MarkStale 트리거는 소스 전환/재로드·기준일·단계 체크·(Sub-system) 선택/모드.
  기존 `_lblStats`에 얹던 ⚠ 경고 문구는 전부 제거 (**통계 라벨은 통계만** — 경고가 통계를 덮어쓰던
  문제 해소). 색/투명도 변경은 증분 즉시 반영이라 stale 아님.
- **버튼 명칭 통일** (P0-2): `적용`→`가시화 적용`(전 탭), ScopePanel `적용`→`범위 적용`,
  `공종 초기화`→`이 탭 가시화 해제`, `전체 초기화`→`전체 가시화 해제`(§6 "초기화 오독" 취지 실현),
  `매칭 Status 출력`→`매칭 Status 엑셀 출력`(§6 결정 반영). 버튼명 인용 안내문도 일괄 갱신.
  §10의 버튼 행 구성 표기는 이 명칭으로 대체됨.
- **긴 작업 단계 문구** (P0-3 부분): marquee에 `모델 태그 인덱스 생성 중…`/`색상 적용 중…` 병기.
  Hydrotest/Equipment/Sub-system 색칠 구간에 진행바 자체도 추가(Spool §10과 동일 try/finally).
  건수/경과/중단은 보류 — 진행 콜백 설계 메모는 리뷰 문서에.
- **색상 편집 접기 `UI/ColorEditCollapse.cs`** (P1): 기본 접힘 — 체크박스+스와치만 노출, ▼·투명도는
  토글로. 탭 빌더 무수정(패널 트리 walk로 ComboBox·"▼"만 Visible 토글).
- **저장 알림 `UI/SaveNotifier.cs`** (P2): CSV 저장 완료 MessageBox → 비모달 창(파일 열기/폴더 열기).
- **Tools 탭 → "고급 진단"** 명칭 변경(P2 부분), **EIT Tray 기준일 행 제거**(비활성 노출 중단 —
  날짜 컬럼 확보 시 §3 패턴으로 복원), Sub-system 검색폭 88→120 + **`이 탭 가시화 해제` 버튼 신설**
  (§10 잔여 해소 — `ResetModule(VisualModule.SubSystem)`).
- **보류(후속 우선순위)**: ① Overview/Preflight 탭(최우선 추천 — NwdScope 발견 여부 사전 점검),
  ② 진행 콜백+중단, ③ DPI/반응형(Windows 실측 병행 필수), ④ 리스트·통계 통합, ⑤ Sub-system 선택
  UX 재설계(§11 dual-list는 최근 사용자 결정이라 실사용 후), ⑥ empty-state 체크리스트, ⑦ 언어 통일.

## 레슨런 (하드 트러블슈팅 기록 — 다시 헤매지 말 것)

### L1. 단면(Clipping): **Section Box는 COM `ClippingPlanes()`에 없다** (가장 값진 교훈)
- 증상: `Clipping 영역` 범위에서 **박스를 걸어도 단일 +z 평면과 결과가 똑같음**. 박스로 영역이 안 좁혀짐.
- 진단: Tools 탭 `Clip Plane 덤프`에서 박스 적용 상태인데 `ClippingPlanes().Count == 1`. → COM 컬렉션은 **Planes 모드 평면만** 담고, 박스를 걸면 잔여 평면 1개만 남아 "박스==평면"으로 보인 것.
- 해결: Navisworks **관리형 `View.GetClippingPlanes()`가 JSON으로 `OrientedBox`를 노출**. 박스는 이 JSON에서 읽어 6개 반평면(안쪽 법선)으로 변환. Planes 모드/무단면은 기존 COM 경로 유지. (상세: 4번 항목.)
- 부수 교훈: **"관리형 API는 단면을 노출 안 한다"는 애초 가정이 틀렸다.** Planes 리스트로는 안 나오지만 `GetClippingPlanes()` JSON에는 박스·평면 전체가 들어있다. 가정 전에 API 문서/실측 확인.

### L2. 라이브 외부 상태(단면/숨김/선택)는 **캐시 금지**
- `ScopeFilter`가 범위별 판정을 `Dictionary`로 캐시했더니, 다른 범위로 갔다 돌아오면 옛 판정이 되살아나 **단면 해제 후에도 숫자가 안 바뀌는** 버그. 이들 상태는 변경 이벤트가 없어 무효화 시점을 알 수 없다. → `[적용]`마다 무조건 재계산 (대상이 매칭 노드 수천 건이라 비용 무시 가능).

### L3. 미매칭은 스코프와 **직교**하는 고정 전역 수치
- Spool/Hydro/Equip/EIT의 미매칭 = 실적엔 있으나 **모델에 노드가 없는 행** → 위치가 없어 선택/단면/숨김으로 판정 불가. 스코프로 0으로 줄이면 "미매칭=모델없음"의 본질이 흐려짐. → 항상 전체 미매칭, 통계 라벨과 분리해 **우측 코너 별도 표시**. (Cable은 box 위치가 있어 예외 — 스코프됨.)

### L4. 빌드별 시그니처가 불확실한 관리형 API는 **리플렉션 호출**
- `View.GetClippingPlanes()`를 직접 호출하면 특정 NW 빌드에서 없을 때 **컴파일이 깨진다**. Linux에서 컴파일 검증이 불가한 이 프로젝트에선 치명적. → 리플렉션(`GetMethod(..., Type.EmptyTypes)`)으로 호출해 없으면 null → 기존 경로 fallback. 컴파일 안전 + graceful degrade.

### L5. 진단 도구에 먼저 투자 (Linux 컴파일 불가 환경 특성)
- COM/Windows 전용이라 개발 머신에서 실행·디버깅이 안 됨. `Clip Plane 덤프`처럼 **상태를 그대로 뱉는 진단 출력**이 근본 원인(Count=1, 원본 JSON)을 한 번에 짚어줬다. 추측성 코드 수정보다 "덤프 보강 → 실측값 받기 → 확신 후 수정" 루프가 빠르고 안전.

### L6. 미확정 규약은 **부분 지원 + 안전 fallback**
- 관리형 박스 JSON에서 축정렬 박스(Rotation≈0)만 확정 처리하고, **회전 박스는 규약 미확정이라 null 반환(→ COM fallback)**. 잘못된 볼륨을 확신 없이 배포하지 않는다. 회전 포맷은 덤프 원본 JSON 확보 후 마무리.

### L7. 케이블 형상 추출 — GenerateSimplePrimitives 실측 교훈
- 케이블(`lcldrvm_container`)은 **Line 프리미티브**(스윕 튜브 wireframe), Triangle 아님.
  **index 버퍼 없음**(de-indexed 명시 정점). 각 Line은 **독립 선분** — 순차로 이으면 형상
  깨짐(가짜 선분). route 중심선 아니지만 clash엔 무관(긴 세로 레일이 볼륨 가로지름).
- 정점은 **프래그먼트 로컬 좌표** — `GetLocalToWorldMatrix()`(병진 ~수십 m) 안 곱하면
  원점 근처로 쏠림. 첫 덤프가 로컬이라 "형상이 route와 안 맞음"으로 드러남 → 변환 적용 후 해결.
- L5 그대로: `Cable Vertex 진단`(선분 CSV 덤프)이 "Line vs Triangle · 좌표계 · index 유무"를
  한 번에 확정. 추측 코드 대신 덤프 먼저.

## 개발 규칙

- Navisworks Simulate 2022 / .NET Framework 4.8 타겟. `Autodesk.Navisworks.*` DLL은 Windows 설치 경로 참조.
- 리눅스/맥에서는 `dotnet` 빌드 불가 (COM interop + Windows-only DLL). Windows에서만 컴파일 검증.
  단, Autodesk 비의존 파일(DataModels/SqlLoader/SqlConnectionSettings/SourceComparer/DataSourcePanel)은
  `Microsoft.NETFramework.ReferenceAssemblies` 패키지로 리눅스에서도 net48 컴파일 검증 가능.
- `oasis.config`(DB 암호 포함)는 커밋 금지(.gitignore 등록) — `oasis.config.sample`만 커밋.
- 새 탭 추가 시 그룹 결정 (매칭 전략 × NWD 스코프 2개 축 — 1번 항목 참조):
  - "digit 포함 DisplayName" 매칭이고 **대상 nwd 스코프도 같으면** 기존 인스턴스 재사용
    (`SpoolTagSearcher`=SPL→HYDROPKG / `HydroTagSearcher`=HYDROPKG / `ElecTagSearcher`=EIT /
    `SubSystemSearcher`=MEQ·SPL·HYDROPKG)
  - 스코프가 다르거나 다른 매칭 전략 → 새 `ModelItemSearcher` 인스턴스 + `NwdScope` 상수 추가
    + `ColorOverrideEngine` 생성자에 추가
