using System;
using System.Collections.Generic;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Orm.DataEntity;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 调拨申请单(STK_TransferApply)推送WMS的公共业务服务：
    /// 把调拨申请单数据组装成调拨单(STK_TransferDirect)保存报文，
    /// WMS收到后原样回推调用K3Cloud保存接口生成调拨单。
    /// 取值/序列化通用辅助复用 ReceiveBillPushService + DynamicObjectHelper 的公共静态方法。
    /// </summary>
    public class TransferApplyPushService
    {
        private readonly Context _ctx;

        public TransferApplyPushService(Context ctx)
        {
            _ctx = ctx;
        }

        /// <summary>
        /// 取调拨申请单分录行集合（默认单据体Key=BillEntry）
        /// </summary>
        public List<DynamicObject> GetEntryRows(DynamicObject billObj)
        {
            var result = new List<DynamicObject>();
            if (billObj == null) return result;

            // 调拨申请单单据体Key: STK_TransferApply 通常为 BillEntry
            // 如与实际元数据不符，改这里即可
            DynamicObjectCollection rows = billObj["STK_STKTRANSFERAPPENTRY"] as DynamicObjectCollection;
            if (rows == null) return result;

            foreach (DynamicObject row in rows)
            {
                if (row != null) result.Add(row);
            }
            return result;
        }

        /// <summary>
        /// 判断是否需要推送WMS：分录中是否存在仓库编号为2010的行
        /// （与其他WMS插件保持一致：只有WMS管理仓库的物料才推送）
        /// </summary>
        public bool ExistsWmsStock(DynamicObject billObj)
        {
            if (billObj == null) return false;

            List<DynamicObject> rows = GetEntryRows(billObj);
            foreach (DynamicObject row in rows)
            {
                if (row == null) continue;

                // 调出仓库：按候选字段名依次尝试
                DynamicObject srcStock = DynamicObjectHelper.TryGetBaseData(row, "SrcStockId", "StockId");
                string srcStockNumber = ReceiveBillPushService.GetValue(srcStock, "Number");
                if (string.Equals(srcStockNumber, "2010", StringComparison.Ordinal))
                    return true;

                // 调入仓库
                DynamicObject destStock = DynamicObjectHelper.TryGetBaseData(row, "DestStockId");
                string destStockNumber = ReceiveBillPushService.GetValue(destStock, "Number");
                if (string.Equals(destStockNumber, "2010", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 调拨申请单(STK_TransferApply)单据类型 → 直接调拨单(STK_TransferDirect)单据类型 映射。
        /// 金蝶两张单据的单据类型体系完全独立，编码不可直接复用，必须按此表转换。
        /// 未知类型回退到标准直接调拨单 ZJDB01_SYS。
        /// </summary>
        private static string MapToTransferDirectBillType(string applyBillType)
        {
            if (string.IsNullOrWhiteSpace(applyBillType)) return "ZJDB01_SYS";
            switch (applyBillType)
            {
                case "DBSD01_SYS": return "ZJDB01_SYS"; // 标准调拨申请单 → 标准直接调拨单
                case "DBSD02_SYS": return "ZJDB05_SYS"; // VMI调拨申请单 → VMI直接调拨单
                case "DBSD03_SYS": return "ZJDB04_SYS"; // 库存调拨申请单 → 库存直接调拨单
                default: return "ZJDB01_SYS";            // 未知类型回退到标准直接调拨单
            }
        }

        /// <summary>
        /// 把调拨申请单 DynamicObject 组装成调拨单(STK_TransferDirect)保存请求
        /// </summary>
        public TransferDirectSaveRequestModel BuildTransferDirectRequest(DynamicObject billObj)
        {
            var request = new TransferDirectSaveRequestModel();
            TransferDirectHeaderModel header = request.data.Model;

            string billNo = ReceiveBillPushService.GetValue(billObj, "BillNo");
            string billDate = ReceiveBillPushService.GetDate(billObj, "Date");

            // === 表头固定值 ===
            header.FID = 0; // 固定0：WMS回写时按新增保存

            // === 单据类型：调拨申请单 BillTypeId → 直接调拨单 FBillTypeID 映射 ===
            // 两张单据的单据类型体系独立，编码不能直接复用，必须按映射表转换
            string srcBillTypeNumber = DynamicObjectHelper.GetBaseNumber(billObj, "BillTypeId");
            header.FBillTypeID.FNumber = MapToTransferDirectBillType(srcBillTypeNumber);

            // FBizType（业务类型）：直接从单据头 FBusinessType 取
            header.FBizType = ReceiveBillPushService.GetValue(billObj, "BusinessType");

            // 调拨方向 FTransferDirect：从单据头取
            header.FTransferDirect = ReceiveBillPushService.GetValue(billObj, "FTransferDirect");

            // 调拨类型 FTransferBizType：从单据头取
            header.FTransferBizType = ReceiveBillPushService.GetValue(billObj, "TRANSTYPE");
            header.FOwnerTypeOutIdHead = "BD_OwnerOrg";
            header.FOwnerTypeIdHead = "BD_OwnerOrg";
            header.FIsIncludedTax = true;
            header.FIsPriceExcludeTax = true;
            header.FExchangeRate = 1.0m;
            header.FWriteOffConsign = false;

            // === 表头取值 ===
            header.FDate = billDate;

            // 汇率类型
            header.FExchangeTypeId.FNumber = "HLTX01_SYS";

            // 结算币别
            header.FSETTLECURRID.FNumber = "PRE001";

            // 先拿分录行，从中提取组织/货主等首行值供表头使用
            List<DynamicObject> rows = GetEntryRows(billObj);

            if (rows.Count > 0)
            {
                DynamicObject firstRow = rows[0];

                // 调出库存组织（分录头 FStockOrgId / StockOrgId）
                header.FStockOutOrgId.FNumber = DynamicObjectHelper.GetBaseNumber(firstRow, "FStockOrgId", "StockOrgId");
                // 调入库存组织（分录头 SrcStockOrgId / OutStockOrgId / StockOrgId）
                header.FStockOrgId.FNumber = DynamicObjectHelper.GetBaseNumber(firstRow, "FStockOrgInId", "StockOrgInId");

                // 调出表头货主（分录头 OwnerOutId / SrcOwnerId）
                string ownerOutNumber = DynamicObjectHelper.GetBaseNumber(firstRow, "FOwnerId", "OwnerId");
                header.FOwnerOutIdHead.FNumber = ownerOutNumber;

                // 表头货主（分录头 OwnerId / DestOwnerId）
                string ownerNumber = DynamicObjectHelper.GetBaseNumber(firstRow, "FOwnerInId", "OwnerInId");
                header.FOwnerIdHead.FNumber = ownerNumber;
            }

            // === 分录 ===
            header.FBillEntry = rows
                .Select(row => BuildEntry(billObj, row, billNo))
                .Where(entry => entry != null)
                .ToList();

            return request;
        }

        /// <summary>
        /// 构建调拨单分录：无物料的行直接跳过（返回null）
        /// </summary>
        private TransferDirectEntryModel BuildEntry(DynamicObject billObj, DynamicObject row, string srcBillNo)
        {
            string materialNumber = DynamicObjectHelper.GetBaseNumber(row, "MaterialId");
            if (string.IsNullOrWhiteSpace(materialNumber))
            {
                return null;
            }

            // 数量
            decimal qty = ReceiveBillPushService.GetDecimal(row, "Qty");
            if (qty == 0) qty = ReceiveBillPushService.GetDecimal(row, "ApplyQty");

            // 基本数量（与数量相同，若无基本单位数量字段）
            decimal baseQty = ReceiveBillPushService.GetDecimal(row, "BaseQty");
            if (baseQty == 0) baseQty = qty;

            // 销售数量
            decimal saleQty = ReceiveBillPushService.GetDecimal(row, "SaleQty");
            if (saleQty == 0) saleQty = qty;

            decimal priceQty = qty;
            decimal priceBaseQty = baseQty;

            // 业务日期：用单据头的 Date
            string businessDate = ReceiveBillPushService.GetDate(billObj, "Date");

            var entry = new TransferDirectEntryModel
            {
                FRowType = "Standard",
                FQty = qty,
                FBaseQty = baseQty,
                FSaleQty = saleQty,
                FSalBaseQty = saleQty,
                FPriceQty = priceQty,
                FPriceBaseQty = priceBaseQty,
                FSrcBillNo = srcBillNo,
                FSrcBillTypeId = "STK_TRANSFERAPPLY",
                FOwnerTypeOutId = "BD_OwnerOrg",
                FOwnerTypeId = "BD_OwnerOrg",
                FKeeperTypeId = "BD_KeeperOrg",
                FKeeperTypeOutId = "BD_KeeperOrg",
                FBusinessDate = businessDate,
                FDiscountRate = 0.0m,
                FISFREE = false,
                FRepairQty = 0.0m,
                FActQty = 0.0m,
                FSecQty = 0.0m,
                FExtAuxUnitQty = 0.0m,
                FOutJoinQty = 0.0m,
                FBASEOUTJOINQTY = 0.0m,
                FTransReserveLink = false,
                FCheckDelivery = false,
                FSOEntryId = 0,
                FQmEntryId = 0,
                FConvertEntryId = 0,
                F_BHD_AppFinancID = 0,
                F_BHD_AppFinancEnID = 0,
                FBomEntryId = 0
            };

            // === 基础资料字段 ===
            entry.FMaterialId.FNumber = materialNumber;
            entry.FDestMaterialId.FNumber = materialNumber;

            // 单位
            string unitNumber = DynamicObjectHelper.GetBaseNumber(row, "FUnitId", "UnitID");
            entry.FUnitID.FNumber = unitNumber;
            entry.FSaleUnitId.FNumber = unitNumber;
            entry.FPriceUnitID.FNumber = unitNumber;

            // 基本单位
            string baseUnitNumber = DynamicObjectHelper.GetBaseNumber(row, "FBaseUnitId", "BaseUnitID");
            if (string.IsNullOrWhiteSpace(baseUnitNumber)) baseUnitNumber = unitNumber;
            entry.FBaseUnitId.FNumber = baseUnitNumber;

            // 批号
            string lotNumber = DynamicObjectHelper.GetBaseNumber(row, "Lot", "FLot");
            entry.FLot.FNumber = lotNumber;
            entry.FDestLot.FNumber = lotNumber;

            // 调出仓库
            string srcStockNumber = DynamicObjectHelper.GetBaseNumber(row, "FStockId", "StockId");
            entry.FSrcStockId.FNumber = srcStockNumber;

            // 调入仓库
            string destStockNumber = DynamicObjectHelper.GetBaseNumber(row, "FStockInId", "StockInId");
            entry.FDestStockId.FNumber = destStockNumber;

            // 调出货位
            string srcStockLocNumber = DynamicObjectHelper.GetBaseNumber(row, "FStockLocId", "StockLocId");
            if (!string.IsNullOrWhiteSpace(srcStockLocNumber))
            {
                entry.FSrcStockLocId["FSRCSTOCKLOCID__FF100004"] = new BaseDataModel { FNumber = srcStockLocNumber };
            }

            // 库存状态
            string srcStockStatus = DynamicObjectHelper.GetBaseNumber(row, "FStockStatusId", "StockStatusId");
            if (string.IsNullOrWhiteSpace(srcStockStatus)) srcStockStatus = "KCZT01_SYS";
            entry.FSrcStockStatusId.FNumber = srcStockStatus;

            string destStockStatus = DynamicObjectHelper.GetBaseNumber(row, "FStockStatusInId", "StockStatusInId");
            entry.FDestStockStatusId.FNumber = destStockStatus;

            // 计划跟踪号
            entry.FMtoNo = ReceiveBillPushService.GetValue(row, "MtoNo");

            // 货主（分录）
            entry.FOwnerOutId.FNumber = DynamicObjectHelper.GetBaseNumber(row, "FOwnerId", "OwnerId");
            entry.FOwnerId.FNumber = DynamicObjectHelper.GetBaseNumber(row, "FOwnerInId", "OwnerInId");

            // 保管者
            entry.FKeeperId.FNumber = DynamicObjectHelper.GetBaseNumber(row, "FOwnerInId", "OwnerInId");
            entry.FKeeperOutId.FNumber = DynamicObjectHelper.GetBaseNumber(row, "FOwnerId", "OwnerId");

            // === 分录关联 (FBillEntry_Link) ===
            BuildEntryLink(billObj, row, entry);

            return entry;
        }

        /// <summary>
        /// 构建分录关联表 FBillEntry_Link
        /// sTableId = sBillId = 当前单据(调拨申请单)的 FId
        /// sId = 当前分录行的 EntryId（分录内码）
        /// </summary>
        private void BuildEntryLink(DynamicObject billObj, DynamicObject row, TransferDirectEntryModel entry)
        {
            // sTableId / sBillId：当前单据（调拨申请单）的 FId
            long sBillId = DynamicObjectHelper.GetLong(billObj, "Id");
            long sTableId = sBillId;

            // sId：当前分录行的 EntryId（分录内码）
            long sId = DynamicObjectHelper.GetLong(row, "Id");

            var link = new TransferDirectEntryLinkModel
            {
                FBillEntry_Link_FRuleId = "StkTransferApply-StkTransferDirect",
                FBillEntry_Link_FSTableId = sTableId,
                FBillEntry_Link_FSTableName = "T_STK_STKTRANSFERAPPENTRY",
                FBillEntry_Link_FSBillId = sBillId,
                FBillEntry_Link_FSId = sId,
                FBillEntry_Link_FBaseQtyOld = entry.FBaseQty,
                FBillEntry_Link_FBaseQty = entry.FBaseQty,
                FBillEntry_Link_FSalBaseQtyOld = 0.0m,
                FBillEntry_Link_FSalBaseQty = entry.FSalBaseQty,
                FBillEntry_Link_FPriceBaseQtyOld = 0.0m,
                FBillEntry_Link_FPriceBaseQty = entry.FPriceBaseQty
            };
            entry.FBillEntry_Link.Add(link);
        }

        #region 回填单据链接字段

        /// <summary>
        /// 根据 2010 仓在调拨申请单中的角色（调出/调入），回填 WMS 新生成的单号。
        ///   2010 为调出仓 → 更新 FLKDELIVERYORDERID（出库关联）
        ///   2010 为调入仓 → 更新 FLKENTRYORDERID（入库关联）
        /// 直接拼 SQL 绕过 DynamicObject 只读校验，参照 ReceiveBillPushService.SaveFwmsstatus 的做法。
        /// </summary>
        /// <param name="ctx">金蝶上下文</param>
        /// <param name="billId">调拨申请单表头 FID</param>
        /// <param name="wmsNumber">WMS 返回的调拨单编号</param>
        /// <param name="is2010Out">分录中存在 2010 作为调出仓</param>
        /// <param name="is2010In">分录中存在 2010 作为调入仓</param>
        public static void SaveTransferApplyWmsLinks(Context ctx, long billId, string wmsNumber,
            bool is2010Out, bool is2010In)
        {
            if (ctx == null || billId <= 0 || string.IsNullOrWhiteSpace(wmsNumber)) return;
            string safeNumber = wmsNumber.Replace("'", "''");

            // 表头表：T_STK_STKTRANSFERAPP
            if (is2010Out)
            {
                string sql = string.Format(
                    "UPDATE T_STK_STKTRANSFERAPP SET FLKDELIVERYORDERID = N'{0}' WHERE FID = {1}",
                    safeNumber, billId);
                DBUtils.Execute(ctx, sql);
            }
            if (is2010In)
            {
                string sql = string.Format(
                    "UPDATE T_STK_STKTRANSFERAPP SET FLKENTRYORDERID = N'{0}' WHERE FID = {1}",
                    safeNumber, billId);
                DBUtils.Execute(ctx, sql);
            }
        }

        #endregion
    }
}
