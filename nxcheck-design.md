# nxcheck — 서버 출고 설정 점검툴 설계 문서

> Ubuntu 서버 출고 전/후 설정 상태를 **읽기 전용(read-only)**으로 점검하는 툴.
> 상태를 절대 변경하지 않으며, 진단·리포트·종료코드만 제공한다.

---

## 1. 핵심 원칙

- **검사 전용.** 어떤 모드에서도 시스템 상태를 바꾸지 않는다. 자동 교정(`--fix`) 없음.
- **엔진 하나, 진입 모드 셋.** 체크 로직은 단일 라이브러리. 트리거 방식과 출력 깊이만 다르다.
- **CLI는 데몬에 의존하지 않는다.** triage 시점은 뭔가 고장난 순간이고, 그 대상이 데몬일 수도 있으므로, CLI는 체크 라이브러리만 공유하고 데몬 프로세스에는 붙지 않는다. 항상 체크를 새로 돌린다.
- **결과 모델 통일.** 모든 체크는 동일 구조를 반환한다: `항목 / 심각도(critical·warning·info) / 상태(pass·fail·skip·error) / 기대값 / 실제값 / 조치힌트`. 출력 포맷(콘솔·JSON)은 이 위에 갈아끼운다.
- **종료코드로 파이프라인 연동.** `0`=전부 통과, `1`=경고, `2`=치명적 실패.
  - **error·skip 매핑(확정):** `critical` 체크가 `error`(못 돌았음)면 `2`, 비critical `error`는 `1`, `skip`은 종료코드 무영향. "못 돌았는데 exit 0"으로 통과시키지 않는다.
- **실행 전제 = root.** daemon은 root로 뜬다. `ss -p`(프로세스명)·`ufw status`·인증서 읽기·`/proc/net/udp` 소켓 귀속 등은 root여야 제대로 보이므로 권한 degrade 처리는 두지 않는다. 단 비-root 실행 시 시작 시 경고 한 줄.
- **외부 명령은 안정적으로 호출.** 로케일·버전 의존 파싱을 막기 위해 `LC_ALL=C` 강제 + 기계가독 출력 우선(`ip -j addr`, `systemctl show -p` 등). 모든 외부 호출에 per-call 타임아웃을 걸고 초과 시 `error("timed out")` (triage는 "고장난 순간"이라 명령이 잘 멈춤 — 특히 DNS 죽으면 이름해석 블록).

---

## 2. 진입 모드

| 모드 | 트리거 | 깊이 | 용도 |
|------|--------|------|------|
| `--once` | 수동/파이프라인 1회 | static(기본) | 출고 직전 QA, 프로비저닝 검증 |
| `daemon` | systemd 상시 | static + flow | 현장 상시 감시, config drift·연결끊김 탐지 |
| `triage` | 문제 발생 시 수동 1회 | static + flow + 깊은 진단 | "명령어 하나로 어디가 깨졌는지" 특정 |

세 모드 모두 read-only이며 동일한 체크 라이브러리를 호출한다. 다른 것은 *언제 도느냐*와 *얼마나 깊이 펼치느냐*뿐이다.

---

## 3. 체크 깊이 2단계

체크는 두 깊이를 갖고, 모드에 따라 펼침 정도가 달라진다.

### 빠른 판정 (출고 전 / static)
- PASS/FAIL 한 줄.
- 갓 켠 서버는 트래픽이 0인 게 정상 → **흐름은 보지 않는다.**
- "설정이 서로 약속을 지키는가"까지만 답한다.

### 깊은 진단 (출고 후 / flow + triage)
- FAIL일 때 *왜* 깨졌는지 사다리를 타고 내려간다.
- 실제 트래픽 수신·드랍률 등 런타임 흐름을 본다.
- PASS면 사다리는 접히고 초록 한 줄만 보인다.

---

## 4. 모듈별 체크 항목

### 4.1 hosts
- `127.0.0.1 localhost` 존재
- `hostnamectl` 호스트명 ↔ hosts 매핑 일치
- 기대 FQDN/IP 엔트리 존재 (예: `01.nxportrait.core`)
- 템플릿 잔재(stale 호스트명·중복 IP) 없음

### 4.2 netplan  *(read-only로 재정의 — `netplan generate`는 /run에 백엔드 렌더 파일을 써서 원칙 #1 위반이므로 제거)*

