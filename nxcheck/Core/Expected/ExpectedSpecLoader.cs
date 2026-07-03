using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NxCheck.Core.Expected;

/// <summary>expected.yaml 로더. underscore 네이밍(warn_pct 등)으로 역직렬화.</summary>
public static class ExpectedSpecLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>파일을 읽어 스펙으로. 파일이 없으면 빈 스펙(폴백).</summary>
    public static ExpectedSpec Load(string path)
    {
        if (!File.Exists(path))
            return ExpectedSpec.Empty;

        var yaml = File.ReadAllText(path);
        return Parse(yaml);
    }

    public static ExpectedSpec Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return ExpectedSpec.Empty;

        return Deserializer.Deserialize<ExpectedSpec>(yaml) ?? ExpectedSpec.Empty;
    }
}
