namespace NxCheck.Core.Checks;

/// <summary>모든 모드가 공유하는 기본 체크 목록(단일 진실).</summary>
public static class CheckCatalog
{
    public static IReadOnlyList<ICheck> Default() =>
    [
        new HostsCheck(),
        new NetplanCheck(),
        new ElasticsearchCheck(),
        new SyslogNgCheck(),
        new NginxCheck(),
        new NxCollectorCheck(),
        new UfwCheck(),
        new CrossCheck(),
    ];
}
