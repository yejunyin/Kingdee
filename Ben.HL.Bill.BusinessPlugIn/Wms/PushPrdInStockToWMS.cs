using System;
using System.Collections.Generic;
using System.ComponentModel;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Newtonsoft.Json;
using Kingdee.BOS.Util;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 生产入库单审核后按金蝶标准API格式推送WMS（支持WMS回写生产入库单）
    /// </summary>
    [HotUpdate]
    [Description("生产入库单审核推送WMS-Ben")]
    public class PushPrdInStockToWMS : AbstractBillPlugIn
    {
        private readonly WmsApiClient _apiClient = new WmsApiClient();

        public override void AfterDoOperation(AfterDoOperationEventArgs e)
        {
            base.AfterDoOperation(e);

            if (!e.Operation.Operation.Equals("Audit", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                DynamicObject billObj = this.Model.DataObject;
                if (billObj == null)
                {
                    this.View.ShowMessage("没有找到需要推送的单据数据");
                    return;
                }

                IntegrationConfig config = IntegrationConfig.Load();
                PrdInStockSaveRequestModel wmsRequest = BuildStandardApiRequest(billObj);
                string jsonData = Serialize(wmsRequest);

                string response = _apiClient.PostJson(config.WmsPrdInStockApiUrl, jsonData);
                this.View.ShowMessage("生产入库单推送WMS成功。WMS返回：" + response);
            }
            catch (Exception ex)
            {
                this.View.ShowMessage("生产入库单推送WMS异常：" + ex.Message);
            }
        }

        private PrdInStockSaveRequestModel BuildStandardApiRequest(DynamicObject billObj)
        {
            var request = new PrdInStockSaveRequestModel();
            PrdInStockHeaderModel header = request.data.Model;

            header.FID = GetLong(billObj, "Id");
            header.FBillNo = GetValue(billObj, "BillNo");
            header.FDate = GetDate(billObj, "FDate");
            header.FOwnerTypeIdHead = GetValue(billObj, "FOwnerTypeIdHead");
            header.FDescription = GetValue(billObj, "FDescription");

            SetBaseDataNumber(billObj, "FBillTypeID", header.FBillTypeID);
            SetBaseDataNumber(billObj, "FPrdOrgId", header.FPrdOrgId);
            SetBaseDataNumber(billObj, "FStockOrgId", header.FStockOrgId);
            SetBaseDataNumber(billObj, "FWorkShopId", header.FWorkShopId);
            SetBaseDataNumber(billObj, "FOwnerIdHead", header.FOwnerIdHead);

            header.FEntity = BuildDetailEntityList(billObj);
            return request;
        }

        private List<PrdInStockEntryModel> BuildDetailEntityList(DynamicObject billObj)
        {
            var result = new List<PrdInStockEntryModel>();
            DynamicObjectCollection rows = billObj["FEntity"] as DynamicObjectCollection;
            if (rows == null)
            {
                return result;
            }

            int seq = 1;
            foreach (DynamicObject row in rows)
            {
                if (row == null) continue;

                var entry = new PrdInStockEntryModel
                {
                    FSeq = seq++,
                    FEntryID = Convert.ToInt32(GetLong(row, "Id")),
                    FRealQty = GetDecimal(row, "FRealQty"),
                    FBaseRealQty = GetDecimal(row, "FBaseRealQty"),
                    FBatchNo = GetValue(row, "FBatchNo"),
                    FMoBillNo = GetValue(row, "FMoBillNo"),
                    FSrcBillNo = GetValue(row, "FSrcBillNo"),
                    FSrcFormId = GetValue(row, "FSrcFormId"),
                    FAuxPropId = BuildAuxPropData(row),
                    FEntity_Link = GetLinkData(row)
                };

                SetBaseDataNumber(row, "FMaterialId", entry.FMaterialId);
                SetBaseDataNumber(row, "FUnitId", entry.FUnitId);
                SetBaseDataNumber(row, "FBaseUnitId", entry.FBaseUnitId);
                SetBaseDataNumber(row, "FStockId", entry.FStockId);
                SetBaseDataNumber(row, "FStockLocId", entry.FStockLocId);
                SetBaseDataNumber(row, "FStockStatusId", entry.FStockStatusId);

                result.Add(entry);
            }
            return result;
        }

        private Dictionary<string, BaseDataModel> BuildAuxPropData(DynamicObject row)
        {
            var auxPropDict = new Dictionary<string, BaseDataModel>();
            string[] auxFields = new string[]
            {
                "FAUXPROPID__FF100001", "FAUXPROPID__FF100002", "FAUXPROPID__FF100003",
                "FAUXPROPID__FF100004", "FAUXPROPID__FF100005", "FAUXPROPID__FF100006"
            };

            DynamicObject auxPropObj = row == null ? null : row["FAuxPropId"] as DynamicObject;
            foreach (string field in auxFields)
            {
                var baseData = new BaseDataModel();
                if (auxPropObj != null && auxPropObj.Contains(field) && auxPropObj[field] != null)
                {
                    DynamicObject ffPropObj = auxPropObj[field] as DynamicObject;
                    if (ffPropObj != null)
                    {
                        baseData.FNumber = GetValue(ffPropObj, "FNumber");
                    }
                }
                auxPropDict[field] = baseData;
            }
            return auxPropDict;
        }

        private List<PrdInStockEntryLinkModel> GetLinkData(DynamicObject row)
        {
            var result = new List<PrdInStockEntryLinkModel>();
            DynamicObjectCollection links = row == null ? null : row["FEntity_Link"] as DynamicObjectCollection;
            if (links == null)
            {
                return result;
            }

            foreach (DynamicObject link in links)
            {
                if (link == null) continue;

                result.Add(new PrdInStockEntryLinkModel
                {
                    FEntity_Link_FFlowId = GetValue(link, "FlowId"),
                    FEntity_Link_FFlowLineId = GetValue(link, "FFlowLineId"),
                    FEntity_Link_FRuleId = GetValue(link, "FRuleId"),
                    FEntity_Link_FSTableName = GetValue(link, "FSTableName"),
                    FEntity_Link_FSBillId = GetLong(link, "FSBillId"),
                    FEntity_Link_FSId = GetLong(link, "FSId")
                });
            }
            return result;
        }

        /// <summary>
        /// 安全提取 DynamicObject 基础资料中的 FNumber
        /// </summary>
        private void SetBaseDataNumber(DynamicObject source, string fieldName, BaseDataModel target)
        {
            DynamicObject value = source == null ? null : source[fieldName] as DynamicObject;
            target.FNumber = GetValue(value, "Number");
        }

        private string GetValue(DynamicObject source, string fieldName)
        {
            return source != null && source.Contains(fieldName) && source[fieldName] != null
                ? source[fieldName].ToString()
                : string.Empty;
        }

        private long GetLong(DynamicObject source, string fieldName)
        {
            long value;
            return long.TryParse(GetValue(source, fieldName), out value) ? value : 0;
        }

        private decimal GetDecimal(DynamicObject source, string fieldName)
        {
            decimal value;
            return decimal.TryParse(GetValue(source, fieldName), out value) ? value : 0;
        }

        private string GetDate(DynamicObject source, string fieldName)
        {
            DateTime value;
            return DateTime.TryParse(GetValue(source, fieldName), out value)
                ? value.ToString("yyyy-MM-dd HH:mm:ss")
                : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        }
    }
}
