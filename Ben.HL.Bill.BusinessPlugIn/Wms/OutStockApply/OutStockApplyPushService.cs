using System;
using System.Collections.Generic;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 出库申请单(STK_OutStockApply)推送WMS的公共业务服务：
    /// 把出库申请单数据组装成其他出库单(STK_MisDelivery)保存报文，供WMS回写其他出库单。
    /// 取值/序列化通用辅助复用 ReceiveBillPushService 的公共静态方法。
    /// </summary>
    public class OutStockApplyPushService
    {
        private readonly Context _ctx;

        public OutStockApplyPushService(Context ctx)
        {
            _ctx = ctx;
        }

        /// <summary>
        /// 取单据分录行集合（出库申请单单据体Key为FEntity）
        /// </summary>
        public List<DynamicObject> GetEntryRows(DynamicObject billObj)
        {
            var result = new List<DynamicObject>();
            if (billObj == null) return result;

            DynamicObjectCollection rows = billObj["FEntity"] as DynamicObjectCollection;
            if (rows == null) return result;

            foreach (DynamicObject row in rows)
            {
                if (row != null) result.Add(row);
            }
            return result;
        }

        /// <summary>
        /// 把出库申请单 DynamicObject 组装成其他出库单(STK_MisDelivery)保存请求
        /// </summary>
        public MisDeliverySaveRequestModel BuildMisDeliveryRequest(DynamicObject billObj)
        {
            var request = new MisDeliverySaveRequestModel();
            MisDeliveryHeaderModel header = request.data.Model;

            string billNo = ReceiveBillPushService.GetValue(billObj, "BillNo");
            header.FID = 0; // 固定0：WMS回写时按新增保存
            header.FDate = ReceiveBillPushService.GetDate(billObj, "Date");

            // 申请组织 → 库存组织/拣货组织/表头货主
            string orgNumber = GetBaseNumber(billObj, "ApplyOrgId", "StockOrgId");
            header.FStockOrgId.FNumber = orgNumber;
            header.FPickOrgId.FNumber = orgNumber;
            header.FOwnerIdHead.FNumber = orgNumber;

            // 申请部门
            string deptNumber = GetBaseNumber(billObj, "ApplyDeptId", "DeptId");
            header.FDeptId.FNumber = deptNumber;

            // 自定义基础资料 F_hll_Base：优先取单据同名字段，缺省用部门编码
            string hllBaseNumber = GetBaseNumber(billObj, "F_hll_Base");
            header.F_hll_Base.FNumber = string.IsNullOrWhiteSpace(hllBaseNumber) ? deptNumber : hllBaseNumber;

            header.FEntity = GetEntryRows(billObj)
                .Select(row => BuildEntry(row, billNo, orgNumber))
                .Where(entry => entry != null)
                .ToList();
            return request;
        }

        /// <summary>
        /// 构建分录：无物料的行直接跳过（返回null）
        /// </summary>
        private MisDeliveryEntryModel BuildEntry(DynamicObject row, string srcBillNo, string orgNumber)
        {
            string materialNumber = GetBaseNumber(row, "MaterialId");
            if (string.IsNullOrWhiteSpace(materialNumber))
            {
                return null;
            }

            // 申请数量(ApplyQty)优先，兼容数量(Qty)
            decimal qty = ReceiveBillPushService.GetDecimal(row, "ApplyQty");
            if (qty == 0)
            {
                qty = ReceiveBillPushService.GetDecimal(row, "Qty");
            }

            var entry = new MisDeliveryEntryModel
            {
                FSrcBillNo = srcBillNo,
                FQty = qty,
                FDistribution = false,
                FExtAuxUnitQty = 0
            };
            entry.FMaterialId.FNumber = materialNumber;
            entry.FUnitID.FNumber = GetBaseNumber(row, "UnitID", "UnitId");
            entry.FBaseUnitId.FNumber = GetBaseNumber(row, "BaseUnitId");
            entry.FStockId.FNumber = GetBaseNumber(row, "StockId");
            entry.FOwnerId.FNumber = orgNumber;
            entry.FKeeperId.FNumber = orgNumber;
            return entry;
        }

        /// <summary>
        /// 按候选字段名依次取基础资料编码（DynamicObject属性名为元数据Key去F前缀）
        /// </summary>
        private static string GetBaseNumber(DynamicObject source, params string[] fieldNames)
        {
            if (source == null || fieldNames == null) return string.Empty;
            foreach (string fieldName in fieldNames)
            {
                DynamicObject baseData = source.Contains(fieldName) ? source[fieldName] as DynamicObject : null;
                string number = ReceiveBillPushService.GetValue(baseData, "Number");
                if (!string.IsNullOrWhiteSpace(number))
                {
                    return number;
                }
            }
            return string.Empty;
        }
    }
}
