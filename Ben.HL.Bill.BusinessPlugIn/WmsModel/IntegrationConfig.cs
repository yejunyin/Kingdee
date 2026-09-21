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
        public string SrmApiUrl { get; private set; }
        public string[] WmsOrgValues { get; private set; }
        public string SrmGroupCode { get; private set; }
        public string SrmUserCode { get; private set; }
        public string SrmDepartmentCode { get; private set; }

        public static IntegrationConfig Load()
        {
            return new IntegrationConfig
            {
                WmsReceiveBillApiUrl = "http://192.168.15.20:8081/prod-api/api/upperSystem/purchaseReceipt/push",
                WmsPurMtrlReturnApiUrl = "http://192.168.15.20:8081/prod-api/api/upperSystem/purchaseReturn/push",
                WmsPrdInStockApiUrl = "http://192.168.15.20:8081/prod-api/api/upperSystem/productionInstock/push",
                WmsPrdReportFinApiUrl = "http://192.168.15.20:8081/prod-api/api/upperSystem/productionReport/push",
                SrmApiUrl = "http://your-srm-api.com/api/receive/batch",
                WmsOrgValues = ParseWmsOrgValues("your-wms-org-id"),
                SrmGroupCode = "your-wms-org-id",
                SrmUserCode = "your-wms-org-id",
                SrmDepartmentCode = "your-wms-org-id"
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