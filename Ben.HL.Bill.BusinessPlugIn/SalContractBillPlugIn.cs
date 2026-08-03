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

            // 1. 检查改变的字段是否为物料字段
            if (e.Field.Key.Equals("F_BHD_MaterialId", StringComparison.OrdinalIgnoreCase))
            {
                int rowIndex = e.Row;

                // 修正点 1：获取物料 ID（直接转换 e.NewValue，兼顾 DynamicObject 与 long 类型）
                long materialId = Convert.ToInt64(e.NewValue);
                if (materialId <= 0) return;

                // 修正点 2：获取表头数据（基础资料建议取主键值或直接强转对象）
                DynamicObject custObj = this.View.Model.GetValue("F_BHD_CusId") as DynamicObject;
                DynamicObject currObj = this.View.Model.GetValue("F_BHD_CurrencyId") as DynamicObject;
                object billDateObj = this.View.Model.GetValue("F_BHD_Date");

                if (custObj == null || currObj == null || billDateObj == null) return;

                long customerId = Convert.ToInt64(custObj["Id"]);
                long currencyId = Convert.ToInt64(currObj["Id"]);
                DateTime billDate = Convert.ToDateTime(billDateObj);

                // 2. 按优先级匹配获取价格 (P1 -> P2)
                decimal price = GetSalesPrice(customerId, currencyId, materialId, billDate);

                // 3. 回写单价到目标字段
                this.View.Model.SetValue("FSysPrice", price, rowIndex);
                this.View.Model.SetValue("F_BHD_ConSalesPrice", price, rowIndex);

                // 刷新对应单据体行（确保 FEntity 替换为实际单据体标识）
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
            string custTypeId = GetCustomerTypeId(customerId);
            if (!string.IsNullOrEmpty(custTypeId))
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
                  AND H.FEFFECTIVEDATE <= '{3}' 
                  AND H.FEXPIRYDATE >= '{3}'
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
        private decimal QueryPriceByCustType(string custTypeId, long currencyId, long materialId, DateTime billDate)
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
                  AND CT.FCUSTTYPEID = '{2}'
                  AND H.FEFFECTIVEDATE <= '{3}' 
                  AND H.FEXPIRYDATE >= '{3}'
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
        /// 查询客户档案中的客户类别ID (GUID 格式字符串)
        /// </summary>
        private string GetCustomerTypeId(long customerId)
        {
            string sql = string.Format("SELECT FCUSTTYPEID FROM T_BD_CUSTOMER WHERE FCUSTID = {0}", customerId);

            DynamicObjectCollection dt = DBServiceHelper.ExecuteDynamicObject(this.Context, sql);
            if (dt != null && dt.Count > 0)
            {
                return Convert.ToString(dt[0]["FCUSTTYPEID"]);
            }

            return string.Empty;
        }
    }
}