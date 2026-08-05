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

            // 判断是否为审核操作（OperationId = 0 通常代表审核）
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
            string srmSourceNo = GetDynamicValue<string>(billObj, "F_HL_SRM_Source_NO");

            // 获取单据类型，根据采购订单类型映射
            string billTypeNumber = GetBillTypeNumber(billObj);

            // 获取表头数据
            var header = new
            {
                FID = GetDynamicValue<long>(billObj, "FID"),
                F_HL_SRM_Source_NO = srmSourceNo,
                FBillTypeID = new { FNUMBER = billTypeNumber },
                FBusinessType = GetDynamicValue<string>(billObj, "FBusinessType"),
                FDate = GetDateTimeString(billObj, "FDate"),
                FStockOrgId = new { FNumber = GetDynamicValue<string>(billObj, "FStockOrgId.FNumber") },
                FPurOrgId = new { FNumber = GetDynamicValue<string>(billObj, "FPurOrgId.FNumber") },
                FSupplierId = new { FNumber = GetDynamicValue<string>(billObj, "FSupplierId.FNumber") },
                FSupplyId = new { FNumber = GetDynamicValue<string>(billObj, "FSupplyId.FNumber") },
                FOwnerTypeIdHead = GetDynamicValue<string>(billObj, "FOwnerTypeIdHead"),
                FOwnerIdHead = new { FNumber = GetDynamicValue<string>(billObj, "FOwnerIdHead.FNumber") },
                F_PVCE_Combo = GetDynamicValue<string>(billObj, "F_PVCE_Combo"),
                FNote = GetDynamicValue<string>(billObj, "FNote"),
                FinanceEntity = new
                {
                    FEntryId = 0,
                    FSettleOrgId = new { FNumber = GetDynamicValue<string>(billObj, "FinanceEntity.FSettleOrgId.FNumber") },
                    FSettleCurrId = new { FNumber = GetDynamicValue<string>(billObj, "FinanceEntity.FSettleCurrId.FNumber") },
                    FPricePoint = GetDynamicValue<string>(billObj, "FinanceEntity.FPricePoint")
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
            DynamicObjectCollection detailEntity = GetDynamicValue<DynamicObjectCollection>(billObj, "FDetailEntity");

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
                        FEntryID = GetDynamicValue<int>(row, "FEntryID"),
                        FMaterialId = new { FNumber = GetDynamicValue<string>(row, "FMaterialId.FNumber") },
                        FUnitId = new { FNumber = GetDynamicValue<string>(row, "FUnitId.FNumber") },
                        FPriceUnitId = new { FNumber = GetDynamicValue<string>(row, "FPriceUnitId.FNumber") },
                        FStockUnitID = new { FNumber = GetDynamicValue<string>(row, "FStockUnitID.FNumber") },
                        FStockQty = GetDynamicValue<decimal>(row, "FStockQty"),
                        FInStockQty = GetDynamicValue<decimal>(row, "FInStockQty"),
                        FActlandQty = GetDynamicValue<decimal>(row, "FActlandQty"),
                        FActReceiveQty = GetDynamicValue<decimal>(row, "FActReceiveQty"),
                        FPriceUnitQty = GetDynamicValue<decimal>(row, "FPriceUnitQty"),
                        FSrcBillNo = GetDynamicValue<string>(row, "FSrcBillNo"),
                        FSrcFormId = "PUR_PurchaseOrder",  // 固定值
                        FTAXPRICE = GetDynamicValue<decimal>(row, "FTAXPRICE"),
                        FEntryTaxRate = GetDynamicValue<decimal>(row, "FEntryTaxRate"),
                        FAuxPropId = BuildAuxPropData(row),
                        FMtoNo = GetDynamicValue<string>(row, "FMtoNo"),
                        FSTOCKID = new { FNumber = GetDynamicValue<string>(row, "FSTOCKID.FNumber") },
                        FDescription = GetDynamicValue<string>(row, "FDescription"),
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
                string value = GetDynamicValue<string>(row, $"FAuxPropId.{field}.FNumber");
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
            var links = GetDynamicValue<DynamicObjectCollection>(row, "FDetailEntity_Link");

            if (links != null && links.Count > 0)
            {
                foreach (DynamicObject link in links)
                {
                    var linkData = new
                    {
                        FDetailEntity_Link_FFlowId = GetDynamicValue<string>(link, "FFlowId") ?? "",
                        FDetailEntity_Link_FFlowLineId = GetDynamicValue<string>(link, "FFlowLineId") ?? "",
                        FDetailEntity_Link_FRuleId = "PUR_PurchaseOrder-PUR_ReceiveBill",
                        FDetailEntity_Link_FSTableName = "t_PUR_POOrderEntry",
                        FDetailEntity_Link_FSBillId = GetDynamicValue<long>(link, "FSBillId"),
                        FDetailEntity_Link_FSId = GetDynamicValue<long>(link, "FSId"),
                        FDetailEntity_Link_FBaseUnitQtyOld = GetDynamicValue<string>(link, "FBaseUnitQtyOld") ?? "0",
                        FDetailEntity_Link_FBaseUnitQty = GetDynamicValue<string>(link, "FBaseUnitQty") ?? "0"
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
                string poBillType = GetDynamicValue<string>(billObj, "FSrcBillTypeId.FNUMBER");

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
        private string GetDateTimeString(DynamicObject obj, string propertyName)
        {
            DateTime? date = GetDynamicValue<DateTime?>(obj, propertyName);
            return date?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 安全获取DynamicObject的属性值
        /// </summary>
        private T GetDynamicValue<T>(DynamicObject obj, string propertyName)
        {
            if (obj == null) return default(T);

            try
            {
                string[] pathParts = propertyName.Split('.');
                object currentObj = obj;

                foreach (string part in pathParts)
                {
                    if (currentObj == null) return default(T);

                    // 使用 as 运算符代替模式匹配
                    DynamicObject dynamicObj = currentObj as DynamicObject;
                    if (dynamicObj != null)
                    {
                        currentObj = dynamicObj[part];
                    }
                    else
                    {
                        // 尝试通过反射获取属性
                        var prop = currentObj.GetType().GetProperty(part);
                        if (prop != null)
                        {
                            currentObj = prop.GetValue(currentObj);
                        }
                        else
                        {
                            return default(T);
                        }
                    }
                }

                if (currentObj == null) return default(T);

                Type targetType = typeof(T);
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    targetType = Nullable.GetUnderlyingType(targetType);
                }

                if (targetType != null && currentObj.GetType() != targetType)
                {
                    return (T)Convert.ChangeType(currentObj, targetType);
                }

                return (T)currentObj;
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