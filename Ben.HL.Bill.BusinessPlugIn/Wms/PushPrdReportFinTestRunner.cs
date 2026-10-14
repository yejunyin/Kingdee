using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 通过金蝶 WebApi 查询生产汇报单(PRD_MORPT)并手工推送 WMS：
    /// 1. AuthService.ValidateUser 登录（Cookie 会话保持）
    /// 2. DynamicFormService.View 按汇报单号查看完整单据
    /// 3. 按 PushPrdReportFinToWMS.BuildPrdInStockDataFromReport 相同口径组装 PrdInStockSaveRequestModel
    /// 4. WmsApiClient.PostJson 推送 WMS（对应插件 config.WmsPrdReportFinApiUrl）
    /// </summary>
    internal static class PushPrdReportFinTestRunner
    {
        private const string WebApiBase = "http://192.168.1.16/k3cloud/";
        private const string AcctId = "692fb7433ff3c1";
        private const string UserName = "叶军营";
        private const string Password = "hl123456.";
        private const string Lcid = "2052";
        private const string MorptFormId = "PRD_MORPT";

        public static void Run(string billNo)
        {
            try
            {
                var cookies = new CookieContainer();
                var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(30);

                    // 1. 登录验证
                    string loginUrl = WebApiBase + "Kingdee.BOS.WebApi.ServicesStub.AuthService.ValidateUser.common.kdsvc";
                    object loginPayload = new { acctID = AcctId, username = UserName, password = Password, lcid = Lcid };
                    string loginResp = PostJson(client, loginUrl, loginPayload);
                    EnsureLoginSuccess(loginResp, cookies);
                    Console.WriteLine("WebApi 登录成功，用户={0} 数据中心={1}", UserName, AcctId);

                    // 2. View 查询生产汇报单
                    string viewUrl = WebApiBase + "Kingdee.BOS.WebApi.ServicesStub.DynamicFormService.View.common.kdsvc";
                    object viewPayload = new
                    {
                        FormId = MorptFormId,
                        Data = new { CreateOrgId = 0, Number = billNo }
                    };
                    string viewResp = PostJson(client, viewUrl, viewPayload);
                    JObject bill = ExtractBillModel(viewResp);
                    if (bill == null)
                    {
                        throw new Exception("未查询到生产汇报单 " + billNo + "，接口返回：\r\n" + viewResp);
                    }

                    // 3. 按插件 BuildPrdInStockDataFromReport 口径组装报文
                    var request = BuildRequest(bill);
                    string jsonData = JsonConvert.SerializeObject(request,
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                    Console.WriteLine("汇报单号={0} 分录数={1}", request.data.Model.FBillNo, request.data.Model.FEntity.Count);
                    foreach (PrdInStockEntryModel en in request.data.Model.FEntity)
                    {
                        Console.WriteLine("  分录{0}: 物料={1} 单位={2} 合格数={3}/{4} 生产订单={5}/{6} 入库组织={7} 车间={8} 批号=[{9}] MTO=[{10}]",
                            en.FSeq, en.FMaterialId.FNumber, en.FUnitId.FNumber, en.FRealQty, en.FBaseRealQty,
                            en.FMoBillNo, en.FMOSEQ, request.data.Model.FStockOrgId.FNumber,
                            request.data.Model.FWorkShopId.FNumber, en.FBatchNo, en.FMtoNo);
                    }
                    Console.WriteLine("===== 推送 WMS 请求报文 =====");
                    Console.WriteLine(jsonData);

                    // 4. 确认后推送（真实推送WMS，防止误触）
                    Console.Write("是否推送WMS？输入 Y 确认推送，其它输入退出：");
                    string answer = Console.IsInputRedirected ? Console.ReadLine() : Console.ReadKey().KeyChar.ToString();
                    Console.WriteLine();
                    if (string.IsNullOrWhiteSpace(answer) || answer.Trim().ToUpper() != "Y")
                    {
                        Console.WriteLine("已取消推送。");
                        return;
                    }

                    IntegrationConfig config = IntegrationConfig.Load();
                    string response = new WmsApiClient().PostJson(config.WmsPrdReportFinApiUrl, jsonData);
                    Console.WriteLine("===== WMS 返回 =====");
                    Console.WriteLine(response);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("测试异常：" + ex.Message);
            }
        }

        /// <summary>
        /// 与插件 PushPrdReportFinToWMS.BuildPrdInStockDataFromReport 相同口径
        /// </summary>
        private static PrdInStockSaveRequestModel BuildRequest(JObject reportObj)
        {
            var request = new PrdInStockSaveRequestModel();
            request.data.Model = BuildHeader(reportObj);
            request.data.Model.FEntity = BuildEntryList(reportObj);
            return request;
        }

        private static PrdInStockHeaderModel BuildHeader(JObject reportObj)
        {
            var header = new PrdInStockHeaderModel();

            // === PRD_MORPT 表头字段 ===
            header.FID = Long(reportObj, "Id");
            header.FBillNo = Str(reportObj, "BillNo");
            header.FDate = GetDate(reportObj, "Date");
            header.FDescription = GetMultiLangText(reportObj, "Description");
            SetBaseDataNumber(reportObj, "BillType", header.FBillTypeID);
            SetBaseDataNumber(reportObj, "PrdOrgId", header.FPrdOrgId);

            // === 入库组织/车间/货主从第一条分录取值 ===
            JObject firstEntry = GetFirstEntry(reportObj);
            if (firstEntry != null)
            {
                SetBaseDataNumber(firstEntry, "StockInOrgId", header.FStockOrgId);
                SetBaseDataNumber(firstEntry, "WorkshopId", header.FWorkShopId);
                header.FOwnerTypeIdHead = Str(firstEntry, "OwnerTypeId");
                SetBaseDataNumber(firstEntry, "OwnerId", header.FOwnerIdHead);
            }
            if (string.IsNullOrEmpty(header.FOwnerTypeIdHead))
            {
                header.FOwnerTypeIdHead = "BD_OwnerOrg";
            }

            return header;
        }

        private static List<PrdInStockEntryModel> BuildEntryList(JObject reportObj)
        {
            var result = new List<PrdInStockEntryModel>();
            JArray rows = reportObj["PRD_MORPTENTRY"] as JArray;
            if (rows == null)
            {
                return result;
            }

            int seq = 1;
            foreach (JToken row in rows)
            {
                if (row == null || row.Type != JTokenType.Object) continue;
                var jo = (JObject)row;

                var entry = new PrdInStockEntryModel
                {
                    FSeq = seq++,
                    FEntryID = (int)Long(jo, "Id"),
                    FRealQty = Dec(jo, "QuaQty"),
                    FBaseRealQty = Dec(jo, "BaseQuaQty"),
                    FBatchNo = GetBatchNo(jo),
                    FMtoNo = Str(jo, "MtoNo"),
                    FMoBillNo = Str(jo, "MoBillNo"),
                    // 插件取 MoSeq；View 返回的实体字段为 MoEntrySeq，作兜底
                    FMOSEQ = Str(jo, "MoSeq", "MoEntrySeq"),
                    FSrcBillNo = Str(jo, "SrcBillNo"),
                    FSrcFormId = Str(jo, "SrcBillType"),
                    FAuxPropId = BuildAuxPropData(jo),
                    FEntity_Link = GetLinkData(jo)
                };

                SetBaseDataNumber(jo, "MaterialId", entry.FMaterialId);
                SetBaseDataNumber(jo, "UnitID", entry.FUnitId);
                SetBaseDataNumber(jo, "BaseUnitId", entry.FBaseUnitId);
                SetBaseDataNumber(jo, "StockId", entry.FStockId);
                SetBaseDataNumber(jo, "StockLocId", entry.FStockLocId);

                result.Add(entry);
            }
            return result;
        }

        private static JObject GetFirstEntry(JObject reportObj)
        {
            JArray rows = reportObj == null ? null : reportObj["PRD_MORPTENTRY"] as JArray;
            return (rows != null && rows.Count > 0) ? rows[0] as JObject : null;
        }

        /// <summary>优先文本 Lot_Text，为空时取批次主档 Lot 的编码（同插件 GetBatchNo）</summary>
        private static string GetBatchNo(JObject row)
        {
            string lotText = Str(row, "Lot_Text");
            if (!string.IsNullOrEmpty(lotText))
            {
                return lotText;
            }
            return BdNumber(row, "Lot");
        }

        private static Dictionary<string, BaseDataModel> BuildAuxPropData(JObject row)
        {
            var auxPropDict = new Dictionary<string, BaseDataModel>();
            string[] auxFields =
            {
                "FAUXPROPID__FF100001", "FAUXPROPID__FF100002", "FAUXPROPID__FF100003",
                "FAUXPROPID__FF100004", "FAUXPROPID__FF100005", "FAUXPROPID__FF100006"
            };

            JObject auxPropObj = row["AuxPropId"] as JObject;
            foreach (string field in auxFields)
            {
                var baseData = new BaseDataModel();
                JObject ffPropObj = auxPropObj == null ? null : auxPropObj[field] as JObject;
                if (ffPropObj != null)
                {
                    baseData.FNumber = Str(ffPropObj, "Number", "FNumber");
                }
                auxPropDict[field] = baseData;
            }
            return auxPropDict;
        }

        private static List<PrdInStockEntryLinkModel> GetLinkData(JObject row)
        {
            var result = new List<PrdInStockEntryLinkModel>();
            JArray links = row["FEntity_Link"] as JArray;
            if (links == null)
            {
                return result;
            }

            foreach (JToken link in links)
            {
                if (link == null || link.Type != JTokenType.Object) continue;
                var jo = (JObject)link;
                result.Add(new PrdInStockEntryLinkModel
                {
                    FEntity_Link_FFlowId = Str(jo, "FlowId"),
                    FEntity_Link_FFlowLineId = Str(jo, "FlowLineId"),
                    FEntity_Link_FRuleId = Str(jo, "RuleId"),
                    FEntity_Link_FSTableName = Str(jo, "STableName"),
                    FEntity_Link_FSBillId = Long(jo, "SBillId"),
                    FEntity_Link_FSId = Long(jo, "SId")
                });
            }
            return result;
        }

        private static void SetBaseDataNumber(JObject source, string fieldName, BaseDataModel target)
        {
            target.FNumber = BdNumber(source, fieldName);
        }

        private static string PostJson(HttpClient client, string url, object payload)
        {
            string json = JsonConvert.SerializeObject(payload);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage resp = client.PostAsync(url, content).Result)
            {
                string body = resp.Content.ReadAsStringAsync().Result;
                if (!resp.IsSuccessStatusCode)
                {
                    throw new Exception("HTTP " + (int)resp.StatusCode + ": " + body);
                }
                return body;
            }
        }

        private static void EnsureLoginSuccess(string loginResp, CookieContainer cookies)
        {
            JObject jo = JObject.Parse(loginResp);
            JToken successFlag = jo["IsSuccessByAPI"];
            if (successFlag != null && successFlag.Type == JTokenType.Boolean)
            {
                if (!(bool)successFlag)
                {
                    throw new Exception("登录失败：" + loginResp);
                }
            }
            else
            {
                JToken msgCode = jo["MessageCode"];
                int code;
                if (msgCode != null && msgCode.Type == JTokenType.Integer && (int)msgCode != 0)
                {
                    throw new Exception("登录失败：" + loginResp);
                }
                if (msgCode != null && msgCode.Type != JTokenType.Integer
                    && int.TryParse(msgCode.ToString(), out code) && code != 0)
                {
                    throw new Exception("登录失败：" + loginResp);
                }
            }
            if (cookies.GetCookies(new Uri(WebApiBase)).Count == 0)
            {
                throw new Exception("登录失败，未获取到会话Cookie：" + loginResp);
            }
            JToken notice = jo["Message"];
            if (notice != null && notice.Type == JTokenType.String && !string.IsNullOrWhiteSpace(notice.ToString()))
            {
                Console.WriteLine("登录提示：" + notice);
            }
        }

        /// <summary>View 返回结构随版本有 Result.Result / Result 两种包裹，递归找含 PRD_MORPTENTRY 的单据节点</summary>
        private static JObject ExtractBillModel(string viewResp)
        {
            JToken root;
            try
            {
                root = JToken.Parse(viewResp);
            }
            catch
            {
                return null;
            }
            JObject errNode = FindNode(root, "ResponseStatus");
            if (errNode != null)
            {
                JToken isSuccess = errNode["IsSuccess"];
                if (isSuccess != null && isSuccess.Type == JTokenType.Boolean && !(bool)isSuccess)
                {
                    throw new Exception("View接口返回失败：" + errNode.ToString(Formatting.None));
                }
            }
            return FindBillModel(root);
        }

        private static JObject FindBillModel(JToken token)
        {
            var jo = token as JObject;
            if (jo != null)
            {
                if (jo["PRD_MORPTENTRY"] != null)
                {
                    return jo;
                }
                foreach (var prop in jo.Properties())
                {
                    JObject hit = FindBillModel(prop.Value);
                    if (hit != null)
                    {
                        return hit;
                    }
                }
            }
            var ja = token as JArray;
            if (ja != null)
            {
                foreach (JToken item in ja)
                {
                    JObject hit = FindBillModel(item);
                    if (hit != null)
                    {
                        return hit;
                    }
                }
            }
            return null;
        }

        private static JObject FindNode(JToken token, string name)
        {
            var jo = token as JObject;
            if (jo != null)
            {
                if (jo[name] != null && jo[name].Type == JTokenType.Object)
                {
                    return (JObject)jo[name];
                }
                foreach (var prop in jo.Properties())
                {
                    JObject hit = FindNode(prop.Value, name);
                    if (hit != null)
                    {
                        return hit;
                    }
                }
            }
            var ja = token as JArray;
            if (ja != null)
            {
                foreach (JToken item in ja)
                {
                    JObject hit = FindNode(item, name);
                    if (hit != null)
                    {
                        return hit;
                    }
                }
            }
            return null;
        }

        private static JToken Token(JToken parent, params string[] keys)
        {
            foreach (string key in keys)
            {
                JToken t = parent[key];
                if (t != null && t.Type != JTokenType.Null && t.Type != JTokenType.Undefined)
                {
                    return t;
                }
            }
            return null;
        }

        private static string Str(JToken parent, params string[] keys)
        {
            JToken t = Token(parent, keys);
            return t == null ? string.Empty : t.ToString();
        }

        private static long Long(JToken parent, params string[] keys)
        {
            JToken t = Token(parent, keys);
            long v;
            return t != null && long.TryParse(t.ToString(), out v) ? v : 0;
        }

        private static decimal Dec(JToken parent, params string[] keys)
        {
            JToken t = Token(parent, keys);
            decimal v;
            return t != null && decimal.TryParse(t.ToString(), out v) ? v : 0m;
        }

        /// <summary>基础资料字段取 Number（View 返回对象内含 Number/FNumber）</summary>
        private static string BdNumber(JToken parent, params string[] keys)
        {
            foreach (string key in keys)
            {
                var jo = parent[key] as JObject;
                if (jo != null)
                {
                    string n = Str(jo, "Number", "FNumber");
                    if (!string.IsNullOrEmpty(n))
                    {
                        return n;
                    }
                    continue;
                }
                JToken t = parent[key];
                if (t != null && t.Type != JTokenType.Null)
                {
                    string v = t.ToString();
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        return v;
                    }
                }
            }
            return string.Empty;
        }

        /// <summary>View 的多语文本字段返回 [{"Key":2052,"Value":"..."}]，取简体值</summary>
        private static string GetMultiLangText(JToken parent, string key)
        {
            var ja = parent[key] as JArray;
            if (ja == null)
            {
                return Str(parent, key);
            }
            string fallback = string.Empty;
            foreach (JToken item in ja)
            {
                string v = item.Value<string>("Value");
                if (string.IsNullOrWhiteSpace(v))
                {
                    continue;
                }
                if (item.Value<int?>("Key") == 2052)
                {
                    return v.Trim();
                }
                if (string.IsNullOrEmpty(fallback))
                {
                    fallback = v.Trim();
                }
            }
            return fallback;
        }

        /// <summary>同插件 GetDate，统一 yyyy-MM-dd HH:mm:ss</summary>
        private static string GetDate(JToken parent, string key)
        {
            DateTime value;
            return DateTime.TryParse(Str(parent, key), out value)
                ? value.ToString("yyyy-MM-dd HH:mm:ss")
                : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
