using System;
using System.Collections.Generic;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.App.Data;
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
        /// 判断出库申请单分录中是否存在物料拣货仓库(T_BD_MATERIALPRODUCE.FPICKSTOCKID
        /// 对应 T_BD_STOCK.FNUMBER)为 2010 的行。
        /// 出库申请单表头 StockOrgId 是申请组织，实际库存组织从分录 StockOrgIdEntry 取第一条。
        /// </summary>
        public bool ExistsPickStock2010(DynamicObject billObj)
        {
            if (billObj == null) return false;

            List<DynamicObject> rows = GetEntryRows(billObj);
            if (rows.Count == 0) return false;

            // 1) 收集所有分录的物料ID（去重）+ 取首行的库存组织内码
            var materialIds = new List<long>();
            long stockOrgId = 0;
            foreach (DynamicObject row in rows)
            {
                if (row == null) continue;

                DynamicObject material = row["MaterialId"] as DynamicObject;
                long mid = ReceiveBillPushService.GetLong(material, "Id");
                if (mid > 0 && !materialIds.Contains(mid))
                {
                    materialIds.Add(mid);
                }

                if (stockOrgId <= 0)
                {
                    DynamicObject stockOrg = row["StockOrgIdEntry"] as DynamicObject;
                    stockOrgId = ReceiveBillPushService.GetLong(stockOrg, "Id");
                }
            }

            if (materialIds.Count == 0 || stockOrgId <= 0) return false;

            // 2) 复用收料通知单同款 SQL
            string sql = string.Format(@"
                                    SELECT TOP 1 1
                                    FROM T_BD_MATERIALPRODUCE p
                                    INNER JOIN T_BD_STOCK s ON s.FSTOCKID = p.FPICKSTOCKID
                                    WHERE p.FMATERIALID IN ({0})
                                    AND p.FUSEORGID = {1}
                                    AND s.FNUMBER = '2010'", string.Join(",", materialIds), stockOrgId);

            DynamicObjectCollection result = DBUtils.ExecuteDynamicObject(_ctx, sql);
            return result != null && result.Count > 0;
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

            // 自定义基础资料 F_hll_Base：优先取单据同名字段，缺省用部门编码
            string hllBaseNumber = GetBaseNumber(billObj, "F_hll_Base");
            header.F_hll_Base.FNumber = string.IsNullOrWhiteSpace(hllBaseNumber) ? deptNumber : hllBaseNumber;

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
