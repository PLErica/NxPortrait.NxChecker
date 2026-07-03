# nxcheck

Ubuntu 서버 출고 설정 점검툴 (read-only). 설계: [`../nxcheck-design.md`](../nxcheck-design.md)

## 구조

```
nxcheck/
├── Core/          # 체크 라이브러리 (모드 무관, 단일 진실)
│   ├── Model/     # CheckResult, Severity, CheckStatus, ExitCodePolicy, CheckContext
│   ├── Runners/   # CommandRunner (타임아웃 + LC_ALL=C)
│   ├── Checks/    # ICheck, CheckEngine, CheckCatalog + 8개 모듈
│   └── Expected/  # ExpectedSpec(+Loader) — expected.yaml 모델
├── Cli/           # nxcheck — --once / triage, 콘솔·JSON 리포터
├── Daemon/        # nxcheckd — BackgroundService, 주기 실행 (+ UseSystemd)
├── Tests/         # xUnit — FakeCommandRunner로 파서·정책·엔진 검증
└── expected.yaml  # 기대값 스펙 샘플
```

## 빌드 / 실행

```bash
dotnet build                      # 전체 솔루션
dotnet test                       # 단위 테스트 (Windows에서도 동작)
dotnet run --project Cli -- --once
dotnet run --project Cli -- triage --flow
dotnet run --project Cli -- --once --json --expected ./expected.yaml
```

종료코드: `0`=전부 통과, `1`=경고, `2`=치명적.
`error`/`skip` 매핑은 `ExitCodePolicy` 참조 (critical error→2, 비critical error→1, skip 무영향).

## 구현 현황 (골격)

| 영역 | 상태 |
|------|------|
| 모델 / 종료코드 정책 | ✅ |
| CommandRunner (타임아웃·로케일 고정) | ✅ |
| 엔진 / 카탈로그 / 리포터(콘솔·JSON) | ✅ |
| CLI(--once/triage) · Daemon(주기·systemd) 골격 | ✅ |
| `hosts` 체크 | ✅ (구현 예시) |
| `netplan` `elasticsearch` `syslog-ng` `nginx` `nxcollector` `ufw` `crosscheck` | ⬜ 스텁(SKIP) — 설계 4.x 참조 |
| 단위 테스트 | ✅ 27개 통과 (정책·로더·hosts·엔진·러너) |

## 남은 일

- 7개 체크 모듈 구현 (각 파일 상단 TODO 주석에 항목 정리)
- 기대값 누락 시 대화형 입력(TTY) 훅
- flow 드랍 delta 샘플링(2단 20/30%, 최소표본 가드, 카운터 리셋 가드)

> 빌드/실행은 Ubuntu 서버(x64)에서. 리눅스 도구(`ss`,`nginx -T` 등) 호출은 그 환경에서만 의미 있음.
