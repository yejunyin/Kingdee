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
    /// 退货申请单审核后按金蝶标准API格式推送WMS
    /// </summary>
    [HotUpdate]
    [Description("退货申请单审核推送WMS-Ben")]
    public class PushPurMtrlReturnAppToWMS : AbstractBillPlugIn
    {
        private const string WMS_API_URL = "http://your-wms-api.com/api/pur_mtrl_return_app";
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

                    PurMtrlReturnAppSaveRequestModel request = BuildStandardApiRequest(billObj);

                    string jsonData = JsonConvert.SerializeObject(request, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        Formatting = Formatting.Indented
                    });

                    this.View.ShowMessage($"推送退货申请单数据长度：{jsonData.Length} 字符");

                    string response = _pushService.CallWmsApi(WMS_API_URL, jsonData);
                    this.View.ShowMessage($"WMS接口返回：{response}");
                }
                catch (Exception ex)
                {
                    this.View.ShowMessage($"推送WMS异常：{ex.Message}");
                }
            }
        }

        private PurMtrlReturnAppSaveRequestModel BuildStandardApiRequest(DynamicObject billObj)
        {
            var request = new PurMtrlReturnAppSaveRequestModel();
            var header = request.data.Model;

            if (billObj.Contains("Id") && billObj["Id"] != null) header.FID = Convert.ToInt64(billObj["Id"]);
            if (billObj.Contains("BillNo") && billObj["BillNo"] != null) header.FBillNo = billObj["BillNo"].ToString();

            if (billObj.Contains("FDate") && billObj["FDate"] != null)
            {
                DateTime dateVal;
                if (DateTime.TryParse(billObj["FDate"].ToString(), out dateVal))
                    header.FDate = dateVal.ToString("yyyy-MM-dd HH:mm:ss");
                else
                    header.FDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }

            if (billObj.Contains("FMRTYPE") && billObj["FMRTYPE"] != null) header.FMRTYPE = billObj["FMRTYPE"].ToString();
            if (billObj.Contains("FOwnerTypeIdHead") && billObj["FOwnerTypeIdHead"] != null) header.FOwnerTypeIdHead = billObj["FOwnerTypeIdHead"].ToString();
            if (billObj.Contains("FNote") && billObj["FNote"] != null) header.FNote = billObj["FNote"].ToString();

            SetBaseDataNumber(billObj, "FBillTypeID", header.FBillTypeID);
            SetBaseDataNumber(billObj, "MRORGID", header.MRORGID);
            SetBaseDataNumber(billObj, "FRetOrgId", header.FRetOrgId);
            SetBaseDataNumber(billObj, "FSupplierId", header.FSupplierId);
            SetBaseDataNumber(billObj, "FStockOrgId", header.FStockOrgId);
            SetBaseDataNumber(billObj, "FOwnerIdHead", header.FOwnerIdHead);

            header.FEntity = BuildDetailEntityList(billObj);

            return request;
        }

        private List<PurMtrlReturnAppEntryModel> BuildDetailEntityList(DynamicObject billObj)
        {
            var detailList = new List<PurMtrlReturnAppEntryModel>();

            if (billObj.Contains("FEntity") && billObj["FEntity"] != null)
            {
                DynamicObjectCollection detailEntity = billObj["FEntity"] as DynamicObjectCollection;
                if (detailEntity != null && detailEntity.Count > 0)
                {
                    int seq = 1;
                    foreach (DynamicObject row in detailEntity)
                    {
                        if (row == null) continue;

                        var entry = new PurMtrlReturnAppEntryModel();
                        entry.FSeq = seq++;

                        if (row.Contains("Id") && row["Id"] != null) entry.FEntryID = Convert.ToInt32(row["Id"]);
                        if (row.Contains("FAppQty") && row["FAppQty"] != null) entry.FAppQty = Convert.ToDecimal(row["FAppQty"]);
                        if (row.Contains("FBaseAppQty") && row["FBaseAppQty"] != null) entry.FBaseAppQty = Convert.ToDecimal(row["FBaseAppQty"]);

                        if (row.Contains("FBatchNo") && row["FBatchNo"] != null) entry.FBatchNo = row["FBatchNo"].ToString();
                        if (row.Contains("FSrcBillNo") && row["FSrcBillNo"] != null) entry.FSrcBillNo = row["FSrcBillNo"].ToString();
                        if (row.Contains("FSrcFormId") && row["FSrcFormId"] != null) entry.FSrcFormId = row["FSrcFormId"].ToString();
                        if (row.Contains("FNote") && row["FNote"] != null) entry.FNote = row["FNote"].ToString();

                        SetBaseDataNumber(row, "FMaterialId", entry.FMaterialId);
                        SetBaseDataNumber(row, "FUnitId", entry.FUnitId);
                        SetBaseDataNumber(row, "FBaseUnitId", entry.FBaseUnitId);
                        SetBaseDataNumber(row, "FStockId", entry.FStockId);
                        SetBaseDataNumber(row, "FStockLocId", entry.FStockLocId);

                        entry.FAuxPropId = BuildAuxPropData(row);
                        entry.FEntity_Link = GetLinkData(row);

                        detailList.Add(entry);
                    }
                }
            }

            return detailList;
        }

        private Dictionary<string, BaseDataModel> BuildAuxPropData(DynamicObject row)
        {
            var auxPropDict = new Dictionary<string, BaseDataModel>();
            string[] auxFields = new string[]
            {
                "FAUXPROPID__FF100001", "FAUXPROPID__FF100002", "FAUXPROPID__FF100003",
                "FAUXPROPID__FF100004", "FAUXPROPID__FF100005", "FAUXPROPID__FF100006"
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

            foreach (string field in auxFields) auxPropDict[field] = new BaseDataModel();
            return auxPropDict;
        }

        private List<PurMtrlReturnAppEntryLinkModel> GetLinkData(DynamicObject row)
        {
            var linkList = new List<PurMtrlReturnAppEntryLinkModel>();

            if (row != null && row.Contains("FEntity_Link") && row["FEntity_Link"] != null)
            {
                DynamicObjectCollection links = row["FEntity_Link"] as DynamicObjectCollection;
                if (links != null && links.Count > 0)
                {
                    foreach (DynamicObject link in links)
                    {
                        if (link == null) continue;
                        var linkData = new PurMtrlReturnAppEntryLinkModel();

                        if (link.Contains("FlowId") && link["FlowId"] != null) linkData.FEntity_Link_FFlowId = link["FlowId"].ToString();
                        if (link.Contains("FFlowLineId") && link["FFlowLineId"] != null) linkData.FEntity_Link_FFlowLineId = link["FFlowLineId"].ToString();
                        if (link.Contains("FRuleId") && link["FRuleId"] != null) linkData.FEntity_Link_FRuleId = link["FRuleId"].ToString();
                        if (link.Contains("FSTableName") && link["FSTableName"] != null) linkData.FEntity_Link_FSTableName = link["FSTableName"].ToString();
                        if (link.Contains("FSBillId") && link["FSBillId"] != null) linkData.FEntity_Link_FSBillId = Convert.ToInt64(link["FSBillId"]);
                        if (link.Contains("FSId") && link["FSId"] != null) linkData.FEntity_Link_FSId = Convert.ToInt64(link["FSId"]);

                        linkList.Add(linkData);
                    }
                }
            }

            if (linkList.Count == 0) linkList.Add(new PurMtrlReturnAppEntryLinkModel());
            return linkList;
        }

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