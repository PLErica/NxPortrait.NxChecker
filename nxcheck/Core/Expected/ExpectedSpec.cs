namespace NxCheck.Core.Expected;

/// <summary>
/// expected.yaml(기대값 정답지)의 객체 모델. 서버 역할이 달라지면 파일만 교체.
/// 모든 필드는 nullable — 누락된 섹션의 체크는 SKIP(또는 대화형 입력)로 처리.
/// </summary>
public sealed class ExpectedSpec
{
    public string? Hostname { get; set; }
    public NetworkSpec? Network { get; set; }
    public ElasticsearchSpec? Elasticsearch { get; set; }
    public NxCollectorSpec? NxCollector { get; set; }
    public SyslogSpec? Syslog { get; set; }
    public DropSpec? Drop { get; set; }
    public UfwSpec? Ufw { get; set; }

    /// <summary>빈(전부 미정의) 스펙. 파일이 없을 때의 폴백.</summary>
    public static ExpectedSpec Empty => new();
}

public sealed class NetworkSpec
{
    public string? Ip { get; set; }
    public string? Gateway { get; set; }
    public List<string>? Dns { get; set; }
    public BondSpec? Bond { get; set; }
}

public sealed class BondSpec
{
    public string? Name { get; set; }
    public string? Mode { get; set; }
    public List<string>? Slaves { get; set; }
}

public sealed class ElasticsearchSpec
{
    /// <summary>예: https://00.mxlandscape:44371</summary>
    public string? Url { get; set; }
    public int? Nodes { get; set; }
    public string? Username { get; set; }
    public string? PasswordEnv { get; set; }
    public string? CaFile { get; set; }
}

public sealed class NxCollectorSpec
{
    /// <summary>systemd 유닛 / 프로세스 이름(기본 nxcollector).</summary>
    public string Unit { get; set; } = "nxcollector";

    /// <summary>설정 파일 경로(있으면 존재 여부 확인).</summary>
    public string? ConfigPath { get; set; }

    public CoreEndpoint? Core { get; set; }
}

public sealed class CoreEndpoint
{
    public string? Host { get; set; }
    public int? Port { get; set; }
}

public sealed class SyslogSpec
{
    /// <summary>nginx가 분배 / syslog-ng가 들어야 하는 UDP 포트 집합(예: 5140~5144).</summary>
    public List<int>? Ports { get; set; }
}

public sealed class DropSpec
{
    public int WarnPct { get; set; } = 20;
    public int CriticalPct { get; set; } = 30;
    public int MinSamples { get; set; } = 100;
    public int SampleWindowSeconds { get; set; } = 10;
}

public sealed class UfwSpec
{
    public List<int>? AllowedPorts { get; set; }
    /// <summary>before.rules에 ICMP unreachable/ping drop 룰을 요구할지.</summary>
    public bool RequireIcmpDrop { get; set; } = true;
}
