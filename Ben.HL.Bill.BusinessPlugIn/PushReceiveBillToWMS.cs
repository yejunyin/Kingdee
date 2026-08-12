using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
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
        // WMS接口地址（从配置读取或硬编码）
        private const string WMS_API_URL = "http://your-wms-api.com/api/receive";

        /// <summary>
        /// 操作执行后触发（包括审核）
        /// </summary>
        public override void AfterDoOperation(AfterDoOperationEventArgs e)
        {
            base.AfterDoOperation(e);

            // 判断是否为审核操作（通过操作名称判断）
            if (e.Operation.Operation.Equals("Audit", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // 获取当前单据数据
                    DynamicObject billObj = this.Model.DataObject;

                    if (billObj == null)
                    {
                        this.View.ShowMessage("没有找到需要推送的单据数据");
                        return;
                    }

                    // 构造金蝶标准API格式
                    var standardRequest = BuildStandardApiRequest(billObj);
                    string jsonData = JsonConvert.SerializeObject(standardRequest,
                        new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Ignore,
                            Formatting = Formatting.Indented
                        });

                    // 日志记录（调试用）
                    this.View.ShowMessage($"推送数据长度：{jsonData.Length} 字符");

                    // 调用WMS接口
                    string response = CallWmsApi(jsonData);
                    this.View.ShowMessage($"WMS接口返回：{response}");
                }
                catch (Exception ex)
                {
                    this.View.ShowMessage($"推送WMS异常：{ex.Message}");
                    // 如果业务要求强一致性，取消注释让审核回滚
                    // throw new Exception($"推送WMS失败：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 构建金蝶标准API请求格式
        /// </summary>
        private object BuildStandardApiRequest(DynamicObject billObj)
        {
            // 获取SRM单号（自定义字段）
            string srmSourceNo = GetHeaderValue<string>("F_HL_SRM_Source_NO");

            // 获取单据类型，根据采购订单类型映射
            string billTypeNumber = GetBillTypeNumber(billObj);

            // 获取表头数据
            var header = new
            {
                FID = GetHeaderValue<long>("Id"),
                F_HL_SRM_Source_NO = srmSourceNo,
                FBillTypeID = new { FNUMBER = billTypeNumber },
                FBusinessType = GetHeaderValue<string>("BusinessType"),
                FDate = GetDateTimeString("FDate"),
                FStockOrgId = new { FNumber = GetBaseDataNumber("FStockOrgId") },
                FPurOrgId = new { FNumber = GetBaseDataNumber("FPurOrgId") },
                FSupplierId = new { FNumber = GetBaseDataNumber("FSupplierId") },
                FSupplyId = new { FNumber = GetBaseDataNumber("FSupplyId") },
                FOwnerTypeIdHead = GetHeaderValue<string>("FOwnerTypeIdHead"),
                FOwnerIdHead = new { FNumber = GetBaseDataNumber("FOwnerIdHead") },
                F_PVCE_Combo = GetHeaderValue<string>("F_PVCE_Combo"),
                FNote = GetHeaderValue<string>("FNote"),
                FinanceEntity = new
                {
                    FEntryId = 0,
                    FSettleOrgId = new { FNumber = GetBaseDataNumber("FinanceEntity.FSettleOrgId") },
                    FSettleCurrId = new { FNumber = GetBaseDataNumber("FinanceEntity.FSettleCurrId") },
                    FPricePoint = GetHeaderValue<string>("FinanceEntity.FPricePoint")
                },
                FDetailEntity = BuildDetailEntityList(billObj)
            };

            // 组装完整请求
            var request = new
            {
                FormId = "PUR_ReceiveBill",
                data = new
                {
                    NeedUpDateFields = new string[] { },
                    NeedReturnFields = new string[] { },
                    IsDeleteEntry = "true",
                    SubSystemId = "",
                    IsVerifyBaseDataField = "false",
                    IsEntryBatchFill = "true",
                    ValidateFlag = "true",
                    NumberSearch = "true",
                    IsAutoAdjustField = "false",
                    InterationFlags = "",
                    IgnoreInterationFlag = "",
                    IsControlPrecision = "false",
                    ValidateRepeatJson = "false",
                    Model = header
                }
            };

            return request;
        }

        /// <summary>
        /// 构建分录列表
        /// </summary>
        private List<object> BuildDetailEntityList(DynamicObject billObj)
        {
            var detailList = new List<object>();
            DynamicObjectCollection detailEntity = this.Model.DataObject["PUR_ReceiveEntry"] as DynamicObjectCollection;

            if (detailEntity != null)
            {
                int seq = 1;
                foreach (DynamicObject row in detailEntity)
                {
                    // 获取源单关联数据
                    var linkData = GetLinkData(row);

                    var detail = new
                    {
                        F_PVCE_HL_SRMSFEQ = seq++,
                        FEntryID = GetEntryValue<int>(row, "Id"),
                        FMaterialId = new { FNumber = GetEntryBaseDataNumber(row, "MaterialID") },
                        FUnitId = new { FNumber = GetEntryBaseDataNumber(row, "FUnitId") },
                        FPriceUnitId = new { FNumber = GetEntryBaseDataNumber(row, "FPriceUnitId") },
                        FStockUnitID = new { FNumber = GetEntryBaseDataNumber(row, "222FStockUnitID") },
                        FStockQty = GetEntryValue<decimal>(row, "FStockQty"),
                        FInStockQty = GetEntryValue<decimal>(row, "FInStockQty"),
                        FActlandQty = GetEntryValue<decimal>(row, "FActlandQty"),
                        FActReceiveQty = GetEntryValue<decimal>(row, "FActReceiveQty"),
                        FPriceUnitQty = GetEntryValue<decimal>(row, "FPriceUnitQty"),
                        FSrcBillNo = GetEntryValue<string>(row, "FSrcBillNo"),
                        FSrcFormId = "PUR_PurchaseOrder",  // 固定值
                        FTAXPRICE = GetEntryValue<decimal>(row, "FTAXPRICE"),
                        FEntryTaxRate = GetEntryValue<decimal>(row, "FEntryTaxRate"),
                        FAuxPropId = BuildAuxPropData(row),
                        FMtoNo = GetEntryValue<string>(row, "FMtoNo"),
                        FSTOCKID = new { FNumber = GetEntryBaseDataNumber(row, "FSTOCKID") },
                        FDescription = GetEntryValue<string>(row, "FDescription"),
                        FDetailEntity_Link = linkData
                    };

                    detailList.Add(detail);
                }
            }

            return detailList;
        }

        /// <summary>
        /// 构建辅助属性数据
        /// </summary>
        private object BuildAuxPropData(DynamicObject row)
        {
            var auxProp = new Dictionary<string, object>();

            // 辅助属性字段列表（根据实际配置调整）
            string[] auxFields = new string[]
            {
                "FAUXPROPID__FF100001",
                "FAUXPROPID__FF100002",
                "FAUXPROPID__FF100003",
                "FAUXPROPID__FF100004",
                "FAUXPROPID__FF100005",
                "FAUXPROPID__FF100006",
                "FAUXPROPID__FF100007",
                "FAUXPROPID__FF100008",
                "FAUXPROPID__FF100009",
                "FAUXPROPID__FF100010",
                "FAUXPROPID__FF100011",
                "FAUXPROPID__FF100012"
            };

            foreach (string field in auxFields)
            {
                string value = GetEntryBaseDataNumber(row, $"FAuxPropId.{field}");
                auxProp[field] = new { FNumber = value ?? "" };
            }

            return auxProp;
        }

        /// <summary>
        /// 构建源单关联数据
        /// </summary>
        private List<object> GetLinkData(DynamicObject row)
        {
            var linkList = new List<object>();

            // 获取关联数据
            var links = row["FDetailEntity_Link"] as DynamicObjectCollection;

            if (links != null && links.Count > 0)
            {
                foreach (DynamicObject link in links)
                {
                    var linkData = new
                    {
                        FDetailEntity_Link_FFlowId = GetLinkValue<string>(link, "FlowId") ?? "",
                        FDetailEntity_Link_FFlowLineId = GetLinkValue<string>(link, "FFlowLineId") ?? "",
                        FDetailEntity_Link_FRuleId = "PUR_PurchaseOrder-PUR_ReceiveBill",  // 固定值
                        FDetailEntity_Link_FSTableName = "t_PUR_POOrderEntry",  // 固定值
                        FDetailEntity_Link_FSBillId = GetLinkValue<long>(link, "FSBillId"),
                        FDetailEntity_Link_FSId = GetLinkValue<long>(link, "FSId"),
                        FDetailEntity_Link_FBaseUnitQtyOld = GetLinkValue<string>(link, "FBaseUnitQtyOld") ?? "0",
                        FDetailEntity_Link_FBaseUnitQty = GetLinkValue<string>(link, "FBaseUnitQty") ?? "0"
                    };
                    linkList.Add(linkData);
                }
            }
            else
            {
                // 如果没有关联数据，创建一个默认的空关联
                var defaultLink = new
                {
                    FDetailEntity_Link_FFlowId = "",
                    FDetailEntity_Link_FFlowLineId = "",
                    FDetailEntity_Link_FRuleId = "PUR_PurchaseOrder-PUR_ReceiveBill",
                    FDetailEntity_Link_FSTableName = "t_PUR_POOrderEntry",
                    FDetailEntity_Link_FSBillId = 0,
                    FDetailEntity_Link_FSId = 0,
                    FDetailEntity_Link_FBaseUnitQtyOld = "0",
                    FDetailEntity_Link_FBaseUnitQty = "0"
                };
                linkList.Add(defaultLink);
            }

            return linkList;
        }

        /// <summary>
        /// 根据采购订单类型获取收料通知单类型
        /// </summary>
        private string GetBillTypeNumber(DynamicObject billObj)
        {
            try
            {
                // 获取源采购订单类型
                string poBillType = GetHeaderValue<string>("FSrcBillTypeId.FNUMBER");

                if (!string.IsNullOrEmpty(poBillType))
                {
                    // 根据采购订单类型映射收料通知单类型
                    if (poBillType == "CGDD02_SYS")
                        return "SLD03_SYS";
                    else if (poBillType == "CGDD09_SYS")
                        return "SLD09_SYS";
                }

                // 默认类型
                return "SLD01_SYS";
            }
            catch
            {
                return "SLD01_SYS";
            }
        }

        /// <summary>
        /// 获取日期时间字符串
        /// </summary>
        private string GetDateTimeString(string propertyName)
        {
            DateTime? date = GetHeaderValue<DateTime?>(propertyName);
            return date?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 获取单据头字段值
        /// </summary>
        private T GetHeaderValue<T>(string fieldName)
        {
            try
            {
                object value = this.Model.GetValue(fieldName);
                if (value == null) return default(T);

                Type targetType = typeof(T);
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    targetType = Nullable.GetUnderlyingType(targetType);
                }

                if (targetType != null && value.GetType() != targetType)
                {
                    return (T)Convert.ChangeType(value, targetType);
                }

                return (T)value;
            }
            catch
            {
                return default(T);
            }
        }

        /// <summary>
        /// 获取单据头基础资料字段的编码
        /// </summary>
        private string GetBaseDataNumber(string fieldName)
        {
            try
            {
                // 支持嵌套路径，如 "FinanceEntity.FSettleOrgId"
                string[] pathParts = fieldName.Split('.');
                object currentObj = this.Model.DataObject;

                foreach (string part in pathParts)
                {
                    if (currentObj == null) return "";

                    DynamicObject dynamicObj = currentObj as DynamicObject;
                    if (dynamicObj != null)
                    {
                        currentObj = dynamicObj[part];
                    }
                    else
                    {
                        var prop = currentObj.GetType().GetProperty(part);
                        if (prop != null)
                        {
                            currentObj = prop.GetValue(currentObj);
                        }
                        else
                        {
                            return "";
                        }
                    }
                }

                DynamicObject baseData = currentObj as DynamicObject;
                if (baseData == null) return "";

                object number = baseData["FNumber"];
                return number?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 获取分录字段的值
        /// </summary>
        private T GetEntryValue<T>(DynamicObject row, string fieldName)
        {
            try
            {
                object value = row[fieldName];
                if (value == null) return default(T);

                Type targetType = typeof(T);
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    targetType = Nullable.GetUnderlyingType(targetType);
                }

                if (targetType != null && value.GetType() != targetType)
                {
                    return (T)Convert.ChangeType(value, targetType);
                }

                return (T)value;
            }
            catch
            {
                return default(T);
            }
        }

        /// <summary>
        /// 获取分录基础资料字段的编码
        /// </summary>
        private string GetEntryBaseDataNumber(DynamicObject row, string fieldName)
        {
            try
            {
                // 支持嵌套路径，如 "FAuxPropId.FAUXPROPID__FF100001"
                string[] pathParts = fieldName.Split('.');
                object currentObj = row;

                foreach (string part in pathParts)
                {
                    if (currentObj == null) return "";

                    DynamicObject dynamicObj = currentObj as DynamicObject;
                    if (dynamicObj != null)
                    {
                        currentObj = dynamicObj[part];
                    }
                    else
                    {
                        var prop = currentObj.GetType().GetProperty(part);
                        if (prop != null)
                        {
                            currentObj = prop.GetValue(currentObj);
                        }
                        else
                        {
                            return "";
                        }
                    }
                }

                DynamicObject baseData = currentObj as DynamicObject;
                if (baseData == null)
                {
                    // 如果已经是字符串，直接返回
                    return currentObj?.ToString() ?? "";
                }

                object number = baseData["FNumber"];
                return number?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 获取关联数据字段的值
        /// </summary>
        private T GetLinkValue<T>(DynamicObject row, string fieldName)
        {
            try
            {
                object value = row[fieldName];
                if (value == null) return default(T);

                Type targetType = typeof(T);
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    targetType = Nullable.GetUnderlyingType(targetType);
                }

                if (targetType != null && value.GetType() != targetType)
                {
                    return (T)Convert.ChangeType(value, targetType);
                }

                return (T)value;
            }
            catch
            {
                return default(T);
            }
        }

        /// <summary>
        /// 调用WMS接口
        /// </summary>
        private string CallWmsApi(string jsonData)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Add("Accept", "application/json");

                    // 如果WMS需要认证，在这里添加
                    // client.DefaultRequestHeaders.Add("Authorization", "Bearer your-token");

                    var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                    var task = client.PostAsync(WMS_API_URL, content);
                    task.Wait();

                    var response = task.Result;
                    var responseContent = response.Content.ReadAsStringAsync();
                    responseContent.Wait();

                    if (response.IsSuccessStatusCode)
                    {
                        return responseContent.Result;
                    }
                    else
                    {
                        return $"HTTP {response.StatusCode}: {responseContent.Result}";
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"调用WMS接口异常: {ex.Message}");
            }
        }
    }
}