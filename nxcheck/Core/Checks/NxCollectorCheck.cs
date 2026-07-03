using System.Net;
using NxCheck.Core.Checks.Support;
using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.6 nxcollector (자체 서비스).
/// - active + enabled
/// - (설정 경로 주어지면) 설정 파일 존재
/// - 크래시루프(flow: NRestarts) — 절대값, 데몬은 향후 Δ
/// - core 연결(ESTAB dport, any-of) ← QA 핵심. 통과하면 이름해석·경로·egress·core수신·데몬건강 일괄 검증.
/// </summary>
public sealed class NxCollectorCheck : ICheck
{
    public string Module => "nxcollector";

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default)
    {
        var spec = ctx.Expected.NxCollector;
        var unit = spec?.Unit ?? "nxcollector";
        var probe = new SystemdProbe(ctx.Runner);
        var results = new List<CheckResult>();

        // ── 서비스 상태 ────────────────────────────────
        var active = await probe.IsActiveAsync(unit, ct);
        results.Add(!active.Queried
            ? CheckResult.Error(Module, "active", Severity.Critical, hint: "systemctl 실행 실패")
            : active.Ok
                ? CheckResult.Pass(Module, "active", Severity.Critical, expected: "active", actual: active.Raw)
                : CheckResult.Fail(Module, "active", Severity.Critical, expected: "active", actual: active.Raw,
                    hint: "nxcollector 서비스가 실행 중이 아님"));

        var enabled = await probe.IsEnabledAsync(unit, ct);
        results.Add(!enabled.Queried
            ? CheckResult.Error(Module, "enabled", Severity.Warning, hint: "systemctl 실행 실패")
            : enabled.Ok
                ? CheckResult.Pass(Module, "enabled", Severity.Warning, expected: "enabled", actual: enabled.Raw)
                : CheckResult.Fail(Module, "enabled", Severity.Warning, expected: "enabled", actual: enabled.Raw,
                    hint: "부팅 시 자동 시작 미설정"));

        // ── 설정 파일 존재(경로 주어질 때만) ─────────────
        if (spec?.ConfigPath is { } cfg)
        {
            results.Add(File.Exists(cfg)
                ? CheckResult.Pass(Module, "설정 파일", Severity.Critical, expected: cfg, actual: "존재")
                : CheckResult.Fail(Module, "설정 파일", Severity.Critical, expected: cfg, actual: "없음",
                    hint: "설정 파일을 찾을 수 없음"));
        }

        // ── 크래시루프(출고 후/flow 깊이) ────────────────
        if (ctx.IncludesFlow)
            results.Add(await CheckCrashLoopAsync(probe, unit, ct));

        // ── core 연결(QA 핵심) ──────────────────────────
        results.Add(await CheckCoreAsync(ctx, spec, unit, ct));

        return results;
    }

    private async Task<CheckResult> CheckCrashLoopAsync(SystemdProbe probe, string unit, CancellationToken ct)
    {
        var restarts = await probe.NRestartsAsync(unit, ct);
        if (restarts is null)
            return CheckResult.Skip(Module, "크래시루프", hint: "NRestarts 조회 실패");

        return restarts == 0
            ? CheckResult.Pass(Module, "크래시루프", Severity.Warning, expected: "재시작 0", actual: "0")
            : CheckResult.Fail(Module, "크래시루프", Severity.Warning,
                expected: "재시작 0", actual: restarts.ToString(),
                hint: "재시작 이력 있음(절대값). 데몬 모드는 구간 Δ로 급반복 판정.");
    }

    private async Task<CheckResult> CheckCoreAsync(CheckContext ctx, Expected.NxCollectorSpec? spec, string unit, CancellationToken ct)
    {
        var core = spec?.Core;
        if (core?.Host is not { } host || core.Port is not { } port)
            return CheckResult.Skip(Module, "core 연결", hint: "core 기대값(host·port) 미정의");

        var ladder = new List<LadderStep>();

        // 1) 데몬 실행중
        var active = await new SystemdProbe(ctx.Runner).IsActiveAsync(unit, ct);
        ladder.Add(new LadderStep("데몬 실행중", active.Ok ? CheckStatus.Pass : CheckStatus.Fail, active.Raw));

        // 2) 호스트 해석 → IP
        var ip = await ResolveAsync(ctx, host, ct);
        ladder.Add(new LadderStep($"{host} 해석",
            ip is null ? CheckStatus.Fail : CheckStatus.Pass, ip is null ? "실패" : $"→ {ip}"));
        if (ip is null)
        {
            return CheckResult.Fail(Module, "core 연결", Severity.Critical,
                expected: $"{host}:{port} ESTAB", actual: "이름해석 실패", ladder: ladder,
                hint: "DNS/hosts에서 core 호스트명을 해석하지 못함");
        }

        // 3) ESTAB 소켓 (any-of)
        var ss = await ctx.Runner.RunAsync("ss",
            ["-tnp", "state", "established", "dst", ip, "dport", "=", $":{port}"], ct: ct);
        var established = ss.Started && HasEstablished(ss.StdOut, port, unit);
        ladder.Add(new LadderStep($"ESTAB :{port} 소켓",
            established ? CheckStatus.Pass : CheckStatus.Fail, established ? null : "없음"));

        if (established)
            return CheckResult.Pass(Module, "core 연결", Severity.Critical,
                expected: $"{host}:{port} ESTAB", actual: $"{ip}:{port} 연결됨");

        // 4) (선택) 능동 프로브 — side-effect 있어 기본 off
        if (ctx.EnableActiveProbe)
        {
            var nc = await ctx.Runner.RunAsync("nc", ["-z", "-w", "2", ip, port.ToString()], ct: ct);
            ladder.Add(new LadderStep($"능동 프로브 {host}:{port}",
                nc.Success ? CheckStatus.Pass : CheckStatus.Fail, nc.Success ? "연결 가능" : "refused/timeout"));
        }

        return CheckResult.Fail(Module, "core 연결", Severity.Critical,
            expected: $"{host}:{port} ESTAB", actual: "ESTAB 소켓 없음", ladder: ladder,
            hint: "core 쪽 미수신 또는 경로/방화벽. nxcollector 설정 자체는 정상일 수 있음.");
    }

    private static async Task<string?> ResolveAsync(CheckContext ctx, string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out _))
            return host;

        var g = await ctx.Runner.RunAsync("getent", ["hosts", host], ct: ct);
        if (!g.Success)
            return null;

        var first = g.StdOut.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var token = first?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return IPAddress.TryParse(token, out _) ? token : null;
    }

    /// <summary>ss 출력에서 dport 일치 + 프로세스명 포함 ESTAB 라인이 하나라도 있으면 true(any-of).</summary>
    private static bool HasEstablished(string ssOutput, int port, string procName) =>
        ssOutput.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith("Recv-Q", StringComparison.Ordinal) && !l.Contains("Netid", StringComparison.Ordinal))
            .Any(l => l.Contains($":{port}", StringComparison.Ordinal) && l.Contains(procName, StringComparison.Ordinal));
}
