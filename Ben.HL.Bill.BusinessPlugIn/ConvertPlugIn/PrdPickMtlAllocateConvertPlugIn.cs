using System;
using System.Collections.Generic;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.App.Data;                       // DBUtils
using Kingdee.BOS.Core;                           // ExtendedDataEntity / ExtendedDataEntitySet
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Core.Metadata.EntityElement;    // Entity
using Kingdee.BOS.Core.Metadata.ConvertElement.PlugIn;      // AbstractConvertPlugIn
using Kingdee.BOS.Core.Metadata.ConvertElement.PlugIn.Args; // AfterConvertEventArgs
using Kingdee.BOS.Orm.DataEntity;                 // DynamicObject / DynamicObjectCollection
using Kingdee.BOS.ServiceHelper;
using System.ComponentModel;
using Kingdee.BOS.Util;                  // BusinessDataServiceHelper / MetaDataServiceHelper

namespace Ben.HL.Bill.BusinessPlugIn.ConvertPlugIn
{
    /// <summary>
    /// 生产投料单(PRD_MOENTRY) -> 生产领料单(PRD_PICKMTL) 下推【转换插件】。
    ///
    /// 分配策略（满足徐哥示例）：
    ///   主键 = 仓库拣货优先级（BD_Stock 自定义字段，值越小越优先）
    ///   次键 = 批号优先级（BD_BatchMainFile 自定义字段）/ 无则按批号创建日期 FIFO
    ///   规则 = 仓库优先级高者先出；同级再按批号优先级/先到先出；
    ///          某仓库库存不足时，自动从下一优先级的仓库补足，并拆出新的【源单关联行】
    ///          （新增行的源单关联子单据体按分配量拆分复制，源投料单的已下推/未下推数量可正确核销）。
    ///
    /// 示例：A 需求 100，B1 有 50（优先级 1），B2 有 200（优先级 2，且 B2 批号更优）。
    ///   因为仓库优先级主导，仍先出 B1 的 50；不足部分再从 B2 出 50。
    ///
    /// 同一张目标单内若有多行相同物料，库存按行依次核销（前一行已占用的库存不会重复分给后一行）。
    ///
    /// ⚠️ 注意：代码中的字段/实体标识为通用命名，部署前必须对照你环境 BOS 实际元数据确认（见文件顶部常量）。
    /// </summary>
    [HotUpdate]
    [Description("生产投料单(PRD_MOENTRY) -> 生产领料单(PRD_PICKMTL) 下推【转换插件】")]
    public class PrdPickMtlAllocateConvertPlugIn : AbstractConvertPlugIn
    {
        // ===================== 部署前必须按你环境 BOS 核对的常量 =====================
        // 仓库拣货优先级：使用 T_BD_STOCK 标准字段 FSORTINGPRIORITY（排序优先级，值越小越优先），无需自建字段
        private const string STOCK_PICK_PRIORITY = "FSORTINGPRIORITY";
        private const string BATCH_PRIORITY = "F_BHD_BatchPriority";        // 批号优先级(int，需在批号主档自建)；不用可忽略
        private const string LOT_CREATE_DATE = "FCREATEDATE";               // T_BD_LOTMASTER 标准字段“创建日期”，FIFO 兜底
        // 仓库拣货优先级总开关：true = 按 T_BD_STOCK.FSORTINGPRIORITY 升序选库（标准字段，已实测存在）；
        //                       false = 所有仓库同级，退化为按批号 FIFO/库存顺序自动选库
        private const bool USE_STOCK_PRIORITY = true;
        // 批号优先级总开关：批号主档物理表 T_BD_LOTMASTER 及标准列 FCREATEDATE 始终存在（经本环境二开包核实）；
        //                  只有上面的“批号优先级”自定义字段需要自建。没建就保持 false，按批号创建日期 FIFO
        private const bool USE_BATCH_PRIORITY = false;
        // ---- 以下标识已经按你环境断点实测的 PRD_PICKMTL DynamicObject 属性名核对 ----
        private const string PICK_HEAD_ENTRY = "FBillHead";                 // 表头实体【元数据Key】（FindByEntityKey/GetEntity 用）
        private const string PICK_ENTRY = "FEntity";                        // 明细实体【元数据Key】；同时也是表头对象上的分录集合属性名（断点实测）
        private const string PICK_LINK_ENTRY = "FEntity_Link";              // 明细上的“源单关联”子单据体属性名（断点 [126] 实测）
        private const string SERIAL_ENTRY = "PRD_PickMtrlSerial";           // 序列号子单据体（拆行时不可共享，断点 [125]）
        private const string LINK_ID = "Id";                                // 源单关联行主键（断点 [0]）
        private const string LINK_BASE_QTY = "BaseActualQty";               // 源单关联-本次下推基本数量（断点 [9]）
        private const string LINK_QTY = "ActualQty";                        // 源单关联-本次下推数量（普通单位）
        private const string FLD_MATERIAL = "MaterialId";                   // 物料（断点 [3]）
        private const string FLD_STOCK = "StockId";                         // 仓库（断点 [7]）
        private const string FLD_LOT = "Lot";                               // 批号（断点 [69]）
        private const string FLD_LOT_TEXT = "Lot_Text";                     // 批号文本（断点 [70]，换批号须清空）
        private const string FLD_STOCK_ORG = "StockOrgId";                  // 表头库存组织
        private const string FLD_NEED_QTY = "BaseAppQty";                   // 申请基本数量=需求量（断点 [31]）
        private const string FLD_BASE_PLAN = "BaseAppQty";                  // 基本单位申请数量
        private const string FLD_BASE_ACTUAL = "BaseActualQty";             // 基本单位实领数量（断点 [51]）
        private const string FLD_PLAN = "AppQty";                           // 申请数量（断点 [22]）
        private const string FLD_ACTUAL = "ActualQty";                      // 实领数量（断点 [23]）
        private const string FLD_STOCK_PLAN = "StockAppQty";                // 库存单位申请数量（断点 [24]，随拆行按比例折算）
        private const string FLD_STOCK_ACTUAL = "StockActualQty";           // 库存单位实领数量（断点 [25]）
        private const string FLD_SEC_ACTUAL = "SecActualQty";               // 辅助单位实领数量（断点 [26]）
        private const string FLD_ENTRY_ID = "Id";                           // 明细分录主键（断点 [0]）
        private const string STOCK_FORMID = "BD_Stock";
        private const string BATCH_FORMID = "BD_BatchMainFile";
        // ⚠️ 可用量口径：你环境实测 FBaseAvbQty 不可信（恒为 0），默认用 基本数量 - 锁定基本数量。
        //    若你环境 FBaseAvbQty 可信，将下方改为 true 可直接用 FBaseAvbQty。
        private const bool USE_DIRECT_AVAILABLE = false;
        // ⚠️ 库存状态过滤：下列为空表示不对库存状态做过滤（会把待检/不合格等也计入）。
        //    如只需“可用”库存，把可用状态的 FStockStatusId 填到下方（如 "10000"），会自动加过滤。
        private const string AVAILABLE_STOCK_STATUS_ID = "";
        // ===========================================================================

