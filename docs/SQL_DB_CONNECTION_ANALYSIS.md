# SQL DB 연결 분석 — [Navis] 스키마 6개 테이블 vs 현재 플러그인

> **구현 현황**: 이 분석에 따라 Spool/Hydrotest/Equipment 탭의 OASIS 로더(`SqlLoader`) +
> 이중 소스 UI(`DataSourcePanel`) + Excel↔OASIS 비교 출력(`SourceComparer`)이 구현됨.
> 아래 본문은 구현 전 분석 기록. 잔여 항목은 `CLAUDE.md` §6 참조.

> 분석 기준: gist(db3b4194f980b52b03424df3534cdca6)의 테이블 6개 구조/샘플 데이터 ↔ 현재 코드베이스.
> 모든 판정은 코드 근거(파일:라인)로 검증함. 컬럼명 비교 규칙: `StringComparer.OrdinalIgnoreCase`
> (대소문자만 무시 — 공백/하이픈/언더스코어는 구분됨).

## 결론 요약

**현재 구현 그대로는 DB 연결이 불가능하다.** 플러그인에는 SQL 관련 코드가 한 줄도 없고
(`SqlClient`/`OleDb`/`ConnectionString`/설정 파일 전무), 5개 탭 모두 `OpenFileDialog` +
`ExcelLoader.Load*(filePath)` 경로만 존재한다. 따라서 "연결에 문제가 있는가"의 1차 답은
**연결 계층 자체를 새로 만들어야 한다**이고, 2차 답은 **테이블별로 호환성 격차가 크게 다르다**이다:

| 테이블 | 대상 탭 | 호환성 | 핵심 문제 |
|---|---|---|---|
| `Piping_Spool` | Spool | ◎ 거의 완벽 | 14개 stage 컬럼 전부 일치. 신규 `FIT-UP`(설치 fit-up)만 미지원 |
| `Piping_HydrotestPKG` | Hydrotest | △ 이름 1개로 전면 실패 | `PKGNO`가 헤더 후보와 불일치 → 로더가 예외. 수정은 한 줄 |
| `Mech_EQ` | Equipment | △ 부분 손상 | Delivery 단계 사망(이중 불일치), SUB-SYSTEM/설명 공란 |
| `All_EQ` | Equipment | ✕ 이중 사망 | 위 문제 + 선행 `/` 태그 → **전 행 매칭 실패** |
| `EIT_Cable` | Cable | ✕ 구조적 불일치 | `Node` 컬럼 부재 — 케이블↔노드 매핑 데이터가 DB에 없음 |
| `EIT_EQ` | (없음) | ✕ 소비처 없음 | 어떤 탭 형태와도 안 맞음. 정책 결정 필요 |

**추가 중요 발견: EIT Tray 탭이 쓸 테이블이 6개 안에 아예 없다.**
EitTrayTab은 `Tray Number / Tray Lth / Install % / Tray install date` 형태를 요구하는데
(`ExcelLoader.cs:232,256-259`), EIT_EQ는 계기(instrument) 목록이지 트레이 진척 테이블이 아니다.
트레이 테이블이 덤프에서 빠진 것인지, DB에 아직 없는 것인지 확인 필요.

---

## 1. 테이블별 상세 분석

### 1.1 [Navis].Piping_Spool → SpoolTab — 거의 그대로 호환

- `SPOOL NO` ≈ 후보 `"Spool No"` ✓, `ISO NO` ≈ `"ISO No"` ✓ (`ExcelLoader.cs:84,124`)
- **14개 stage 컬럼 전부 문자 그대로 일치** (`DataModels.cs:87-95`):
  `B/V, F/up, W/D, NDE, PWHT, S/out, G-후공정인계, Galv2, Pnt1, Pnt2, Stock, H/O일자, Setting, Welding`
