using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.Permission;
using Kingdee.BOS.Core.Report;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Resource;
using Kingdee.BOS.ServiceHelper;
using System.ComponentModel;
using Kingdee.BOS.Core.DynamicForm;
using Newtonsoft.Json;

namespace Ben.HL.Bill.BusinessPlugIn
{
    [Kingdee.BOS.Util.HotUpdate]
    [Description("Ben测试-MES接口校验")]
    public class buttonclass : AbstractBillPlugIn
    {
        // 标志位：是否需要阻止操作
        private bool _needBlockOperation = false;
        // 错误消息
        private string _errorMessage = string.Empty;

        public override void OnInitialize(InitializeEventArgs e)
        {
            base.OnInitialize(e);
        }

        /// <summary>
        /// 拦截操作执行前的事件
        /// </summary>
        public override void BeforeDoOperation(BeforeDoOperationEventArgs e)
        {
            base.BeforeDoOperation(e);

            // 检查是否需要阻止操作
            if (_needBlockOperation)
            {
                e.Cancel = true;
                this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
                _needBlockOperation = false; // 重置标志
                return;
            }

            // 可以根据操作名称进行更精细的控制
            // 例如：只阻止"下达"操作
            // if (e.OperationName == "Submit" && _needBlockOperation)
            // {
            //     e.Cancel = true;
            //     this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
            //     _needBlockOperation = false;
            //     return;
            // }
        }

        /// <summary>
        /// 工具栏按钮点击事件
        /// </summary>
        public override void EntryBarItemClick(BarItemClickEventArgs e)
        {
            base.EntryBarItemClick(e);

            // 重置标志位
            _needBlockOperation = false;
            _errorMessage = string.Empty;

            switch (e.BarItemKey)
            {
                case "tbBtnToRelease":
                    ExecuteToRelease();
                    break;
                case "tbBtnToStart":
                    ExecuteToRelease();
                    break;
                case "tbBtnUndoToPlanConfirm":
                    ExecuteUndoToPlanConfirm();
                    break;
                case "tbBtnUndoToPlan":
                    ExecuteUndoToPlanConfirm();
                    break;
                default:
                    break;
            }

            // 取消默认事件处理，由 BeforeDoOperation 来控制是否执行操作
            //e.Cancel = true;
            //return;
        }

