using NxCheck.Core.Model;

namespace NxCheck.Core.Checks;

/// <summary>
/// 단일 진실 체크 모듈. 모드 무관 — 동일 라이브러리를 --once/daemon/triage가 공유.
/// 깊이(ctx.Depth)에 따라 펼침 정도만 달라진다.
/// </summary>
public interface ICheck
{
    /// <summary>모듈 식별자(hosts, netplan, ...). 결과의 Module과 일치.</summary>
    string Module { get; }

    Task<IReadOnlyList<CheckResult>> RunAsync(CheckContext ctx, CancellationToken ct = default);
}