- **미지원**: DB에 `FIT-UP` 컬럼이 추가로 존재 (Setting과 Welding 사이 = **설치 단계** fit-up,
  제작 `F/up`과 별개). `SpoolStage`에 대응 멤버가 없어 조용히 버려짐. 실제 설치 단계라면
  enum/OrderedStages/Labels/ColumnMap/InstallStages/SpoolDefaults 색상에 1개 추가 필요
  (SpoolTab 색상 UI는 InstallStages에서 자동 생성되므로 그 뒤는 자동).
- `PRJTNO` 미사용 — 프로젝트 필터 없음(아래 3.1).

### 1.2 [Navis].Piping_HydrotestPKG → HydrotestTab — 이름 하나로 전면 차단

- **차단**: `PKGNO`가 후보 `{"Test Package No.","Test Package No","TestPkgId","Test Pkg No"}`
  (`ExcelLoader.cs:16`)와 불일치 → 헤더 행 탐지 실패 → **로더가 예외를 던지고 아무것도 로드 안 됨**.
  수정은 후보 배열에 `"PKGNO"` 한 줄 추가(또는 SqlLoader에서 명시 매핑).
- `LINESVC` ↔ `{"Line Service","LineService","Service"}` 불일치 → LineService 공란 (표시용이라 경미).
- `System` 컬럼("0201")은 sysCol 후보 `"System"`과 일치 ✓ — 단 더 세밀한 `Sub-System`("0201-00")은 버려짐.
- **6개 stage 컬럼(Review, Line inspection, Flushing, Hydrotest, Drying, Reinstatement) 전부 일치** ✓.
- 샘플 데이터 참고: ROW1처럼 Line inspection=Flushing=Hydrotest=Drying이 같은 날짜면
  `GetStageAtDate` 역순 스캔 특성상 그 날 이후 **바로 Drying으로 표시**된다(중간 단계 건너뜀).
  이 날짜들이 계획일이라면 "지난 계획일 = 달성"으로 오표시되는 점도 유의(스테이지 로직은 실적일 가정).

### 1.3 [Navis].All_EQ / Mech_EQ → EquipmentTab — Delivery 사망 + 슬래시 태그 전멸

매핑되는 것: `TAG NO`≈`"Tag No"` ✓, `RFQ NO`≈`"RFQ No"` ✓, `Confirmed ETA` ✓,
`Loading/Setting/Inspection` ✓. 매핑 실패:

1. **Delivery 단계 이중 사망** (`ExcelLoader.cs:189,215-216`):
   - 컬럼명: 로더는 `"Delivery"`만 찾는데 DB는 `Delivered` → 미발견.
   - 의미: 기존 로직은 셀 값이 문자열 `"Delivered"`일 때 Confirmed ETA를 날짜로 쓰는 구조인데,
     DB `Delivered`는 **날짜**(2025-06-20)다. 이름을 맞춰도 `"2025-06-20".Equals("Delivered")`는
     영원히 false → `EquipmentStage.Delivery`가 절대 설정 안 됨.
   - 결과: 입고됐지만 Loading 전인 장비가 전부 회색(미착수)으로 표시.
   - 해법: SqlLoader에서 `Delivered` 날짜를 `StageDates[Delivery]`에 직접 매핑 (다른 stage와 동일 패턴).
     **주의**: `UserDataService.BuildEquipmentPropVec`가 Delivery stage 날짜를 명시적으로 skip하고
     (`UserDataService.cs:265`) 빈 DeliveryStatus 텍스트를 쓰므로, 속성 쓰기/NWD Export까지
     일관되려면 UserDataService도 같이 수정해야 함.
2. **All_EQ 선행 슬래시 태그 → 전 행 매칭 실패** (`/101560-SP-70035`):
   인덱스 키는 `TrimStart('/')+ToUpperInvariant`로 저장되는데(`ModelItemSearcher.cs:90,122,148`)
   조회(`FindBySpoolIds`, `:238`)는 **원본 TagNo를 그대로** 씀(사전은 대소문자만 무시).
   `BuildIndexForTags`의 depth 탐지(`:53`)는 태그를 정규화해서 **인덱스는 정상으로 빌드되는데
   조회만 전부 미스**나는 비대칭 — 미매칭 원인 추적을 어렵게 하는 함정.
   EitTrayData/CableNodeData에는 있는 `NormalizeId`가 EquipmentData에는 없음 → 추가 필요.
