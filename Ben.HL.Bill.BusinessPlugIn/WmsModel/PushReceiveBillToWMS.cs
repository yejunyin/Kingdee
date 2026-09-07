using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;
using Newtonsoft.Json;

namespace Ben.HL.Bill.BusinessPlugIn
{
    [HotUpdate]
    [Description("收料通知单审核推送WMS-Ben")]
    public class PushReceiveBillToWMS : AbstractBillPlugIn
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
                List<ReceiveEntrySource> entries = GetEntrySources(billObj);
                Dictionary<string, string> materialOrgValues = QueryMaterialOrgValues(config, entries);
                HashSet<string> wmsOrgValues = new HashSet<string>(config.WmsOrgValues, StringComparer.OrdinalIgnoreCase);
                List<ReceiveEntrySource> wmsEntries = entries
                    .Where(entry => materialOrgValues.ContainsKey(entry.MaterialId)
                        && IsWmsValue(materialOrgValues[entry.MaterialId], wmsOrgValues))
                    .ToList();

                WmsSaveReceiveBillRequestModel wmsRequest = BuildStandardApiRequest(billObj, entries);
                string wmsJson = Serialize(wmsRequest);
                _apiClient.PostJson(config.WmsApiUrl, wmsJson);

                if (wmsEntries.Count == 0)
                {
                    this.View.ShowMessage("WMS推送成功；没有需要回写SRM的WMS管辖分录");
                    return;
                }

