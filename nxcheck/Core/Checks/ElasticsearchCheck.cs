using NxCheck.Core.Checks.Support;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using NxCheck.Core.Runners;

namespace NxCheck.Core.Checks;

/// <summary>
/// 4.3 elasticsearch (클러스터 노드, HTTPS, 비표준 포트).
/// - active + enabled
/// - `_cat/health?v`: red→FAIL, yellow→주의(경고), node.total 기대 일치
/// - `_cat/allocation?v`: 디스크 watermark(기본 high 90 / flood 95)
/// - jvm.options 힙: Xms=Xmx · &lt;31GB (런타임 RAM 사용률은 미검사)
/// HTTP 조회는 curl로(HTTPS·CA·인증 처리 용이).
/// </summary>
public sealed class ElasticsearchCheck(
    string jvmOptionsPath = "/etc/elasticsearch/jvm.options") : ICheck
{
    private const long CompressedOopsLimit = 31L * 1024 * 1024 * 1024; // 31GB
    private const int DiskHighDefault = 90;
    private const int DiskFloodDefault = 95;

    public string Module => "elasticsearch";

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default)
    {
        var spec = ctx.Expected.Elasticsearch;
        var probe = new SystemdProbe(ctx.Runner);
        var results = new List<CheckResult>();

        // 서비스 상태
        var active = await probe.IsActiveAsync("elasticsearch", ct);
        results.Add(ServiceResult("active", active, Severity.Critical));
        var enabled = await probe.IsEnabledAsync("elasticsearch", ct);
        results.Add(ServiceResult("enabled", enabled, Severity.Warning));

        // 힙 설정(파일 직독)
        results.Add(CheckHeap());

        // URL 기반 HTTP 체크
        if (spec?.Url is { } url)
        {
            results.Add(await CheckHealthAsync(ctx, spec, url, ct));
            results.Add(await CheckDiskAsync(ctx, spec, url, ct));
        }
        else
        {
            results.Add(CheckResult.Skip(Module, "클러스터 상태", hint: "elasticsearch.url 미정의"));
        }

        return results;
    }

    private CheckResult ServiceResult(string item, ServiceState st, Severity sev) =>
        !st.Queried ? CheckResult.Error(Module, item, sev, hint: "systemctl 실행 실패")
        : st.Ok ? CheckResult.Pass(Module, item, sev, expected: item, actual: st.Raw)
        : CheckResult.Fail(Module, item, sev, expected: item, actual: st.Raw);

    private CheckResult CheckHeap()
    {
        if (!File.Exists(jvmOptionsPath))
            return CheckResult.Skip(Module, "heap config", hint: $"{jvmOptionsPath} 없음");

        var heap = JvmHeapParser.Parse(File.ReadAllText(jvmOptionsPath));
        if (heap.Xms is null || heap.Xmx is null)
            return CheckResult.Skip(Module, "heap config", hint: "jvm.options에 -Xms/-Xmx 없음(jvm.options.d 사용?)");

        if (!heap.Symmetric)
            return CheckResult.Fail(Module, "heap config", Severity.Warning,
                expected: "Xms=Xmx", actual: $"Xms={heap.Xms} Xmx={heap.Xmx}",
                hint: "힙 고정(Xms=Xmx) 권장 — 런타임 리사이즈 방지");

        if (heap.XmxBytes is { } bytes && bytes >= CompressedOopsLimit)
            return CheckResult.Fail(Module, "heap config", Severity.Warning,
                expected: "<31GB", actual: heap.Xmx,
                hint: "31GB 이상 — compressed oops 경계 초과로 오히려 비효율");

        return CheckResult.Pass(Module, "heap config", Severity.Warning,
            expected: "Xms=Xmx, <31GB", actual: heap.Xmx);
    }

    private async Task<CheckResult> CheckHealthAsync(CheckContext ctx, ElasticsearchSpec spec, string url, CancellationToken ct)
    {
        var r = await Curl(ctx, spec, url, "/_cat/health?v", ct);
        if (!r.Started)
            return CheckResult.Error(Module, "클러스터 상태", Severity.Critical, hint: $"curl 실행 실패: {r.FailureReason}");
        if (!r.Success)
            return CheckResult.Error(Module, "클러스터 상태", Severity.Critical,
                hint: $"health 조회 실패({r.FailureReason}) — 인증/CA/네트워크 확인");

        var rows = CatTableParser.Parse(r.StdOut);
        if (rows.Count == 0 || !rows[0].TryGetValue("status", out var status))
            return CheckResult.Error(Module, "클러스터 상태", Severity.Critical, hint: "_cat/health 파싱 실패");

        // 노드 수 확인
        if (spec.Nodes is { } expectedNodes
            && rows[0].TryGetValue("node.total", out var nt) && int.TryParse(nt, out var actualNodes)
            && actualNodes != expectedNodes)
        {
            return CheckResult.Fail(Module, "클러스터 상태", Severity.Critical,
                expected: $"green, 노드 {expectedNodes}", actual: $"{status}, 노드 {actualNodes}",
                hint: "클러스터 노드 수 불일치 — 노드 이탈 가능");
        }

        return status.ToLowerInvariant() switch
        {
            "green" => CheckResult.Pass(Module, "클러스터 상태", Severity.Critical, expected: "green", actual: "green"),
            "yellow" => CheckResult.Fail(Module, "클러스터 상태", Severity.Warning,
                expected: "green", actual: "yellow", hint: "레플리카 미할당 등 — 클러스터라 주의"),
            _ => CheckResult.Fail(Module, "클러스터 상태", Severity.Critical,
                expected: "green", actual: status, hint: "red — 프라이머리 샤드 미할당(데이터 위험)"),
        };
    }

    private async Task<CheckResult> CheckDiskAsync(CheckContext ctx, ElasticsearchSpec spec, string url, CancellationToken ct)
    {
        var r = await Curl(ctx, spec, url, "/_cat/allocation?v", ct);
        if (!r.Started || !r.Success)
            return CheckResult.Skip(Module, "디스크 watermark", hint: "_cat/allocation 조회 실패");

        var rows = CatTableParser.Parse(r.StdOut);
        var maxDisk = rows
            .Select(row => row.TryGetValue("disk.percent", out var v) && int.TryParse(v, out var p) ? p : -1)
            .DefaultIfEmpty(-1).Max();

        if (maxDisk < 0)
            return CheckResult.Skip(Module, "디스크 watermark", hint: "disk.percent 없음");

        return maxDisk switch
        {
            >= DiskFloodDefault => CheckResult.Fail(Module, "디스크 watermark", Severity.Critical,
                expected: $"<{DiskFloodDefault}%", actual: $"{maxDisk}%",
                hint: "flood 초과 — 인덱스 read-only 잠금 위험"),
            >= DiskHighDefault => CheckResult.Fail(Module, "디스크 watermark", Severity.Warning,
                expected: $"<{DiskHighDefault}%", actual: $"{maxDisk}%",
                hint: "high 초과 — 샤드 재배치 시작"),
            _ => CheckResult.Pass(Module, "디스크 watermark", Severity.Warning,
                expected: $"<{DiskHighDefault}%", actual: $"{maxDisk}%"),
        };
    }

    private static Task<CommandResult> Curl(CheckContext ctx, ElasticsearchSpec spec, string url, string path, CancellationToken ct)
    {
        var args = new List<string> { "-s", "--max-time", "5" };
        if (spec.CaFile is { } ca) { args.Add("--cacert"); args.Add(ca); }
        if (spec.Username is { } user)
        {
            var pass = spec.PasswordEnv is { } env ? Environment.GetEnvironmentVariable(env) : null;
            args.Add("-u");
            args.Add(pass is null ? user : $"{user}:{pass}");
        }
        args.Add($"{url.TrimEnd('/')}{path}");
        return ctx.Runner.RunAsync("curl", args, ct: ct);
    }
}
