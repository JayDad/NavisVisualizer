# NavisVisualizer Architecture

## Overview

Navisworks Simulate 2022 플러그인. Excel/OASIS(사내 SQL Server) 실적 데이터를 3D 모델에 색상으로 시각화.

```
Excel (.xlsx/.xls/.xlsb)          OASIS SQL Server ([Navis] 스키마)
  → ExcelLoader                     → SqlLoader
    (ExcelDataReader, 헤더 자동 탐지)   (테이블별 명시 SELECT + 명시 컬럼 매핑)
              └──────────┬──────────────┘
  → Data Models (날짜 기반 Stage 계산)   ← 두 로더가 동일 List<T> 반환
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
| **Sub-system** | 2모드: 마스터 단계 5 (Walkdown→PCC) / 요소 진행 3단계 | Tag No. + Test Package No. (Sub-system 축 통합) | 재귀 탐색 (WalkAndIndex) | `TagSearcher` 공유 |

### Searcher 분리 근거
- **TagSearcher**: Spool / Hydrotest / EIT Tray / Sub-system은 *동일* 매칭 전략(`WalkAndIndex` + `FindBySpoolIds`) — 한 번 빌드하면 전부 조회 가능
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

### 1b. OASIS SQL Loading (`Loaders/SqlLoader.cs`)

- **System.Data.SqlClient** (net48 내장, 추가 패키지·배포 파일 없음)
- Excel과 달리 헤더 추측 없음 — 테이블별 명시 SELECT + 명시 컬럼→프로퍼티 매핑.
  컬럼명이 틀리면 SQL Server가 즉시 오류 (조용한 빈 값 문제 원천 차단)
- 연결 설정: 플러그인 DLL 옆 `oasis.config` (key=value, `oasis.config.sample` 참조).
  `%APPDATA%\NavisVisualizer\oasis.config` 존재 시 그 파일 우선. 사내망 전용 전제 —
  **DB 계정은 SELECT 전용 필수**
- 프로젝트 필터: `project=` 설정 시 WHERE 적용 (EQ 계열 `PJTNO` / Piping 계열 `PRJTNO`)
- 키 dedupe (첫 행 우선), 날짜는 typed DateTime 그대로 + varchar는 InvariantCulture 우선 파싱

| 탭 | 테이블 | 특이 매핑 |
|---|---|---|
| Spool | `[Navis].[Piping_Spool]` | 14 stage 전부. `FIT-UP`(설치 fit-up)은 stage 미정의로 미사용 |
| Hydrotest | `[Navis].[Piping_HydrotestPKG]` | `PKGNO`→TestPkgId, `Sub-System`→SystemNo, `LINESVC`→LineService |
| Equipment | `[Navis].[Mech_EQ]` + `[All_EQ]` 병합 (Mech 우선) | TAG NO 선행 `/` 정규화, `Delivered` **날짜**→StageDates[Delivery] 직접 매핑 |

- **Sub-system 탭은 OASIS 전용** — 새 SQL 없이 `LoadEquipment`+`LoadHydrotest`를 재사용해
  Sub-system 축으로 감싼다 (`SqlLoader.LoadSubSystemElements`). Equipment `SUB-SYSTEM`→TAG NO,
  Piping `Sub-System`→PKGNO. Sub-system 미지정 행은 제외(건수 보고). PKG 노드 색칠이
  하위 스풀/배관을 커버하므로 배관은 PKG 단위로 충분
- **Sub-system 마스터**: `SqlLoader.LoadSubSystemMaster` ← `[Navis].[SubSystem_Master]`
  (`SUB-SYSTEM/DESCRIPTION` + `MCC Plan`(계획일) + 마일스톤 실적일 `Walkdown/Partial MCC/MCC/PCC` +
  `A/B/C-ITR TOTAL·DONE`, `PUNCH A/B TOTAL·CLOSED`, `PJTNO` 필터 — 계약은 CLAUDE.md 11번).
  요소 로드와 별도 try — **테이블 미구성이면 요소 파생 목록으로 자동 fallback**
  (단계별 가시화 모드만 비활성). MCC 계획일 경과 + P-MCC/MCC 실적 미입력 = `IsDelayed`(지연)
- EIT Tray / Cable 탭은 OASIS 미지원 — 트레이 진척 테이블 부재, EIT_Cable에 Node 매핑 부재
  (상세: `docs/SQL_DB_CONNECTION_ANALYSIS.md`)

**이중 소스 UI (`UI/DataSourcePanel.cs`)** — Spool/Hydrotest/Equipment 탭 상단 공용 블록:
- [Excel 로드] [OASIS 로드] + 소스별 ● 상태(건수·출처·시각)
- "적용 기준" 라디오: 로드된 소스만 활성, 첫 로드 자동 선택. 전환 시 매칭 셋 초기화 +
  적용 이력이 있으면 자동 재적용 (Equipment는 태그 셋 기반 레벨 타겟 인덱스도 재빌드)
- [비교 출력]: 둘 다 로드 시 활성. 키 조인 후 Excel에만/OASIS에만/필드 불일치를
  CSV(UTF-8 BOM)로 바탕화면에 출력 (`Loaders/SourceComparer.cs`) — OASIS 이행 검증용

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

**Sub-system 마스터** (6단계 — `SubSystemMasterData.GetStageAtDate`):
- NotStarted → Walkdown → Partial MCC → MCC → RFCC → PCC (마일스톤 실적일 역순 스캔)
- ITR(`done/total`)·Punch(`A·B` open 수치)는 stage 계산에 안 쓰고 선택 테이블/리포트에 status로 병기

**Sub-system 요소** (`ProgressStatus` 3단계 정규화):
- 공종마다 stage 수가 달라(Equipment 4 / Hydrotest 6) 단일 색상 체계로 묶기 위해
  `SubSystemElement.StatusAt(기준일)`이 미착수/진행중/완료로 접는다
  (마지막 stage 도달 = 완료, 그 외 착수 = 진행중) — 원본 `GetStageAtDate` 재사용

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
  5. 결과를 캐시에 저장 (VisualModule → Stage → ModelItemCollection)
```