**static**
- `/etc/netplan/*.yaml` 파싱 (문법 + 스키마)
- 파일의 IP ↔ **서버 실제 IP 일치** (`ip -j addr`, json 파싱)
- **bonding 검증**: bond 인터페이스 존재 · 슬레이브 전원 up · 모드 일치 (`/proc/net/bonding/<bond>` 읽기)
- 기대 인터페이스가 static/dhcp 의도대로 · 게이트웨이·DNS 일치
- 복수 yaml 간 충돌 없음

**flow(출고 후)**
- **기본 게이트웨이 ping** 도달 확인 — outbound 트래픽이 나가므로 static이 아닌 flow 깊이. (게이트웨이 대상 ICMP는 통상 benign이라 active-probe 기본off 예외로 허용)

### 4.3 elasticsearch  *(실환경: 클러스터 노드, HTTPS, 비표준 포트)*
- 서비스 active + enabled
- 기대 바인드로 listen — **포트는 expected.yaml URL 기준**(실환경 예: `https://00.mxlandscape:44371`, 호스트명 기반 HTTPS). 9200 고정 가정 아님
- **`_cat/health?v` 파싱**으로 클러스터 상태: `status` red면 FAIL, **yellow는 주의 신호**(클러스터라 yellow도 경고 취급) · `node.total`이 기대 노드 수와 일치
- HTTPS이므로 필요 시 **CA·credential(user/pass 또는 API key)** 을 expected.yaml에서 주입
- JVM heap **config** 정합: `Xms=Xmx`(heap 고정) · `<31GB`(compressed oops 경계). *런타임 RAM 사용률은 보지 않음 — 실환경에서 거의 항상 80%+라 노이즈만 됨*
- `elasticsearch.yml`: cluster.name · network.host · discovery
- 디스크 watermark 미초과 — **ES 기본값 기준(low 85% / high 90% / flood 95%)**, expected.yaml 오버라이드. (디스크가 차면 인덱스 read-only 잠금 = 실제 장애라 유지)

### 4.4 syslog-ng  *(실환경: 포트당 데몬 1개씩 다중 인스턴스 — 보통 5140~5144 5개, 현장 따라 10·20개도 가능)*
- **인스턴스별 루프**로 검사: 각 인스턴스 active + enabled
- `syslog-ng -s` 문법 통과 (인스턴스별 config)
- **UDP 5140~5144(가변) 리스너 바인드** (`ss -ulnp`) — 포트 범위는 expected.yaml에서. 설계의 `51xx` 표기는 실환경 `5140~5144`로 정정
- destination 정의 / 원격 타깃
- **rsyslog 비활성 확인** (충돌 방지)
- → nginx 정합은 4.8 교차검증 참조

### 4.5 nginx
- `nginx -t` 통과
- active + enabled
- 80/443 listen
- TLS 인증서 존재 · 만료 N일 이상 · 체인 유효
- sites-enabled dangling 심볼릭링크 없음
- 불필요한 default 사이트 없음
- **stream** 모듈로 방화벽 로그를 syslog-ng UDP 리스너에 분배 (→ 4.8)

### 4.6 nxcollector (자체 서비스)
- active + enabled
- 설정파일 존재 · 파싱 가능
- 파일 소유권 · 퍼미션
- **크래시루프 감지(모드별 차등)**: `--once`(출고 QA)는 active+enabled까지만, `daemon`/`triage`는 `systemctl show -p NRestarts,ExecMainStartTimestamp`로 윈도우 내 재시작 Δ>0 → warning, 급반복 → critical
- **core 연결 확인 *(QA 핵심)*** — `01.nxportrait.core:5141`로 outbound(dst) ESTAB:

  ```
  ss -tnp state established dst <core_IP> dport = :5141
  ```
  - 프로세스가 nxcollector면 PASS
  - peer 복수면 **any-of**: 하나라도 ESTAB이면 통과 (이중화 대응)
  - 이 한 줄이 통과하면 이름해석·네트워크 경로·ufw egress·core 수신·nxcollector 건강이 한 번에 검증됨

