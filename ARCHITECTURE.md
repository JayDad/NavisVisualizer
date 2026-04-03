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

## Data Flow

### 1. Excel Loading (`Loaders/ExcelLoader.cs`)

- **ExcelDataReader** 사용 → .xlsx, .xls, .xlsb 모두 지원
- 헤더 행 자동 탐지: 상위 20행 스캔하여 "Spool Number" 또는 "Test Package No." 포함 행 검색
- 병합 셀(카테고리 행) 자동 건너뜀
- 시트 이름이 다를 경우 모든 시트를 순회하며 헤더 검색

### 2. Stage Computation (`Models/DataModels.cs`)

**기준일(Reference Date) 기반 동적 Stage 계산:**

```
GetStageAtDate(referenceDate):
  stages 배열을 역순으로 순회
  → 날짜가 존재하고 referenceDate 이하인 가장 마지막 Stage 반환
  → 없으면 NotStarted
```

- **Spool**: 14단계 (B/V → Welding)
  - Fabrication: B/V, F/up, W/D, NDE, PWHT, S/out, 후공정인계, Galv, Pnt1, Pnt2, Stock, H/O
  - Install: Setting, Welding
- **Hydrotest**: 6단계 (Review → Reinstatement)
  - Review, Line Inspection, Flushing, Hydrotest, Drying, Reinstatement

### 3. Model Item Indexing (`Searchers/ModelItemSearcher.cs`)

**DisplayName 기반 매칭:**

```
BuildIndex:
  모델 트리의 모든 아이템을 순회
  → 자식이 있는 그룹 노드만 인덱싱 (leaf geometry 제외)
  → DisplayName에서 앞의 '/' 제거 후 대문자 정규화
  → Dictionary<string, List<ModelItem>>에 저장
```

**성능 최적화:**
- leaf 노드(Pipe, Elbow 등) 건너뛰기 → 인덱싱 대상 대폭 감소
- ToList() 없이 스트림 순회 → 메모리 절약
- 한 번 빌드하면 Spool/Hydrotest 탭 모두 공유

### 4. Color Override Engine (`Visualizers/ColorOverrideEngine.cs`)

**Stage별 배치 처리:**

```
Apply:
  1. 각 Spool/Package의 Stage 계산
  2. Stage별로 ModelItem 그룹핑
  3. Reset (기존 오버라이드 초기화)
  4. Stage당 1회 API 호출 (OverridePermanentColor + OverridePermanentTransparency)
  5. 결과를 캐시에 저장 (Stage → ModelItemCollection)
```

**증분 업데이트 (Incremental Update):**

```
UpdateStageColor(stageKey, setting):
  캐시된 ModelItemCollection 재사용
  → Reset 없이 해당 Stage만 색상/투명도 변경
  → 색상 피커나 투명도 변경 시 즉시 반영
```

**성능 비교:**

| 방식 | API 호출 수 | 설명 |
|------|------------|------|
| 건별 호출 (초기) | N (수천) | Spool/Package 하나당 1회 |
| Stage별 배치 | 7~15 | Stage 하나당 1회 |
| 증분 업데이트 | 2 | 변경된 Stage만 (Color + Transparency) |

**미매칭 처리:**
- Reset으로 초기화 → 매칭된 것만 색칠
- 미매칭 아이템은 원래 모습 유지 (투명 처리 없음)
- 전체 모델 순회하여 unmatched 찾는 비용 제거

### 5. User-Defined Properties (`Services/UserDataService.cs`)

- COM API (ComApiBridge) 통해 매칭된 요소에 "Spool 실적" 속성 탭 삽입
- dynamic COM interop 사용 (Interop namespace 의존성 회피)
- NWD Export 시 속성 포함 → Freedom에서도 확인 가능

### 6. UI Architecture (`UI/`)

- **MainDockablePanel**: Searcher, OverrideEngine, Services를 공유하는 컨테이너
- **SpoolTab / HydrotestTab**: 동일 패턴
  - DateTimePicker (기준일)
  - 2열 색상 패널 (즉시 반영)
  - 전체/매칭/미매칭 탭 필터
  - ListView 컬럼 정렬
  - 텍스트 검색
- **ToolsTab**: Property Dumper, Model Tree Dumper (디버깅)

## Model Tree Structure

```
NWD File
 └─ Project / ISO Group
    └─ Area
       └─ System No. / ISO No.
          └─ Hydrotest Package No.  ← HydrotestTab 매칭 레벨
             └─ Spool No.           ← SpoolTab 매칭 레벨
                └─ Pipe, Elbow...   ← Geometry (인덱싱 제외)
```

## Dependencies

| Package | Version | 용도 |
|---------|---------|------|
| ExcelDataReader | 3.6.0 | Excel 파일 읽기 (.xlsx/.xls/.xlsb) |
| ExcelDataReader.DataSet | 3.6.0 | DataSet 변환 |
| Microsoft.CSharp | 4.7.0 | dynamic 키워드 지원 |
| Autodesk.Navisworks.Api | 2022 | Navisworks .NET API |
| Autodesk.Navisworks.ComApi | 2022 | COM API Bridge |
| Autodesk.Navisworks.Interop.ComApi | 2022 | COM 타입 정의 |