3. `SUB-SYSTEM`(하이픈) ↔ `{"SUB SYSTEM","Sub System","SubSystem","System"}` 불일치 → 공란.
4. `TAG DESCRIPTION` ↔ `{"Equipment Description","Description","Desc"}` 불일치 → 설명 공란
   (리스트 표시·검색·CSV·속성 쓰기 모두 빈 값).
5. **테이블 2개 → 탭 1개**: EquipmentTab은 단일 리스트를 교체 로드할 뿐 병합 개념이 없음.
   All_EQ와 Mech_EQ를 UNION할지, 공종 드롭다운으로 선택할지 정책 결정 필요.
   UNION 시 TAG NO 중복이면 중복 행 그대로 생겨 통계 이중 계산(로더에 dedupe 없음).
6. `WEIGHT`, `AITR*`, `Punch*`, `PJTNO`는 어디서도 안 읽음 — AITR/Punch 진척은
   리스트/CSV에 노출하면 유용한 신규 데이터(기회).
7. 참고: Mech_EQ ROW1처럼 Loading=Setting 동일 날짜면 Loading 단계는 화면에서 영영 안 보임
   (역순 스캔이 항상 Setting 반환) — hydrotest와 동일한 동일날짜 collapse.

### 1.4 [Navis].EIT_Cable → CableTab — 구조적 불일치 (최대 이슈)

- **차단 1 — Node 부재**: `LoadCablePull`은 한 헤더 행에 `"Node"`와 `"Cable No"`가 **둘 다**
  있어야 하며(`ExcelLoader.cs:297-319`) 없으면 예외. EIT_Cable에는 `CABLE NO`만 있다.
- **차단 2 — 데이터 모델 불일치**: CableTab 전체가 **노드(트레이 박스) 단위 집계** 구조다.
  Excel 입력은 "케이블이 지나는 노드마다 1행"(3개 노드 통과 = 3행)이고, 이 행 순서가
  루트의 홉 순서까지 암묵적으로 인코딩한다(`CableTab.cs:732-758`). EIT_Cable은
  케이블당 1행 + `ROUTE` 식별자 하나(`LQ_LT_IN_LV1048`)뿐 — **케이블↔노드 매핑
  (route detail) 테이블이 DB 스키마에 없다.** 이게 보완되지 않으면 CableTab은 이 테이블로
  동작할 수 없다. 추가 시 홉 순서 컬럼(SEQ)도 필수 — SQL 결과 순서는 ORDER BY 없이는 미정의.
  또한 `ROUTE` 값 형식이 NodeId 형식(`101780-EMCT-52101_A-ND`)과 달라 보여 키 관계 확인 필요.
- **오매핑 함정 (silent wrong-data)**: DB `DESIGN LTH`가 하필 `"Design Lth"` 후보와 일치해서
  **엉뚱한 필드** `RouteDesignLth`(선언만 있고 아무도 안 읽는 죽은 필드)로 들어가고,
  정작 진척 계산에 쓰는 `DesignLth`(후보 `"Cable Design Lth"`)와 `PulledLth`(후보 `"Cable Pulled Lth"`)는
  null → `OverallProgress` null → **전 노드 미착수 표시**. 에러 없이 조용히 틀리는 대표 사례.
- `Pulling %`는 매핑되지만(`PullingProgress`) 표시용일 뿐 — 노드 stage는
  `sum(PulledLth)/sum(DesignLth)`만 사용(`DataModels.cs:373-390`)이라 색상은 여전히 깨짐.
- 이름 불일치(공란화): `INSTALL_MODULE`, `ROUTE_SYS`, `CABLE_TYPE`, `CABLE_CORE`, `CABLE_SIZE`
  (언더스코어/접두어 차이), `ROUTE`/`ROUTE_TYPE`은 후보 자체가 없음.
  일치: `FROM/TO MODULE`, `FROM/TO EQUIP`, `SYSTEM`, `OUT DIA`, `TRAY SYS` ✓.
