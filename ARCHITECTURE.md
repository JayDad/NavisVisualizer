# NavisVisualizer Architecture

## Overview

Navisworks Simulate 2022 플러그인. Excel 실적 데이터를 3D 모델에 색상으로 시각화.

```
Excel (.xlsx/.xls/.xlsb)
  → ExcelLoader (ExcelDataReader, 헤더 자동 탐지)
  → Data Models (날짜 기반 Stage 계산)
  → ModelItemSearcher (DisplayName 인덱싱)
  → ColorOverrideEngine (Stage별 배치 색상 적용)
  → Navisworks 3D View
```

## Modules

| 모듈 | Stage 수 | 매칭 키 | 인덱싱 방식 | Searcher |
|------|---------|---------|------------|----------|
| **Spool** | 14 (B/V → Welding) | Spool Number (DisplayName) | 재귀 탐색 (WalkAndIndex) | `TagSearcher` 공유 |
| **Hydrotest** | 6 (Review → Reinstatement) | Test Package No. (DisplayName) | 재귀 탐색 (WalkAndIndex) | `TagSearcher` 공유 |
| **Equipment** | 4 (Delivery → Inspection) | Tag No. (DisplayName, prefix 지원) | 레벨 타겟 (BuildIndexForTags) | `EquipmentSearcher` 전용 |
| **EIT Tray** | 4 (Tray 설치 → Cable 완료) | Tray Number (leading `/` 정규화 후) | 재귀 탐색 (WalkAndIndex) | `TagSearcher` 공유 |

### Searcher 분리 근거
- **TagSearcher**: Spool / Hydrotest / EIT Tray는 *동일* 매칭 전략(`WalkAndIndex` + `FindBySpoolIds`) — 한 번 빌드하면 셋 다 조회 가능
- **EquipmentSearcher**: 레벨-타겟 전략으로 인덱스 구조가 근본적으로 달라 충돌 방지 목적의 물리적 분리
- 단일 Searcher 공유 시: Equipment 먼저 적용 → TagSearcher 탭은 비어 있는 레벨-타겟 인덱스를 재사용 → 0 매칭 (버그)

## Data Flow

### 1. Excel Loading (`Loaders/ExcelLoader.cs`)

- **ExcelDataReader** 사용 → .xlsx, .xls, .xlsb 모두 지원
- 헤더 행 자동 탐지: 상위 20행 스캔하여 헤더 키워드 포함 행 검색
- 병합 셀(카테고리 행) 자동 건너뜀
- 시트 이름이 다를 경우 모든 시트를 순회하며 헤더 검색

**헤더 키워드:**
- Spool: "Spool Number", "SpoolId", "Spool No"
- Hydrotest: "Test Package No.", "TestPkgId"
- Equipment: "Tag No.", "Tag No"

### 2. Stage Computation (`Models/DataModels.cs`)

**기준일(Reference Date) 기반 동적 Stage 계산:**

```
GetStageAtDate(referenceDate):
  stages 배열을 역순으로 순회
  → 날짜가 존재하고 referenceDate 이하인 가장 마지막 Stage 반환
  → 없으면 NotStarted
```

**Spool** (14단계):
- Fabrication: B/V, F/up, W/D, NDE, PWHT, S/out, 후공정인계, Galv, Pnt1, Pnt2, Stock, H/O
- Install: Setting, Welding

**Hydrotest** (6단계):
- Review, Line Inspection, Flushing, Hydrotest, Drying, Reinstatement

**Equipment** (4단계):
- Delivery (Delivered + Confirmed ETA), Loading, Setting, Inspection
- Delivery 특수 로직: "Delivered" 텍스트 상태 + Confirmed ETA 날짜 조합

**EIT Tray** (4단계):
- NotStarted → TrayInstalled → CablePulling → CableCompleted
- 기준일과 `Tray install date` 비교로 NotStarted/Installed 판정
- Tray Installed 이후: `Best Cable Progress` 기준 (≥100% → Completed, >0% → Pulling)
- Cable Progress는 날짜 미보유 → 현재 상태 기준 (입력 데이터에 per-cable 완료일 추가 시 stage별 날짜 로직으로 교체)

### 3. Model Item Indexing (`Searchers/ModelItemSearcher.cs`)

**두 가지 인덱싱 전략:**

#### A. 재귀 탐색 (`WalkAndIndex`) — Spool/Hydrotest/EIT Tray용

```
WalkAndIndex(item):
  DisplayName에 숫자 포함? → 인덱싱
    자식에 숫자 포함 노드 OR 구조 컨테이너(geometry 없고 자식 있음) 존재? → 계속 재귀
    자식이 전부 geometry leaf? → STOP (하위는 geometry)
  숫자 없음? → 계속 재귀 (태그 레벨 아직 안 도달)
```

> federated NWD에서 파일 노드(`MEBTray1.nwc`)가 파일명 숫자 때문에 tag-like로 잡히고
> 그 아래가 `/SM/MEB/ELEC` 같은 digit 없는 범주 노드면, "자식에 숫자 없음 → STOP"이
> 너무 일찍 발동해 하위 트레이가 통째로 미인덱싱됐다. 구조 컨테이너로 내려가도록 보완.

