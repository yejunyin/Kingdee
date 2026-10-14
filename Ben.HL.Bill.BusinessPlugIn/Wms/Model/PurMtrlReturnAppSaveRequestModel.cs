using System.Collections.Generic;

namespace Ben.HL.Bill.BusinessPlugIn
{
    // API 根请求包
    public class PurMtrlReturnAppSaveRequestModel
    {
        public string FormId { get; set; } = "PUR_MRAPP"; // 根据后台传参调整为 PUR_MRAPP
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
        
        public BaseDataModel FPURCHASEORGID { get; set; } = new BaseDataModel(); // 采购组织
        public BaseDataModel FAPPORGID { get; set; } = new BaseDataModel();     // 申请组织
        public BaseDataModel FSUPPLIERID { get; set; } = new BaseDataModel();   // 供应商
        public string FRMTYPE { get; set; }                                     // 退料类型 (RMTYPE)
        public string FRemarks { get; set; }                                   // 备注

        // 明细单据体映射为 PUR_MRAPPENTRY
        public List<PurMtrlReturnAppEntryModel> FPUR_MRAPPENTRY { get; set; } = new List<PurMtrlReturnAppEntryModel>();
    }

    // 明细表模型
    public class PurMtrlReturnAppEntryModel
    {
        public int FEntryID { get; set; }
        public int FSeq { get; set; }
        public BaseDataModel FMATERIALID { get; set; } = new BaseDataModel();  // 物料
        public BaseDataModel FUNITID { get; set; } = new BaseDataModel();      // 单位
        public BaseDataModel FBASEUNITID { get; set; } = new BaseDataModel();  // 基本单位
        
        public decimal FMRAPPQTY { get; set; }     // 申请退料数量 (MRAPPQTY)
        public decimal FBASEUNITQTY { get; set; }  // 基本单位数量 (BASEUNITQTY)
        
        public BaseDataModel FStockId { get; set; } = new BaseDataModel();     // 仓库
        public string FLot { get; set; }                                       // 批号文本
        public string FSRCBILLNO { get; set; }                                 // 源单编号
        public string FSRCBILLTYPEID { get; set; }                             // 源单类型
        public string FNOTE_M { get; set; }                                    // 行备注
        
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
    }
}