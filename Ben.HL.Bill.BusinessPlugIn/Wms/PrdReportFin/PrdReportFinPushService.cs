using System;
using System.Collections.Generic;
using Kingdee.BOS;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;
using Newtonsoft.Json;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 生产汇报单(PRD_MORPT)推送WMS的公共业务服务
    /// 供表单插件 PushPrdReportFinToWMS 和列表插件 PushPrdReportFinListToWMS 共用
    /// </summary>
    public class PrdReportFinPushService
    {
        /// <summary>
        /// 从 PRD_MORPT（生产汇报单）提取数据，组装成 PRD_InStock（生产入库单）的标准API请求结构
        /// </summary>
        public static PrdInStockSaveRequestModel BuildPrdInStockDataFromReport(DynamicObject reportObj)
        {
            var request = new PrdInStockSaveRequestModel();
            request.data.Model = BuildHeader(reportObj);
            request.data.Model.FEntity = BuildEntryList(reportObj);
            return request;
        }

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        /// <summary>
        /// 判断生产汇报单分录中是否存在物料拣货仓库(T_BD_MATERIALPRODUCE.FPICKSTOCKID
        /// 对应 T_BD_STOCK.FNUMBER)为 2010 的行。
        /// 生产汇报单表头无库存组织字段，库存组织从分录 StockInOrgId 取第一条。
        /// </summary>
        public static bool ExistsPickStock2010(DynamicObject reportObj, Context ctx)
        {
            if (reportObj == null || ctx == null) return false;

            DynamicObjectCollection rows = reportObj["PRD_MORPTENTRY"] as DynamicObjectCollection;
            if (rows == null || rows.Count == 0) return false;

            // 1) 收集所有分录的物料ID（去重）
            var materialIds = new List<long>();
            long stockOrgId = 0;
            foreach (DynamicObject row in rows)
            {
                if (row == null) continue;

                DynamicObject material = row["MaterialId"] as DynamicObject;
                long mid = GetLong(material, "Id");
                if (mid > 0 && !materialIds.Contains(mid))
                {
                    materialIds.Add(mid);
                }

                // 从第一条有值的分录取库存组织
                if (stockOrgId <= 0)
                {
                    DynamicObject stockOrg = row["StockInOrgId"] as DynamicObject;
                    stockOrgId = GetLong(stockOrg, "Id");
                }
            }

            if (materialIds.Count == 0 || stockOrgId <= 0) return false;

            // 2) 生产汇报单 MaterialId 是物料主表 FMASTERID，联 T_BD_MATERIAL（物料组织行）
            //    和 T_BD_MATERIALPRODUCE（生产页签）一起筛，避免只取到某个组织的数据
            string sql = string.Format(@"
                                    SELECT TOP 1 1
                                    FROM T_BD_MATERIAL m
                                    LEFT JOIN T_BD_MATERIALPRODUCE p
                                        ON p.FMATERIALID = m.FMATERIALID
                                        AND p.FUSEORGID = m.FUSEORGID
                                    INNER JOIN T_BD_STOCK s ON s.FSTOCKID = p.FPICKSTOCKID
                                    WHERE m.FMASTERID IN ({0})
                                    AND p.FUSEORGID = {1}
                                    AND s.FNUMBER = '2010'", string.Join(",", materialIds), stockOrgId);

            DynamicObjectCollection result = DBUtils.ExecuteDynamicObject(ctx, sql);
            return result != null && result.Count > 0;
        }

        #region 构建方法

        private static PrdInStockHeaderModel BuildHeader(DynamicObject reportObj)
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
                SetBaseDataNumber(firstEntry, "StockInOrgId", header.FStockOrgId);
                SetBaseDataNumber(firstEntry, "WorkshopId", header.FWorkShopId);
                header.FOwnerTypeIdHead = GetValue(firstEntry, "OwnerTypeId");
                SetBaseDataNumber(firstEntry, "OwnerId", header.FOwnerIdHead);
            }
            if (string.IsNullOrEmpty(header.FOwnerTypeIdHead))
            {
                header.FOwnerTypeIdHead = "BD_OwnerOrg";
            }

            return header;
        }

        private static List<PrdInStockEntryModel> BuildEntryList(DynamicObject reportObj)
        {
            var result = new List<PrdInStockEntryModel>();

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
                    FRealQty = GetDecimal(row, "QuaQty"),
                    FBaseRealQty = GetDecimal(row, "BaseQuaQty"),
                    FBatchNo = GetBatchNo(row),
                    FMtoNo = GetValue(row, "MtoNo"),
                    FMoBillNo = GetValue(row, "MoBillNo"),
                    FMOSEQ = GetValue(row, "MoSeq"),
                    FSrcBillNo = GetValue(row, "SrcBillNo"),
                    FSrcFormId = GetValue(row, "SrcBillType"),
                    FAuxPropId = BuildAuxPropData(row),
                    FEntity_Link = GetLinkData(row)
                };

                SetBaseDataNumber(row, "MaterialId", entry.FMaterialId);
                SetBaseDataNumber(row, "UnitID", entry.FUnitId);
                SetBaseDataNumber(row, "BaseUnitId", entry.FBaseUnitId);
                SetBaseDataNumber(row, "StockId", entry.FStockId);
                SetBaseDataNumber(row, "StockLocId", entry.FStockLocId);

                result.Add(entry);
            }
            return result;
        }

        private static DynamicObject GetFirstEntry(DynamicObject reportObj)
        {
            DynamicObjectCollection rows = reportObj == null ? null : reportObj["PRD_MORPTENTRY"] as DynamicObjectCollection;
            return (rows != null && rows.Count > 0) ? rows[0] : null;
        }

        private static string GetBatchNo(DynamicObject row)
        {
            string lotText = GetValue(row, "Lot_Text");
            if (!string.IsNullOrEmpty(lotText))
            {
                return lotText;
            }
            DynamicObject lotObj = row != null && row.Contains("Lot") ? row["Lot"] as DynamicObject : null;
            return GetValue(lotObj, "Number");
        }

        private static Dictionary<string, BaseDataModel> BuildAuxPropData(DynamicObject row)
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

        private static List<PrdInStockEntryLinkModel> GetLinkData(DynamicObject row)
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

        private static void SetBaseDataNumber(DynamicObject source, string fieldName, BaseDataModel target)
        {
            DynamicObject value = source != null && source.Contains(fieldName) ? source[fieldName] as DynamicObject : null;
            target.FNumber = GetValue(value, "Number");
        }

        #endregion

        #region 通用取值

        public static string GetValue(DynamicObject source, string fieldName)
        {
            return source != null && source.Contains(fieldName) && source[fieldName] != null
                ? source[fieldName].ToString()
                : string.Empty;
        }

        private static long GetLong(DynamicObject source, string fieldName)
        {
            long value;
            return long.TryParse(GetValue(source, fieldName), out value) ? value : 0;
        }

        private static decimal GetDecimal(DynamicObject source, string fieldName)
        {
            decimal value;
            return decimal.TryParse(GetValue(source, fieldName), out value) ? value : 0;
        }

        private static string GetDate(DynamicObject source, string fieldName)
        {
            DateTime value;
            return DateTime.TryParse(GetValue(source, fieldName), out value)
                ? value.ToString("yyyy-MM-dd HH:mm:ss")
                : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        #endregion
    }
}
