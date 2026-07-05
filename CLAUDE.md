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

### 6. OASIS(SQL) 연동 잔여 항목

Spool / Hydrotest / Equipment 탭은 OASIS 로드 구현 완료 (`SqlLoader`, 테이블 6개 분석은
`docs/SQL_DB_CONNECTION_ANALYSIS.md`). 남은 것:

- **EIT Tray**: 트레이 진척 테이블(`Tray Number`/`Install %` 형태)이 DB에 없음 — 확인 대기.
- **Cable**: `EIT_Cable`에 Node 컬럼 부재. 케이블↔노드 매핑(route detail + 홉 순서 SEQ)
  테이블이 생겨야 연동 가능. `PULLING LTH` 의미(실적 vs 발주 길이)도 확인 필요
  (샘플에서 design < pulling인데 Pulling % = 0.0%).
- **EIT_EQ**: 소비 탭 미정. WRKDTE 단일 단계 + TagSearcher 재사용이 유력.
- **Spool `FIT-UP`**: 설치 fit-up 단계(Setting↔Welding 사이). SpoolStage 추가 여부 결정 대기 —
  추가 시 enum/OrderedStages/Labels/ColumnMap/InstallStages/SpoolDefaults 6곳.
- **Equipment 병합 정책**: 현재 Mech_EQ 우선 + All_EQ 보충(dedupe). 정책 바뀌면
  `SqlLoader.LoadEquipment`의 테이블 순회 순서만 조정.

### 7. 공종(모듈)별 초기화 — 아이디어 기록 (미구현)

현재 "전체 초기화"는 어느 탭에서 눌러도 `ResetAllPermanentMaterials()` — 모든 공종 색이
같이 사라진다. 공종별 초기화(`ResetModule`)를 넣으려면:

- API는 지원됨: `DocumentModels.ResetPermanentMaterials(ModelItemCollection)` (아이템 단위 리셋)
- **최신 캐시가 아니라 "누적 painted 셋"을 리셋해야 함** — 같은 탭에서 적용을 여러 번 하면
  (기준일 변경, 단계 체크 해제) 이전 적용에서 칠했지만 최신 캐시에 없는 아이템이 생김.
  모듈별로 `ApplyOverride`에 넘긴 컬렉션을 합집합으로 누적했다가 그걸 리셋.
- Cable 모듈 리셋은 색 + `RestoreHiddenCableBoxes` + 필터 포커스 상태 + cable 전용 캐시 정리까지.
- Spool↔Hydrotest 겹침은 유리하게 동작: Spool(하위 노드)만 리셋하면 상위 PKG 오버라이드가
  다시 드러남 (레이어 벗기기).
- UI: 각 탭 `[적용] [공종 초기화] [전체 초기화]` 3버튼.
- 기반은 이미 있음: stage 캐시가 `VisualModule`별로 분리되어 있어 (ColorOverrideEngine)
  누적 painted 셋만 추가하면 됨.

## 개발 규칙

- Navisworks Simulate 2022 / .NET Framework 4.8 타겟. `Autodesk.Navisworks.*` DLL은 Windows 설치 경로 참조.
- 리눅스/맥에서는 `dotnet` 빌드 불가 (COM interop + Windows-only DLL). Windows에서만 컴파일 검증.
  단, Autodesk 비의존 파일(DataModels/SqlLoader/SqlConnectionSettings/SourceComparer/DataSourcePanel)은
  `Microsoft.NETFramework.ReferenceAssemblies` 패키지로 리눅스에서도 net48 컴파일 검증 가능.
- `oasis.config`(DB 암호 포함)는 커밋 금지(.gitignore 등록) — `oasis.config.sample`만 커밋.
- 새 탭 추가 시 그룹 결정:
  - "digit 포함 DisplayName" 매칭 → `TagSearcher` 재사용
  - 그 외 매칭 전략 → 새 `ModelItemSearcher` 인스턴스 + `ColorOverrideEngine` 생성자에 추가
