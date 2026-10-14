using System.Collections.Generic;

namespace Ben.HL.Bill.BusinessPlugIn
{
    // API 根请求包
    public class WmsSaveReceiveBillRequestModel
    {
        public string FormId { get; set; } = "PUR_ReceiveBill";
        public WmsSaveDataModel data { get; set; } = new WmsSaveDataModel();
    }

    public class WmsSaveDataModel
    {
        public string[] NeedUpDateFields { get; set; } = new string[] { };
        public string[] NeedReturnFields { get; set; } = new string[] { };
        public string IsDeleteEntry { get; set; } = "true";
        public string SubSystemId { get; set; } = "";
        public string IsVerifyBaseDataField { get; set; } = "false";
        public string IsEntryBatchFill { get; set; } = "true";
        public string ValidateFlag { get; set; } = "true";
        public string NumberSearch { get; set; } = "true";
        public string IsAutoAdjustField { get; set; } = "false";
        public string InterationFlags { get; set; } = "";
        public string IgnoreInterationFlag { get; set; } = "";
        public string IsControlPrecision { get; set; } = "false";
        public string ValidateRepeatJson { get; set; } = "false";
        public ReceiveBillHeaderModel Model { get; set; } = new ReceiveBillHeaderModel();
    }

    // 表头模型
    public class ReceiveBillHeaderModel
    {
        public long FID { get; set; }
        public string FBillNo { get; set; }
        public string F_HL_SRM_Source_NO { get; set; }
        public BaseDataModel FBillTypeID { get; set; } = new BaseDataModel();
        public string FBusinessType { get; set; }
        public string FDate { get; set; }
        public BaseDataModel FStockOrgId { get; set; } = new BaseDataModel();
        public BaseDataModel FPurOrgId { get; set; } = new BaseDataModel();
        public BaseDataModel FSupplierId { get; set; } = new BaseDataModel();
        public BaseDataModel FSupplyId { get; set; } = new BaseDataModel();
        public string FOwnerTypeIdHead { get; set; }
        public BaseDataModel FOwnerIdHead { get; set; } = new BaseDataModel();
        public string F_PVCE_Combo { get; set; }
        public string FNote { get; set; }
        public FinanceEntityModel FinanceEntity { get; set; } = new FinanceEntityModel();
        public List<ReceiveBillEntryModel> FDetailEntity { get; set; } = new List<ReceiveBillEntryModel>();
    }

    public class FinanceEntityModel
    {
        public int FEntryId { get; set; } = 0;
        public BaseDataModel FSettleOrgId { get; set; } = new BaseDataModel();
        public BaseDataModel FSettleCurrId { get; set; } = new BaseDataModel();
        public string FPricePoint { get; set; }
    }

    // 明细表模型
    public class ReceiveBillEntryModel
    {
        public int F_PVCE_HL_SRMSFEQ { get; set; }
        public int FEntryID { get; set; }
        public BaseDataModel FMaterialId { get; set; } = new BaseDataModel();
        public BaseDataModel FUnitId { get; set; } = new BaseDataModel();
        public BaseDataModel FPriceUnitId { get; set; } = new BaseDataModel();
        public BaseDataModel FStockUnitID { get; set; } = new BaseDataModel();
        public decimal FStockQty { get; set; }
        public decimal FInStockQty { get; set; }
        public decimal FActlandQty { get; set; }
        public decimal FActReceiveQty { get; set; }
        public decimal FPriceUnitQty { get; set; }
        public string FSrcBillNo { get; set; }
        public string FSrcFormId { get; set; } = "PUR_PurchaseOrder";
        public decimal FTAXPRICE { get; set; }
        public decimal FEntryTaxRate { get; set; }
        public Dictionary<string, BaseDataModel> FAuxPropId { get; set; } = new Dictionary<string, BaseDataModel>();
        public string FMtoNo { get; set; }
        public BaseDataModel FSTOCKID { get; set; } = new BaseDataModel();
        public string FDescription { get; set; }
        public List<DetailEntityLinkModel> FDetailEntity_Link { get; set; } = new List<DetailEntityLinkModel>();
    }

    // 关联子表模型
    public class DetailEntityLinkModel
    {
        public string FDetailEntity_Link_FFlowId { get; set; } = "";
        public string FDetailEntity_Link_FFlowLineId { get; set; } = "";
        public string FDetailEntity_Link_FRuleId { get; set; } = "PUR_PurchaseOrder-PUR_ReceiveBill";
        public string FDetailEntity_Link_FSTableName { get; set; } = "t_PUR_POOrderEntry";
        public long FDetailEntity_Link_FSBillId { get; set; }
        public long FDetailEntity_Link_FSId { get; set; }
        public string FDetailEntity_Link_FBaseUnitQtyOld { get; set; } = "0";
        public string FDetailEntity_Link_FBaseUnitQty { get; set; } = "0";
    }

    // 基础资料通用模型
    public class BaseDataModel
    {
        public string FNumber { get; set; } = "";
    }
}