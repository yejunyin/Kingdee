using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// WMS接口调用客户端（供多个插件调用）
    /// </summary>
    public class WmsApiClient
    {
        static WmsApiClient()
        {
            // 部分网关对 Expect:100-continue 处理异常（工具不带此头能通、HttpClient 返回404/417），全局关闭
            System.Net.ServicePointManager.Expect100Continue = false;
        }

        /// <summary>
        /// 创建直连内网的 HttpClient：
        /// 必须 UseProxy=false——金蝶服务器上若配了系统/IE代理，
        /// HttpClient 默认走代理，内网WMS地址会被代理返回404（Postman等工具不走代理所以正常）
        /// </summary>
        private static HttpClient CreateHttpClient(int timeoutSeconds)
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false,
                Proxy = null
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            return client;
        }

        /// <summary>
        /// 调用WMS接口（POST JSON）
        /// </summary>
        /// <param name="apiUrl">WMS接口地址</param>
        /// <param name="jsonData">请求体JSON</param>
        /// <param name="timeoutSeconds">超时秒数，默认30秒</param>
        /// <returns>接口响应内容；非成功状态码返回 "HTTP {StatusCode}: {响应}"</returns>
        public string CallWmsApi(string apiUrl, string jsonData, int timeoutSeconds = 30)
        {
            return PostJson(apiUrl, jsonData, timeoutSeconds);
        }

        public string PostJson(string apiUrl, string jsonData, int timeoutSeconds = 30)
        {
            try
            {
                using (var client = CreateHttpClient(timeoutSeconds))
                {
                    client.DefaultRequestHeaders.Add("Accept", "application/json");

                    var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                    var task = client.PostAsync(apiUrl, content);
                    task.Wait();

                    var response = task.Result;
                    var responseContent = response.Content.ReadAsStringAsync();
                    responseContent.Wait();
                    string result = responseContent.Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"HTTP {response.StatusCode}: {result}");
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"调用接口异常 [{apiUrl}]: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 调用SRM SOAP接口（JSON作为arg0参数包裹在SOAP报文中提交）
        /// </summary>
        /// <param name="apiUrl">SRM接口地址</param>
        /// <param name="jsonData">arg0节点中的JSON报文</param>
        /// <param name="timeoutSeconds">超时秒数，默认30秒</param>
        /// <returns>接口响应内容；非成功状态码返回 "HTTP {StatusCode}: {响应}"</returns>
        public string PostSoap(string apiUrl, string jsonData, int timeoutSeconds = 30)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                    string escapedJson = System.Security.SecurityElement.Escape(jsonData);
                    string soapXml = string.Format(
@"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:api=""http://api.ws.rs.com/"">
   <soapenv:Header/>
   <soapenv:Body>
      <api:tasksheetReceive>
         <arg0>{0}</arg0>
      </api:tasksheetReceive>
   </soapenv:Body>
</soapenv:Envelope>", escapedJson);

                    var content = new StringContent(soapXml, Encoding.UTF8, "text/xml");
                    var task = client.PostAsync(apiUrl, content);
                    task.Wait();

                    var response = task.Result;
                    var responseContent = response.Content.ReadAsStringAsync();
                    responseContent.Wait();
                    string result = responseContent.Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"HTTP {response.StatusCode}: {result}");
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"调用SRM接口异常: {ex.Message}", ex);
            }
        }

        #region WMS 返回 JSON 通用解析

        /// <summary>
        /// 从 WMS 返回的 JSON 中解析出 Result.Number（新生成的单据编号）。
        /// 兼容金蝶标准 Save 接口格式：{ "Result": { "ResponseStatus": { "IsSuccess": true, ... }, "Number": "xxx" } }
        /// 优先顶层 Result.Number；兜底 SuccessEntitys[0].Number。
        /// </summary>
        public static string ParseResultNumber(string jsonResponse)
        {
            if (string.IsNullOrWhiteSpace(jsonResponse)) return null;
            try
            {
                var json = JObject.Parse(jsonResponse);
                var result = json["Result"];
                if (result == null) return null;

                string number = (string)result["Number"];
                if (!string.IsNullOrWhiteSpace(number)) return number;

                var successEntitys = result["ResponseStatus"]?["SuccessEntitys"] as JArray;
                if (successEntitys != null && successEntitys.Count > 0)
                {
                    string entityNum = (string)successEntitys[0]["Number"];
                    if (!string.IsNullOrWhiteSpace(entityNum)) return entityNum;
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// 从 WMS 返回 JSON 中解析 ResponseStatus.IsSuccess，判断接口是否执行成功。
        /// </summary>
        public static bool ParseIsSuccess(string jsonResponse)
        {
            if (string.IsNullOrWhiteSpace(jsonResponse)) return false;
            try
            {
                var json = JObject.Parse(jsonResponse);
                var respStatus = json["Result"]?["ResponseStatus"];
                if (respStatus == null) return false;
                var isSuccess = respStatus["IsSuccess"];
                if (isSuccess == null) return false;
                return (bool)isSuccess;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