        public override void AfterConvert(AfterConvertEventArgs e)
        {
            base.AfterConvert(e);
            if (e.Result == null || e.TargetBusinessInfo == null) return;

            var entryEntity = e.TargetBusinessInfo.GetEntity(PICK_ENTRY);
            if (entryEntity == null) return;

            // 本版本 SDK 结果集无 DynamicObjects 属性，用 FindByEntityKey 取表头实体
            ExtendedDataEntity[] heads = e.Result.FindByEntityKey(PICK_HEAD_ENTRY);
            if (heads == null || heads.Length == 0) return;

            // 元数据整张单只加载一次，避免每行/每个分配都去 GetFormMetaData（原实现为 N+1 调用）
            var engine = new AllocationEngine(this.Context);

            foreach (ExtendedDataEntity head in heads)
            {
                var bill = head.DataEntity;
                if (bill == null) continue;
                var entry = GetCollection(bill, PICK_ENTRY, "Entity");
                if (entry == null || entry.Count == 0) continue;
                engine.AllocateBill(bill, entry, entryEntity);
            }
        }

        /// <summary>
        /// 取子单据体集合：标准字段在 DynamicObject 上属性名通常去 F 前缀，
        /// 但集合实体（FEntity/FEntity_Link）实测保留 F，故按候选名依次尝试。
        /// </summary>
        private static DynamicObjectCollection GetCollection(DynamicObject obj, params string[] names)
        {
            if (obj == null) return null;
            foreach (var name in names)
            {
                if (obj.DynamicObjectType.Properties.Contains(name))
                    return obj[name] as DynamicObjectCollection;
            }
            return null;
        }

