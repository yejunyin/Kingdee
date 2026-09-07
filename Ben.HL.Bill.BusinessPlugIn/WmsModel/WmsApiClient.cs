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
    }
}
