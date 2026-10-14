using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 通过金蝶 WebApi 查询收料通知单并测试 SRM 推送：
    /// 1. AuthService.ValidateUser 登录（Cookie 会话保持）
    /// 2. DynamicFormService.View 按单号查看完整单据
    /// 3. 按 PushReceiveBillToWMS.BuildSrmRequest 相同口径组装 SrmReceiveRequestModel
    /// 4. WmsApiClient.PostSoap 推送SRM（对应插件第50-51行）
    /// </summary>
    internal static class PushReceiveBillTestRunner
    {
        private const string WebApiBase = "http://192.168.1.16/k3cloud/";
        private const string AcctId = "692fb7433ff3c1";
        private const string UserName = "叶军营";
        private const string Password = "hl123456.";
        private const string Lcid = "2052";
        private const string ReceiveBillFormId = "PUR_ReceiveBill";
        private const string DepartmentCode = "1008";

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

                    // 2. View 查询收料通知单
                    string viewUrl = WebApiBase + "Kingdee.BOS.WebApi.ServicesStub.DynamicFormService.View.common.kdsvc";
                    object viewPayload = new
                    {
                        FormId = ReceiveBillFormId,
                        Data = new { CreateOrgId = 0, Number = billNo }
                    };
                    string viewResp = PostJson(client, viewUrl, viewPayload);
                    JObject bill = ExtractBillModel(viewResp);
                    if (bill == null)
                    {
                        throw new Exception("未查询到收料通知单 " + billNo + "，接口返回：\r\n" + viewResp);
                    }

                    // 3. 打印单据概要
                    string billNoActual = Str(bill, "BillNo", "FBillNo");
                    string sourceNo = Str(bill, "HL_SRM_Source_NO", "FHL_SRM_Source_NO", "F_HL_SRM_Source_NO");
                    Console.WriteLine("单据号={0} SRM来源单号={1}", billNoActual, sourceNo);

                    JArray rows = bill["PUR_ReceiveEntry"] as JArray;
                    if (rows == null || rows.Count == 0)
                    {
                        throw new Exception("收料通知单分录为空：" + billNo);
                    }
                    for (int i = 0; i < rows.Count; i++)
                    {
                        JToken r = rows[i];
                        Console.WriteLine("  分录{0}: 物料={1}/{2} 单位={3} 批号={4} 实收={5} 仓库={6} 库位={7}",
                            i + 1,
                            BdNumber(r, "MaterialID"),
                            BdLocaleText(r, "MaterialID", "Name"),
                            BdNumber(r, "UnitId"),
                            Str(r, "Lot_Text", "FLot_Text"),
                            Dec(r, "ActReceiveQty"),
                            BdNumber(r, "StockID", "STOCKID"),
                            BdNumber(r, "StockLocId", "LocationId"));
                    }

                    // 4. 按插件 BuildSrmRequest 口径组装报文
                    var request = BuildSrmRequest(billNoActual, sourceNo, rows, bill);
                    string srmJson = JsonConvert.SerializeObject(request,
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                    Console.WriteLine("===== SRM 请求报文 =====");
                    Console.WriteLine(srmJson);

                    // 5. 确认后推送（真实回写SRM，防止误触）
                    Console.Write("是否推送SRM？输入 Y 确认推送，其它输入退出：");
                    string answer = Console.IsInputRedirected ? Console.ReadLine() : Console.ReadKey().KeyChar.ToString();
                    Console.WriteLine();
                    if (string.IsNullOrWhiteSpace(answer) || answer.Trim().ToUpper() != "Y")
                    {
                        Console.WriteLine("已取消推送。");
                        return;
                    }

                    IntegrationConfig config = IntegrationConfig.Load();
                    string srmResponse = new WmsApiClient().PostSoap(config.SrmApiUrl, srmJson);
                    Console.WriteLine("===== SRM 返回 =====");
                    Console.WriteLine(srmResponse);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("测试异常：" + ex.Message);
            }
        }

        private static SrmReceiveRequestModel BuildSrmRequest(string erpDoc, string receiveDoc, JArray rows, JObject bill)
        {
            return new SrmReceiveRequestModel
            {
                groupCode = BdNumber(bill, "StockOrgId"),
                ERPDoc = erpDoc,
                userCode = BdNumber(bill, "PurchaserId"),
                departmentCode = DepartmentCode,
                docDate = ToDate10(Token(bill, "Date", "FDate")),
                supCode = BdNumber(bill, "SupplierId"),
                supNamess = BdLocaleText(bill, "SupplierId", "Name"),
                srtRtfList = BuildEntries(erpDoc, receiveDoc, rows)
            };
        }

        private static List<SrmReceiveEntryModel> BuildEntries(string erpDoc, string receiveDoc, JArray rows)
        {
            var list = new List<SrmReceiveEntryModel>();
            int seq = 1;
            foreach (JToken r in rows)
            {
                int docSN = (int)(Dec(r, "Seq", "FSeq") == 0m ? seq : Dec(r, "Seq", "FSeq"));
                list.Add(new SrmReceiveEntryModel
                {
                    docSN = docSN,
                    recieveDoc = erpDoc,
                    recieveDocSN = docSN,
                    srmrecieveDocSN = receiveDoc,
                    materialCode = BdNumber(r, "MaterialID"),
                    materialName = BdLocaleText(r, "MaterialID", "Name"),
                    materialSpec = BdLocaleText(r, "MaterialID", "Specification"),
                    unitCode = BdNumber(r, "UnitId"),
                    batchNo = Str(r, "Lot_Text", "FLot_Text"),
                    num = Dec(r, "ActReceiveQty"),
                    warehouseCode = "005.025",
                    // 插件原代码取 LocationId（该单实体实际字段为 StockLocId），这里优先取真实库位
                    locationCode = "005.025.01"
                });
                seq++;
            }
            return list;
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
                // 正常登录时 MessageCode 可能是 "CheckPasswordPolicy"（密码到期提示），Message 有提示文案，以 IsSuccessByAPI 为准
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
            // 以是否写入会话Cookie兜底
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

        /// <summary>
        /// View 返回结构随版本有 Result.Result / Result 两种包裹，递归找含明细分录的单据节点
        /// </summary>
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
                if (jo["PUR_ReceiveEntry"] != null)
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

        private static decimal Dec(JToken parent, params string[] keys)
        {
            JToken t = Token(parent, keys);
            decimal v;
            return t != null && decimal.TryParse(t.ToString(), out v) ? v : 0m;
        }

        /// <summary>基础资料字段取 Number，支持多个候选字段名；兼容直接返回字符串/数字的情况</summary>
        private static string BdNumber(JToken parent, params string[] keys)
        {
            foreach (string key in keys)
            {
                JToken t = parent[key];
                var jo = t as JObject;
                if (jo != null)
                {
                    string n = Str(jo, "Number", "FNumber");
                    if (!string.IsNullOrEmpty(n))
                    {
                        return n;
                    }
                    continue;
                }
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

        /// <summary>
        /// 取基础资料多语文本（Name/Specification）：View返回的是 [{"Key":2052,"Value":"..."}] 数组，
        /// 兜底取 MultiLanguageText 中 LocaleId=2052 的同名属性
        /// </summary>
        private static string BdLocaleText(JToken parent, string bdKey, string textKey)
        {
            var jo = parent[bdKey] as JObject;
            if (jo == null)
            {
                return string.Empty;
            }

            JToken text = jo[textKey];
            var ja = text as JArray;
            if (ja != null)
            {
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
                if (!string.IsNullOrEmpty(fallback))
                {
                    return fallback;
                }
            }
            else if (text != null && text.Type != JTokenType.Null)
            {
                return text.ToString().Trim();
            }

            var mlt = jo["MultiLanguageText"] as JArray;
            if (mlt != null)
            {
                string fallback = string.Empty;
                foreach (JToken item in mlt)
                {
                    string v = item.Value<string>(textKey);
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        continue;
                    }
                    if (item.Value<int?>("LocaleId") == 2052)
                    {
                        return v.Trim();
                    }
                    if (string.IsNullOrEmpty(fallback))
                    {
                        fallback = v.Trim();
                    }
                }
                if (!string.IsNullOrEmpty(fallback))
                {
                    return fallback;
                }
            }
            return string.Empty;
        }

        /// <summary>兼容 /Date(毫秒)/、ISO、yyyy-MM-dd HH:mm:ss 三种返回，取前10位日期</summary>
        private static string ToDate10(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return DateTime.Now.ToString("yyyy-MM-dd");
            }
            string s = token.ToString();
            Match m = Regex.Match(s, @"/Date\((-?\d+)\)/");
            DateTime dt;
            if (m.Success)
            {
                dt = new DateTime(1970, 1, 1).AddMilliseconds(long.Parse(m.Groups[1].Value));
            }
            else if (!DateTime.TryParse(s, out dt))
            {
                return DateTime.Now.ToString("yyyy-MM-dd");
            }
            return dt.ToString("yyyy-MM-dd");
        }
    }
}