        /// <summary>
        /// 单张目标单的分配执行器：缓存库存查询结果与基础资料，保证同单多行同物料时库存顺序核销。
        /// </summary>
        private class AllocationEngine
        {
            private readonly Context _ctx;
            private readonly FormMetadata _stockMeta;
            private readonly FormMetadata _batchMeta;

            // key = 库存组织ID:物料ID，value = 可继续核销的库存（RemainQty 随分配递减）
            private readonly Dictionary<string, List<StockLot>> _inventoryCache =
                new Dictionary<string, List<StockLot>>();

            public AllocationEngine(Context ctx)
            {
                _ctx = ctx;
                _stockMeta = MetaDataServiceHelper.GetFormMetaData(ctx, STOCK_FORMID);
                _batchMeta = MetaDataServiceHelper.GetFormMetaData(ctx, BATCH_FORMID);
            }

            public void AllocateBill(DynamicObject bill, DynamicObjectCollection entry, Entity entryEntity)
            {
                long stockOrgId = GetStockOrgId(bill);

                // 快照源行，避免遍历中因追加新行而重复处理
                var srcRows = entry.Cast<DynamicObject>().ToList();

                // 先算出每一行的分配结果，再统一批量加载基础资料、统一写回
                var plans = new List<RowPlan>();
                var allStockIds = new HashSet<long>();
                var allLotIds = new HashSet<long>();

                foreach (var srcRow in srcRows)
                {
                    var material = srcRow[FLD_MATERIAL] as DynamicObject;
                    if (material == null) continue;
                    long materialId = Convert.ToInt64(material["Id"]);
                    decimal need = ToDecimal(srcRow[FLD_NEED_QTY]);
                    if (need <= 0) continue;

                    var stocks = GetMaterialInventory(materialId, stockOrgId);
                    var allocs = Allocate(need, stocks);
                    if (allocs.Count == 0) continue; // 无可用库存：保留原行，转手工处理

                    foreach (var a in allocs)
                    {
                        allStockIds.Add(a.StockId);
                        if (a.LotId.HasValue) allLotIds.Add(a.LotId.Value);
                    }

                    plans.Add(new RowPlan
                    {
                        SrcRow = srcRow,
                        NeedQty = need,
                        // 必须在改写前快照：多单位数量 + 源单关联数量，否则首行收缩后拆行会重复乘比例
                        QtySnaps = SnapshotQtyFields(srcRow),
                        LinkSnaps = SnapshotLinks(srcRow),
                        Allocations = allocs
                    });
                }

                if (plans.Count == 0) return;

                // 仓库/批号基础资料整单批量加载一次，替代逐行 Load
                var stockMap = LoadBaseDataMap(_stockMeta, allStockIds);
                var lotMap = LoadBaseDataMap(_batchMeta, allLotIds);

                foreach (var plan in plans)
                {
                    ApplyPlan(plan, entry, entryEntity, stockMap, lotMap);
                }
            }

            /// <summary>
            /// 获取某物料的可用库存（按组织+物料缓存）；同单多行同物料共用一份并顺序核销。
            /// </summary>
            private List<StockLot> GetMaterialInventory(long materialId, long stockOrgId)
            {
                string key = stockOrgId + ":" + materialId;
                List<StockLot> stocks;
                if (!_inventoryCache.TryGetValue(key, out stocks))
                {
                    stocks = QueryInventory(materialId, stockOrgId);
                    _inventoryCache[key] = stocks;
                }
                return stocks;
            }