#### B. 레벨 타겟 (`BuildIndexForTags`) — Equipment용

```
Step 1: Excel 태그로 트리 탐색 → 첫 매칭 depth 기록
Step 2: 해당 depth의 노드만 인덱싱 → 나머지 전체 스킵

예: 200개 Equipment 태그가 depth 5에 존재
  → depth 5만 순회 (~200 노드 인덱싱)
  → 하위 수백만 geometry 노드 완전 스킵
```

**공통:**
- DisplayName에서 앞의 '/' 제거 후 대문자 정규화
- '/' 포함 키 자동 prefix 등록 (예: `TAG/VENSKID` → `TAG`도 등록)
- 모델 변경 자동 감지 (`NeedsRebuild` — 파일 경로 + 모델 수 비교)

### 4. Color Override Engine (`Visualizers/ColorOverrideEngine.cs`)

**Stage별 배치 처리:**

```
Apply:
  1. 각 항목의 Stage 계산 (기준일 기반)
  2. Stage별로 ModelItem 그룹핑
  3. Stage당 1회 API 호출 (OverridePermanentColor)
  4. 투명도 0%인 Stage는 OverridePermanentTransparency 생략
  5. 결과를 캐시에 저장 (Stage → ModelItemCollection)
```

**증분 업데이트:**
- 색상/투명도 변경 시 캐시된 Collection 재사용
- Reset 없이 해당 Stage만 즉시 반영

**성능 최적화 이력:**

| 단계 | 변경 | 효과 |
|------|------|------|
| 건별 API 호출 | Stage별 배치 | 수천 → 7~15회 |
| 전체 투명 처리 | Reset + 매칭만 색칠 | 전체 모델 순회 제거 |
| 전체 트리 인덱싱 | 레벨 타겟 인덱싱 | Equipment 134s → <1s |
| DescendantsAndSelf | 매칭 노드만 색칠 | 하위 트리 순회 제거 |
| ~~투명도 0% 체크~~ (철회) | ~~API 호출 생략~~ | 투명도 증분 조정 시 이전 override가 남아 복원 불가 — 항상 호출로 복귀 |

### 5. User-Defined Properties (`Services/UserDataService.cs`)

- COM API (`ComApiBridge`) + `Type.InvokeMember`로 IDispatch 직접 호출
- `dynamic` 키워드로는 순수 COM 객체 메서드 접근 불가 → `InvokeMember` 사용
- `SetUserDefined` (v1): 쓰기 가능, `UserDefined` (v2): 읽기 미지원
- "User Property" 탭으로 특성 패널에 표시
- NWD Export 시 속성 포함 → Freedom에서도 확인 가능

**속성 내용:**
- Spool: Spool Number, ISO No, 현재 단계, 14개 Stage 날짜
- Equipment: Tag No, Description, Sub System, RFQ No, Delivery, 현재 단계, ETA, 날짜

### 6. UI Architecture (`UI/`)

**탭 구성:** Hydrotest | Spool | Equipment | Tools

**공통 패턴 (3개 탭 동일):**
- DateTimePicker (기준일, 기본: 오늘)
- 2열 색상 패널 (색상 피커 + 투명도 드롭다운)
- 색상 변경 시 증분 업데이트 (캐시 활용)
- 전체/매칭/미매칭 탭 필터 (건수 표시)
- 검색 + "매칭 Status 출력" CSV Export
- ListView 컬럼 정렬 (오름차순/내림차순)
- 적용 / 전체 초기화 / 속성 쓰기 / Viewpoint 저장 / NWD Export

**Tools 탭:**
- Property Dumper: 선택 아이템 속성 CSV 출력
- Model Tree Dumper: 모델 트리 구조 CSV 출력
- User Data Test: COM 속성 쓰기 1건 테스트 + 진단

## Model Tree Structure

```
Equipment NWD:
 └─ Area
    └─ System
       └─ Tag No. (101210-PBA-10240)        ← Equipment 매칭 레벨
       └─ Tag No./VENSKID (prefix 매칭)      ← Equipment 매칭 레벨
          └─ geometry...                      ← 인덱싱 스킵

Piping NWD:
 └─ ISO Group
    └─ ISO No.
       └─ Hydrotest Package No.              ← Hydrotest 매칭 레벨
          └─ Spool No.                        ← Spool 매칭 레벨
             └─ Pipe, Elbow...                ← 인덱싱 스킵
```

## Dependencies

| Package | Version | 용도 |
|---------|---------|------|
| ExcelDataReader | 3.6.0 | Excel 파일 읽기 (.xlsx/.xls/.xlsb) |
| ExcelDataReader.DataSet | 3.6.0 | DataSet 변환 |
| Microsoft.CSharp | 4.7.0 | dynamic 키워드 (COM interop) |
| Autodesk.Navisworks.Api | 2022 | Navisworks .NET API |
| Autodesk.Navisworks.ComApi | 2022 | COM API Bridge |
| Autodesk.Navisworks.Interop.ComApi | 2022 | COM 타입 정의 (enum) |
