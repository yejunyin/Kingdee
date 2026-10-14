using System;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 手工推送测试入口（仅测试用，不要注册为单据插件）。
    /// 走金蝶 WebApi：ValidateUser 登录 → View 查询生产汇报单 → 构建PRD_InStock报文 → PostJson 推送WMS。
    /// 用法：Ben.HL.Bill.BusinessPlugIn.TestConsole.exe [汇报单号]
    /// 示例：Ben.HL.Bill.BusinessPlugIn.TestConsole.exe RBNO2610130090
    /// 注意：汇报单号第4位是字母 O（RBNO + 日期YYMMDD + 4位序号），不是数字 0。
    /// </summary>
    public static class PushReceiveBillToWmsTest
    {
        public static void Main(string[] args)
        {
            string billNo = args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
                ? args[0].Trim()
                : "RBNO2610130090";

            PushPrdReportFinTestRunner.Run(billNo);
        }
    }
}