            private void ApplyPlan(RowPlan plan, DynamicObjectCollection entry, Entity entryEntity,
                Dictionary<long, DynamicObject> stockMap, Dictionary<long, DynamicObject> lotMap)
            {
                bool first = true;
                foreach (var a in plan.Allocations)
                {
                    decimal scale = plan.NeedQty > 0 ? a.Qty / plan.NeedQty : 0m;
                    DynamicObject row;
                    if (first)
                    {
                        row = plan.SrcRow;        // 第一行复用原行
                        first = false;
                    }
                    else
                    {
                        row = new DynamicObject(entryEntity.DynamicObjectType);
                        CopyRow(plan.SrcRow, row, entryEntity);
                        entry.Add(row);           // 不足部分新增一行
                    }

                    // 源单关联：按本次分配比例拆分，保证源投料单已下推/未下推数量按实际分配量核销
                    ApplyLink(row, plan.LinkSnaps, scale, !first);

                    DynamicObject stockObj;
                    if (stockMap.TryGetValue(a.StockId, out stockObj))
                        row[FLD_STOCK] = stockObj;

                    if (a.LotId.HasValue)
                    {
                        DynamicObject lotObj;
                        if (lotMap.TryGetValue(a.LotId.Value, out lotObj))
                            row[FLD_LOT] = lotObj;
                    }
                    else
                    {
                        row[FLD_LOT] = null;
                    }
                    // 批号文本是冗余展示字段，换批号必须同步清空，否则界面仍显示旧批号
                    if (row.DynamicObjectType.Properties.Contains(FLD_LOT_TEXT))
                        row[FLD_LOT_TEXT] = "";

                    // 基本单位数量：按分配结果精确写入（申请=实领），不用乘比例，避免各行舍入后合计偏差
                    row[FLD_BASE_PLAN] = a.Qty;
                    row[FLD_BASE_ACTUAL] = a.Qty;

                    // 普通单位/库存单位/辅助单位数量：按快照值 × 比例 折算（各单位间比例一致，无需单位换算）
                    ApplyScaledQty(row, plan.QtySnaps, scale, FLD_PLAN);
                    ApplyScaledQty(row, plan.QtySnaps, scale, FLD_ACTUAL);
                    ApplyScaledQty(row, plan.QtySnaps, scale, FLD_STOCK_PLAN);
                    ApplyScaledQty(row, plan.QtySnaps, scale, FLD_STOCK_ACTUAL);
                    ApplyScaledQty(row, plan.QtySnaps, scale, FLD_SEC_ACTUAL);
                }
            }

            /// <summary>
            /// 查询某物料在所有仓库/批号上的可用量，并按 仓库优先级 -> 批号优先级 -> 批号日期 排序。
            /// </summary>
            private List<StockLot> QueryInventory(long materialId, long stockOrgId)
            {
                string availExpr = USE_DIRECT_AVAILABLE
                    ? "ISNULL(inv.FBASEAVBQTY,0)"
                    : "(ISNULL(inv.FBASEQTY,0) - ISNULL(inv.FBASELOCKQTY,0))";

                string statusFilter = string.IsNullOrWhiteSpace(AVAILABLE_STOCK_STATUS_ID)
                    ? ""
                    : string.Format(" AND inv.FSTOCKSTATUSID = {0} ", AVAILABLE_STOCK_STATUS_ID);

                string orgFilter = stockOrgId > 0
                    ? string.Format(" AND inv.FSTOCKORGID = {0} ", stockOrgId)
                    : "";

                // 按开关决定是否引用自定义列：字段未建时不 JOIN/不引用，避免“列名无效”直接报错
                // ⚠️ DBUtils 执行原生 SQL，必须用 T_ 前缀【物理表名】，不能用 BD_Stock/STK_Inventory 等 ORM 逻辑名
                string stockJoin = USE_STOCK_PRIORITY
                    ? "LEFT JOIN T_BD_STOCK s ON s.FSTOCKID = inv.FSTOCKID"
                    : "";
                string pickExpr = USE_STOCK_PRIORITY
                    ? "ISNULL(s." + STOCK_PICK_PRIORITY + ", 9999)"
                    : "0";
                string batchPrioExpr = USE_BATCH_PRIORITY
                    ? "ISNULL(b." + BATCH_PRIORITY + ", 9999)"
                    : "9999";

                // 物料口径：单据上的 materialId 是 T_BD_MATERIAL.FMATERIALID（组织内物料内码），
                // 而 T_STK_INVENTORY.FMATERIALID 记的是物料主档 FMASTERID；
                // 必须 INNER JOIN T_BD_MATERIAL 并以 m.FMATERIALID 定位，同时用 m.FUSEORGID = inv.FSTOCKORGID 区分使用组织
                // （一个主档物料分配到多个组织时，各自库存独立）。
                // 批号主档物理表为 T_BD_LOTMASTER（主键 FLOTID，库存表 inv.FLOT 关联；FCREATEDATE 做 FIFO）。
                string sql = string.Format(@"
                    SELECT inv.FSTOCKID         AS FStockId,
                            inv.FLOT             AS FLot,
                            {0}                  AS FAvailQty,
                            {1}                  AS FPickPriority,
                            {2}                  AS FBatchPriority,
                            b.{3}                AS FBatchCreateDate
                    FROM T_STK_INVENTORY inv
                    INNER JOIN T_BD_MATERIAL m  ON m.FMASTERID = inv.FMATERIALID
                                               AND m.FUSEORGID = inv.FSTOCKORGID
                    {4}
                    LEFT JOIN T_BD_LOTMASTER b ON b.FLOTID = inv.FLOT
                    WHERE m.FMATERIALID = {5}
                        AND ISNULL(inv.FBASEQTY,0) > 0
                        {6}
                        {7}
                    -- 必须按 SELECT 输出别名排序：直接写整数常量 0/9999 会被 SQL Server 解释成“选择列表列序号”而报错
                    ORDER BY FPickPriority ASC, FBatchPriority ASC, FBatchCreateDate ASC",
                    availExpr,
                    pickExpr,
                    batchPrioExpr,
                    LOT_CREATE_DATE,
                    stockJoin,
                    materialId,
                    statusFilter,
                    orgFilter);

                // 本环境 DBUtils.Execute(Context,sql) 返回受影响行数(int)，取数须用 ExecuteDynamicObject
                DynamicObjectCollection rows = DBUtils.ExecuteDynamicObject(_ctx, sql);

                var list = new List<StockLot>();
                foreach (DynamicObject r in rows)
                {
                    list.Add(new StockLot
                    {
                        StockId = ToLong(r["FStockId"]),
                        LotId = ToNullableLong(r["FLot"]),
                        RemainQty = ToDecimal(r["FAvailQty"]),
                        PickPriority = ToInt(r["FPickPriority"], 9999),
                        BatchPriority = ToInt(r["FBatchPriority"], 9999),
                        BatchCreateDate = ToNullableDateTime(r["FBatchCreateDate"])
                    });
                }
                return list;
            }

