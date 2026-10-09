using CostLedger.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CostLedger.Infrastructure;

/// <summary>报表字段的取值类型（决定可否聚合、如何比较/格式化）</summary>
public enum ReportValueKind { Text, Number, Money, Date, Bool, Enum }

/// <summary>数据源的一个可报表字段：键=实体属性名，标签=中文列名，Format=默认格式</summary>
public record ReportFieldDef(string Key, string Label, ReportValueKind Kind, string? Format = null);

/// <summary>
/// 报表数据源：把某实体暴露为可报表的数据集合（含字段目录与查询委托）。
/// 用代码声明字段目录（中文标签），避免依赖元数据表里没有属性绑定的核心字段。
/// </summary>
public record ReportDataSource(
    string Code,
    string Name,
    Type EntityType,
    Func<AppDbContext, IEnumerable<object>> Query,
    IReadOnlyList<ReportFieldDef> Fields)
{
    public ReportFieldDef? Field(string key) => Fields.FirstOrDefault(f => f.Key == key);
}

/// <summary>M2-C 报表数据源注册表。新增可报表实体只需在此登记一行。</summary>
public static class ReportDataSources
{
    public static readonly IReadOnlyDictionary<string, ReportDataSource> All = new List<ReportDataSource>
    {
        new("Project", "项目", typeof(Project), db => db.Projects.Cast<object>(), new List<ReportFieldDef>
        {
            new("Code", "项目编号", ReportValueKind.Text),
            new("Name", "项目名称", ReportValueKind.Text),
            new("OwnerName", "建设单位", ReportValueKind.Text),
            new("StartDate", "开工日期", ReportValueKind.Date, "yyyy-MM-dd"),
            new("PlannedEndDate", "计划完工", ReportValueKind.Date, "yyyy-MM-dd"),
        }),
        new("Party", "往来单位", typeof(Party), db => db.Parties.Cast<object>(), new List<ReportFieldDef>
        {
            new("Name", "单位名称", ReportValueKind.Text),
            new("RoleCode", "角色", ReportValueKind.Text),
            new("TaxNo", "税号", ReportValueKind.Text),
            new("Contact", "联系人", ReportValueKind.Text),
        }),
        new("Contract", "合同", typeof(Contract), db => db.Contracts.Cast<object>(), new List<ReportFieldDef>
        {
            new("Code", "合同编号", ReportValueKind.Text),
            new("TypeCode", "合同类型", ReportValueKind.Text),
            new("Title", "合同名称", ReportValueKind.Text),
            new("Amount", "合同金额", ReportValueKind.Money, "N2"),
            new("TaxRate", "税率", ReportValueKind.Number, "P0"),
            new("SignDate", "签约日期", ReportValueKind.Date, "yyyy-MM-dd"),
            new("Status", "状态", ReportValueKind.Text),
        }),
        new("CostCategory", "成本科目", typeof(CostCategory), db => db.CostCategories.Cast<object>(), new List<ReportFieldDef>
        {
            new("Code", "科目编码", ReportValueKind.Text),
            new("Name", "科目名称", ReportValueKind.Text),
            new("Type", "类型", ReportValueKind.Enum),
        }),
    }.ToDictionary(d => d.Code);

    public static ReportDataSource? Get(string code)
        => All.TryGetValue(code, out var ds) ? ds : null;
}