                SrmReceiveRequestModel srmRequest = BuildSrmRequest(billObj, wmsEntries, config);
                string srmResponse = _apiClient.PostJson(config.SrmApiUrl, Serialize(srmRequest));
                this.View.ShowMessage(string.Format("WMS推送成功，SRM批量回写成功，共{0}条。SRM返回：{1}", wmsEntries.Count, srmResponse));
            }
            catch (Exception ex)
            {
                this.View.ShowMessage("收料通知单联动处理异常：" + ex.Message);
            }
        }

        private Dictionary<string, string> QueryMaterialOrgValues(IntegrationConfig config, List<ReceiveEntrySource> entries)
        {
            List<string> materialIds = entries.Select(entry => entry.MaterialId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            var result = new Dictionary<string, string>();
            if (materialIds.Count == 0)
            {
                return result;
            }

            string ids = string.Join(",", materialIds.Select(ToNumericId));
            string sql = string.Format(@"
                SELECT FMATERIALID, F_BHD_ORGID_9ZU
                FROM T_BD_MATERIAL
                WHERE FMATERIALID IN ({0})", ids);

            DynamicObjectCollection rows = DBServiceHelper.ExecuteDynamicObject(this.Context, sql);
            foreach (DynamicObject row in rows)
            {
                string materialId = row["FMATERIALID"] == null ? string.Empty : row["FMATERIALID"].ToString();
                string orgValue = row["F_BHD_ORGID_9ZU"] == null ? string.Empty : row["F_BHD_ORGID_9ZU"].ToString();
                if (!string.IsNullOrEmpty(materialId))
                {
                    result[materialId] = orgValue;
                }
            }
            return result;
        }

        private string ToNumericId(string value)
        {
            long id;
            if (!long.TryParse(value, out id))
            {
                throw new Exception("物料内码格式错误：" + value);
            }
            return id.ToString();
        }

        private bool IsWmsValue(string materialOrgValue, HashSet<string> configuredValues)
        {
            return materialOrgValue.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(value => configuredValues.Contains(value.Trim()));
        }

        private List<ReceiveEntrySource> GetEntrySources(DynamicObject billObj)
        {
            var result = new List<ReceiveEntrySource>();
            DynamicObjectCollection rows = billObj["PUR_ReceiveEntry"] as DynamicObjectCollection;
            if (rows == null)
            {
                return result;
            }

            int seq = 1;
            foreach (DynamicObject row in rows)
            {
                DynamicObject material = row["MaterialID"] as DynamicObject;
                string materialId = GetValue(material, "Id");
                result.Add(new ReceiveEntrySource
                {
                    Row = row,
                    Seq = seq++,
                    MaterialId = materialId
                });
            }
            return result;
        }

        private WmsSaveReceiveBillRequestModel BuildStandardApiRequest(DynamicObject billObj, List<ReceiveEntrySource> selectedEntries)
        {
            var request = new WmsSaveReceiveBillRequestModel();
            ReceiveBillHeaderModel header = request.data.Model;

            header.FID = GetLong(billObj, "Id");
            header.F_HL_SRM_Source_NO = GetValue(billObj, "HL_SRM_Source_NO");
            header.FBusinessType = GetValue(billObj, "BusinessType");
            header.FDate = GetDate(billObj, "Date");
            header.FOwnerTypeIdHead = GetValue(billObj, "OwnerTypeIdHead");
            header.F_PVCE_Combo = GetValue(billObj, "F_PVCE_Combo");
            header.FNote = GetValue(billObj, "Note");
            header.FBillTypeID.FNumber = GetBillTypeNumber(billObj);
            SetBaseDataNumber(billObj, "StockOrgId", header.FStockOrgId);
            SetBaseDataNumber(billObj, "PurOrgId", header.FPurOrgId);
            SetBaseDataNumber(billObj, "SupplierId", header.FSupplierId);
            SetBaseDataNumber(billObj, "SupplyId", header.FSupplyId);
            SetBaseDataNumber(billObj, "OwnerIdHead", header.FOwnerIdHead);

            DynamicObjectCollection financeRows = billObj["Receivefinance"] as DynamicObjectCollection;
            if (financeRows != null && financeRows.Count > 0)
            {
                SetBaseDataNumber(financeRows[0], "SettleOrgId", header.FinanceEntity.FSettleOrgId);
                SetBaseDataNumber(financeRows[0], "SettleCurrId", header.FinanceEntity.FSettleCurrId);
                header.FinanceEntity.FPricePoint = GetValue(financeRows[0], "PricePoint");
            }

            header.FDetailEntity = selectedEntries.Select(BuildEntry).ToList();
            return request;
        }
        
        /// <summary>
        /// 构建分录列表
        /// </summary>
        private ReceiveBillEntryModel BuildEntry(ReceiveEntrySource source)
        {
            DynamicObject row = source.Row;
            var entry = new ReceiveBillEntryModel
            {
                F_PVCE_HL_SRMSFEQ = source.Seq,
                FEntryID = Convert.ToInt32(GetLong(row, "Id")),
                FStockQty = GetDecimal(row, "StockQty"),
                FInStockQty = GetDecimal(row, "InStockQty"),
                FActlandQty = GetDecimal(row, "ActlandQty"),
                FActReceiveQty = GetDecimal(row, "ActReceiveQty"),
                FPriceUnitQty = GetDecimal(row, "PriceUnitQty"),
                FTAXPRICE = GetDecimal(row, "TAXPRICE"),
                FEntryTaxRate = GetDecimal(row, "EntryTaxRate"),
                FSrcBillNo = GetValue(row, "SrcBillNo"),
                FMtoNo = GetValue(row, "MtoNo"),
                FDescription = GetValue(row, "Description")
            };
            SetBaseDataNumber(row, "MaterialID", entry.FMaterialId);
            SetBaseDataNumber(row, "UnitId", entry.FUnitId);
            SetBaseDataNumber(row, "PriceUnitId", entry.FPriceUnitId);
            SetBaseDataNumber(row, "StockUnitID", entry.FStockUnitID);
            SetBaseDataNumber(row, "STOCKID", entry.FSTOCKID);
            entry.FDetailEntity_Link = GetLinkData(row);
            return entry;
        }

        private SrmReceiveRequestModel BuildSrmRequest(DynamicObject billObj, List<ReceiveEntrySource> selectedEntries, IntegrationConfig config)
        {
            string erpDoc = GetValue(billObj, "BillNo");
            DynamicObject supplier = billObj["SupplierId"] as DynamicObject;
            string receiveDoc = GetValue(billObj, "HL_SRM_Source_NO");

            return new SrmReceiveRequestModel
            {
                groupCode = config.SrmGroupCode,
                ERPDoc = erpDoc,
                userCode = config.SrmUserCode,
                departmentCode = config.SrmDepartmentCode,
                docDate = GetDate(billObj, "Date").Replace("-", "/").Substring(0, 10),
                supCode = GetValue(supplier, "Number"),
                supNamess = GetValue(supplier, "Name"),
                srtRtfList = selectedEntries.Select(entry => new SrmReceiveEntryModel
                {
                    docSN = entry.Seq,
                    recieveDoc = receiveDoc,
                    recieveDocSN = entry.Seq,
                    srmrecieveDocSN = GetValue(entry.Row, "F_PVCE_HL_SRMSFEQ"),
                    materialCode = GetValue(entry.Row["MaterialID"] as DynamicObject, "Number"),
                    materialName = GetValue(entry.Row["MaterialID"] as DynamicObject, "Name"),
                    materialSpec = GetValue(entry.Row["MaterialID"] as DynamicObject, "Specification"),
                    unitCode = GetValue(entry.Row["UnitId"] as DynamicObject, "Number"),
                    batchNo = GetValue(entry.Row, "Lot_Text"),
                    num = GetDecimal(entry.Row, "ActReceiveQty"),
                    warehouseCode = GetValue(entry.Row["STOCKID"] as DynamicObject, "Number"),
                    locationCode = GetValue(entry.Row, "LocationId")
                }).ToList()
            };
        }

        /// <summary>
        /// 构建关联表数据
        /// </summary>
        private List<DetailEntityLinkModel> GetLinkData(DynamicObject row)
        {
            var result = new List<DetailEntityLinkModel>();
            DynamicObjectCollection links = row["FDetailEntity_Link"] as DynamicObjectCollection;
            if (links != null)
            {
                foreach (DynamicObject link in links)
                {
                    result.Add(new DetailEntityLinkModel
                    {
                        FDetailEntity_Link_FFlowId = GetValue(link, "FlowId"),
                        FDetailEntity_Link_FFlowLineId = GetValue(link, "FlowLineId"),
                        FDetailEntity_Link_FSBillId = GetLong(link, "SBillId"),
                        FDetailEntity_Link_FSId = GetLong(link, "SId"),
                        FDetailEntity_Link_FBaseUnitQtyOld = GetValue(link, "BaseUnitQtyOld"),
                        FDetailEntity_Link_FBaseUnitQty = GetValue(link, "BaseUnitQty")
                    });
                }
            }
            return result;
        }
        /// <summary>
        /// 安全提取 DynamicObject 基础资料中的 FNumber 辅助方法
        /// </summary>
        private void SetBaseDataNumber(DynamicObject source, string fieldName, BaseDataModel target)
        {
            DynamicObject value = source == null ? null : source[fieldName] as DynamicObject;
            target.FNumber = GetValue(value, "Number");
        }

        private string GetBillTypeNumber(DynamicObject billObj)
        {
            DynamicObject sourceType = billObj["SrcBillTypeId"] as DynamicObject;
            string number = GetValue(sourceType, "Number");
            if (number == "CGDD02_SYS") return "SLD03_SYS";
            if (number == "CGDD09_SYS") return "SLD09_SYS";
            return "SLD01_SYS";
        }

        private string GetValue(DynamicObject source, string fieldName)
        {
            return source != null && source.Contains(fieldName) && source[fieldName] != null ? source[fieldName].ToString() : string.Empty;
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
            return DateTime.TryParse(GetValue(source, fieldName), out value) ? value.ToString("yyyy-MM-dd HH:mm:ss") : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private class ReceiveEntrySource
        {
            public DynamicObject Row { get; set; }
            public int Seq { get; set; }
            public string MaterialId { get; set; }
        }
    }
}