            /// <summary>
            /// 按优先级顺序对需求做贪心分配，并直接核销各库存批次的剩余量（同单后续行可见）。
            /// </summary>
            private List<Allocation> Allocate(decimal need, List<StockLot> stocks)
            {
                var result = new List<Allocation>();
                decimal remain = need;

                var ordered = stocks
                    .Where(s => s.RemainQty > 0)
                    .OrderBy(s => s.PickPriority)                        // 仓库优先级：主
                    .ThenBy(s => s.BatchPriority)                        // 批号优先级：次
                    .ThenBy(s => s.BatchCreateDate ?? DateTime.MaxValue) // FIFO 兜底
                    .ToList();

                foreach (var s in ordered)
                {
                    if (remain <= 0) break;
                    decimal take = Math.Min(remain, s.RemainQty);
                    if (take <= 0) continue;
                    result.Add(new Allocation { StockId = s.StockId, LotId = s.LotId, Qty = take });
                    s.RemainQty -= take;
                    remain -= take;
                }
                return result;
            }

            // ----------------------- 多单位数量快照/折算 -----------------------

            // 需要随拆行按比例折算的非基本单位数量字段（基本单位数量按分配结果精确写入，不在此列）
            private static readonly string[] SCALED_QTY_FIELDS =
            {
                FLD_PLAN, FLD_ACTUAL, FLD_STOCK_PLAN, FLD_STOCK_ACTUAL, FLD_SEC_ACTUAL
            };

            /// <summary>在任何改写前快照源行各单位数量原值，供拆出的每一行按比例折算。</summary>
            private Dictionary<string, decimal> SnapshotQtyFields(DynamicObject row)
            {
                var snap = new Dictionary<string, decimal>();
                foreach (var fld in SCALED_QTY_FIELDS)
                {
                    if (row.DynamicObjectType.Properties.Contains(fld))
                        snap[fld] = ToDecimal(row[fld]);
                }
                return snap;
            }

