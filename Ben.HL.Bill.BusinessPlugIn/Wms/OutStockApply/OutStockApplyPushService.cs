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

            DynamicObjectCollection rows = billObj["BillEntry"] as DynamicObjectCollection;
            if (rows == null) return result;

            foreach (DynamicObject row in rows)
            {
                if (row != null) result.Add(row);
            }
            return result;
        }

        /// <summary>
        /// 判断出库申请单分录中是否存在仓库为 2010 的行。
        /// 直接检查分录 StockId 基础资料的 Number 是否等于 "2010"，无需查库。
        /// </summary>
        public bool ExistsPickStock2010(DynamicObject billObj)
        {
            if (billObj == null) return false;

            List<DynamicObject> rows = GetEntryRows(billObj);
            foreach (DynamicObject row in rows)
            {
                if (row == null) continue;
                DynamicObject stock = row["StockId"] as DynamicObject;
                string stockNumber = ReceiveBillPushService.GetValue(stock, "Number");
                if (string.Equals(stockNumber, "2010", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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

            // 先拿分录行，从中提取库存组织和货主的首行值供表头使用
            List<DynamicObject> rows = GetEntryRows(billObj);
            string stockOrgEntryNumber = rows.Count > 0 ? GetBaseNumber(rows[0], "StockOrgIdEntry") : string.Empty;
            string ownerNumber = rows.Count > 0 ? GetBaseNumber(rows[0], "OwnerId") : string.Empty;

            // 库存组织（明细.StockOrgIdEntry）→ 表头 FStockOrgId
            header.FStockOrgId.FNumber = stockOrgEntryNumber;
            // 申请组织（单据头.StockOrgId）→ 表头 FPickOrgId（拣货组织）
            string applyOrgNumber = GetBaseNumber(billObj, "StockOrgId");
            header.FPickOrgId.FNumber = applyOrgNumber;
            // 货主（明细.OwnerId）→ 表头 FOwnerIdHead
            header.FOwnerIdHead.FNumber = ownerNumber;

            // 申请部门
            string deptNumber = GetBaseNumber(billObj, "ApplyDeptId", "DeptId");
            header.FDeptId.FNumber = deptNumber;

            // 自定义基础资料 FBizType：优先取单据同名字段，缺省用部门编码
            header.F_hll_Base.FNumber = "003"; ;

            header.FEntity = rows
                .Select(row => BuildEntry(row, billNo, applyOrgNumber))
                .Where(entry => entry != null)
                .ToList();
            return request;
        }

        /// <summary>
        /// 构建分录：无物料的行直接跳过（返回null）
        /// </summary>
        /// <param name="row">当前分录行</param>
        /// <param name="srcBillNo">源单号</param>
        /// <param name="keeperOrgNumber">申请组织编码，用作分录保管者</param>
        private MisDeliveryEntryModel BuildEntry(DynamicObject row, string srcBillNo, string keeperOrgNumber)
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
            // 分录货主：明细实体属性 OwnerId
            entry.FOwnerId.FNumber = GetBaseNumber(row, "OwnerId");
            // 保管者：申请组织
            entry.FKeeperId.FNumber = keeperOrgNumber;
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
