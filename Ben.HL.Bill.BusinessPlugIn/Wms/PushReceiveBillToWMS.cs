using System;
using System.Collections.Generic;
using System.ComponentModel;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;
using log4net;

namespace Ben.HL.Bill.BusinessPlugIn
{
    [HotUpdate]
    [Description("收料通知单审核推送WMS-Ben")]
    public class PushReceiveBillToWMS : AbstractBillPlugIn
    {
        private static readonly ILog _benLog = LogManager.GetLogger("Ben.WMS");
        private readonly WmsApiClient _apiClient = new WmsApiClient();

        public override void AfterDoOperation(AfterDoOperationEventArgs e)
        {
            base.AfterDoOperation(e);

            if (e.Operation.Operation.Equals("Audit", StringComparison.OrdinalIgnoreCase))
            {
                DynamicObject billObj = this.Model.DataObject;
                if (billObj == null)
                {
                    this.View.ShowMessage("没有找到需要推送的单据数据");
                    return;
                }

                var svc = new ReceiveBillPushService(this.Context);

                // 仅当分录中存在物料的拣货仓库(PickStockId.FNumber)为2010时，才推送WMS/SRM
                if (!svc.ExistsPickStock2010(billObj))
                {
                    return;
                }

                try
                {
                    IntegrationConfig config = IntegrationConfig.Load();
                    List<ReceiveBillPushService.ReceiveEntrySource> entries = svc.GetEntrySources(billObj);

                    WmsSaveReceiveBillRequestModel wmsRequest = svc.BuildStandardApiRequest(billObj, entries);
                    string wmsJson = ReceiveBillPushService.Serialize(wmsRequest);
                    string wmsResponse = _apiClient.PostJson(config.WmsReceiveBillApiUrl, wmsJson);
                    // 输出推送报文及返回至 Ben.WMS 专属日志文件
                    _benLog.Info("收料通知单推送WMS请求报文：" + wmsJson + "；WMS返回：" + wmsResponse);

                    // HL_SRM_Source_NO不为空时才推送SRM
                    string srmSourceNo = ReceiveBillPushService.GetValue(billObj, "HL_SRM_Source_NO");
                    if (string.IsNullOrWhiteSpace(srmSourceNo))
                    {
                        this.View.ShowMessage(string.Format("WMS推送成功，WMS返回：{0}；HL_SRM_Source_NO为空，无需推送SRM", wmsResponse));
                        return;
                    }

                    SrmReceiveRequestModel srmRequest = svc.BuildSrmRequest(billObj, entries, config);
                    string srmJson = ReceiveBillPushService.Serialize(srmRequest);
                    string srmResponse = _apiClient.PostSoap(config.SrmApiUrl, srmJson);
                    // 输出回写报文及返回至 Ben.WMS 专属日志文件
                    _benLog.Info("收料通知单SRM回写请求报文：" + srmJson + "；SRM返回：" + srmResponse);
                    this.View.ShowMessage(string.Format("WMS推送成功，WMS返回：{0}；SRM回写成功，共{1}条。SRM返回：{2}", wmsResponse, entries.Count, srmResponse));
                }
                catch (Exception ex)
                {
                    this.View.ShowMessage("收料通知单联动处理异常：" + ex.Message);
                }
            }
        }
    }
}
