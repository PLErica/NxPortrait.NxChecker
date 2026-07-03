using NxCheck.Core.Checks.Support;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.2 netplan (read-only — `netplan generate`는 /run에 쓰므로 사용 안 함).
/// static: /etc/netplan/*.yaml 문법 · 파일↔실제 IP 일치(ip -j addr) · bonding(/proc/net/bonding)
/// flow:   기본 게이트웨이 ping 도달 (outbound라 flow 깊이, benign 예외)
/// </summary>
public sealed class NetplanCheck(
    string netplanDir = "/etc/netplan",
    string bondingDir = "/proc/net/bonding") : ICheck
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    public string Module => "netplan";

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default)
    {
        var results = new List<CheckResult>();

        results.Add(CheckYamlSyntax());

        var net = ctx.Expected.Network;

        if (net?.Ip is { } expectedIp)
            results.Add(await CheckActualIpAsync(ctx, expectedIp, ct));

        if (net?.Bond is { } bond)
            results.Add(CheckBonding(bond));

        if (ctx.IncludesFlow && net?.Gateway is { } gw)
            results.Add(await CheckGatewayAsync(ctx, gw, ct));

        return results;
    }

    private CheckResult CheckYamlSyntax()
    {
        if (!Directory.Exists(netplanDir))
            return CheckResult.Error(Module, "netplan 파일", Severity.Critical, hint: $"{netplanDir} 없음");

        var files = Directory.GetFiles(netplanDir, "*.yaml").Concat(Directory.GetFiles(netplanDir, "*.yml")).ToList();
        if (files.Count == 0)
            return CheckResult.Skip(Module, "netplan 파일", hint: $"{netplanDir}에 yaml 없음");

        foreach (var f in files)
        {
            try
            {
                Yaml.Deserialize<object?>(File.ReadAllText(f));
            }
            catch (YamlException ex)
            {
                return CheckResult.Fail(Module, "netplan 문법", Severity.Critical,
                    expected: "유효한 YAML", actual: $"{Path.GetFileName(f)} 파싱 오류",
                    hint: ex.Message);
            }
        }

        return CheckResult.Pass(Module, "netplan 문법", Severity.Critical,
            expected: "유효한 YAML", actual: $"{files.Count}개 파일 OK");
    }

    private async Task<CheckResult> CheckActualIpAsync(CheckContext ctx, string expectedIp, CancellationToken ct)
    {
        var r = await ctx.Runner.RunAsync("ip", ["-j", "addr"], ct: ct);
        if (!r.Started)
            return CheckResult.Error(Module, "실제 IP 일치", Severity.Critical, hint: $"ip 실행 실패: {r.FailureReason}");

        var actual = IpAddrParser.IPv4Locals(r.StdOut);
        return actual.Contains(expectedIp)
            ? CheckResult.Pass(Module, "실제 IP 일치", Severity.Critical, expected: expectedIp, actual: expectedIp)
            : CheckResult.Fail(Module, "실제 IP 일치", Severity.Critical,
                expected: expectedIp, actual: actual.Count == 0 ? "없음" : string.Join(",", actual),
                hint: "netplan 기대 IP가 실제 인터페이스에 적용돼 있지 않음");
    }

    private CheckResult CheckBonding(BondSpec bond)
    {
        var name = bond.Name;
        if (name is null)
            return CheckResult.Skip(Module, "bonding", hint: "bond 이름 미정의");

        var path = Path.Combine(bondingDir, name);
        if (!File.Exists(path))
            return CheckResult.Fail(Module, "bonding", Severity.Critical,
                expected: $"{name} 본딩 존재", actual: "없음",
                hint: $"{path} 없음 — 본딩 미구성 또는 인터페이스명 불일치");

        var info = BondingParser.Parse(File.ReadAllText(path));

        // 모드 일치(부분 매칭 — "IEEE 802.3ad ..."에 "802.3ad" 포함)
        if (bond.Mode is { } expectedMode && info.Mode is { } actualMode
            && !actualMode.Contains(expectedMode, StringComparison.OrdinalIgnoreCase))
        {
            return CheckResult.Fail(Module, "bonding", Severity.Critical,
                expected: $"mode {expectedMode}", actual: actualMode, hint: "본딩 모드 불일치");
        }

        // 기대 슬레이브 존재 + 전원 up
        var expectedSlaves = bond.Slaves ?? [];
        var down = new List<string>();
        var missing = new List<string>();
        foreach (var slave in expectedSlaves)
        {
            var found = info.Slaves.FirstOrDefault(s => s.Name == slave);
            if (found.Name is null) missing.Add(slave);
            else if (!found.Up) down.Add(slave);
        }

        if (missing.Count > 0 || down.Count > 0)
            return CheckResult.Fail(Module, "bonding", Severity.Critical,
                expected: $"슬레이브 {string.Join(",", expectedSlaves)} up",
                actual: $"{(missing.Count > 0 ? $"누락 {string.Join(",", missing)} " : "")}{(down.Count > 0 ? $"down {string.Join(",", down)}" : "")}".Trim(),
                hint: "본딩 슬레이브 누락 또는 링크 down");

        return CheckResult.Pass(Module, "bonding", Severity.Critical,
            expected: $"mode {bond.Mode}, 슬레이브 up",
            actual: $"{info.Mode}, {info.Slaves.Count(s => s.Up)}/{info.Slaves.Count} up");
    }

    private async Task<CheckResult> CheckGatewayAsync(CheckContext ctx, string gateway, CancellationToken ct)
    {
        var r = await ctx.Runner.RunAsync("ping", ["-c", "1", "-W", "2", gateway], ct: ct);
        if (!r.Started)
            return CheckResult.Error(Module, "게이트웨이 ping", Severity.Warning, hint: $"ping 실행 실패: {r.FailureReason}");

        return r.Success
            ? CheckResult.Pass(Module, "게이트웨이 ping", Severity.Warning, expected: $"{gateway} 도달", actual: "응답")
            : CheckResult.Fail(Module, "게이트웨이 ping", Severity.Warning,
                expected: $"{gateway} 도달", actual: r.TimedOut ? "타임아웃" : "무응답",
                hint: "기본 게이트웨이에 도달 못함 — 경로/링크 확인");
    }
}
