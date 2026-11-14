using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kingdee.BOS;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 收料通知单推送WMS/SRM的公共业务服务
    /// 供表单插件 PushReceiveBillToWMS 和列表插件 PushReceiveBillListToWMS 共用
    /// </summary>
    public class ReceiveBillPushService
    {
        private readonly Context _ctx;

        public ReceiveBillPushService(Context ctx)
        {
            _ctx = ctx;
        }

        #region 单据分录取值 & 拣货仓库判断

        public List<ReceiveEntrySource> GetEntrySources(DynamicObject billObj)
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
                result.Add(new ReceiveEntrySource
                {
                    Row = row,
                    Seq = seq++
                });
            }
            return result;
        }

        /// <summary>
        /// 判断分录中是否存在物料拣货仓库(T_BD_MATERIALPRODUCE.FPICKSTOCKID对应编号)为2010的行。
        /// PickStockId未映射到收料通知单分录物料对象，直接查询物料基础资料表。
        /// </summary>
        public bool ExistsPickStock2010(DynamicObject billObj)
        {
            DynamicObjectCollection rows = billObj["PUR_ReceiveEntry"] as DynamicObjectCollection;
            if (rows == null || rows.Count == 0)
            {
                return false;
            }

            var materialIds = new List<long>();
            foreach (DynamicObject row in rows)
            {
                DynamicObject material = row["MaterialID"] as DynamicObject;
                long materialId = GetLong(material, "Id");
                if (materialId > 0 && !materialIds.Contains(materialId))
                {
                    materialIds.Add(materialId);
                }
            }
            if (materialIds.Count == 0)
            {
                return false;
            }

            // 收料组织（库存组织）
            DynamicObject stockOrg = billObj["StockOrgId"] as DynamicObject;
            long stockOrgId = GetLong(stockOrg, "Id");
            if (stockOrgId <= 0)
            {
                return false;
            }

            // 物料生产页签表 FPICKSTOCKID -> T_BD_STOCK.FSTOCKID，按收料组织 FUSEORGID 限定
            string sql = string.Format(@"
                                    SELECT TOP 1 1
                                    FROM T_BD_MATERIALPRODUCE p
                                    INNER JOIN T_BD_STOCK s ON s.FSTOCKID = p.FPICKSTOCKID
                                    WHERE p.FMATERIALID IN ({0})
                                    AND p.FUSEORGID = {1}
                                    AND s.FNUMBER = '2010'", string.Join(",", materialIds), stockOrgId);

            DynamicObjectCollection result = DBUtils.ExecuteDynamicObject(_ctx, sql);
            return result != null && result.Count > 0;
        }

        #endregion

        #region 请求模型构建

        public WmsSaveReceiveBillRequestModel BuildStandardApiRequest(DynamicObject billObj, List<ReceiveEntrySource> selectedEntries)
        {
            var request = new WmsSaveReceiveBillRequestModel();
            ReceiveBillHeaderModel header = request.data.Model;

            header.FID = GetLong(billObj, "Id");
            header.FBillNo = GetValue(billObj, "BillNo");
            header.F_HL_SRM_Source_NO = GetValue(billObj, "HL_SRM_Source_NO");
            header.FBusinessType = GetValue(billObj, "BusinessType");
            header.FDate = GetDate(billObj, "Date");
            header.FOwnerTypeIdHead = GetValue(billObj, "OwnerTypeIdHead");
            header.F_PVCE_Combo = GetValue(billObj, "F_PVCE_Combo");
            header.FNote = GetValue(billObj, "Note");
            SetBaseDataNumber(billObj, "BillTypeId", header.FBillTypeID);
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

            LoadPurchaseOrderTaxRates(selectedEntries);
            header.FDetailEntity = selectedEntries.Select(BuildEntry).ToList();
            return request;
        }

        /// <summary>
        /// 从收料通知单分录的源单关联(FDetailEntity_Link)中提取采购订单分录内码(FSId)，
        /// 批量查询 t_PUR_POOrderEntry 的 FEntryTaxRate 并回填到 ReceiveEntrySource.PurchaseOrderTaxRate。
        /// 仅当收料通知单分录自身没有税率时才需要从源单取值。
        /// </summary>
        private void LoadPurchaseOrderTaxRates(List<ReceiveEntrySource> entries)
        {
            if (entries == null || entries.Count == 0) return;

            // 1) 收集所有关联的采购订单分录内码 (FSId)
            var poEntryIds = new List<long>();
            foreach (var entry in entries)
            {
                DynamicObjectCollection links = entry.Row != null
                    ? entry.Row["FDetailEntity_Link"] as DynamicObjectCollection
                    : null;
                if (links == null) continue;
                foreach (DynamicObject link in links)
                {
                    long sId = GetLong(link, "SId");
                    if (sId > 0 && !poEntryIds.Contains(sId))
                    {
                        poEntryIds.Add(sId);
                    }
                }
            }
            if (poEntryIds.Count == 0) return;

            // 2) 批量查询采购订单分录税率（物理表 T_PUR_POORDERENTRY_F，税率字段 FTAXRATE）
            string sql = string.Format(
                "SELECT FEntryID, FTAXRATE FROM T_PUR_POORDERENTRY_F WHERE FEntryID IN ({0})",
                string.Join(",", poEntryIds));

            DynamicObjectCollection result = DBUtils.ExecuteDynamicObject(_ctx, sql);
            if (result == null || result.Count == 0) return;

            var taxRateMap = new Dictionary<long, decimal>();
            foreach (DynamicObject r in result)
            {
                long id = GetLong(r, "FEntryID");
                decimal rate = GetDecimal(r, "FTAXRATE");
                if (id > 0 && !taxRateMap.ContainsKey(id))
                {
                    taxRateMap[id] = rate;
                }
            }

            // 3) 回填到每个 entry：取第一条能匹配上的采购订单分录税率
            foreach (var entry in entries)
            {
                DynamicObjectCollection links = entry.Row != null
                    ? entry.Row["FDetailEntity_Link"] as DynamicObjectCollection
                    : null;
                if (links == null) continue;
                foreach (DynamicObject link in links)
                {
                    long sId = GetLong(link, "SId");
                    if (sId > 0 && taxRateMap.ContainsKey(sId))
                    {
                        entry.PurchaseOrderTaxRate = taxRateMap[sId];
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 构建分录列表
        /// </summary>
        public ReceiveBillEntryModel BuildEntry(ReceiveEntrySource source)
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
                // 收料通知单分录自身无税率字段，优先取源单(采购订单分录)查到的税率，
                // 若未查到(无关联或关联的PO无税率)则回退尝试分录自身字段，最后兜底为 0
                FEntryTaxRate = source.PurchaseOrderTaxRate != 0
                    ? source.PurchaseOrderTaxRate
                    : GetDecimal(row, "EntryTaxRate"),
                FSrcBillNo = GetValue(row, "SrcBillNo"),
                FMtoNo = GetValue(row, "MtoNo"),
                FDescription = GetValue(row, "Description"),
                FAuxPropId = BuildAuxPropData(row)
            };
            SetBaseDataNumber(row, "MaterialID", entry.FMaterialId);
            SetBaseDataNumber(row, "UnitId", entry.FUnitId);
            SetBaseDataNumber(row, "PriceUnitId", entry.FPriceUnitId);
            SetBaseDataNumber(row, "StockUnitID", entry.FStockUnitID);
            entry.FSTOCKID.FNumber = "2010";
            entry.FLot = GetValue(row, "Lot_Text");
            entry.FDetailEntity_Link = GetLinkData(row);
            return entry;
        }

        public SrmReceiveRequestModel BuildSrmRequest(DynamicObject billObj, List<ReceiveEntrySource> selectedEntries, IntegrationConfig config)
        {
            string erpDoc = GetValue(billObj, "BillNo");
            DynamicObject supplier = billObj["SupplierId"] as DynamicObject;
            DynamicObject groupCode = billObj["StockOrgId"] as DynamicObject;
            DynamicObject userCode = billObj["PurchaserId"] as DynamicObject;
            string receiveDoc = GetValue(billObj, "HL_SRM_Source_NO");

            return new SrmReceiveRequestModel
            {
                groupCode = GetValue(groupCode, "Number"),
                ERPDoc = erpDoc,
                userCode = GetValue(userCode, "Number"),
                departmentCode = "1008",
                docDate = GetDate(billObj, "Date").Substring(0, 10),
                supCode = GetValue(supplier, "Number"),
                supNamess = GetValue(supplier, "Name"),
                srtRtfList = selectedEntries.Select(entry => new SrmReceiveEntryModel
                {
                    docSN = entry.Seq,
                    recieveDoc = erpDoc,
                    recieveDocSN = entry.Seq,
                    srmrecieveDocSN = receiveDoc,
                    materialCode = GetValue(entry.Row["MaterialID"] as DynamicObject, "Number"),
                    materialName = GetValue(entry.Row["MaterialID"] as DynamicObject, "Name"),
                    materialSpec = GetValue(entry.Row["MaterialID"] as DynamicObject, "Specification"),
                    unitCode = GetValue(entry.Row["UnitId"] as DynamicObject, "Number"),
                    batchNo = GetValue(entry.Row, "Lot_Text"),
                    num = GetDecimal(entry.Row, "ActReceiveQty"),
                    warehouseCode = GetValue(entry.Row["STOCKID"] as DynamicObject, "Number"),
                    locationCode = GetValue(entry.Row["STOCKID"] as DynamicObject, "Number") + ".01",
                }).ToList()
            };
        }

        /// <summary>
        /// 构建关联表数据
        /// </summary>
        public List<DetailEntityLinkModel> GetLinkData(DynamicObject row)
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

        #endregion

        #region 通用辅助方法

        /// <summary>
        /// 构建分录辅助资料字典，输出结构：{ "FAUXPROPID__FF100001": { "FNumber": "xxx" }, ... }
        /// 取值路径：row → AuxPropId → F100001~F100006 → Number
        /// </summary>
        private static Dictionary<string, BaseDataModel> BuildAuxPropData(DynamicObject row)
        {
            // Key = WMS API 期望的字典键；Value = 金蝶 DynamicObject 里 AuxPropId 子实体的真实字段名
            var fieldMap = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("FAUXPROPID__FF100001", "F100001"),
                new KeyValuePair<string, string>("FAUXPROPID__FF100002", "F100002"),
                new KeyValuePair<string, string>("FAUXPROPID__FF100003", "F100003"),
                new KeyValuePair<string, string>("FAUXPROPID__FF100004", "F100004"),
                new KeyValuePair<string, string>("FAUXPROPID__FF100005", "F100005"),
                new KeyValuePair<string, string>("FAUXPROPID__FF100006", "F100006"),
            };

            var auxPropDict = new Dictionary<string, BaseDataModel>();
            DynamicObject auxPropObj = row != null ? row["AuxPropId"] as DynamicObject : null;
            foreach (var kv in fieldMap)
            {
                var baseData = new BaseDataModel();
                if (auxPropObj != null && auxPropObj.Contains(kv.Value) && auxPropObj[kv.Value] != null)
                {
                    DynamicObject ffPropObj = auxPropObj[kv.Value] as DynamicObject;
                    if (ffPropObj != null)
                    {
                        baseData.FNumber = GetValue(ffPropObj, "FNumber");
                    }
                }
                auxPropDict[kv.Key] = baseData;
            }
            return auxPropDict;
        }

        /// <summary>
        /// 安全提取 DynamicObject 基础资料中的 FNumber 辅助方法
        /// </summary>
        public static void SetBaseDataNumber(DynamicObject source, string fieldName, BaseDataModel target)
        {
            DynamicObject value = source == null ? null : source[fieldName] as DynamicObject;
            target.FNumber = GetValue(value, "Number");
        }

        public static string GetValue(DynamicObject source, string fieldName)
        {
            return source != null && source.Contains(fieldName) && source[fieldName] != null ? source[fieldName].ToString() : string.Empty;
        }

        public static long GetLong(DynamicObject source, string fieldName)
        {
            long value;
            return long.TryParse(GetValue(source, fieldName), out value) ? value : 0;
        }

        public static decimal GetDecimal(DynamicObject source, string fieldName)
        {
            decimal value;
            return decimal.TryParse(GetValue(source, fieldName), out value) ? value : 0;
        }

        public static string GetDate(DynamicObject source, string fieldName)
        {
            DateTime value;
            return DateTime.TryParse(GetValue(source, fieldName), out value) ? value.ToString("yyyy-MM-dd HH:mm:ss") : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        #endregion

        #region 响应解析 & Fwmsstatus 组装

        /// <summary>
        /// 解析 WMS JSON 返回，提取 msg 字段
        /// 示例结构：{"code":200,"msg":"ERP采购收货申请已接收...","data":{...}}
        /// </summary>
        public static string ParseWmsResponse(string wmsResponse)
        {
            if (string.IsNullOrWhiteSpace(wmsResponse))
            {
                return "WMS返回为空";
            }
            try
            {
                JObject json = JObject.Parse(wmsResponse);
                JToken msgToken = json["msg"];
                if (msgToken != null && msgToken.Type != JTokenType.Null)
                {
                    return msgToken.ToString();
                }
            }
            catch
            {
                // 不是合法 JSON 就直接返回原文
            }
            return wmsResponse;
        }

        /// <summary>
        /// 解析 SRM SOAP 返回，提取 <return> 节点内部 JSON。
        /// 规则：status=200 返回"成功"；其他状态优先取 message 字段，否则拼"失败(status=xxx)"
        /// 示例结构：<soap:Envelope>...<return>{"message":"...","status":"500"}</return>...</soap:Envelope>
        ///           或 <return>{"status":"200"}</return>
        /// </summary>
        public static string ParseSrmResponse(string srmResponse)
        {
            if (string.IsNullOrWhiteSpace(srmResponse))
            {
                return "SRM返回为空";
            }

            string innerJson = null;
            // 正则抓取 <return>...</return> 中间的内容（忽略命名空间前缀）
            var match = Regex.Match(srmResponse, @"<[^>]*return[^>]*>(.*?)</[^>]*return[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success && match.Groups.Count > 1)
            {
                innerJson = match.Groups[1].Value.Trim();
            }

            if (!string.IsNullOrWhiteSpace(innerJson))
            {
                try
                {
                    JObject json = JObject.Parse(innerJson);
                    string status = json["status"] != null ? json["status"].ToString() : string.Empty;
                    if (string.Equals(status, "200", StringComparison.Ordinal))
                    {
                        return "成功";
                    }
                    // 非 200，优先 message，没有就拼 status
                    JToken msgToken = json["message"];
                    if (msgToken != null && msgToken.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(msgToken.ToString()))
                    {
                        return msgToken.ToString();
                    }
                    return string.Format("失败(status={0})", status);
                }
                catch
                {
                    // JSON 解析失败，直接返回 <return> 内容
                    return innerJson;
                }
            }
            return srmResponse;
        }

        /// <summary>
        /// 组装单据头 Fwmsstatus 字段值，格式：WMS: xxx \n SRM: xxx（或 SRM无需同步）
        /// </summary>
        public static string BuildFwmsstatusValue(string wmsMsg, string srmMsg, bool srmSkipped)
        {
            string srmPart = srmSkipped ? "SRM无需同步" : srmMsg;
            return string.Format("WMS: {0}\r\nSRM: {1}", wmsMsg ?? string.Empty, srmPart ?? string.Empty);
        }

        /// <summary>
        /// 直接拼 SQL 更新收料通知单表头自定义字段 FWMSSTATUS。
        /// 单据插件/列表插件场景均可调用，绕过 DynamicObject 只读校验。
        /// </summary>
        public static int SaveFwmsstatus(Context ctx, long billId, string value)
        {
            if (ctx == null || billId <= 0) return 0;
            // T_PUR_RECEIVE 是收料通知单物理表头表；FWMSSTATUS 是自定义字段物理列名（全大写）
            string sql = string.Format(
                "UPDATE T_PUR_RECEIVE SET FWMSSTATUS = N'{0}' WHERE FID = {1}",
                (value ?? string.Empty).Replace("'", "''"),
                billId);
            return DBUtils.Execute(ctx, sql);
        }

        #endregion

        public class ReceiveEntrySource
        {
            public DynamicObject Row { get; set; }
            public int Seq { get; set; }
            /// <summary>
            /// 从源单（采购订单分录）查询得到的税率。默认 0 表示未查到，
            /// 此时 BuildEntry 会回退尝试收料通知单分录自身的 EntryTaxRate 字段。
            /// </summary>
            public decimal PurchaseOrderTaxRate { get; set; }
        }
    }
}
