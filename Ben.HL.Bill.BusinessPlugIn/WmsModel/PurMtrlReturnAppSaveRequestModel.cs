using System.Collections.Generic;

namespace Ben.HL.Bill.BusinessPlugIn
{
    // API 根请求包
    public class PurMtrlReturnAppSaveRequestModel
    {
        public string FormId { get; set; } = "PUR_MtrlReturnApp";
        public PurMtrlReturnAppSaveDataModel data { get; set; } = new PurMtrlReturnAppSaveDataModel();
    }

    public class PurMtrlReturnAppSaveDataModel
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
        public PurMtrlReturnAppHeaderModel Model { get; set; } = new PurMtrlReturnAppHeaderModel();
    }

    // 表头模型
    public class PurMtrlReturnAppHeaderModel
    {
        public long FID { get; set; }
        public string FBillNo { get; set; }
        public BaseDataModel FBillTypeID { get; set; } = new BaseDataModel();
        public string FDate { get; set; }
        public BaseDataModel MRORGID { get; set; } = new BaseDataModel();      // 退货组织
        public BaseDataModel FRetOrgId { get; set; } = new BaseDataModel();     // 退货部门/组织
        public BaseDataModel FSupplierId { get; set; } = new BaseDataModel();   // 供应商
        public BaseDataModel FStockOrgId { get; set; } = new BaseDataModel();   // 库存组织
        public string FMRTYPE { get; set; }                                     // 退货类型
        public string FOwnerTypeIdHead { get; set; }
        public BaseDataModel FOwnerIdHead { get; set; } = new BaseDataModel();
        public string FNote { get; set; }
        public List<PurMtrlReturnAppEntryModel> FEntity { get; set; } = new List<PurMtrlReturnAppEntryModel>();
    }

    // 明细表模型
    public class PurMtrlReturnAppEntryModel
    {
        public int FEntryID { get; set; }
        public int FSeq { get; set; }
        public BaseDataModel FMaterialId { get; set; } = new BaseDataModel();
        public BaseDataModel FUnitId { get; set; } = new BaseDataModel();
        public BaseDataModel FBaseUnitId { get; set; } = new BaseDataModel();
        public decimal FAppQty { get; set; }       // 申请退货数量
        public decimal FBaseAppQty { get; set; }   // 计基本申请退货数量
        public BaseDataModel FStockId { get; set; } = new BaseDataModel();
        public BaseDataModel FStockLocId { get; set; } = new BaseDataModel();
        public string FBatchNo { get; set; }
        public string FSrcBillNo { get; set; }
        public string FSrcFormId { get; set; }
        public string FNote { get; set; }
        public Dictionary<string, BaseDataModel> FAuxPropId { get; set; } = new Dictionary<string, BaseDataModel>();
        public List<PurMtrlReturnAppEntryLinkModel> FEntity_Link { get; set; } = new List<PurMtrlReturnAppEntryLinkModel>();
    }

    // 关联子表模型
    public class PurMtrlReturnAppEntryLinkModel
    {
        public string FEntity_Link_FFlowId { get; set; } = "";
        public string FEntity_Link_FFlowLineId { get; set; } = "";
        public string FEntity_Link_FRuleId { get; set; } = "";
        public string FEntity_Link_FSTableName { get; set; } = "";
        public long FEntity_Link_FSBillId { get; set; }
        public long FEntity_Link_FSId { get; set; }
        public string FEntity_Link_FBaseAppQtyOld { get; set; } = "0";
        public string FEntity_Link_FBaseAppQty { get; set; } = "0";
    }
}