namespace LmInstaller.Core.Installation;

public class InstallException : Exception
{
    public InstallException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The target folder already exists and the policy requires a user decision.</summary>
public sealed class InstallConflictException : InstallException
{
    public InstallConflictException(string directoryName, string targetPath)
        : base($"目标目录已存在：{targetPath}")
    {
        DirectoryName = directoryName;
        TargetPath = targetPath;
    }

    public string DirectoryName { get; }

    public string TargetPath { get; }
}
