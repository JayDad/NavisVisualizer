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

### 2. WalkAndIndex 조기 정지 한계 (우선순위: 낮음)

`WalkAndIndex`는 tag-like 노드가 "tag-like 자식이 없을 때" 정지한다. 하지만 `/CM/PDA/ELEC/PCVTRAY-STW`처럼 **digit 없는 범주 노드 바로 아래에 geometry가 직접 붙은 경우**엔 조기 정지가 발동하지 않아 geometry까지 전부 방문.

항목 1 적용 시 자연스럽게 완화되지만, 단일 NWD 시나리오에서도 최적화가 필요하면 `BuildIndexForTrayIds`(Equipment의 `BuildIndexForTags`와 동일 방식)를 추가하는 방안 검토.

### 3. Cable Stage 날짜화 (EIT Tray)

현재 `EitTrayData.GetStageAtDate`는 Tray install date만 날짜로 쓰고 Cable Pulling / Completed는 *현재 상태*로 판정한다. 입력 데이터에 `Cable Pull date` / `Cable Complete date` 컬럼이 추가되면 `Dictionary<EitStage, DateTime?> StageDates` 패턴으로 교체 (Spool/Hydrotest와 동일 구조).

## 개발 규칙

- Navisworks Simulate 2022 / .NET Framework 4.8 타겟. `Autodesk.Navisworks.*` DLL은 Windows 설치 경로 참조.
- 리눅스/맥에서는 `dotnet` 빌드 불가 (COM interop + Windows-only DLL). Windows에서만 컴파일 검증.
- 새 탭 추가 시 그룹 결정:
  - "digit 포함 DisplayName" 매칭 → `TagSearcher` 재사용
  - 그 외 매칭 전략 → 새 `ModelItemSearcher` 인스턴스 + `ColorOverrideEngine` 생성자에 추가
