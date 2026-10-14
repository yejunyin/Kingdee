using System.Collections.Generic;

namespace Ben.HL.Bill.BusinessPlugIn
{
    // API 根请求包
    public class PrdReturnMtrlSaveRequestModel
    {
        public string FormId { get; set; } = "PRD_PPMRTBILL";
        public PrdReturnMtrlSaveDataModel data { get; set; } = new PrdReturnMtrlSaveDataModel();
    }

    public class PrdReturnMtrlSaveDataModel
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
        public PrdReturnMtrlHeaderModel Model { get; set; } = new PrdReturnMtrlHeaderModel();
    }

    // 表头模型
    public class PrdReturnMtrlHeaderModel
    {
        public long FID { get; set; }
        public string FBillNo { get; set; }
        public BaseDataModel FBillTypeID { get; set; } = new BaseDataModel();
        public string FDate { get; set; }
        public BaseDataModel FPrdOrgId { get; set; } = new BaseDataModel();
        public BaseDataModel FStockOrgId { get; set; } = new BaseDataModel();
        public BaseDataModel FWorkShopId { get; set; } = new BaseDataModel();
        public string FOwnerTypeIdHead { get; set; }
        public BaseDataModel FOwnerIdHead { get; set; } = new BaseDataModel();
        public string FDescription { get; set; }
        public List<PrdReturnMtrlEntryModel> FEntity { get; set; } = new List<PrdReturnMtrlEntryModel>();
    }

    // 明细表模型 (FEntity)
    public class PrdReturnMtrlEntryModel
    {
        public int FEntryID { get; set; }
        public int FSeq { get; set; }
        public BaseDataModel FMaterialId { get; set; } = new BaseDataModel();
        public BaseDataModel FUnitId { get; set; } = new BaseDataModel();
        public BaseDataModel FBaseUnitId { get; set; } = new BaseDataModel();
        public decimal Qty { get; set; }
        public decimal FAppQty { get; set; }
        public decimal FBaseQty { get; set; }
        public BaseDataModel FStockId { get; set; } = new BaseDataModel();
        public BaseDataModel FStockLocId { get; set; } = new BaseDataModel();
        public BaseDataModel FStockStatusId { get; set; } = new BaseDataModel();
        public string FBatchNo { get; set; }
        public string FMoBillNo { get; set; }
        public string PPBomBillNo { get; set; }
        public string FSrcBillNo { get; set; }
        public string FSrcFormId { get; set; }
        public Dictionary<string, BaseDataModel> FAuxPropId { get; set; } = new Dictionary<string, BaseDataModel>();
        public List<PrdReturnMtrlEntryLinkModel> FEntity_Link { get; set; } = new List<PrdReturnMtrlEntryLinkModel>();
    }

    // 关联子表模型
    public class PrdReturnMtrlEntryLinkModel
    {
        public string FEntity_Link_FFlowId { get; set; } = "";
        public string FEntity_Link_FFlowLineId { get; set; } = "";
        public string FEntity_Link_FRuleId { get; set; } = "";
        public string FEntity_Link_FSTableName { get; set; } = "";
        public long FEntity_Link_FSBillId { get; set; }
        public long FEntity_Link_FSId { get; set; }
        public string FEntity_Link_FBaseQtyOld { get; set; } = "0";
        public string FEntity_Link_FBaseQty { get; set; } = "0";
    }

}