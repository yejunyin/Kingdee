using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.App.Data;

namespace Ben.HL.Bill.BusinessPlugIn
{
    [Kingdee.BOS.Util.HotUpdate]
    [Description("储运托单表单插件 - 审核后更新发运计划分录MRP关闭状态")]
    public class StorageBillPlugIn : AbstractBillPlugIn
    {
        /// <summary>
        /// 操作（如审核）执行完成后触发
        /// </summary>
        public override void AfterDoOperation(AfterDoOperationEventArgs e)
        {
            base.AfterDoOperation(e);

            // 1. 确保操作执行成功，且操作指令为“审核” (Audit)
            if (e.Operation.Operation.Equals("Audit", StringComparison.OrdinalIgnoreCase))
            {
                // 2. 执行更新发运计划分录逻辑
                UpdatePlanMRPCloseStatus();
            }
        }

        /// <summary>
        /// 更新关联发运计划分录的 MRP 关闭状态
        /// </summary>
        private void UpdatePlanMRPCloseStatus()
        {
            // 获取当前储运托单的数据对象
            DynamicObject billData = this.Model.DataObject;
            if (billData == null) return;

            // 收集涉及到的发运计划【分录行内码 FEntryID】
            List<long> planEntryIds = new List<long>();

            // 获取储运托单表体分录
            DynamicObjectCollection entryRows = billData["F_BHD_GoodsEntity"] as DynamicObjectCollection;
            if (entryRows != null)
            {
                foreach (var row in entryRows)
                {
                    // 获取关联的发运计划明细/源单分录内码
                    long planEntryId = Convert.ToInt64(row["F_BHD_DeliveryPlanId"]);

                    if (planEntryId > 0 && !planEntryIds.Contains(planEntryId))
                    {
                        planEntryIds.Add(planEntryId);
                    }
                }
            }

            if (!planEntryIds.Any()) return;

            // 3. 针对具体的发运计划分录表执行更新
            string entryIdsStr = string.Join(",", planEntryIds);

            // 精准匹配发运计划分录表 BHD_t_ES_SaleGoodsEntry 的 FEntryID
            string sql = $@"
                UPDATE BHD_t_ES_SaleGoodsEntry 
                SET FMRPCloseStatus = 'B' 
                WHERE FEntryID IN ({entryIdsStr}) 
                  AND ISNULL(F_BHD_Qty, 0) <= ISNULL(F_BHD_JoinStorageBaseQty, 0)
                  AND ISNULL(FMRPCloseStatus, 'A') <> 'B'";

            int updatedRows = DBUtils.Execute(this.Context, sql);

            // 4. (可选) 界面友好提示
            if (updatedRows > 0)
            {
                this.View.ShowMessage($"成功将关联的 {updatedRows} 条发运计划明细的关闭状态更新为【已关闭】！");
            }
        }
    }
}