namespace CostLedger.Domain;

/// <summary>报表列聚合方式（仅数值列可用）</summary>
public enum AggregateType { None, Sum, Avg, Count, Min, Max }

/// <summary>报表筛选运算符</summary>
public enum FilterOperator { Contains, Equals, NotEquals, GreaterThan, LessThan, GreaterOrEqual, LessOrEqual }

/// <summary>
/// M2-C 报表定义（报表中心的一级）。
/// 数据源指向某 EntityDefCode（见 Infrastructure.ReportDataSources）；
/// 列与筛选为子表，均为"数据"而非代码，后台配置即生效。
/// </summary>
public class ReportDef
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;      // 报表编码
    public string Name { get; set; } = string.Empty;      // 报表名称
    public string Title { get; set; } = string.Empty;     // 打印/预览标题
    public string DataSourceCode { get; set; } = string.Empty; // 数据源实体代码
    public string? Description { get; set; }

    public ICollection<ReportColumn> Columns { get; set; } = new List<ReportColumn>();
    public ICollection<ReportFilter> Filters { get; set; } = new List<ReportFilter>();
}

/// <summary>报表列定义：引用数据源字段键，控制显隐/顺序/格式/聚合</summary>
public class ReportColumn
{
    public int Id { get; set; }
    public int ReportDefId { get; set; }
    public ReportDef? ReportDef { get; set; }
    public string FieldKey { get; set; } = string.Empty;  // 数据源字段键（实体属性名）
    public string Label { get; set; } = string.Empty;     // 列标题（可覆盖字段默认标签）
    public bool Visible { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? Format { get; set; }                   // 数字 "N2" / 日期 "yyyy-MM-dd"
    public AggregateType Aggregate { get; set; } = AggregateType.None;
}

/// <summary>报表筛选条件（简单的 字段-运算符-值 查询构建）</summary>
public class ReportFilter
{
    public int Id { get; set; }
    public int ReportDefId { get; set; }
    public ReportDef? ReportDef { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public FilterOperator Operator { get; set; } = FilterOperator.Contains;
    public string Value { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