- `Equip No` 컬럼이 없어 Equip No 검색축이 조용히 죽음(FROM/TO EQUIP과는 별개 필드).
- **데이터 의미 확인 필요**: 두 샘플 모두 PULLING LTH(63, 65) > DESIGN LTH(62, 64)인데
  `Pulling %`는 0.0% — PULLING LTH가 "실제 포설 길이"가 아니라 **발주/절단 계획 길이**로 보임.
  그대로 PulledLth에 연결하면 진척 103% → **전량 포설완료(녹색) 오표시**(정반대 오류).
  컬럼 의미를 데이터 오너에게 확정받기 전에는 진척 수식에 연결하면 안 됨.

### 1.5 [Navis].EIT_EQ — 소비할 탭이 없음

- `TAG NO | TAG DESCRIPTION | WEIGHT | WRKDTE` 형태는 어떤 로더와도 안 맞음:
  LoadEitTray는 `"Tray Number"` 필수 → 예외. LoadEquipment에 넣으면 로드는 되지만
  stage 컬럼이 하나도 없어 **영구 미착수**(회색 70% 투명)로만 칠해짐. WRKDTE는 아무데도 안 감.
- 결정 필요: 어느 탭이 소비하나? WRKDTE(작업/설치일 추정)가 단일 단계
  (`{미착수, 설치완료}` + `WRKDTE→Installed`)를 구동하는 최소 모듈이 자연스러움.
  태그(101180-AT-10051)는 digit 포함이라 CLAUDE.md 규칙상 **TagSearcher 재사용** 대상이고
  WalkAndIndex가 이미 인덱싱 가능한 형태. 반면 EquipmentSearcher(레벨 타겟)는 ELEC 트리의
  깊이 불균일(CLAUDE.md 항목 2) 때문에 위험 — 첫 매칭 깊이 하나만 인덱싱하므로
  깊이가 섞여 있으면 조용히 누락됨.

---

## 2. 횡단 이슈 (SqlLoader 구현 시 공통 처리)

### 2.1 프로젝트 필터
- 컬럼명이 테이블마다 다름: All_EQ/Mech_EQ는 `PJTNO`, Piping 2종은 `PRJTNO` —
  WHERE 절 컬럼명을 테이블별로 관리해야 함.
- **EIT_EQ/EIT_Cable에는 프로젝트 컬럼이 아예 없음** → 멀티 프로젝트 DB라면 타 프로젝트
  데이터 혼입 위험. 스키마 보완 또는 별도 스코핑 방법 확인 필요.
- 플러그인에는 프로젝트 개념 자체가 없음(모델/CSV/필터 어디에도) — 로더 파라미터로 도입.

### 2.2 ParsePercentage의 0~100 숫자 함정
`ExcelLoader.cs:497-510`: 문자열 `"0.0%"`/`"100.0%"`는 안전하게 처리되지만, DB가 **숫자형
0~100 스케일**로 주면 `>1.0` 휴리스틱 때문에 `1`(=1%)이 `1.0`(=100%)으로, `0.5`(=0.5%)가
50%로 오역된다. EitTray `Install %`도 동일 경로. SQL 쪽에서 스케일을 계약으로 고정하거나
(SELECT에서 `/100.0`), 로더에서 명시 스케일 파라미터를 받아야 함.

### 2.3 날짜/숫자 문화권
`ParseCellValue`는 typed `DateTime`이면 그대로 통과(안전), 문자열이면 CurrentCulture
`DateTime.TryParse`(로케일 취약, 실패 시 **조용히 null** → 이전 단계로 후퇴).
숫자 파서(`ParseInt/ParseDouble/ParsePercentage` 문자열 분기)도 CurrentCulture.
→ SELECT에서 DATE/DECIMAL 타입을 유지해 typed로 넘기는 것이 최선(문자열화 금지).