**증분 업데이트:**
- 색상/투명도 변경 시 캐시된 Collection 재사용
- Reset 없이 해당 Stage만 즉시 반영

**Stage 캐시는 공종(VisualModule)별로 격리:**
- 키가 enum 이름 문자열이라 단일 캐시로는 "NotStarted"(전 모듈), "Setting"(Spool/Equipment)이
  충돌 — 한 탭의 증분 색 변경이 다른 탭이 칠한 컬렉션을 덧칠하는 간섭이 있었음
- 각 `Apply*`는 자기 모듈 캐시만 Clear/채움 → 다른 공종 적용 후에도 자기 색 미세조정 가능
- `Apply*`는 전체 Reset 없이 자기 매칭 아이템만 칠하므로 공종 간 색은 뷰에서 공존
  (Tray 노드와 Cable `-BOX`는 물리적으로 다른 객체)
- "전체 초기화"만 문서 전체 리셋 (공종별 초기화는 CLAUDE.md 향후 고려사항 7 참조)

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

**탭 구성:** Hydrotest | Spool | Equipment | EIT Tray | Cable Pull | Sub-system | Tools

**공통 패턴 (날짜 기반 탭 동일):**
- DateTimePicker (기준일, 기본: 오늘)
- 2열 색상 패널 (색상 피커 + 투명도 드롭다운)
- 색상 변경 시 증분 업데이트 (캐시 활용)
- 전체/매칭/미매칭 탭 필터 (건수 표시)
- 검색 + "매칭 Status 출력" CSV Export
- ListView 컬럼 정렬 (오름차순/내림차순)
- 적용 / 전체 초기화 / 속성 쓰기 / Viewpoint 저장 / NWD Export

**Sub-system 탭 (`UI/SubSystemTab.cs`):**
- OASIS 전용 로드 (단일 소스 — DataSourcePanel 미사용). 요소 + 마스터를 한 번에 로드,
  마스터 미구성이면 요소 파생 목록 fallback + 단계 모드 라디오 비활성
- 시각화 2모드 라디오: **Sub-system 단계별**(마스터 Walkdown→PCC 6색 — 선택한 sub-system의
  요소 전체가 그 sub-system의 현재 마일스톤 색을 받음, 기본·마스터 필요) /
  **요소 진행상태별**(미착수·진행중·완료 3색). 색상 그리드는 모드에 따라 전환 표시
- 선택 UI (dual-list): 좌측 검색(코드+설명) + status 테이블(Sub-system/Description/단계/
  ITR/Punch/요소, ~400개 스크롤) ↔ **[▶ ◀ ▶▶ ◀◀] 화살표** ↔ 우측 선택 누적 테이블
  (단계색 스와치·단계·요소·매칭) + 하단 선택 개수/요소 합계 라벨. 다중 선택 후 ▶ 추가 /
  ◀ 제거, ▶▶ 필터 결과 전체, ◀◀ 전체 해제, 더블클릭 = 추가/제거. 이미 담긴 좌측 행은
  녹색 배경, 요소 0건(마스터에만 존재)은 회색 글자, 마스터 외 요소 그룹은 "(마스터 외)"
- 우측 행 클릭 → 해당 sub-system 매칭 아이템 3D 선택·포커스
- [현황 리포트 출력]: CSV 리포트 (헤더 블록 + Sub-system별 요약(Description/단계/ITR/
  Punch/공종/매칭/진행/완료율) + 상세 리스트). 선택이 있으면 선택만, 없으면 전체.
  매칭 O/X는 마지막 적용 스냅샷 기준, 미적용 sub-system은 "-" (CLAUDE.md 8번 단기안의 첫 구현)
- 적용은 `ColorOverrideEngine.ApplySubSystem` — 그룹 키(SubSystemStage명 또는 ProgressStatus명)로
  묶어 그룹당 1회 색상, 캐시는 `VisualModule.SubSystem`으로 격리. 증분 색 변경은
  마지막 적용 모드와 같은 그리드에서만 유효

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
