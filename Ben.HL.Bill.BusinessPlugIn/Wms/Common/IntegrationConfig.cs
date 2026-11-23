using System;
using System.Linq;

namespace Ben.HL.Bill.BusinessPlugIn
{
    public class IntegrationConfig
    {
        public string WmsReceiveBillApiUrl { get; private set; }
        public string WmsPurMtrlReturnApiUrl { get; private set; }
        public string WmsPrdInStockApiUrl { get; private set; }
        public string WmsPrdReportFinApiUrl { get; private set; }
        public string WmsOutStockApplyApiUrl { get; private set; }
        public string WmsTransferApplyApiUrl { get; private set; }
        public string SrmApiUrl { get; private set; }

        public static IntegrationConfig Load()
        {
            return new IntegrationConfig
            {
                WmsReceiveBillApiUrl = "http://192.168.15.20:8081/prod-api/api/upperSystem/purchaseReceipt/push",
                WmsPurMtrlReturnApiUrl = "http://192.168.15.20:8081/prod-api/api/upperSystem/purchaseReturn/push",
                WmsPrdInStockApiUrl = "http://192.168.15.20:8081/prod-api/api/upperSystem/productionInstock/push",
                WmsPrdReportFinApiUrl = "http://192.168.15.20:8081/prod-api/api/upperSystem/semiProductReportIn/push",
                // TODO: 出库申请单推送地址为占位规律地址，拿到WMS真实地址后改这一行
                WmsOutStockApplyApiUrl = "http://192.168.15.20:8081/prod-api/api/upperSystem/otherOutbound/push",
                // TODO: 调拨申请单推送地址为预留占位，拿到WMS真实地址后改这一行
                WmsTransferApplyApiUrl = "http://192.168.15.20:8081/prod-api/api/upperSystem/transferApply/push",
                SrmApiUrl = "http://192.168.1.151:9527/scm-webservice/api/order/OrderApiServiceImplService"
            };
        }

        private static string[] ParseWmsOrgValues(string value)
        {
            return value
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToArray();
        }
    }
}