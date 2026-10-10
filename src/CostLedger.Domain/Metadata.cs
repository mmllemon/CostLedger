namespace CostLedger.Domain;

/// <summary>扩展字段数据类型——通用渲染器据此把 FieldDef 映射为对应界面控件</summary>
public enum FieldType
{
    Text,    // 文本 → TextBox
    Number,  // 数字 → NumericUpDown
    Money,   // 金额 → NumericUpDown（千分位、两位小数）
    Date,    // 日期 → DatePicker
    Boolean, // 是/否 → CheckBox
    Lookup   // 下拉 → ComboBox（选项来自某 LookupDef）
}

/// <summary>
/// 字典定义（主数据全局共享库的一级）。
/// 例：往来单位角色 / 合同类型 / 税率模板 / 计量单位 / 项目属性。
/// 由后台"字典管理"可增删，取代原硬编码枚举。
/// </summary>
public class LookupDef
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;   // 全局唯一，如 "PartyRole"
    public string Name { get; set; } = string.Empty;   // 显示名，如 "往来单位角色"
    public string? Description { get; set; }
    public ICollection<LookupItem> Items { get; set; } = new List<LookupItem>();
}

/// <summary>字典值（主数据全局共享库的二级）</summary>
public class LookupItem
{
    public int Id { get; set; }
    public int LookupDefId { get; set; }
    public LookupDef? LookupDef { get; set; }
    public string Code { get; set; } = string.Empty;   // 值代码，如 "Supplier"
    public string Name { get; set; } = string.Empty;   // 显示名，如 "供方"
    public int SortOrder { get; set; }
}

/// <summary>
/// 实体定义（元数据注册表的一级）。
/// 例：Project / Party / Contract / Measurement / Payment。
/// </summary>
public class EntityDef
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;   // "Party" / "Contract" ...
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>true=后台自建对象类型（用户可在「自建对象」中增删记录）；false=系统实体（由 GenericRecordView 渲染）。</summary>
    public bool IsUserDefined { get; set; }
    public ICollection<FieldDef> Fields { get; set; } = new List<FieldDef>();
}

/// <summary>
/// 字段定义（元数据注册表的二级）。
/// 后台"字段管理"可对任意实体新增/删除字段，通用渲染器据此自动出表单。
/// 铁律：扩展字段默认 IsParticipateCost=false，绝不参与成本脊柱归集。
/// </summary>
public class FieldDef
{
    public int Id { get; set; }
    public int EntityDefId { get; set; }
    public EntityDef? EntityDef { get; set; }
    public string EntityDefCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;     // 显示名
    public FieldType Type { get; set; }
    public bool IsRequired { get; set; }
    public int MaxLength { get; set; } = 0;              // 文本最大长度，0=不限
    public string? LookupDefCode { get; set; }          // Type=Lookup 时引用字典 Code
    public int DisplayOrder { get; set; }
    public bool IsCore { get; set; } = false;           // true=核心硬编码字段（不由动态面板渲染）；false=扩展字段（动态渲染）
    public bool IsParticipateCost { get; set; } = false;// 铁律：默认不参与成本归集
}

/// <summary>
/// 扩展字段值（EAV 存储：每个 owner 的每个扩展字段一行）。
/// 统一以字符串存储 Value，按 FieldType 解析；避免为每个扩展字段改表结构。
/// </summary>
public class CustomFieldValue
{
    public int Id { get; set; }
    public string EntityDefCode { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public int FieldDefId { get; set; }
    public FieldDef? FieldDef { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 运行时自建对象的一条记录（用户在「自建对象」中新建的"对象类型"的实例）。
/// 身份字段（编号/名称）落本表，所有用户自定义字段值存于 DynamicValue（EAV）。
/// </summary>
public class DynamicRecord
{
    public int Id { get; set; }
    public string EntityDefCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;   // 业务编号（用户填）
    public string Name { get; set; } = string.Empty;   // 显示名（如设备名称/车牌）
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<DynamicValue> Values { get; set; } = new List<DynamicValue>();
}

/// <summary>
/// 自建对象记录的字段值（EAV）。一个 DynamicRecord 的每个字段一行；统一存字符串，按 FieldType 解析。
/// </summary>
public class DynamicValue
{
    public int Id { get; set; }
    public int RecordId { get; set; }
    public DynamicRecord? Record { get; set; }
    public int FieldDefId { get; set; }
    public FieldDef? FieldDef { get; set; }
    public string Value { get; set; } = string.Empty;
}
