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

## 구현 현황

| 영역 | 상태 |
|------|------|
| 모델 / 종료코드 정책 | ✅ |
| CommandRunner (타임아웃·로케일 고정) | ✅ |
| 엔진 / 카탈로그 / 리포터(콘솔·JSON) | ✅ |
| CLI(--once/triage) · Daemon(주기·systemd) 골격 | ✅ |
| 공유 헬퍼 (SystemdProbe·CatTable·JvmHeap·UfwStatus·IpAddr·Bonding·NginxStream·NginxConf·Ss·SyslogNgStats·ProcNetUdp·DropRateEvaluator) | ✅ |
| `hosts` (loopback·호스트명 매핑) | ✅ |
| `netplan` (yaml·실제IP·bonding·게이트웨이 ping) | ✅ |
| `elasticsearch` (_cat/health·node.total·heap·watermark, curl) | ✅ |
| `syslog-ng` (다중 인스턴스·문법·UDP 바인드·rsyslog 충돌) | ✅ |
| `nginx` (-t·active·80/443 listen·TLS 만료·sites-enabled) | ✅ |
| `nxcollector` (core ESTAB·서비스·크래시루프) | ✅ |
| `ufw` (status verbose·before.rules ICMP·drift·syslog 교차) | ✅ |
| `crosscheck` (nginx↔syslog-ng 포트 집합 정합) | ✅ |
| `drop` (flow 드랍률 delta·2단 임계·최소표본·리셋 가드) | ✅ |
| 단위 테스트 | ✅ 130개 통과 |

**8개 체크 모듈 전부 구현 완료.**

## 남은 일 (선택)

- 기대값 누락 시 대화형 입력(TTY) 훅
- 데몬 flow delta를 주기 기반으로(현재 one-shot은 SampleWindow 대기)
- TLS 체인 유효성(현재는 만료일만), netplan DNS 일치, ES 인증서/워터마크 임계 expected.yaml 외부화

> 빌드/실행은 Ubuntu 서버(x64)에서. 리눅스 도구(`ss`,`nginx -T` 등) 호출은 그 환경에서만 의미 있음.