### 2.4 중복 행
LoadHydrotest/LoadSpool/LoadEquipment는 dedupe가 없다(LoadEitTray만 있음).
SQL 조인/뷰가 키당 여러 행을 주면 리스트 중복, 통계 이중 계산, MatchedCount 왜곡.
→ 쿼리에서 1 키 1 행 보장 또는 로더에 dedupe 추가.

### 2.5 UI 스레드 블로킹
모든 로드는 UI 스레드 동기 실행(비동기 코드 전무). SqlConnection 기본 타임아웃(접속 15s)
동안 Navisworks 전체가 얼어붙음. DB 조회는 Navisworks API를 안 건드리므로 `Task.Run` +
`Control.Invoke`로 결과만 마샬링하는 구조가 안전(최소한 `Connect Timeout=5` 명시).

### 2.6 미매칭 0건 진단 부재
4개 날짜 탭의 UpdateStats는 `MatchedCount > 0`일 때만 매칭/미매칭 줄을 표시 —
전량 미매칭(1.3의 슬래시 문제 같은) 상황에서 오히려 진단 줄이 사라진다. 속성 쓰기도
"속성 삽입 대상이 없습니다"라는 일반 오류뿐. DB 연동 디버깅을 위해 0건일 때도 표시 권장.

### 2.7 기타 (기존 결함, DB 무관하지만 연동 검증 때 헷갈릴 것들)
- `ApplyEquipment`만 `_cachedStageCollections.Clear()`를 안 함 + stage 캐시 키가 enum
  `ToString()`이라 탭 간 충돌(`"Setting"`, `"NotStarted"`) — 탭 전환 후 색 조정 시 상호 오염 가능.
- CSV Export는 적용(Apply) 전이면 전 행 매칭 "O"로 출력(매칭 셋이 비어 있을 때 true 처리).
- 테스트 프로젝트는 현재 컴파일 불가(구 API `HydrotestStatus`/`.Status`/`.SpoolIds` 참조) +
  Windows 전용 csproj 참조 — SqlLoader를 테스트 가능하게 만들려면 먼저 정비 필요.

---

## 3. 구현 방향 권장

### 3.1 아키텍처
- **`Loaders/SqlLoader.cs` 신설, 테이블별 명시 SELECT + 명시 컬럼→프로퍼티 매핑 (권장)**.
  근거: EIT_Cable의 `DESIGN LTH` 오매핑처럼 이름 후보 방식은 **에러 없이 조용히 틀린다**.
  DB 스키마는 안정된 계약이므로 헤더 추정이 불필요하고, 불일치는 즉시 드러나는 에러가 된다.
- 드라이버: **`System.Data.SqlClient` (net48 내장)** — csproj 무변경, 배포 파일 추가 없음.
  `Microsoft.Data.SqlClient`는 네이티브 SNI 등 의존성 트리가 커서 Navisworks(Roamer.exe)
  AppDomain에 바인딩 리다이렉트 없이 올리기 위험(플러그인은 호스트 exe.config를 못 고침).
- 통합 지점: 각 탭 `BtnLoad_Click`과 동형인 "DB 로드" 버튼(또는 소스 드롭다운) +
  공용 연결 설정 다이얼로그. `SqlLoader.LoadX(conn, project) → List<T>`가 기존 리스트에
  꽂히면 **DataModels/Searcher/ColorOverrideEngine은 무변경** (검증 완료 — 소비 경로가
  전부 인메모리 List 기준).
- 대안/중간 단계:
  - **DB 뷰에서 Excel 헤더명으로 alias** (`[PKGNO] AS [Test Package No.]` 등) → 기존 후보
    리스트 재사용 가능. 단, 선택 컬럼 silent-skip 특성은 남음.
  - **인터림 무코드 경로**: 뷰를 Excel로 정기 export하면 지금 플러그인 그대로 동작 —
    현장 DB 접속 정책(방화벽/계정)이 정리되는 동안 실용적.
