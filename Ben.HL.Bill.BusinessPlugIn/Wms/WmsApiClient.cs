using System;
using System.Net.Http;
using System.Text;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// WMS接口调用客户端（供多个插件调用）
    /// </summary>
    public class WmsApiClient
    {
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
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
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
                throw new Exception($"调用接口异常: {ex.Message}", ex);
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
    }
}
