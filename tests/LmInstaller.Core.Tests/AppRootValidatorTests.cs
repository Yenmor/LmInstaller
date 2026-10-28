using LmInstaller.Core.Configuration;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class AppRootValidatorTests
{
    [Fact]
    public void Rejects_an_empty_path()
    {
        var result = new AppRootValidator().Validate("  ");

        Assert.Equal(AppRootValidationStatus.EmptyPath, result.Status);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Creates_and_accepts_a_missing_directory()
    {
        using var temp = new TestDirectory();
        var target = temp.Combine("apps", "portable");

        var result = new AppRootValidator().Validate(target);

        Assert.True(result.IsValid);
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void Rejects_a_file_path()
    {
        using var temp = new TestDirectory();
        var file = temp.WriteFile("apps.txt", "not a directory");

        var result = new AppRootValidator().Validate(file);

        Assert.Equal(AppRootValidationStatus.NotADirectory, result.Status);
    }

    [Fact]
    public void Rejects_the_manager_temporary_directory()
    {
        using var temp = new TestDirectory();
        var tempRoot = temp.CreateSubdirectory("manager-temp");
        var validator = new AppRootValidator(new[] { tempRoot });

        var result = validator.Validate(tempRoot);

        Assert.Equal(AppRootValidationStatus.SameAsManagerData, result.Status);
    }

    [Fact]
    public void Rejects_a_directory_inside_a_forbidden_directory()
    {
        using var temp = new TestDirectory();
        var managerData = temp.CreateSubdirectory("manager-data");
        var nested = Path.Combine(managerData, "apps");
        var validator = new AppRootValidator(new[] { managerData });

        var result = validator.Validate(nested);

        Assert.Equal(AppRootValidationStatus.SameAsManagerData, result.Status);
    }

    [Fact]
    public void Rejects_a_directory_that_cannot_be_written_to()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix permission bits are not applicable.
        }

        using var temp = new TestDirectory();
        var readOnly = temp.CreateSubdirectory("readonly");
        File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var result = new AppRootValidator().Validate(readOnly);

            Assert.Equal(AppRootValidationStatus.NotWritable, result.Status);
        }
        finally
        {
            File.SetUnixFileMode(
                readOnly,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
