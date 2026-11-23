using System.Collections.Generic;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 调拨单(STK_TransferDirect)保存请求模型
    /// 由调拨申请单(STK_TransferApply)审核后推送给WMS，
    /// WMS原样回推调用K3Cloud保存接口生成调拨单。
    /// 报文结构完全遵循金蝶云星空 STK_TransferDirect Save 接口格式。
    /// </summary>
    public class TransferDirectSaveRequestModel
    {
        public string FormId { get; set; } = "STK_TransferDirect";
        public TransferDirectSaveDataModel data { get; set; } = new TransferDirectSaveDataModel();
    }

    public class TransferDirectSaveDataModel
    {
        public string[] NeedUpDateFields { get; set; } = new string[] { };
        public string[] NeedReturnFields { get; set; } = new string[] { };
        public string IsDeleteEntry { get; set; } = "true";
        public string SubSystemId { get; set; } = "";
        public string IsVerifyBaseDataField { get; set; } = "false";
        public string IsEntryBatchFill { get; set; } = "true";
        public string ValidateFlag { get; set; } = "true";
        public string NumberSearch { get; set; } = "true";
        public string IsAutoAdjustField { get; set; } = "true";
        public string InterationFlags { get; set; } = "";
        public string IgnoreInterationFlag { get; set; } = "";
        public string IsControlPrecision { get; set; } = "false";
        public string ValidateRepeatJson { get; set; } = "true";
        public TransferDirectHeaderModel Model { get; set; } = new TransferDirectHeaderModel();
    }

    // 调拨单表头
    public class TransferDirectHeaderModel
    {
        public long FID { get; set; } = 0;                                           // 0=新增
        public BaseDataModel FBillTypeID { get; set; } = new BaseDataModel();        // 单据类型（FNUMBER=ZJDB01_SYS）
        public string FBizType { get; set; } = "NORMAL";                             // 业务类型
        public string FTransferDirect { get; set; } = "GENERAL";                     // 调拨方向
        public string FTransferBizType { get; set; } = "OverOrgTransfer";            // 调拨业务类型
        public BaseDataModel FStockOutOrgId { get; set; } = new BaseDataModel();     // 调出库存组织
        public string FOwnerTypeOutIdHead { get; set; } = "BD_OwnerOrg";              // 调出表头货主类型
        public BaseDataModel FOwnerOutIdHead { get; set; } = new BaseDataModel();    // 调出表头货主
        public BaseDataModel FStockOrgId { get; set; } = new BaseDataModel();        // 调入库存组织
        public bool FIsIncludedTax { get; set; } = true;                             // 价税合计
        public BaseDataModel FExchangeTypeId { get; set; } = new BaseDataModel();    // 汇率类型
        public bool FIsPriceExcludeTax { get; set; } = true;                         // 单价不含税
        public BaseDataModel FSETTLECURRID { get; set; } = new BaseDataModel();       // 结算币别
        public string FOwnerTypeIdHead { get; set; } = "BD_OwnerOrg";                // 表头货主类型
        public decimal FExchangeRate { get; set; } = 1.0m;                           // 汇率
        public BaseDataModel FOwnerIdHead { get; set; } = new BaseDataModel();        // 表头货主
        public string FDate { get; set; }                                            // 单据日期
        public BaseDataModel FCustID { get; set; } = new BaseDataModel();            // 客户
        public BaseDataModel FSUPPLIERID { get; set; } = new BaseDataModel();        // 供应商
        public bool FWriteOffConsign { get; set; } = false;                          // 核销受托
        public TransferDirectCreatorIdModel F_hll_CreatorId { get; set; } = new TransferDirectCreatorIdModel(); // 创建人
        public int F_hll_PrintTimes { get; set; } = 0;                               // 打印次数
        public List<TransferDirectEntryModel> FBillEntry { get; set; } = new List<TransferDirectEntryModel>();
    }

    // 调拨单创建人子实体
    public class TransferDirectCreatorIdModel
    {
        public long FUserID { get; set; } = 0;
    }

    // 调拨单分录 (FBillEntry)
    public class TransferDirectEntryModel
    {
        public string FRowType { get; set; } = "Standard";                           // 行类型
        public BaseDataModel FMaterialId { get; set; } = new BaseDataModel();         // 物料
        public BaseDataModel FUnitID { get; set; } = new BaseDataModel();            // 单位
        public decimal FQty { get; set; }                                            // 数量
        public BaseDataModel FLot { get; set; } = new BaseDataModel();               // 批号
        public BaseDataModel FSrcStockId { get; set; } = new BaseDataModel();         // 调出仓库
        // 调出货位：可能多层嵌套 FSRCSTOCKLOCID__FF100004
        public Dictionary<string, BaseDataModel> FSrcStockLocId { get; set; } = new Dictionary<string, BaseDataModel>();
        public BaseDataModel FDestStockId { get; set; } = new BaseDataModel();        // 调入仓库
        public BaseDataModel FSrcStockStatusId { get; set; } = new BaseDataModel();  // 调出库存状态
        public BaseDataModel FDestStockStatusId { get; set; } = new BaseDataModel(); // 调入库存状态
        public string FMtoNo { get; set; }                                           // 计划跟踪号
        public string FBusinessDate { get; set; }                                    // 业务日期
        public string FSrcBillTypeId { get; set; } = "STK_TRANSFERAPPLY";            // 源单类型：调拨申请单
        public string FOwnerTypeOutId { get; set; } = "BD_OwnerOrg";                  // 调出货主类型
        public BaseDataModel FOwnerOutId { get; set; } = new BaseDataModel();         // 调出货主
        public string FOwnerTypeId { get; set; } = "BD_OwnerOrg";                     // 货主类型
        public BaseDataModel FOwnerId { get; set; } = new BaseDataModel();            // 货主
        public string FSrcBillNo { get; set; }                                        // 源单编号
        public decimal FSecQty { get; set; } = 0.0m;                                  // 辅助数量
        public decimal FExtAuxUnitQty { get; set; } = 0.0m;                           // 扩展辅助单位数量
        public BaseDataModel FBaseUnitId { get; set; } = new BaseDataModel();         // 基本单位
        public decimal FBaseQty { get; set; }                                         // 基本数量
        public bool FISFREE { get; set; } = false;                                    // 是否赠品
        public string FKeeperTypeId { get; set; } = "BD_KeeperOrg";                   // 保管者类型
        public decimal FActQty { get; set; } = 0.0m;                                  // 实际数量
        public BaseDataModel FKeeperId { get; set; } = new BaseDataModel();           // 保管者
        public string FKeeperTypeOutId { get; set; } = "BD_KeeperOrg";                // 调出保管者类型
        public BaseDataModel FKeeperOutId { get; set; } = new BaseDataModel();        // 调出保管者
        public decimal FDiscountRate { get; set; } = 0.0m;                            // 折扣率
        public decimal FRepairQty { get; set; } = 0.0m;                               // 返修数量
        public BaseDataModel FDestLot { get; set; } = new BaseDataModel();            // 调入批号
        public BaseDataModel FDestMaterialId { get; set; } = new BaseDataModel();     // 调入物料
        public BaseDataModel FSaleUnitId { get; set; } = new BaseDataModel();         // 销售单位
        public decimal FSaleQty { get; set; }                                         // 销售数量
        public decimal FSalBaseQty { get; set; }                                      // 销售基本数量
        public BaseDataModel FPriceUnitID { get; set; } = new BaseDataModel();        // 计价单位
        public decimal FPriceQty { get; set; }                                       // 计价数量
        public decimal FPriceBaseQty { get; set; }                                    // 计价基本数量
        public decimal FOutJoinQty { get; set; } = 0.0m;                              // 出库关联数量
        public decimal FBASEOUTJOINQTY { get; set; } = 0.0m;                          // 基本出库关联数量
        public long FSOEntryId { get; set; } = 0;                                     // 销售订单分录内码
        public bool FTransReserveLink { get; set; } = false;                          // 预留关联
        public long FQmEntryId { get; set; } = 0;                                     // 质量检验分录内码
        public long FConvertEntryId { get; set; } = 0;                                // 转换分录内码
        public bool FCheckDelivery { get; set; } = false;                             // 检查发货
        public long F_BHD_AppFinancID { get; set; } = 0;                              // BHD申请财务内码
        public long F_BHD_AppFinancEnID { get; set; } = 0;                            // BHD申请财务分录内码
        public long FBomEntryId { get; set; } = 0;                                    // BOM分录内码
        public List<TransferDirectEntryLinkModel> FBillEntry_Link { get; set; } = new List<TransferDirectEntryLinkModel>(); // 分录关联
    }

    // 调拨单分录关联表 (FBillEntry_Link)
    public class TransferDirectEntryLinkModel
    {
        public string FBillEntry_Link_FRuleId { get; set; }                           // 转换规则ID
        public long FBillEntry_Link_FSTableId { get; set; }                           // 源表内码
        public string FBillEntry_Link_FSTableName { get; set; }                       // 源表名
        public long FBillEntry_Link_FSBillId { get; set; }                            // 源单内码
        public long FBillEntry_Link_FSId { get; set; }                                // 源分录内码
        public decimal FBillEntry_Link_FBaseQtyOld { get; set; }                      // 原基本数量
        public decimal FBillEntry_Link_FBaseQty { get; set; }                         // 基本数量
        public decimal FBillEntry_Link_FSalBaseQtyOld { get; set; }                   // 原销售基本数量
        public decimal FBillEntry_Link_FSalBaseQty { get; set; }                      // 销售基本数量
        public decimal FBillEntry_Link_FPriceBaseQtyOld { get; set; }                 // 原计价基本数量
        public decimal FBillEntry_Link_FPriceBaseQty { get; set; }                    // 计价基本数量
    }
}
