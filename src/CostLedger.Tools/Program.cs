using CostLedger.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CostLedger.Tools;

/// <summary>
/// 把成本数据采集表（制表符目录）导入生成一个可直接被桌面程序打开的真实账套文件。
/// 用法：dotnet run --project src/CostLedger.Tools -- &lt;输出账套.db&gt; &lt;数据目录&gt;
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: CostLedger.Tools <输出账套.db> <数据目录>");
            return 2;
        }

        var dbPath = args[0];
        var dataDir = args[1];

        var (path, report) = await EconomicDataImporter.ImportToFileDatabaseAsync(dbPath, dataDir);

        Console.WriteLine($"账套已生成：{path}");
        Console.WriteLine($"导入结果：接受 {report.Imported} 条，拒绝 {report.Rejected} 条");
        foreach (var e in report.Rejections)
            Console.WriteLine($"[拒] {e.Table} | {e.Key} | {e.Message}");
        Console.WriteLine();
        Console.WriteLine("项目经济摘要：");
        foreach (var s in report.ProjectSummaries)
            Console.WriteLine("  " + s);

        return report.Rejected == 0 ? 0 : 1;
    }
}
