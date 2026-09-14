# packages-offline — 사내망 오프라인 NuGet 복원용

`nuget.config`가 이 폴더를 첫 번째 패키지 소스로 쓴다. 인터넷(api.nuget.org)이 막힌 PC에서도
`deploy.bat`이 여기서 패키지를 복원한다.

| 패키지 | 버전 | 용도 |
|---|---|---|
| ExcelDataReader | 3.6.0 | Excel 로드 |
| ExcelDataReader.DataSet | 3.6.0 | DataSet 변환 (ExcelDataReader 3.6.0 의존) |
| Microsoft.CSharp | 4.7.0 | COM late-binding(`dynamic`) |

net48 타깃 기준 위 3개 외 추가 의존 패키지 없음 (nuspec 확인 2026-09).
`NavisVisualizer.csproj`에 PackageReference를 추가/변경하면 해당 `.nupkg`(+의존)를 여기에 같이 넣는다:
`https://api.nuget.org/v3-flatcontainer/{id소문자}/{버전}/{id소문자}.{버전}.nupkg`
