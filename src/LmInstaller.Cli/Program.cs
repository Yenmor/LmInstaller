using System.Text;
using LmInstaller.Cli;

Console.OutputEncoding = Encoding.UTF8;

try
{
    return await Cli.RunAsync(args).ConfigureAwait(false);
}
catch (CliException ex)
{
    Console.Error.WriteLine("错误：" + ex.Message);
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"执行失败：{ex.Message}");
    return 1;
}
