using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;

using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;

namespace Ben.HL.Bill.BusinessPlugIn
{
    [HotUpdate]
    [Description("选择物料获取销售单价-Ben")]
    public class SalContractBillPlugIn : AbstractBillPlugIn
    {
        public override void DataChanged(DataChangedEventArgs e)
        {
            base.DataChanged(e);

            // 1. 检查改变的字段是否为物料字段（请根据实际字段标识调整）
            if (e.Field.Key.Equals("FMaterialId", StringComparison.OrdinalIgnoreCase))
            {
                int rowIndex = e.Row;

                // 获取物料ID
                DynamicObject materialObj = e.NewValue as DynamicObject;
                if (materialObj == null) return;
                long materialId = Convert.ToInt64(materialObj["Id"]);

                // 获取表头：客户、币别、单据日期
                DynamicObject custObj = this.View.Model.GetValue("FCustomerId") as DynamicObject;
                DynamicObject currObj = this.View.Model.GetValue("FCurrencyId") as DynamicObject;
                DateTime billDate = Convert.ToDateTime(this.View.Model.GetValue("FDate"));

                if (custObj == null || currObj == null) return;

                long customerId = Convert.ToInt64(custObj["Id"]);
                long currencyId = Convert.ToInt64(currObj["Id"]);

                // 2. 按优先级匹配获取价格 (P1 -> P2)
                decimal price = GetSalesPrice(customerId, currencyId, materialId, billDate);

                // 3. 回写单价到目标字段
                this.View.Model.SetValue("FSysPrice", price, rowIndex);
                this.View.Model.SetValue("F_BHD_ConSalesPrice", price, rowIndex);

                // 刷新对应单据体行（请将 FEntity 替换为实际的单据体Key）
                this.View.UpdateView("FEntity", rowIndex);
            }
        }

        /// <summary>
        /// 获取销售价格（P1 优先，P2 备选）
        /// </summary>
        private decimal GetSalesPrice(long customerId, long currencyId, long materialId, DateTime billDate)
        {
            // P1 (最高优先级): 限定客户 + 币别
            decimal priceP1 = QueryPriceByCustomer(customerId, currencyId, materialId, billDate);
            if (priceP1 > 0)
            {
                return priceP1;
            }

            // P2 优先级: 客户类别 + 币别
            long custTypeId = GetCustomerTypeId(customerId);
            if (custTypeId > 0)
            {
                decimal priceP2 = QueryPriceByCustType(custTypeId, currencyId, materialId, billDate);
                if (priceP2 > 0)
                {
                    return priceP2;
                }
            }

            return 0m;
        }

        /// <summary>
        /// P1: 拼装 SQL 匹配【指定客户 + 币别】
        /// </summary>
        private decimal QueryPriceByCustomer(long customerId, long currencyId, long materialId, DateTime billDate)
        {
            string dateStr = billDate.ToString("yyyy-MM-dd");

            string sql = string.Format(@"
                SELECT TOP 1 E.FPRICE 
                FROM T_SAL_PRICELIST H
                INNER JOIN T_SAL_PRICELISTENTRY E ON H.FID = E.FID
                INNER JOIN T_SAL_APPLYCUSTOMER C ON H.FID = C.FID
                WHERE H.FDOCUMENTSTATUS = 'C' 
                  AND H.FForbidStatus = 'A'
                  AND H.FCURRENCYID = {0}
                  AND E.FMATERIALID = {1}
                  AND C.FCUSTID = {2}
                  AND H.EFFECTIVEDATE <= '{3}' 
                  AND H.EXPIRYDATE >= '{3}'
                ORDER BY H.FAPPROVEDATE DESC",
                currencyId, materialId, customerId, dateStr);

            DynamicObjectCollection dt = DBServiceHelper.ExecuteDynamicObject(this.Context, sql);
            if (dt != null && dt.Count > 0)
            {
                return Convert.ToDecimal(dt[0]["FPRICE"]);
            }

            return 0m;
        }

        /// <summary>
        /// P2: 拼装 SQL 匹配【客户类别 + 币别】
        /// </summary>
        private decimal QueryPriceByCustType(long custTypeId, long currencyId, long materialId, DateTime billDate)
        {
            string dateStr = billDate.ToString("yyyy-MM-dd");

            string sql = string.Format(@"
                SELECT TOP 1 E.FPRICE 
                FROM T_SAL_PRICELIST H
                INNER JOIN T_SAL_PRICELISTENTRY E ON H.FID = E.FID
                INNER JOIN T_SAL_APPLYCUSTOMER CT ON H.FID = CT.FID
                WHERE H.FDOCUMENTSTATUS = 'C' 
                  AND H.FForbidStatus = 'A'
                  AND H.FCURRENCYID = {0}
                  AND E.FMATERIALID = {1}
                  AND CT.FCUSTTYPEID = {2}
                  AND H.EFFECTIVEDATE <= '{3}' 
                  AND H.EXPIRYDATE >= '{3}'
                ORDER BY H.FAPPROVEDATE DESC",
                currencyId, materialId, custTypeId, dateStr);

            DynamicObjectCollection dt = DBServiceHelper.ExecuteDynamicObject(this.Context, sql);
            if (dt != null && dt.Count > 0)
            {
                return Convert.ToDecimal(dt[0]["FPRICE"]);
            }

            return 0m;
        }

        /// <summary>
        /// 查询客户档案中的客户类别ID
        /// </summary>
        private long GetCustomerTypeId(long customerId)
        {
            string sql = string.Format("SELECT FCUSTTYPEID FROM T_BD_CUSTOMER WHERE FCUSTID = {0}", customerId);

            DynamicObjectCollection dt = DBServiceHelper.ExecuteDynamicObject(this.Context, sql);
            if (dt != null && dt.Count > 0)
            {
                return Convert.ToInt64(dt[0]["FCUSTTYPEID"]);
            }

            return 0;
        }
    }
}