### 4.7 ufw
- active · 부팅 시 enable
- default incoming=deny · outgoing=allow
- 필요한 포트만 열림
- **drift 탐지**: 기대 외 열린 포트 없음
- **교차검증**: syslog-ng 듣는 UDP 5140~5144 ⊆ ufw 허용 포트 (막혀서 패킷 안 닿는 경우 탐지)
- **ICMP 드랍 설정 확인**: `/etc/ufw/before.rules`에 ICMP unreachable / ping drop 룰이 설정돼 있는지 확인 (정적 config 파일 직독이라 파싱 안정적)
- **읽기 소스**: `ufw status verbose`(LC_ALL=C 고정 파싱) + `/etc/ufw/before.rules` 직독. ufw는 json 출력이 없으므로 텍스트+config 파일 조합으로 읽는다 (iptables 백엔드 직독까지는 불필요)

### 4.8 nginx ↔ syslog-ng 포트 정합 (교차검증)
한 컴포넌트 내부 상태가 아니라 *두 config가 서로 약속을 지키는지* 보는 contract 체크.

- **집합 비교**(갯수 비교 아님): nginx가 분배하는 포트 집합 == syslog-ng가 실제 듣는 5140~5144 집합
  - 갯수만 보면 `{5140,5141,5142}` vs `{5140,5141,5199}`가 3=3으로 통과하지만 5199는 수신자 없음(유실)·5142는 송신자 없음. 집합 비교는 이 off-by-one을 잡는다.
- **집합 정규화:** IPv4/IPv6/와일드카드(`0.0.0.0` vs `::` vs 특정 IP)를 정규화해서 비교 — 서비스가 `::`만 바인드하면 v4도 커버하므로 정규화 없이는 false FAIL.
- **런타임 진실에서 읽는다:**
  - nginx 쪽: `nginx -T` 덤프에서 stream upstream server 포트 추출
  - syslog-ng 쪽: `ss -ulnp`로 실제 바인드된 UDP 5140~5144 리스너 추출
  - → "파일은 맞는데 데몬이 포트를 못 열었다"는 런타임 괴리까지 탐지
- **선행 의존:** `nginx -T`가 실패(config 깨짐)하면 nginx 쪽 집합을 못 얻으므로 orphan false-positive 대신 `error`/`skip` 반환 (한쪽을 못 읽으면 비교 자체가 무의미).

#### 출고 전 (static)
- 위 집합 정합 + ufw allow만 확인. 흐름은 보지 않음.

#### 출고 후 (flow) — 드랍률
- 포트별 수신 + 드랍률 측정, **2단 임계: 20% 초과 warning · 30% 초과 critical** (값은 expected.yaml 튜닝).
- **드랍률은 구간값(delta)이다.** 카운터는 부팅 후 누적이므로 단일 read의 30%는 "부팅 이래 평균"일 뿐. 진짜 "지금" 드랍을 보려면 두 시점을 떠서 증가분으로 계산:
  `Δ드랍 / Δ(수신+드랍)` (카운터 읽기 → N초 대기 → 다시 읽기)
- 따라서 본질적으로 시간 폭을 갖는 체크. **데몬은 두 시점을 자연히 보유**해 delta가 공짜지만, one-shot CLI는 그 자리에서 **기본 10초**(`--sample-window`로 조정) 멈추고 두 번 떠야 한다.
- **최소표본 가드:** 윈도우 내 `(수신+드랍)`이 임계 표본(예 100패킷) 미만이면 % 판정 보류 → `info`/`skip`("트래픽 부족"). 갓 켠/저트래픽 포트의 false FAIL 방지.
- **카운터 리셋 가드:** 서비스 재시작은 app 카운터, 재부팅은 커널 카운터를 0으로 리셋한다. 음수 delta가 나오면 "리셋 감지"로 그 구간을 skip.
- **드랍 분모 출처(확정 = 둘 다, 역할 분리):**
  - **A — 커널/소켓(헤드라인):** 포트별 drop을 `ss -uanmp`의 소켓별 drop 카운터 또는 `/proc/net/udp` drops 컬럼에서. **포트=소켓 1:1이라 syslog-ng config와 무관하게 항상 포트별.** (주의: `nstat UdpRcvbufErrors`는 **전역** 카운터라 포트별이 안 나옴 → 전역 sanity 교차용으로만)
  - **B — syslog-ng app(사다리 다음 칸):** A가 깨끗한데 로그가 비면 `syslog-ng-ctl stats`의 dropped로 내려가 내부 큐/destination 적체를 본다. 포트당 데몬 1개 토폴로지면 인스턴스=포트라 B도 자연히 포트별; 단일 source 다중 포트면 합산만 — **source:포트 매핑을 자동 판별**해 B 포트별 가용성 결정, 안 되면 A가 백업.
  - 진단 분기: `A↑ B≈0` → 수신버퍼/`net.core.rmem`, `A≈0 B↑` → destination·디스크·원격, `둘 다↑` → 과부하.

