namespace LmInstaller.Cli;

/// <summary>
/// Minimal "--name value" / "--flag" parser. No third-party dependency so the tool stays runnable
/// in an offline environment.
/// </summary>
internal sealed class Arguments
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    private Arguments()
    {
    }

    public static Arguments Parse(IEnumerable<string> args)
    {
        var result = new Arguments();
        var pending = args.ToList();

        for (var i = 0; i < pending.Count; i++)
        {
            var token = pending[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = token[2..];
            if (i + 1 < pending.Count && !pending[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result._values[name] = pending[i + 1];
                i++;
            }
            else
            {
                result._values[name] = null;
            }
        }

        return result;
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public string Require(string name)
    {
        var value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException($"缺少必需参数 --{name}");
        }

        return value;
    }

    public string GetOrDefault(string name, string fallback) => Get(name) ?? fallback;
}

internal sealed class CliException : Exception
{
    public CliException(string message)
        : base(message)
    {
    }
}
