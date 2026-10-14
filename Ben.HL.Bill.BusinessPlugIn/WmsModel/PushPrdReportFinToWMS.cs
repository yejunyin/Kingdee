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
    /// 生产汇报单(PRD_MORPT)审核后推送WMS，WMS完成入库后回写创建生产入库单(PRD_InStock)
    /// 推送数据格式为生产入库单标准API结构，WMS可直接调用PRD_InStock保存接口
    /// 字段名已按 PRD_MORPT 真实单据结构核对（表头+PRD_MORPTENTRY分录）
    /// </summary>
    [HotUpdate]
    [Description("生产汇报单审核推送WMS-Ben")]
    public class PushPrdReportFinToWMS : AbstractBillPlugIn
    {
        private readonly WmsApiClient _apiClient = new WmsApiClient();

        public override void AfterDoOperation(AfterDoOperationEventArgs e)
        {
            base.AfterDoOperation(e);

            if (!e.Operation.Operation.Equals("Save", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                DynamicObject billObj = this.Model.DataObject;
                if (billObj == null)
                {
                    this.View.ShowMessage("没有找到需要推送的生产汇报单数据");
                    return;
                }

                IntegrationConfig config = IntegrationConfig.Load();
                // 推送的数据结构是【生产入库单 PRD_InStock】的标准API格式，
                // WMS 可直接用此结构调用金蝶 PRD_InStock.Save 创建生产入库单
                PrdInStockSaveRequestModel wmsRequest = BuildPrdInStockDataFromReport(billObj);
                string jsonData = Serialize(wmsRequest);

                string response = _apiClient.PostJson(config.WmsPrdReportFinApiUrl, jsonData);
                this.View.ShowMessage("生产汇报单推送WMS成功。WMS返回：" + response);
            }
            catch (Exception ex)
            {
                this.View.ShowMessage("生产汇报单推送WMS异常：" + ex.Message);
            }
        }

        /// <summary>
        /// 从 PRD_MORPT（生产汇报单）提取数据，组装成 PRD_InStock（生产入库单）的标准API请求结构
        /// </summary>
        private PrdInStockSaveRequestModel BuildPrdInStockDataFromReport(DynamicObject reportObj)
        {
            var request = new PrdInStockSaveRequestModel();
            request.data.Model = BuildHeader(reportObj);
            request.data.Model.FEntity = BuildEntryList(reportObj);
            return request;
        }

        /// <summary>
        /// 构建生产入库单表头（来源：生产汇报单表头 + 分录上的组织/车间/货主）
        /// </summary>
        private PrdInStockHeaderModel BuildHeader(DynamicObject reportObj)
        {
            var header = new PrdInStockHeaderModel();

            // === PRD_MORPT 表头字段：Id / BillNo / Date / BillType / PrdOrgId / Description ===
            header.FID = GetLong(reportObj, "Id");
            header.FBillNo = GetValue(reportObj, "BillNo");
            header.FDate = GetDate(reportObj, "Date");
            header.FDescription = GetValue(reportObj, "Description");
            SetBaseDataNumber(reportObj, "BillType", header.FBillTypeID);
            SetBaseDataNumber(reportObj, "PrdOrgId", header.FPrdOrgId);

            // === PRD_MORPT 表头没有入库组织/车间/货主字段，从第一条分录取值 ===
            DynamicObject firstEntry = GetFirstEntry(reportObj);
            if (firstEntry != null)
            {
                SetBaseDataNumber(firstEntry, "StockInOrgId", header.FStockOrgId); // 入库组织
                SetBaseDataNumber(firstEntry, "WorkshopId", header.FWorkShopId);   // 生产车间（分录上的 WorkshopId，表头的为空）
                header.FOwnerTypeIdHead = GetValue(firstEntry, "OwnerTypeId");     // 货主类型 BD_OwnerOrg
                SetBaseDataNumber(firstEntry, "OwnerId", header.FOwnerIdHead);     // 货主
            }
            if (string.IsNullOrEmpty(header.FOwnerTypeIdHead))
            {
                header.FOwnerTypeIdHead = "BD_OwnerOrg";
            }

            return header;
        }

        /// <summary>
        /// 构建生产入库单分录（来源：生产汇报单分录 PRD_MORPTENTRY）
        /// </summary>
        private List<PrdInStockEntryModel> BuildEntryList(DynamicObject reportObj)
        {
            var result = new List<PrdInStockEntryModel>();

            // PRD_MORPT 的单据体实体名为 PRD_MORPTENTRY
            DynamicObjectCollection rows = reportObj["PRD_MORPTENTRY"] as DynamicObjectCollection;
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
                    // 入库数量取汇报单的合格数量 QuaQty / BaseQuaQty
                    FRealQty = GetDecimal(row, "QuaQty"),
                    FBaseRealQty = GetDecimal(row, "BaseQuaQty"),
                    // 批次：汇报单的 Lot_Text（文本）/ Lot（批次主档）
                    FBatchNo = GetBatchNo(row),
                    FMoBillNo = GetValue(row, "MoBillNo"),
                    FMOSEQ = (int)GetLong(row, "MoSeq"),
                    // 源单关联：SrcBillType / SrcBillNo 指向生产订单 PRD_MO
                    FSrcBillNo = GetValue(row, "SrcBillNo"),
                    FSrcFormId = GetValue(row, "SrcBillType"),
                    FAuxPropId = BuildAuxPropData(row),
                    FEntity_Link = GetLinkData(row)
                };

                SetBaseDataNumber(row, "MaterialId", entry.FMaterialId);
                SetBaseDataNumber(row, "UnitID", entry.FUnitId);
                SetBaseDataNumber(row, "BaseUnitId", entry.FBaseUnitId);
                // 仓库/仓位：汇报单分录上有 StockId/StockLocId 字段，但通常为空，为空时由 WMS 回写时分配
                SetBaseDataNumber(row, "StockId", entry.FStockId);
                SetBaseDataNumber(row, "StockLocId", entry.FStockLocId);
                // 汇报单上没有库存状态字段，FStockStatusId 由 WMS 回写时指定

                result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// 取第一条汇报单分录（入库组织/车间/货主在分录上）
        /// </summary>
        private DynamicObject GetFirstEntry(DynamicObject reportObj)
        {
            DynamicObjectCollection rows = reportObj == null ? null : reportObj["PRD_MORPTENTRY"] as DynamicObjectCollection;
            return (rows != null && rows.Count > 0) ? rows[0] : null;
        }

        /// <summary>
        /// 汇报单批次字段：优先取文本 Lot_Text，为空时取批次主档 Lot 的编码
        /// </summary>
        private string GetBatchNo(DynamicObject row)
        {
            string lotText = GetValue(row, "Lot_Text");
            if (!string.IsNullOrEmpty(lotText))
            {
                return lotText;
            }
            DynamicObject lotObj = row != null && row.Contains("Lot") ? row["Lot"] as DynamicObject : null;
            return GetValue(lotObj, "Number");
        }

        private Dictionary<string, BaseDataModel> BuildAuxPropData(DynamicObject row)
        {
            var auxPropDict = new Dictionary<string, BaseDataModel>();
            string[] auxFields = new string[]
            {
                "FAUXPROPID__FF100001", "FAUXPROPID__FF100002", "FAUXPROPID__FF100003",
                "FAUXPROPID__FF100004", "FAUXPROPID__FF100005", "FAUXPROPID__FF100006"
            };

            DynamicObject auxPropObj = row == null ? null : row["AuxPropId"] as DynamicObject;
            foreach (string field in auxFields)
            {
                var baseData = new BaseDataModel();
                if (auxPropObj != null && auxPropObj.Contains(field) && auxPropObj[field] != null)
                {
                    DynamicObject ffPropObj = auxPropObj[field] as DynamicObject;
                    if (ffPropObj != null)
                    {
                        baseData.FNumber = GetValue(ffPropObj, "Number");
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
                    FEntity_Link_FFlowLineId = GetValue(link, "FlowLineId"),
                    FEntity_Link_FRuleId = GetValue(link, "RuleId"),
                    FEntity_Link_FSTableName = GetValue(link, "STableName"),
                    FEntity_Link_FSBillId = GetLong(link, "SBillId"),
                    FEntity_Link_FSId = GetLong(link, "SId")
                });
            }
            return result;
        }

        private void SetBaseDataNumber(DynamicObject source, string fieldName, BaseDataModel target)
        {
            DynamicObject value = source != null && source.Contains(fieldName) ? source[fieldName] as DynamicObject : null;
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
