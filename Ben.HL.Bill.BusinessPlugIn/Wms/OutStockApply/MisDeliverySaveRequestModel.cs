using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ben.HL.Bill.BusinessPlugIn
{
    // 其他出库单(STK_MisDelivery)保存请求模型
    // 由出库申请单(STK_OutStockApply)审核后推送给WMS，WMS据此调用K3Cloud保存接口回写其他出库单
    public class MisDeliverySaveRequestModel
    {
        [JsonProperty("formid")]
        public string FormId { get; set; } = "STK_MisDelivery";
        public MisDeliverySaveDataModel data { get; set; } = new MisDeliverySaveDataModel();
    }

    public class MisDeliverySaveDataModel
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
        public MisDeliveryHeaderModel Model { get; set; } = new MisDeliveryHeaderModel();
    }

    // 表头模型
    public class MisDeliveryHeaderModel
    {
        public long FID { get; set; }                                                      // 0=新增
        public BaseDataModel FBillTypeID { get; set; } = new BaseDataModel { FNumber = "QTCKD01_SYS" }; // 单据类型：其他出库单
        public BaseDataModel FStockOrgId { get; set; } = new BaseDataModel();              // 库存组织
        public BaseDataModel FPickOrgId { get; set; } = new BaseDataModel();               // 拣货组织
        public string FStockDirect { get; set; } = "GENERAL";                              // 库存方向
        public string FDate { get; set; }                                                  // 单据日期
        public BaseDataModel FDeptId { get; set; } = new BaseDataModel();                  // 部门
        public string FBizType { get; set; } = "0";                                        // 业务类型
        public string FOwnerTypeIdHead { get; set; } = "BD_OwnerOrg";                      // 表头货主类型
        public BaseDataModel FOwnerIdHead { get; set; } = new BaseDataModel();             // 表头货主
        public BaseDataModel F_hll_Base { get; set; } = new BaseDataModel();               // 自定义基础资料字段
        public List<MisDeliveryEntryModel> FEntity { get; set; } = new List<MisDeliveryEntryModel>();
    }

    // 明细表模型 (FEntity)
    public class MisDeliveryEntryModel
    {
        public BaseDataModel FMaterialId { get; set; } = new BaseDataModel();              // 物料
        public BaseDataModel FUnitID { get; set; } = new BaseDataModel();                  // 单位
        public decimal FQty { get; set; }                                                  // 数量
        public BaseDataModel FBaseUnitId { get; set; } = new BaseDataModel();              // 基本单位
        public BaseDataModel FStockId { get; set; } = new BaseDataModel();                 // 仓库
        public string FSrcBillTypeId { get; set; } = "STK_OutStockApply";                  // 源单类型：出库申请单
        public string FOwnerTypeId { get; set; } = "BD_OwnerOrg";                          // 分录货主类型
        public string FSrcBillNo { get; set; }                                             // 源单编号
        public BaseDataModel FOwnerId { get; set; } = new BaseDataModel();                 // 分录货主
        public BaseDataModel FStockStatusId { get; set; } = new BaseDataModel { FNumber = "KCZT01_SYS" }; // 库存状态
        public string FKeeperTypeId { get; set; } = "BD_KeeperOrg";                        // 保管者类型
        public bool FDistribution { get; set; }                                            // 是否分销
        public BaseDataModel FKeeperId { get; set; } = new BaseDataModel();                // 保管者
        public decimal FExtAuxUnitQty { get; set; }                                        // 辅助单位数量
    }
}
