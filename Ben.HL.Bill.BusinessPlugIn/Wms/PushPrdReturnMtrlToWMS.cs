using System;
using System.Collections.Generic;
using System.ComponentModel;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;
using Newtonsoft.Json;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 生产退料单审核后按金蝶标准API格式推送WMS
    /// </summary>
    [HotUpdate]
    [Description("生产退料单审核推送WMS-Ben")]
    public class PushPrdReturnMtrlToWMS : AbstractBillPlugIn
    {
        private const string WMS_API_URL = "http://your-wms-api.com/api/prd_return_mtrl";
        private WmsApiClient _pushService = new WmsApiClient();

        public override void AfterDoOperation(AfterDoOperationEventArgs e)
        {
            base.AfterDoOperation(e);

            if (e.Operation.Operation.Equals("Audit", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    DynamicObject billObj = this.Model.DataObject;
                    if (billObj == null)
                    {
                        this.View.ShowMessage("没有找到需要推送的单据数据");
                        return;
                    }

                    // 1. 构建强类型请求对象
                    PrdReturnMtrlSaveRequestModel request = BuildStandardApiRequest(billObj);

                    // 2. 序列化 JSON
                    string jsonData = JsonConvert.SerializeObject(request, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        Formatting = Formatting.Indented
                    });

                    this.View.ShowMessage($"推送生产退料单数据长度：{jsonData.Length} 字符");

                    // 3. 调用 API
                    string response = _pushService.CallWmsApi(WMS_API_URL, jsonData);
                    this.View.ShowMessage($"WMS接口返回：{response}");
                }
                catch (Exception ex)
                {
                    this.View.ShowMessage($"推送WMS异常：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 使用显式赋值格式构建请求结构
        /// </summary>
        private PrdReturnMtrlSaveRequestModel BuildStandardApiRequest(DynamicObject billObj)
        {
            var request = new PrdReturnMtrlSaveRequestModel();
            var header = request.data.Model;

            // --- 主表基础字段赋值 ---
            if (billObj.Contains("Id") && billObj["Id"] != null)
            {
                header.FID = Convert.ToInt64(billObj["Id"]);
            }

            if (billObj.Contains("BillNo") && billObj["BillNo"] != null)
            {
                header.FBillNo = billObj["BillNo"].ToString();
            }

            if (billObj.Contains("FDate") && billObj["FDate"] != null)
            {
                DateTime dateVal;
                if (DateTime.TryParse(billObj["FDate"].ToString(), out dateVal))
                {
                    header.FDate = dateVal.ToString("yyyy-MM-dd HH:mm:ss");
                }
                else
                {
                    header.FDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }
            }

            if (billObj.Contains("FOwnerTypeIdHead") && billObj["FOwnerTypeIdHead"] != null)
            {
                header.FOwnerTypeIdHead = billObj["FOwnerTypeIdHead"].ToString();
            }

            if (billObj.Contains("FDescription") && billObj["FDescription"] != null)
            {
                header.FDescription = billObj["FDescription"].ToString();
            }

            // --- 主表基础资料字段 ---
            SetBaseDataNumber(billObj, "FBillTypeID", header.FBillTypeID);
            SetBaseDataNumber(billObj, "FPrdOrgId", header.FPrdOrgId);
            SetBaseDataNumber(billObj, "FStockOrgId", header.FStockOrgId);
            SetBaseDataNumber(billObj, "FWorkShopId", header.FWorkShopId);
            SetBaseDataNumber(billObj, "FOwnerIdHead", header.FOwnerIdHead);

            // --- 明细表 (FEntity) ---
            header.FEntity = BuildDetailEntityList(billObj);

            return request;
        }

        /// <summary>
        /// 构建生产退料单分录列表
        /// </summary>
        private List<PrdReturnMtrlEntryModel> BuildDetailEntityList(DynamicObject billObj)
        {
            var detailList = new List<PrdReturnMtrlEntryModel>();

            if (billObj.Contains("FEntity") && billObj["FEntity"] != null)
            {
                DynamicObjectCollection detailEntity = billObj["FEntity"] as DynamicObjectCollection;
                if (detailEntity != null && detailEntity.Count > 0)
                {
                    int seq = 1;
                    foreach (DynamicObject row in detailEntity)
                    {
                        if (row == null) continue;

                        var entry = new PrdReturnMtrlEntryModel();
                        entry.FSeq = seq++;

                        if (row.Contains("Id") && row["Id"] != null) entry.FEntryID = Convert.ToInt32(row["Id"]);

                        // 数量字段
                        if (row.Contains("Qty") && row["Qty"] != null) entry.Qty = Convert.ToDecimal(row["Qty"]);
                        if (row.Contains("FAppQty") && row["FAppQty"] != null) entry.FAppQty = Convert.ToDecimal(row["FAppQty"]);
                        if (row.Contains("FBaseQty") && row["FBaseQty"] != null) entry.FBaseQty = Convert.ToDecimal(row["FBaseQty"]);

                        // 文本/批号/追溯字段
                        if (row.Contains("FBatchNo") && row["FBatchNo"] != null) entry.FBatchNo = row["FBatchNo"].ToString();
                        if (row.Contains("FMoBillNo") && row["FMoBillNo"] != null) entry.FMoBillNo = row["FMoBillNo"].ToString();
                        if (row.Contains("PPBomBillNo") && row["PPBomBillNo"] != null) entry.PPBomBillNo = row["PPBomBillNo"].ToString();
                        if (row.Contains("FSrcBillNo") && row["FSrcBillNo"] != null) entry.FSrcBillNo = row["FSrcBillNo"].ToString();
                        if (row.Contains("FSrcFormId") && row["FSrcFormId"] != null) entry.FSrcFormId = row["FSrcFormId"].ToString();

                        // 分录基础资料
                        SetBaseDataNumber(row, "FMaterialId", entry.FMaterialId);
                        SetBaseDataNumber(row, "FUnitId", entry.FUnitId);
                        SetBaseDataNumber(row, "FBaseUnitId", entry.FBaseUnitId);
                        SetBaseDataNumber(row, "FStockId", entry.FStockId);
                        SetBaseDataNumber(row, "FStockLocId", entry.FStockLocId);
                        SetBaseDataNumber(row, "FStockStatusId", entry.FStockStatusId);

                        // 辅助属性构建
                        entry.FAuxPropId = BuildAuxPropData(row);

                        // 单据关联链路构建
                        entry.FEntity_Link = GetLinkData(row);

                        detailList.Add(entry);
                    }
                }
            }

            return detailList;
        }

        /// <summary>
        /// 构建辅助属性
        /// </summary>
        private Dictionary<string, BaseDataModel> BuildAuxPropData(DynamicObject row)
        {
            var auxPropDict = new Dictionary<string, BaseDataModel>();
            string[] auxFields = new string[]
            {
                "FAUXPROPID__FF100001", "FAUXPROPID__FF100002", "FAUXPROPID__FF100003",
                "FAUXPROPID__FF100004", "FAUXPROPID__FF100005", "FAUXPROPID__FF100006",
                "FAUXPROPID__FF100007", "FAUXPROPID__FF100008", "FAUXPROPID__FF100009",
                "FAUXPROPID__FF100010", "FAUXPROPID__FF100011", "FAUXPROPID__FF100012"
            };

            if (row != null && row.Contains("FAuxPropId") && row["FAuxPropId"] != null)
            {
                DynamicObject auxPropObj = row["FAuxPropId"] as DynamicObject;
                if (auxPropObj != null)
                {
                    foreach (string field in auxFields)
                    {
                        var baseData = new BaseDataModel();
                        if (auxPropObj.Contains(field) && auxPropObj[field] != null)
                        {
                            DynamicObject ffPropObj = auxPropObj[field] as DynamicObject;
                            if (ffPropObj != null && ffPropObj.Contains("FNumber") && ffPropObj["FNumber"] != null)
                            {
                                baseData.FNumber = ffPropObj["FNumber"].ToString();
                            }
                        }
                        auxPropDict[field] = baseData;
                    }
                    return auxPropDict;
                }
            }

            // 兜底补空
            foreach (string field in auxFields)
            {
                auxPropDict[field] = new BaseDataModel();
            }

            return auxPropDict;
        }

        /// <summary>
        /// 构建关联表数据
        /// </summary>
        private List<PrdReturnMtrlEntryLinkModel> GetLinkData(DynamicObject row)
        {
            var linkList = new List<PrdReturnMtrlEntryLinkModel>();

            if (row != null && row.Contains("FEntity_Link") && row["FEntity_Link"] != null)
            {
                DynamicObjectCollection links = row["FEntity_Link"] as DynamicObjectCollection;
                if (links != null && links.Count > 0)
                {
                    foreach (DynamicObject link in links)
                    {
                        if (link == null) continue;

                        var linkData = new PrdReturnMtrlEntryLinkModel();

                        if (link.Contains("FlowId") && link["FlowId"] != null) linkData.FEntity_Link_FFlowId = link["FlowId"].ToString();
                        if (link.Contains("FFlowLineId") && link["FFlowLineId"] != null) linkData.FEntity_Link_FFlowLineId = link["FFlowLineId"].ToString();
                        if (link.Contains("FRuleId") && link["FRuleId"] != null) linkData.FEntity_Link_FRuleId = link["FRuleId"].ToString();
                        if (link.Contains("FSTableName") && link["FSTableName"] != null) linkData.FEntity_Link_FSTableName = link["FSTableName"].ToString();
                        if (link.Contains("FSBillId") && link["FSBillId"] != null) linkData.FEntity_Link_FSBillId = Convert.ToInt64(link["FSBillId"]);
                        if (link.Contains("FSId") && link["FSId"] != null) linkData.FEntity_Link_FSId = Convert.ToInt64(link["FSId"]);
                        if (link.Contains("FBaseQtyOld") && link["FBaseQtyOld"] != null) linkData.FEntity_Link_FBaseQtyOld = link["FBaseQtyOld"].ToString();
                        if (link.Contains("FBaseQty") && link["FBaseQty"] != null) linkData.FEntity_Link_FBaseQty = link["FBaseQty"].ToString();

                        linkList.Add(linkData);
                    }
                }
            }

            if (linkList.Count == 0)
            {
                // 无关联时添加默认空数据
                linkList.Add(new PrdReturnMtrlEntryLinkModel());
            }

            return linkList;
        }

        /// <summary>
        /// 安全提取 DynamicObject 基础资料中的 FNumber 辅助方法
        /// </summary>
        private void SetBaseDataNumber(DynamicObject sourceObj, string fieldName, BaseDataModel targetModel)
        {
            if (sourceObj != null && targetModel != null && sourceObj.Contains(fieldName) && sourceObj[fieldName] != null)
            {
                DynamicObject baseDataObj = sourceObj[fieldName] as DynamicObject;
                if (baseDataObj != null && baseDataObj.Contains("FNumber") && baseDataObj["FNumber"] != null)
                {
                    targetModel.FNumber = baseDataObj["FNumber"].ToString();
                }
            }
        }
    }
}