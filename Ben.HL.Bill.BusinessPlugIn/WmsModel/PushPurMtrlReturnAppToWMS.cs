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
    [HotUpdate]
    [Description("退料申请单审核推送WMS-Ben")]
    public class PushPurMtrlReturnAppToWMS : AbstractBillPlugIn
    {
        private const string WMS_API_URL = "http://your-wms-api.com/api/pur_mrapp";
        private WmsApiClient _pushService = new WmsApiClient();

        public override void AfterDoOperation(AfterDoOperationEventArgs e)
        {
            base.AfterDoOperation(e);

            //if (e.Operation.Operation.Equals("Audit", StringComparison.OrdinalIgnoreCase))
            //{
                try
                {
                    DynamicObject billObj = this.Model.DataObject;
                    if (billObj == null)
                    {
                        ShowClientMessage("没有找到需要推送的单据数据");
                        return;
                    }

                    PurMtrlReturnAppSaveRequestModel request = BuildStandardApiRequest(billObj);

                    string jsonData = JsonConvert.SerializeObject(request, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        Formatting = Formatting.Indented
                    });

                    ShowClientMessage($"推送退料申请单数据长度：{jsonData.Length} 字符");

                    string response = _pushService.CallWmsApi(WMS_API_URL, jsonData);
                    ShowClientMessage($"WMS接口返回：{response}");
                }
                catch (Exception ex)
                {
                    ShowClientMessage($"推送WMS异常：{ex.Message}");
                }
            //}
        }

        private PurMtrlReturnAppSaveRequestModel BuildStandardApiRequest(DynamicObject billObj)
        {
            var request = new PurMtrlReturnAppSaveRequestModel();
            var header = request.data.Model;

            // 1. 单头基础字段
            if (billObj.Contains("Id") && billObj["Id"] != null) header.FID = Convert.ToInt64(billObj["Id"]);
            if (billObj.Contains("BillNo") && billObj["BillNo"] != null) header.FBillNo = billObj["BillNo"].ToString();

            if (billObj.Contains("FDate") && billObj["FDate"] != null)
            {
                DateTime dateVal;
                if (DateTime.TryParse(billObj["FDate"].ToString(), out dateVal))
                    header.FDate = dateVal.ToString("yyyy-MM-dd HH:mm:ss");
            }

            if (billObj.Contains("RMTYPE") && billObj["RMTYPE"] != null) header.FRMTYPE = billObj["RMTYPE"].ToString();
            if (billObj.Contains("FRemarks") && billObj["FRemarks"] != null) header.FRemarks = billObj["FRemarks"].ToString();

            // 2. 单头基础资料读取
            SetBaseDataNumber(billObj, "FBillTypeID", header.FBillTypeID);
            SetBaseDataNumber(billObj, "PURCHASEORGID", header.FPURCHASEORGID);
            SetBaseDataNumber(billObj, "APPORGID", header.FAPPORGID);
            SetBaseDataNumber(billObj, "SUPPLIERID", header.FSUPPLIERID);

            // 3. 构建单据体 PUR_MRAPPENTRY
            header.FPUR_MRAPPENTRY = BuildDetailEntityList(billObj);

            return request;
        }

        private List<PurMtrlReturnAppEntryModel> BuildDetailEntityList(DynamicObject billObj)
        {
            var detailList = new List<PurMtrlReturnAppEntryModel>();

            // 匹配后台的单据体字段 PUR_MRAPPENTRY
            if (billObj.Contains("PUR_MRAPPENTRY") && billObj["PUR_MRAPPENTRY"] != null)
            {
                DynamicObjectCollection detailEntity = billObj["PUR_MRAPPENTRY"] as DynamicObjectCollection;
                if (detailEntity != null && detailEntity.Count > 0)
                {
                    int seq = 1;
                    foreach (DynamicObject row in detailEntity)
                    {
                        if (row == null) continue;

                        var entry = new PurMtrlReturnAppEntryModel();
                        entry.FSeq = seq++;

                        if (row.Contains("Id") && row["Id"] != null) entry.FEntryID = Convert.ToInt32(row["Id"]);

                        // 核心数量字段匹配
                        if (row.Contains("MRAPPQTY") && row["MRAPPQTY"] != null) entry.FMRAPPQTY = Convert.ToDecimal(row["MRAPPQTY"]);
                        if (row.Contains("BASEUNITQTY") && row["BASEUNITQTY"] != null) entry.FBASEUNITQTY = Convert.ToDecimal(row["BASEUNITQTY"]);

                        // 批号（优先读取文本）
                        if (row.Contains("FLot_Text") && row["FLot_Text"] != null && !string.IsNullOrWhiteSpace(row["FLot_Text"].ToString()))
                        {
                            entry.FLot = row["FLot_Text"].ToString();
                        }

                        // 源单与备注
                        if (row.Contains("SRCBILLNO") && row["SRCBILLNO"] != null) entry.FSRCBILLNO = row["SRCBILLNO"].ToString();
                        if (row.Contains("SRCBILLTYPEID") && row["SRCBILLTYPEID"] != null) entry.FSRCBILLTYPEID = row["SRCBILLTYPEID"].ToString();
                        if (row.Contains("NOTE_M") && row["NOTE_M"] != null) entry.FNOTE_M = row["NOTE_M"].ToString();

                        // 单据体基础资料匹配
                        SetBaseDataNumber(row, "MATERIALID", entry.FMATERIALID);
                        SetBaseDataNumber(row, "UNITID", entry.FUNITID);
                        SetBaseDataNumber(row, "BASEUNITID", entry.FBASEUNITID);
                        SetBaseDataNumber(row, "StockId", entry.FStockId);

                        // 源单关联链路（推送下游/下游生成单据的关键）
                        entry.FEntity_Link = GetLinkData(row);

                        detailList.Add(entry);
                    }
                }
            }

            return detailList;
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
                        if (link.Contains("FlowLineId") && link["FlowLineId"] != null) linkData.FEntity_Link_FFlowLineId = link["FlowLineId"].ToString();
                        if (link.Contains("RuleId") && link["RuleId"] != null) linkData.FEntity_Link_FRuleId = link["RuleId"].ToString();
                        if (link.Contains("STableName") && link["STableName"] != null) linkData.FEntity_Link_FSTableName = link["STableName"].ToString();
                        if (link.Contains("SBillId") && link["SBillId"] != null) linkData.FEntity_Link_FSBillId = Convert.ToInt64(link["SBillId"]);
                        if (link.Contains("SId") && link["SId"] != null) linkData.FEntity_Link_FSId = Convert.ToInt64(link["SId"]);

                        linkList.Add(linkData);
                    }
                }
            }

            return linkList;
        }

        private void SetBaseDataNumber(DynamicObject sourceObj, string fieldName, BaseDataModel targetModel)
        {
            if (sourceObj != null && targetModel != null && sourceObj.Contains(fieldName) && sourceObj[fieldName] != null)
            {
                DynamicObject baseDataObj = sourceObj[fieldName] as DynamicObject;
                if (baseDataObj != null && baseDataObj.Contains("Number") && baseDataObj["Number"] != null)
                {
                    targetModel.FNumber = baseDataObj["Number"].ToString();
                }
            }
        }

        private void ShowClientMessage(string message)
        {
            if (this.View != null)
            {
                this.View.ShowMessage(message);
            }
        }
    }
}