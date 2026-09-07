using System;
using System.Configuration;
using System.Linq;

namespace Ben.HL.Bill.BusinessPlugIn
{
    public class IntegrationConfig
    {
        public string WmsApiUrl { get; private set; }
        public string SrmApiUrl { get; private set; }
        public string[] WmsOrgValues { get; private set; }
        public string SrmGroupCode { get; private set; }
        public string SrmUserCode { get; private set; }
        public string SrmDepartmentCode { get; private set; }

        public static IntegrationConfig Load()
        {
            return new IntegrationConfig
            {
                WmsApiUrl = Required("WmsApiUrl"),
                SrmApiUrl = Required("SrmApiUrl"),
                WmsOrgValues = ParseWmsOrgValues(Required("WmsOrgValues")), 
                SrmGroupCode = Required("SrmGroupCode"),
                SrmUserCode = Required("SrmUserCode"),
                SrmDepartmentCode = Required("SrmDepartmentCode")
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

        private static string Required(string key)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ConfigurationErrorsException("缺少配置项：" + key);
            }
            return value.Trim();
        }
    }
}
