namespace CostLedger.Infrastructure;

/// <summary>
/// 往来单位内部汇报报表的口径声明。放在基础设施层而非视图层：
/// 成本岗的"未付/未收"是合同额与已登记金额之推算差额，不能被读成财务应付/应收账款结论。
/// 文案与截至日期必须一起输出，缺任何一句都会让责任边界模糊。
/// </summary>
public static class PartyLedgerCaliber
{
    public static string Build(string asOfDate) =>
        $"数据截至：{asOfDate}　编制：成本岗。" +
        "本表为项目成本管理信息，合同额与已付/已收来自成本岗登记及财务、项目部提供的汇总；" +
        "未付（推算）＝对下合同额 − 已登记付款，未收（推算）＝对上合同额 − 已登记收款，" +
        "为推算差额，不等于财务应付/应收账款（未含未开票、质保金、暂扣等因素），" +
        "成本岗不对其完整性与合规性作出判断。";
}
