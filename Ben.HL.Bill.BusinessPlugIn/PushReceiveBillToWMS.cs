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
    /// 收料通知单审核后按金蝶标准API格式推送WMS
    /// </summary>
    [HotUpdate]
    [Description("收料通知单审核推送WMS-Ben")]
    public class PushReceiveBillToWMS : AbstractBillPlugIn
    {
        private const string WMS_API_URL = "http://your-wms-api.com/api/receive";
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
                    WmsSaveReceiveBillRequestModel request = BuildStandardApiRequest(billObj);

                    // 2. 序列化 JSON
                    string jsonData = JsonConvert.SerializeObject(request, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        Formatting = Formatting.Indented
                    });

                    this.View.ShowMessage($"推送数据长度：{jsonData.Length} 字符");

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
        private WmsSaveReceiveBillRequestModel BuildStandardApiRequest(DynamicObject billObj)
        {
            var request = new WmsSaveReceiveBillRequestModel();
            var header = request.data.Model;

            // --- 主表基础字段赋值 ---
            if (billObj.Contains("Id") && billObj["Id"] != null)
            {
                header.FID = Convert.ToInt64(billObj["Id"]);
            }

            if (billObj.Contains("F_HL_SRM_Source_NO") && billObj["F_HL_SRM_Source_NO"] != null)
            {
                header.F_HL_SRM_Source_NO = billObj["F_HL_SRM_Source_NO"].ToString();
            }

            if (billObj.Contains("BusinessType") && billObj["BusinessType"] != null)
            {
                header.FBusinessType = billObj["BusinessType"].ToString();
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

            if (billObj.Contains("F_PVCE_Combo") && billObj["F_PVCE_Combo"] != null)
            {
                header.F_PVCE_Combo = billObj["F_PVCE_Combo"].ToString();
            }

            if (billObj.Contains("FNote") && billObj["FNote"] != null)
            {
                header.FNote = billObj["FNote"].ToString();
            }

            // 单据类型 (映射逻辑)
            header.FBillTypeID.FNumber = GetBillTypeNumber(billObj);

            // --- 主表基础资料字段 ---
            SetBaseDataNumber(billObj, "FStockOrgId", header.FStockOrgId);
            SetBaseDataNumber(billObj, "FPurOrgId", header.FPurOrgId);
            SetBaseDataNumber(billObj, "FSupplierId", header.FSupplierId);
            SetBaseDataNumber(billObj, "FSupplyId", header.FSupplyId);
            SetBaseDataNumber(billObj, "FOwnerIdHead", header.FOwnerIdHead);

            // --- 财务子表 (FinanceEntity) ---
            if (billObj.Contains("FinanceEntity") && billObj["FinanceEntity"] != null)
            {
                DynamicObjectCollection financeData = billObj["FinanceEntity"] as DynamicObjectCollection;
                if (financeData != null && financeData.Count > 0)
                {
                    DynamicObject financeObj = financeData[0];
                    if (financeObj != null)
                    {
                        SetBaseDataNumber(financeObj, "FSettleOrgId", header.FinanceEntity.FSettleOrgId);
                        SetBaseDataNumber(financeObj, "FSettleCurrId", header.FinanceEntity.FSettleCurrId);

                        if (financeObj.Contains("FPricePoint") && financeObj["FPricePoint"] != null)
                        {
                            header.FinanceEntity.FPricePoint = financeObj["FPricePoint"].ToString();
                        }
                    }
                }
            }

            // --- 明细表 (PUR_ReceiveEntry) ---
            header.FDetailEntity = BuildDetailEntityList(billObj);

            return request;
        }

        /// <summary>
        /// 构建分录列表
        /// </summary>
        private List<ReceiveBillEntryModel> BuildDetailEntityList(DynamicObject billObj)
        {
            var detailList = new List<ReceiveBillEntryModel>();

            if (billObj.Contains("PUR_ReceiveEntry") && billObj["PUR_ReceiveEntry"] != null)
            {
                DynamicObjectCollection detailEntity = billObj["PUR_ReceiveEntry"] as DynamicObjectCollection;
                if (detailEntity != null && detailEntity.Count > 0)
                {
                    int seq = 1;
                    foreach (DynamicObject row in detailEntity)
                    {
                        if (row == null) continue;

                        var entry = new ReceiveBillEntryModel();
                        entry.F_PVCE_HL_SRMSFEQ = seq++;

                        if (row.Contains("Id") && row["Id"] != null) entry.FEntryID = Convert.ToInt32(row["Id"]);
                        if (row.Contains("FStockQty") && row["FStockQty"] != null) entry.FStockQty = Convert.ToDecimal(row["FStockQty"]);
                        if (row.Contains("FInStockQty") && row["FInStockQty"] != null) entry.FInStockQty = Convert.ToDecimal(row["FInStockQty"]);
                        if (row.Contains("FActlandQty") && row["FActlandQty"] != null) entry.FActlandQty = Convert.ToDecimal(row["FActlandQty"]);
                        if (row.Contains("FActReceiveQty") && row["FActReceiveQty"] != null) entry.FActReceiveQty = Convert.ToDecimal(row["FActReceiveQty"]);
                        if (row.Contains("FPriceUnitQty") && row["FPriceUnitQty"] != null) entry.FPriceUnitQty = Convert.ToDecimal(row["FPriceUnitQty"]);
                        if (row.Contains("FTAXPRICE") && row["FTAXPRICE"] != null) entry.FTAXPRICE = Convert.ToDecimal(row["FTAXPRICE"]);
                        if (row.Contains("FEntryTaxRate") && row["FEntryTaxRate"] != null) entry.FEntryTaxRate = Convert.ToDecimal(row["FEntryTaxRate"]);

                        if (row.Contains("FSrcBillNo") && row["FSrcBillNo"] != null) entry.FSrcBillNo = row["FSrcBillNo"].ToString();
                        if (row.Contains("FMtoNo") && row["FMtoNo"] != null) entry.FMtoNo = row["FMtoNo"].ToString();
                        if (row.Contains("FDescription") && row["FDescription"] != null) entry.FDescription = row["FDescription"].ToString();

                        // 分录基础资料
                        SetBaseDataNumber(row, "MaterialID", entry.FMaterialId);
                        SetBaseDataNumber(row, "FUnitId", entry.FUnitId);
                        SetBaseDataNumber(row, "FPriceUnitId", entry.FPriceUnitId);
                        SetBaseDataNumber(row, "222FStockUnitID", entry.FStockUnitID);
                        SetBaseDataNumber(row, "FSTOCKID", entry.FSTOCKID);

                        // 辅助属性构建
                        entry.FAuxPropId = BuildAuxPropData(row);

                        // 单据关联链路构建
                        entry.FDetailEntity_Link = GetLinkData(row);

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
        private List<DetailEntityLinkModel> GetLinkData(DynamicObject row)
        {
            var linkList = new List<DetailEntityLinkModel>();

            if (row != null && row.Contains("FDetailEntity_Link") && row["FDetailEntity_Link"] != null)
            {
                DynamicObjectCollection links = row["FDetailEntity_Link"] as DynamicObjectCollection;
                if (links != null && links.Count > 0)
                {
                    foreach (DynamicObject link in links)
                    {
                        if (link == null) continue;

                        var linkData = new DetailEntityLinkModel();

                        if (link.Contains("FlowId") && link["FlowId"] != null) linkData.FDetailEntity_Link_FFlowId = link["FlowId"].ToString();
                        if (link.Contains("FFlowLineId") && link["FFlowLineId"] != null) linkData.FDetailEntity_Link_FFlowLineId = link["FFlowLineId"].ToString();
                        if (link.Contains("FSBillId") && link["FSBillId"] != null) linkData.FDetailEntity_Link_FSBillId = Convert.ToInt64(link["FSBillId"]);
                        if (link.Contains("FSId") && link["FSId"] != null) linkData.FDetailEntity_Link_FSId = Convert.ToInt64(link["FSId"]);
                        if (link.Contains("FBaseUnitQtyOld") && link["FBaseUnitQtyOld"] != null) linkData.FDetailEntity_Link_FBaseUnitQtyOld = link["FBaseUnitQtyOld"].ToString();
                        if (link.Contains("FBaseUnitQty") && link["FBaseUnitQty"] != null) linkData.FDetailEntity_Link_FBaseUnitQty = link["FBaseUnitQty"].ToString();

                        linkList.Add(linkData);
                    }
                }
            }

            if (linkList.Count == 0)
            {
                // 无关联时添加默认空数据
                linkList.Add(new DetailEntityLinkModel());
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

        /// <summary>
        /// 单据类型转换逻辑
        /// </summary>
        private string GetBillTypeNumber(DynamicObject billObj)
        {
            try
            {
                if (billObj != null && billObj.Contains("FSrcBillTypeId") && billObj["FSrcBillTypeId"] != null)
                {
                    DynamicObject poTypeObj = billObj["FSrcBillTypeId"] as DynamicObject;
                    if (poTypeObj != null && poTypeObj.Contains("FNumber") && poTypeObj["FNumber"] != null)
                    {
                        string poBillType = poTypeObj["FNumber"].ToString();
                        if (poBillType == "CGDD02_SYS") return "SLD03_SYS";
                        if (poBillType == "CGDD09_SYS") return "SLD09_SYS";
                    }
                }
            }
            catch
            {
                // 忽略异常，降级返回默认值
            }
            return "SLD01_SYS";
        }
    }
}