            private static void ApplyScaledQty(DynamicObject row, Dictionary<string, decimal> snaps, decimal scale, string field)
            {
                decimal original;
                if (!snaps.TryGetValue(field, out original)) return;
                if (!row.DynamicObjectType.Properties.Contains(field)) return;
                row[field] = Math.Round(original * scale, 6);
            }

            // ----------------------- 源单关联拆分 -----------------------

            /// <summary>
            /// 在任何比例收缩之前，快照源行“源单关联”子单据体的原始数量，
            /// 避免首行先收缩、后续拆行又基于收缩值再次乘比例导致数量错误。
            /// </summary>
            private List<LinkSnap> SnapshotLinks(DynamicObject row)
            {
                var snaps = new List<LinkSnap>();
                var link = GetCollection(row, PICK_LINK_ENTRY);
                if (link == null) return snaps;

                foreach (DynamicObject lk in link)
                {
                    snaps.Add(new LinkSnap
                    {
                        Source = lk,
                        BaseQty = GetPropDecimal(lk, LINK_BASE_QTY),
                        Qty = GetPropDecimal(lk, LINK_QTY)
                    });
                }
                return snaps;
            }

            /// <summary>
            /// 把源单关联数量按本次分配比例写入目标行。
            /// 复用原行：直接在原有子行上收缩（保留 Id）；
            /// 新增行：基于快照复制新子行（Id 置 0 交由保存时新增）。
            /// 注意：BaseActualQtyOld（操作前已下推量）为历史快照，原样复制不参与拆分。
            /// </summary>
            private void ApplyLink(DynamicObject row, List<LinkSnap> snaps, decimal scale, bool isNewRow)
            {
                if (snaps.Count == 0) return;

                if (isNewRow)
                {
                    var dstLink = GetCollection(row, PICK_LINK_ENTRY);
                    if (dstLink == null) return;

                    foreach (var snap in snaps)
                    {
                        var newLk = new DynamicObject(snap.Source.DynamicObjectType);
                        foreach (var p in snap.Source.DynamicObjectType.Properties)
                        {
                            // 关联行上同样可能带只读展示字段（多语文本等），赋值即抛 OrmException，跳过
                            if (p.IsReadOnly) continue;
                            if (p.Name == LINK_ID) { newLk[p.Name] = 0; continue; }
                            if (p.Name == LINK_BASE_QTY) { newLk[p.Name] = Math.Round(snap.BaseQty * scale, 6); continue; }
                            if (p.Name == LINK_QTY) { newLk[p.Name] = Math.Round(snap.Qty * scale, 6); continue; }
                            newLk[p.Name] = snap.Source[p.Name];
                        }
                        dstLink.Add(newLk);
                    }
                }
                else
                {
                    var link = GetCollection(row, PICK_LINK_ENTRY);
                    if (link == null || link.Count != snaps.Count) return;

                    for (int i = 0; i < snaps.Count; i++)
                    {
                        var lk = link[i];
                        if (lk.DynamicObjectType.Properties.Contains(LINK_BASE_QTY))
                            lk[LINK_BASE_QTY] = Math.Round(snaps[i].BaseQty * scale, 6);
                        if (lk.DynamicObjectType.Properties.Contains(LINK_QTY))
                            lk[LINK_QTY] = Math.Round(snaps[i].Qty * scale, 6);
                    }
                }
            }

            // ----------------------- 基础资料批量加载 -----------------------

            /// <summary>
            /// 按主键集合批量加载基础资料，返回 Id -> DynamicObject 映射。
            /// </summary>
            private Dictionary<long, DynamicObject> LoadBaseDataMap(FormMetadata meta, HashSet<long> ids)
            {
                var map = new Dictionary<long, DynamicObject>();
                if (meta == null || ids == null || ids.Count == 0) return map;

                object[] pkIds = ids.Where(id => id > 0).Distinct().Cast<object>().ToArray();
                if (pkIds.Length == 0) return map;

                // 本版本 Load 只接受 DynamicObjectType（无 BusinessInfo 重载）
                var objs = BusinessDataServiceHelper.Load(_ctx, pkIds, meta.BusinessInfo.GetDynamicObjectType()) as DynamicObject[];
                if (objs == null) return map;

                foreach (var obj in objs)
                {
                    if (obj == null) continue;
                    map[Convert.ToInt64(obj["Id"])] = obj;
                }
                return map;
            }

            // ----------------------- 工具方法 -----------------------