- 설정 저장: `%APPDATA%\NavisVisualizer\settings.json` (플러그인은 app.config 사용 불가).
  SQL 인증 암호는 DPAPI(`ProtectedData`, net48 내장) 또는 `Integrated Security=SSPI` 우선.
- 보안/위생: 읽기 전용 계정(`GRANT SELECT ON SCHEMA::Navis`), 전 식별자 대괄호 인용
  (`[SUB-SYSTEM]`, `[Pulling %]`, `[G-후공정인계]`, `[H/O일자]`), 프로젝트 값은 SqlParameter.
- 파서 재사용: `ExcelLoader`의 `Parse*` 헬퍼는 private — 공용 internal 클래스로 추출하면
  SqlLoader가 재사용 + (Autodesk 참조 없는 프로젝트로 분리 시) 리눅스에서도 매핑 로직 단위 테스트 가능.

### 3.2 코드 수정 필요 목록 (우선순위순)

| # | 항목 | 규모 |
|---|---|---|
| 1 | SqlLoader + 연결 설정 UI/저장 신설 | 신규 계층 |
| 2 | Equipment: `Delivered` 날짜 → `StageDates[Delivery]` 직접 매핑 (+ `UserDataService.cs:265` skip 제거) | 소 |
| 3 | Equipment: TagNo 정규화(`TrimStart('/')`) — `EquipmentData.NormalizeId` 추가 또는 로더에서 strip | 소 |
| 4 | Hydrotest: `PKGNO`/`LINESVC` 매핑 (SqlLoader 명시 매핑이면 자동 해결) | 소 |
| 5 | Equipment: `SUB-SYSTEM`/`TAG DESCRIPTION` 매핑 | 소 |
| 6 | Spool: `FIT-UP` 설치 단계 추가 여부 결정 후 반영 | 소~중 |
| 7 | 프로젝트 필터(PJTNO/PRJTNO) 파라미터화 | 소 |
| 8 | Cable: route-detail(노드 매핑) 확보 후 로더 설계 | **데이터 선결** |
| 9 | EIT_EQ 소비 모듈 신설(WRKDTE 단일 단계 + TagSearcher 재사용) | 중 |
| 10 | 방어: dedupe, % 스케일 계약, 비동기 로드, 미매칭 0건 표시 | 소 |

### 3.3 데이터 오너에게 확인할 사항 (코드보다 먼저)

1. **EIT_Cable ↔ 노드 매핑**: 케이블이 지나는 트레이 노드 목록(+순서 SEQ)을 주는 테이블/뷰가
   있는가? `ROUTE`(`LQ_LT_IN_LV1048`) 값과 3D NodeId(`...-ND`) 형식의 관계는?
2. **PULLING LTH 의미**: 실제 포설 길이인가, 발주/절단 계획 길이인가?
   (샘플: design 62 < pulling 63인데 Pulling % = 0.0%)
3. **EIT Tray 진척 테이블**: `Tray Number / Install %` 형태 테이블은 어디에 있는가?
4. **EIT_EQ 용도**: 어느 화면에서 소비? WRKDTE는 무슨 날짜(설치 실적일?)인가?
5. **All_EQ vs Mech_EQ 관계**: 포함 관계/공종 분리? Equipment 탭에서 어떻게 선택·병합?
6. **날짜 성격**: stage 날짜들이 실적일인가 계획일인가? (현재 로직은 "지난 날짜 = 달성" 가정)
7. **All_EQ TAG NO의 선행 `/`**: 데이터 정제 대상인가, 규약인가?
8. **Hydrotest PKGNO 형식**: 3D 모델 DisplayName에 `TRIONH-...` 전체 문자열이 그대로 있는가?
   (매칭은 정확 일치 — 실모델에서 1건 검증 권장)

---

*검증 방법: 테이블별 검증 에이전트 6개(주장별 반박 시도, 파일:라인 근거) + 누락 검사 1회.
전 주장 CONFIRMED (EIT_EQ의 EquipmentSearcher 적용 가능성 1건만 PARTIAL — 깊이 균일성 가정 필요).*
