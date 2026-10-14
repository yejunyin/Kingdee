using System.Collections.Generic;

namespace Ben.HL.Bill.BusinessPlugIn
{
    // API 根请求包
    public class PrdInStockSaveRequestModel
    {
        public string FormId { get; set; } = "PRD_INSTOCK";
        public PrdInStockSaveDataModel data { get; set; } = new PrdInStockSaveDataModel();
    }

    public class PrdInStockSaveDataModel
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
        public PrdInStockHeaderModel Model { get; set; } = new PrdInStockHeaderModel();
    }

    // 表头模型
    public class PrdInStockHeaderModel
    {
        public long FID { get; set; }
        public string FBillNo { get; set; }
        public BaseDataModel FBillTypeID { get; set; } = new BaseDataModel();
        public string FDate { get; set; }
        public BaseDataModel FPrdOrgId { get; set; } = new BaseDataModel();      // 生产组织
        public BaseDataModel FStockOrgId { get; set; } = new BaseDataModel();    // 入库组织
        public BaseDataModel FWorkShopId { get; set; } = new BaseDataModel();    // 生产车间
        public string FOwnerTypeIdHead { get; set; }
        public BaseDataModel FOwnerIdHead { get; set; } = new BaseDataModel();
        public string FDescription { get; set; }
        public List<PrdInStockEntryModel> FEntity { get; set; } = new List<PrdInStockEntryModel>();
    }

    // 明细表模型 (FEntity)
    public class PrdInStockEntryModel
    {
        public int FEntryID { get; set; }
        public int FSeq { get; set; }
        public BaseDataModel FMaterialId { get; set; } = new BaseDataModel();
        public BaseDataModel FUnitId { get; set; } = new BaseDataModel();
        public BaseDataModel FBaseUnitId { get; set; } = new BaseDataModel();
        public decimal FRealQty { get; set; }       // 实收数量
        public decimal FBaseRealQty { get; set; }   // 计基本实收数量
        public BaseDataModel FStockId { get; set; } = new BaseDataModel();
        public BaseDataModel FStockLocId { get; set; } = new BaseDataModel();
        public BaseDataModel FStockStatusId { get; set; } = new BaseDataModel();
        public string FBatchNo { get; set; }
        public string FMoBillNo { get; set; }      // 生产订单号
        public int FMOSEQ { get; set; }            // 生产订单行号
        public string FSrcBillNo { get; set; }
        public string FSrcFormId { get; set; } = "PRD_PrdOrder";
        public Dictionary<string, BaseDataModel> FAuxPropId { get; set; } = new Dictionary<string, BaseDataModel>();
        public List<PrdInStockEntryLinkModel> FEntity_Link { get; set; } = new List<PrdInStockEntryLinkModel>();
    }

    // 关联子表模型
    public class PrdInStockEntryLinkModel
    {
        public string FEntity_Link_FFlowId { get; set; } = "";
        public string FEntity_Link_FFlowLineId { get; set; } = "";
        public string FEntity_Link_FRuleId { get; set; } = "PRD_PrdOrder-PRD_InStock";
        public string FEntity_Link_FSTableName { get; set; } = "t_PRD_PrdOrderEntry";
        public long FEntity_Link_FSBillId { get; set; }
        public long FEntity_Link_FSId { get; set; }
        public string FEntity_Link_FBaseRealQtyOld { get; set; } = "0";
        public string FEntity_Link_FBaseRealQty { get; set; } = "0";
    }
}