            private static long GetStockOrgId(DynamicObject bill)
            {
                // 标准字段属性名去 F 前缀，个别补丁版本可能保留 F，两种都尝试
                var org = GetPropValue(bill, FLD_STOCK_ORG, "F" + FLD_STOCK_ORG) as DynamicObject;
                return org == null ? 0 : Convert.ToInt64(org["Id"]);
            }

            /// <summary>
            /// 复制源行除“仓库/批号/批号文本/数量/分录主键/子单据体”外的所有字段到新行（保留物料、单位等基础信息）。
            /// 源单关联由 ApplyLink 另行复制；序列号子单据体不可跨拆分行共享；数量字段随后按分配量精确重写。
            /// </summary>
            private static void CopyRow(DynamicObject src, DynamicObject dst, Entity entity)
            {
                foreach (var p in entity.DynamicObjectType.Properties)
                {
                    // 多语文本(MultiLanguageText，如物料名称等展示字段)等属性 IsReadOnly=true，
                    // 直接赋值会被 SDK 抛 OrmException“实体属性…被标记为只读”；这类字段保存时按基础资料自动重算，跳过即可
                    if (p.IsReadOnly) continue;
                    if (p.Name == FLD_ENTRY_ID ||
                        p.Name == FLD_STOCK ||
                        p.Name == FLD_LOT || p.Name == FLD_LOT_TEXT ||
                        p.Name == FLD_BASE_PLAN || p.Name == FLD_BASE_ACTUAL ||
                        p.Name == FLD_PLAN || p.Name == FLD_ACTUAL ||
                        p.Name == FLD_STOCK_PLAN || p.Name == FLD_STOCK_ACTUAL ||
                        p.Name == FLD_SEC_ACTUAL ||
                        p.Name == PICK_LINK_ENTRY || p.Name == SERIAL_ENTRY) continue;
                    dst[p.Name] = src[p.Name];
                }
            }

            /// <summary>按候选属性名顺序取第一个存在的属性值。</summary>
            private static object GetPropValue(DynamicObject obj, params string[] names)
            {
                if (obj == null) return null;
                foreach (var name in names)
                {
                    if (obj.DynamicObjectType.Properties.Contains(name)) return obj[name];
                }
                return null;
            }

            private static decimal GetPropDecimal(DynamicObject obj, string propName)
            {
                if (obj == null || !obj.DynamicObjectType.Properties.Contains(propName)) return 0m;
                return ToDecimal(obj[propName]);
            }

            private static decimal ToDecimal(object v)
            {
                if (v == null || v == DBNull.Value) return 0m;
                try { return Convert.ToDecimal(v); }
                catch { return 0m; }
            }

            private static long ToLong(object v)
            {
                if (v == null || v == DBNull.Value) return 0L;
                try { return Convert.ToInt64(v); }
                catch { return 0L; }
            }

            private static long? ToNullableLong(object v)
            {
                if (v == null || v == DBNull.Value) return null;
                try
                {
                    long val = Convert.ToInt64(v);
                    return val == 0 ? (long?)null : val;
                }
                catch { return null; }
            }

            private static int ToInt(object v, int defaultValue)
            {
                if (v == null || v == DBNull.Value) return defaultValue;
                try { return Convert.ToInt32(v); }
                catch { return defaultValue; }
            }

            private static DateTime? ToNullableDateTime(object v)
            {
                if (v == null || v == DBNull.Value) return null;
                try { return Convert.ToDateTime(v); }
                catch { return null; }
            }
        }

        // ----------------------- 内部数据结构 -----------------------
        private class StockLot
        {
            public long StockId;
            public long? LotId;
            public decimal RemainQty;                // 可继续核销的剩余量
            public int PickPriority;
            public int BatchPriority;
            public DateTime? BatchCreateDate;
        }

        private class Allocation
        {
            public long StockId;
            public long? LotId;
            public decimal Qty;
        }

        private class LinkSnap
        {
            public DynamicObject Source;
            public decimal BaseQty;
            public decimal Qty;
        }

        private class RowPlan
        {
            public DynamicObject SrcRow;
            public decimal NeedQty;
            public Dictionary<string, decimal> QtySnaps;
            public List<LinkSnap> LinkSnaps;
            public List<Allocation> Allocations;
        }
    }
}