```
✗ syslog-ng 5142 드랍률  [FAIL]
  10초 구간: 수신 8,420  드랍 4,690  → 35.8%   (warn 20% / crit 30%)
  ├ 소켓 drop(A) Δ +4,690                 ← 수신버퍼 넘침
  ├ syslog-ng-ctl dropped(B) Δ +0         ← 앱 큐는 정상 → 커널단 병목
  └ 5140/5141/5143/5144 정상 (<1%)
  ⇒ 추정: 5142 트래픽 급증 또는 syslog-ng 소비 지연.
     net.core.rmem 또는 source flags(so-rcvbuf) 확인.
```

### 4.9 공통 (출고 검수 보조)
- OS / 커널 버전
- **시간동기화 (자동 감지)** — chrony / systemd-timesyncd / ntpd 중 활성 데몬을 감지한 뒤 그에 맞게 offset·동기상태 판정 (chrony면 `chronyc tracking`, timesyncd면 `timedatectl`). ES 클러스터·로그 상관관계에 중요

---

## 5. triage 출력 예시 (깊은 진단 사다리)

PASS면 초록 한 줄, FAIL이면 사다리를 펼쳐 *어디서 끊겼는지* 짚는다.

```
✗ nxcollector → core 연결  [FAIL]
  ├ 데몬 실행중 ............ OK
  ├ 01.nxportrait.core 해석  OK → 10.0.0.21
  ├ ESTAB :5141 소켓 ....... 없음          ← 여기서 끊김
  └ 능동 프로브 core:5141 .. refused
  ⇒ 추정: core 쪽 5141 미수신 또는 경로/방화벽.
     nxcollector 설정 자체는 정상.
```

```
✗ nginx ↔ syslog-ng 포트 정합  [FAIL]
  nginx upstream 타깃 : 5101 5102 5103 5104
  syslog-ng 리스닝   : 5101 5102      5104
  ├ 5103 → nginx는 분배하는데 syslog-ng가 안 듣는 중   ← 로그 유실
  └ orphan 없음(반대 방향)
  ⇒ 추정: syslog-ng config에 5103 source 누락 또는 바인드 실패.
```

> 능동 프로브(체커가 직접 core:5141 connect)는 **side effect가 있으므로**(peer에 stray 세션 발생) 기본 비활성. 수동 소켓 확인이 FAIL일 때 "config 문제냐 네트워크 문제냐"를 가르는 진단 fallback으로만 사용.

---

## 6. 언어 / 골격

- **.NET 10 기반.** 출고 1회 + 상시 데몬 + 온디맨드 triage를 한 코드베이스로 흡수하려면 데몬에 강한 .NET이 유리.
  - 데몬: `BackgroundService`(Worker) + `UseSystemd()` + 구조적 로깅
  - CLI: 동일 체크 라이브러리를 `--once` / `triage`로 호출
- 체크 본체는 `systemctl` · `ss` · `nginx -T` · `syslog-ng -s` · `ufw status` · `nstat` · `netplan` 등 리눅스 도구를 호출하고 출력을 파싱하는 글루.

```
nxcheck/
├── Core/            # 체크 라이브러리 (모드 무관, 단일 진실)
│   ├── Checks/      # hosts, netplan, elasticsearch, syslog-ng,
│   │                #   nginx, nxcollector, ufw, crosscheck
│   ├── Model/       # CheckResult(항목·심각도·상태·기대·실제·힌트)
│   └── Runners/     # 도구 호출 + 출력 파서
├── Cli/             # --once / triage 진입점, 콘솔·JSON 리포터
├── Daemon/          # BackgroundService, 주기 실행, flow delta 보유
└── expected.yaml    # 기대값 스펙 (IP·포트·버전·도메인) — 역할별 교체
```