        /// <summary>
        /// POST 请求 - 推送MES数据，检查是否可以下达
        /// 如果MES返回code=500，则阻止下达操作
        /// </summary>
        private void ExecuteToRelease()
        {
            try
            {
                // 获取当前单据数据
                DynamicObject dataPacket = this.Model.DataObject;
                DynamicObjectCollection entrys = dataPacket["TreeEntity"] as DynamicObjectCollection;
                // 获取生产订单号 - 请根据实际字段名修改
                string productionOrderCode = dataPacket["BillNo"]?.ToString();
                if (string.IsNullOrEmpty(productionOrderCode))
                {
                    _needBlockOperation = true;
                    _errorMessage = "无法获取生产订单号，请检查单据";
                    this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
                    return;
                }

                if (entrys == null || entrys.Count == 0)
                {
                    _needBlockOperation = true;
                    _errorMessage = "无法获取分录数据，请检查单据";
                    this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
                    return;
                }

                // 获取第一个分录的数据（根据实际业务，可能需要汇总所有分录的数量）
                DynamicObject firstEntry = entrys[0];
                // 获取计划生产数量 - 请根据实际字段名修改
                int scheduledProducedUnits = Convert.ToInt32(firstEntry["Qty"]);
                if (scheduledProducedUnits <= 0)
                {
                    _needBlockOperation = true;
                    _errorMessage = "计划生产数量必须大于0，请检查单据";
                    this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
                    return;
                }

                // 获取订单状态 - 请根据实际字段名修改
                string orderERPStatus =  "下达";

                string url = "http://192.168.1.6:80/iMark/v1/DBProductionOrderInfo/modifyIfNoProcessOrder";

                // 构建请求体
                var bodyData = new
                {
                    productionOrderCode = productionOrderCode,
                    scheduledProducedUnits = scheduledProducedUnits,
                    orderERPStatus = orderERPStatus
                };

                // 序列化请求体
                string postData = JsonConvert.SerializeObject(bodyData);

                // 创建请求
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.Timeout = 30000; // 30秒超时

                // 添加 Authorization 头
                string authInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes("bg:bg957768"));
                request.Headers.Add("Authorization", "Basic " + authInfo);

                // 写入请求体
                byte[] byteData = Encoding.UTF8.GetBytes(postData);
                request.ContentLength = byteData.Length;

                using (Stream requestBody = request.GetRequestStream())
                {
                    requestBody.Write(byteData, 0, byteData.Length);
                }

                // 获取响应
                using (WebResponse response = request.GetResponse())
                {
                    using (Stream responseStream = response.GetResponseStream())
                    {
                        using (StreamReader streamReader = new StreamReader(responseStream, Encoding.UTF8))
                        {
                            string responseData = streamReader.ReadToEnd();

                            // 解析返回结果
                            var result = JsonConvert.DeserializeObject<ApiResponse>(responseData);

                            // 判断返回的 Code 是否为 500
                            if (result != null && result.code == 500)
                            {
                                // 阻止下达，显示提示信息
                                _needBlockOperation = true;
                                _errorMessage = string.IsNullOrEmpty(result.msg) ? "MES接口返回错误，无法下达" : result.msg;
                                //this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
                                //this.View.ShowErrMessage(_errorMessage);
                                return;
                            }

                            // Code 不是 500，表示校验通过，允许下达
                            if (result != null && result.success)
                            {
                                this.View.ShowMessage("MES接口校验通过，可以下达");
                            }
                            else
                            {
                                this.View.ShowMessage("MES接口返回：" + (result?.msg ?? "未知状态"), MessageBoxType.Error);
                            }
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                // 处理 HTTP 错误
                _needBlockOperation = true;

                if (ex.Response != null)
                {
                    using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
                    {
                        string errorResponse = reader.ReadToEnd();

                        // 尝试解析 HTTP 错误响应中的 JSON
                        try
                        {
                            var errorResult = JsonConvert.DeserializeObject<ApiResponse>(errorResponse);
                            if (errorResult != null && errorResult.code == 500)
                            {
                                _errorMessage = string.IsNullOrEmpty(errorResult.msg) ? "MES接口返回错误，无法下达" : errorResult.msg;
                            }
                            else
                            {
                                _errorMessage = "请求失败：" + errorResponse;
                            }
                        }
                        catch
                        {
                            _errorMessage = "请求失败：" + errorResponse;
                        }
                    }
                }
                else
                {
                    _errorMessage = "网络请求失败：" + ex.Message;
                }

                this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
            }
            catch (Exception ex)
            {
                // 处理其他异常
                _needBlockOperation = true;
                _errorMessage = "执行MES校验失败：" + ex.Message;
                this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
            }
        }

        /// <summary>
        /// GET 请求 - 查询MES数据判断是否可以反执行
        /// </summary>
        private void ExecuteUndoToPlanConfirm()
        {
            try
            {
                // 获取当前单据数据
                DynamicObject dataPacket = this.Model.DataObject;

                // 获取生产订单号
                string productionOrderCode = dataPacket["BillNo"]?.ToString();
                if (string.IsNullOrEmpty(productionOrderCode))
                {
                    this.View.ShowMessage("无法获取生产订单号，请检查单据", MessageBoxType.Error);
                    return;
                }

                string url = $"http://192.168.1.6:80/iMark/v1/DBProcessOrderInfo/getDBProcessOrderInfoListByProductionOrderCode?productionOrderCode={productionOrderCode}";

                // 创建请求
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.Timeout = 30000;

                // 添加 Authorization 头
                string authInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes("bg:bg957768"));
                request.Headers.Add("Authorization", "Basic " + authInfo);

                // 获取响应
                using (WebResponse response = request.GetResponse())
                {
                    using (Stream responseStream = response.GetResponseStream())
                    {
                        using (StreamReader streamReader = new StreamReader(responseStream, Encoding.UTF8)) 
                        {
                            string responseData = streamReader.ReadToEnd();

                            // 反序列化为动态对象
                            dynamic result = JsonConvert.DeserializeObject(responseData);

                            // 根据返回内容判断业务逻辑
                            // TODO: 根据实际业务需求添加判断逻辑

                            // 示例：如果返回的数据不为空，说明MES中已有工序订单，不允许反执行
                            // 这里需要根据实际的返回格式来调整
                            bool hasProcessOrder = false;

                            // 尝试判断是否有数据
                            if (result != null)
                            {
                                // 根据实际返回结构判断，以下是示例
                                // 如果是数组且长度>0
                                // if (result.data is Newtonsoft.Json.Linq.JArray array && array.Count > 0)
                                // {
                                //     hasProcessOrder = true;
                                // }

                                // 如果是对象且有记录
                                 if (result.data != null && result.data.Count > 0)
                                 {
                                     hasProcessOrder = true;
                                 }
                            }

                            if (hasProcessOrder)
                            {
                                _needBlockOperation = true;
                                _errorMessage = "MES中已存在工序订单，不允许反执行";
                                //this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
                                return;
                            }

                            this.View.ShowMessage("校验通过，可以反执行");
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
                    {
                        string errorResponse = reader.ReadToEnd();
                        this.View.ShowMessage("查询MES失败：" + errorResponse, MessageBoxType.Error);
                    }
                }
                else
                {
                    this.View.ShowMessage("查询MES失败：" + ex.Message, MessageBoxType.Error);
                }
            }
            catch (Exception ex)
            {
                this.View.ShowMessage("执行失败：" + ex.Message, MessageBoxType.Error);
            }
        }

        /// <summary>
        /// API响应实体类
        /// </summary>
        public class ApiResponse
        {
            public object data { get; set; }
            public object result { get; set; }
            public int code { get; set; }
            public string msg { get; set; }
            public bool success { get; set; }
        }
    }
}