- **기대값은 `expected.yaml` 외부화.** 서버 역할이 달라져도 파일만 교체.
- **기대값 빠졌을 때(확정):** `bash 창에서 무조건 실행`이 전제이므로 빠진 값은 **대화형 프롬프트로 입력** 받아 그 회차 사용(+yaml 저장 제안). 단 daemon(systemd)은 TTY가 없으므로 "expected.yaml 완비 전제 + 비TTY면 해당 값 skip"으로 안전망만 둔다. 기대값 불필요한 generic 체크(문법·active/enabled)는 항상 진행.

---

## 7. 확정된 결정 (구 미확정)

| # | 항목 | 결정 |
|---|------|------|
| 1 | 드랍 분모 출처 | **C (둘 다, 역할 분리).** A=커널/소켓(`ss`/`/proc/net/udp`, 항상 포트별, 헤드라인), B=syslog-ng-ctl stats(사다리 다음 칸). `nstat`은 전역이라 sanity 교차용만. → 4.8 flow 참조 |
| 2 | one-shot 샘플 윈도우 | **기본 10초** + 최소표본 가드(<100패킷 판정 보류), `--sample-window`로 튜닝. 데몬은 주기가 윈도우라 불필요 |
| 3 | 드랍 임계 단계화 | **2단: 20% warning / 30% critical** (expected.yaml 튜닝), 최소표본 floor와 AND |
| 4 | syslog-ng 포트별 stats | **source:포트 매핑 자동 판별 + A 백업.** 포트당 데몬 1개(5140~5144, 가변) 토폴로지면 인스턴스=포트로 B도 포트별, 단일 source 다중 포트면 B 합산만 — 포트 귀속은 항상 A가 보장 |
| 5 | nxcollector 깊이 | **모드별 차등.** `--once`=active+enabled, `daemon`/`triage`=+`NRestarts` 크래시루프 감지. *버전 비교는 미사용(불필요), core 연결(ESTAB)이 QA 핵심* (→ 4.6) |

### 추가 확정 (설계 보강 — 환경 정합 & 엔지니어링)

| 영역 | 결정 |
|------|------|
| 종료코드 error/skip | critical error→`2`, 비critical error→`1`, skip 무영향 (→ 1장) |
| 실행 권한 | root 데몬 전제, 권한 degrade 미처리 / 비-root 시작 경고 (→ 1장) |
| netplan | `netplan generate` 제거(원칙 #1) → YAML파싱 + 실제IP 대조 + bonding + 게이트웨이 ping(flow) (→ 4.2) |
| elasticsearch | 9200 고정 X → expected URL(예 `https://00.mxlandscape:44371`), `_cat/health?v` 파싱, HTTPS CA/auth, yellow 주의 (→ 4.3) |
| syslog-ng | 단일→다중 인스턴스 가정, 포트 `51xx`→`5140~5144`(가변) (→ 4.4) |
| 시간동기 | chrony 고정 X → chrony/timesyncd/ntpd 자동 감지 (→ 4.9) |
| 파싱 안정성 | `LC_ALL=C` + 기계가독(`ip -j`, `systemctl show -p`) (→ 1장) |
| 타임아웃 | 모든 외부 호출 per-call 타임아웃 → `error("timed out")` (→ 1장) |
| v4/v6 정규화 | 포트/listen 집합 비교 시 `0.0.0.0`/`::`/특정IP 정규화 (→ 4.8) |
| 카운터 리셋 가드 | 음수 delta → "리셋 감지" skip (→ 4.8 flow) |
| crosscheck 의존 | `nginx -T` 실패 시 false-positive 대신 error/skip (→ 4.8) |
| ES 임계 | RAM 사용률 미검사(항상 80%+ 노이즈), heap config(Xms=Xmx·<31GB)·watermark(ES 기본 85/90/95) 유지 (→ 4.3) |
| nxcollector 버전 | 버전 비교 미사용 — core 연결(ESTAB)이 QA 핵심 (→ 4.6) |
| ufw 읽기 소스 | `ufw status verbose` + `/etc/ufw/before.rules` 직독(ICMP 드랍 룰 확인), iptables 백엔드 불필요 (→ 4.7) |

> 설계 미확정 항목 모두 해소. 이후는 구현 단계 디테일(코드 구조·파서 정규식·CheckResult 직렬화 포맷 등).
